"""Surface features of a car body: glass, lights, grilles, intakes, trim and
panel gaps, chosen per car by its `look`.

Every region is a function region(u, v, z, n) in the car frame (u forward,
v left, z up; n = (nu, nv, nz) the surface normal there), negative inside.
They only need to be right on the body surface, where they are evaluated.

Two kinds:
- Hole: cut out of the painted body along region = 0, with a dark wall
  down into the surface and an insert of `material` `depth` below it.
- Patch: a crisp piece of the surface (no hole) moved `depth` inward,
  e.g. a lens inside a light's housing, with optional material splits.
"""
import numpy as np

from .carbody import band, circle, rbox2, smax, smin, sstep


class Hole:
    kind = 'hole'

    def __init__(self, name, region, depth, material, lod=2, wall='Gap', margin=None):
        self.name = name
        self.region = region
        self.depth = depth
        self.material = material
        self.lod = lod
        self.wall = wall
        self.margin = margin if margin is not None else depth * 1.5 + 0.006


class Patch:
    kind = 'patch'

    def __init__(self, name, region, depth, material, lod=2, splits=()):
        self.name = name
        self.region = region
        self.depth = depth
        self.material = material
        self.lod = lod
        self.splits = list(splits)  # [(region, material), ...] applied in order


def facing(nu_min=None, nu_max=None, nv_min=None, nz_min=None, nz_max=None, scale=0.08):
    """A pseudo-distance that is negative where the normal points the right way."""
    def f(n):
        nu, nv, nz = n
        d = None
        for val, lim, sign in ((nu, nu_min, 1), (nu, nu_max, -1), (np.abs(nv), nv_min, 1), (nz, nz_min, 1), (nz, nz_max, -1)):
            if lim is None:
                continue
            e = (lim - val) * sign * scale
            d = e if d is None else np.maximum(d, e)
        return d
    return f


def grown(region, g, u_min=None):
    """The region grown by g (the bezel around a lens), optionally only ahead of u_min."""
    if u_min is None:
        return lambda u, v, z, n: region(u, v, z, n) - g
    return lambda u, v, z, n: smax(region(u, v, z, n) - g, u_min - u, 0.004)


def make_features(b):
    s, L = b.s, b.look
    hw = s.width / 2
    cw = b.cabin_half_width
    out = []
    front = facing(nu_min=0.18)
    rear = facing(nu_max=-0.18)
    side = facing(nv_min=0.45)
    top = facing(nz_min=0.45)
    on_front = lambda u, n: smax((s.axle_f + 0.18) - u, front(n), 0.004)
    on_rear = lambda u, n: smax(u - (s.axle_r - 0.30), rear(n), 0.004)

    # --- Glass ---------------------------------------------------------------------------
    if s.style != 'roadster':
        out += glass(b, side, top, rear)

    # --- Headlights ------------------------------------------------------------------------
    hl = headlight_shape(b)
    if hl is not None:
        hl_lens, hl_grow, housing = hl
        out.append(Hole('Headlights', grown(hl_lens, hl_grow, (s.axle_f + 0.15)), 0.018, housing))
        drl = L.get('drl')
        splits = []
        if drl == 'rim':
            w = L.get('drl_w', 0.007)
            splits.append((lambda u, v, z, n: -(hl_lens(u, v, z, n) + w), 'DRL'))
        elif callable(drl):
            splits.append((drl(b), 'DRL'))
        out.append(Patch('HeadlightLens', hl_lens, 0.007, 'LightFront', splits=splits))

    # --- Taillights ---------------------------------------------------------------------------
    tl = taillight_shape(b)
    if tl is not None:
        tl_lens, tl_grow = tl
        out.append(Hole('Taillights', grown(tl_lens, tl_grow), 0.016, 'Gloss'))
        out.append(Patch('TaillightLens', tl_lens, 0.006, 'LightRear', splits=[(tl_bars(b), 'LightRearBar')] if L.get('tl_bars') else []))

    if s.mirror:
        cu, cv, cz, _ = b.mirror_frame()
        m = s.mirror_scale

        def mirror_glass(u, v, z, n):
            d = rbox2(np.abs(v) - cv, z - cz, 0.072 * m, 0.036 * m, 0.026 * m)
            return smax(d, smax(u - (cu - 0.02 * m), facing(nu_max=-0.6)(n), 0.004), 0.004)
        out.append(Hole('MirrorGlass', mirror_glass, 0.006, 'Mirror', lod=1, wall='Gloss', margin=0.006))

    # --- Front --------------------------------------------------------------------------------
    out += front_openings(b, on_front)
    # --- Rear ---------------------------------------------------------------------------------
    out += rear_openings(b, on_rear)
    # --- Sides --------------------------------------------------------------------------------
    out += side_trim(b, side)
    if L.get('stripes'):
        sv, sw = L['stripes']
        glass_regions = [f.region for f in out if f.kind == 'hole' and f.material == 'Glass']

        def stripes(u, v, z, n):
            d = np.abs(np.abs(v) - sv) - sw
            d = smax(d, facing(nz_min=0.25)(n), 0.004)
            for g in glass_regions:
                d = np.maximum(d, -(g(u, v, z, n) - 0.012))  # paint, not glass, with a little gap
            return d
        out.append(Hole('Stripes', stripes, 0.0008, 'Accent', wall='Accent', margin=0.003))
    if s.style == 'pickup':
        u0, u1 = s.u_rear + 0.03, b.cab_back_u - 0.04

        def liner(u, v, z, n):
            d = smax(band(u, u0, u1), np.abs(v) - (b.plan_width(u) - 0.055), 0.01)
            return smax(d, z - (s.belt_z - 0.012), 0.004)
        out.append(Hole('BedLiner', liner, 0.002, 'Cladding', wall='Cladding', margin=0.006))
    # --- Panel gaps (LOD0) -------------------------------------------------------------------
    if L.get('gaps', True):
        out += panel_gaps(b, side, top, rear)
    return out


# --- Glass ----------------------------------------------------------------------------------------
def glass(b, side, top, rear):
    s, L = b.s, b.look
    cw = b.cabin_half_width
    out = []
    rail_drop = L.get('rail_drop', 0.065)

    def a_pillar(z):
        return s.cowl_u - (z - s.cowl_z) / s.windshield_slope - L.get('a_pillar', 0.10)

    def rear_edge(z):
        if s.style == 'pickup':
            return b.cab_back_u + 0.10 + 0 * z
        if s.squareback:
            return b.rear_base_u + (z - s.deck_z) / s.rear_slope + L.get('d_pillar', 0.13)
        return b.rear_base_u + (z - s.deck_z) / s.rear_slope + L.get('c_pillar', 0.24)

    belt = s.belt_z + L.get('dlo_belt', 0.035)

    def dlo(u, v, z, n):
        rail = b.roof_z(u, cw(u, z)) - rail_drop
        d = smax(belt - z, z - rail, 0.025)
        d = smax(d, smax(u - a_pillar(z), rear_edge(z) - u, 0.03), 0.03)
        d = smax(d, (cw(u, z) - 0.07) - np.abs(v), 0.01)
        return smax(d, side(n), 0.01)

    pillars = []
    if s.doors >= 4 or s.style in ('wagon', 'suv'):
        bu = L.get('b_pillar', None)
        if bu is None:
            bu = s.cowl_u - 1.12
        pillars.append((bu, L.get('b_width', 0.045), L.get('b_lean', 0.10)))
    if s.style in ('wagon', 'suv') or L.get('c_window'):
        cu = L.get('c_pillar_u', b.rear_base_u + 0.75)
        pillars.append((cu, L.get('c_width', 0.06), L.get('c_lean', -0.25)))
    for c_u in L.get('extra_pillars', []):
        pillars.append((c_u, 0.04, 0.0))

    def pillar_band(u, v, z, n):
        d = None
        for pu, pw, lean in pillars:
            e = np.abs(u - (pu + lean * (z - s.belt_z))) - pw
            d = e if d is None else np.minimum(d, e)
        return d

    if pillars:
        def windows(u, v, z, n):
            return smax(dlo(u, v, z, n), -pillar_band(u, v, z, n), 0.006)

        pillar_mat = L.get('pillar_mat', 'Gloss')

        def pillar_trim(u, v, z, n):
            return smax(dlo(u, v, z, n) - 0.012, pillar_band(u, v, z, n) - 0.012, 0.004)

        out.append(Hole('Pillars', pillar_trim, 0.003, pillar_mat, wall='Gloss', margin=0.008))
    else:
        windows = dlo
    out.append(Hole('SideGlass', windows, 0.012, 'Glass', margin=0.03))

    def windshield(u, v, z, n):
        d = smax(np.abs(v) - (cw(u, z) - 0.075), s.cowl_z + 0.03 - z, 0.03)
        d = smax(d, z - (s.height - L.get('ws_top', 0.075)), 0.03)
        d = smax(d, (b.roof_front_u - 0.2) - u, 0.01)
        return smax(d, facing(nu_min=0.15)(n), 0.01)

    out.append(Hole('Windshield', windshield, 0.012, 'Glass', margin=0.03))

    if s.style == 'pickup':
        def rear_window(u, v, z, n):
            d = rbox2(v, z - (s.belt_z + (s.height - 0.1 - s.belt_z) / 2 + 0.02), 0.48, (s.height - 0.1 - s.belt_z) / 2 - 0.07, 0.05)
            return smax(d, facing(nu_max=-0.5)(n), 0.01)
    elif s.squareback:
        def rear_window(u, v, z, n):
            d = smax(np.abs(v) - (cw(u, z) - L.get('rw_side', 0.10)), s.deck_z + 0.05 - z, 0.03)
            d = smax(d, z - (s.height - 0.085), 0.03)
            d = smax(d, u - (b.roof_rear_u + 0.05), 0.01)
            return smax(d, facing(nu_max=-0.25)(n), 0.01)
    else:
        def rear_window(u, v, z, n):
            d = smax(np.abs(v) - (cw(u, z) - 0.115), s.deck_z + L.get('rw_base', 0.055) - z, 0.03)
            d = smax(d, z - (s.height - 0.09), 0.03)
            return smax(d, u - (b.roof_rear_u + 0.1), 0.01)
    out.append(Hole('RearGlass', rear_window, 0.012, 'Glass', margin=0.03))

    if L.get('sunroof'):
        def sunroof(u, v, z, n):
            d = rbox2(u - (b.roof_front_u - 0.22 - L['sunroof'] / 2), v, L['sunroof'] / 2, 0.42, 0.06)
            return smax(d, top(n), 0.01)
        out.append(Hole('Sunroof', sunroof, 0.006, 'Glass', lod=1, margin=0.015))
    if L.get('black_roof'):
        def roof(u, v, z, n):
            rail = b.roof_z(u, cw(u, z)) - rail_drop - 0.01
            d = smax(rail - z, smax(u - (b.roof_front_u - 0.10), (b.roof_rear_u + 0.02) - u, 0.02), 0.01)
            return smax(d, top(n), 0.01)
        rm = L.get('roof_mat', 'Gloss')
        out.append(Hole('BlackRoof', roof, 0.002, rm, wall=rm, margin=0.006))
    return out


# --- Lights -----------------------------------------------------------------------------------------
def headlight_shape(b):
    """(lens region, bezel growth, housing material) for the car's headlight style."""
    s, L = b.s, b.look
    hw = s.width / 2
    style = L.get('headlights', 'slim')
    front = facing(nu_min=0.15)
    if style == 'slim':
        v0, v1 = L.get('hl_v0', 0.40), hw - L.get('hl_in', 0.09)
        z0 = s.hood_front_z - L.get('hl_drop', 0.07)
        rise = L.get('hl_rise', 0.045)
        h0, h1 = L.get('hl_h', 0.026), L.get('hl_h1', 0.016)

        def lens(u, v, z, n):
            av = np.abs(v)
            t = np.clip((av - v0) / (v1 - v0), 0, 1)
            zc = z0 + rise * t
            h = h0 + h1 * (1 - t) ** 2
            d = smax(np.abs(z - zc) - h, smax(v0 - av, av - v1, 0.02), 0.02)
            return smax(d, (s.axle_f + 0.3) - u, 0.01)
        return lens, 0.016, L.get('hl_housing', 'Gloss')
    if style == 'round':
        hv, hz, r = L.get('hl_v', hw - 0.24), L.get('hl_z', s.hood_front_z - 0.11), L.get('hl_r', 0.085)

        def lens(u, v, z, n):
            return smax(circle(np.abs(v) - hv, z - hz, r), smax((s.axle_f + 0.2) - u, front(n), 0.005), 0.005)
        return lens, L.get('hl_bezel', 0.016), L.get('hl_housing', 'Chrome')
    if style == 'quad':
        hz, r = L.get('hl_z', s.hood_front_z - 0.12), L.get('hl_r', 0.075)
        v_out, v_in = L.get('hl_v', hw - 0.20), L.get('hl_v2', hw - 0.40)

        def lens(u, v, z, n):
            av = np.abs(v)
            d = np.minimum(circle(av - v_out, z - hz, r), circle(av - v_in, z - hz, r))
            return smax(d, smax((s.axle_f + 0.2) - u, front(n), 0.005), 0.005)
        return lens, 0.014, 'Chrome'
    if style == 'rect':
        hv, hz = L.get('hl_v', hw - 0.27), L.get('hl_z', s.hood_front_z - 0.12)
        hx, hh, rr = L.get('hl_w', 0.17), L.get('hl_h', 0.07), L.get('hl_round', 0.03)

        def lens(u, v, z, n):
            return smax(rbox2(np.abs(v) - hv, z - hz, hx, hh, rr), smax((s.axle_f + 0.2) - u, front(n), 0.005), 0.005)
        return lens, 0.014, L.get('hl_housing', 'Gloss')
    if style == 'blade':
        hz = L.get('hl_z', s.hood_front_z - 0.05)

        def lens(u, v, z, n):
            av = np.abs(v)
            zc = hz + 0.03 * sstep(hw - 0.5, hw - 0.12, av)
            d = smax(np.abs(z - zc) - L.get('hl_h', 0.011), av - (hw - 0.10), 0.01)
            return smax(d, (s.axle_f + 0.25) - u, 0.005)
        return lens, 0.012, 'Gloss'
    if style == 'swept':
        v0, v1 = L.get('hl_v0', 0.30), hw - L.get('hl_in', 0.07)
        z0 = s.hood_front_z - L.get('hl_drop', 0.11)

        def lens(u, v, z, n):
            av = np.abs(v)
            t = np.clip((av - v0) / (v1 - v0), 0, 1)
            zc = z0 + L.get('hl_rise', 0.06) * t
            h = L.get('hl_h', 0.045) + L.get('hl_h1', 0.02) * (1 - t)
            d = smax(np.abs(z - zc) - h, smax(v0 - av, av - v1, 0.03), 0.03)
            return smax(d, (s.axle_f + 0.25) - u, 0.01)
        return lens, 0.016, L.get('hl_housing', 'Gloss')
    return None


def taillight_shape(b):
    s, L = b.s, b.look
    hw = s.width / 2
    style = L.get('taillights', 'bar')
    rear = facing(nu_max=-0.12)
    tl_z = L.get('tl_z', s.tail_z - 0.13)
    # Lamps may wrap round the tail's corners but never run along the flanks.
    wrap = facing(nu_max=0.08 if style in ('bar', 'clusters') else -0.25)
    lim = lambda u, n: smax(u - (s.axle_r - 0.45), wrap(n), 0.005)
    if style == 'bar':
        def lens(u, v, z, n):
            av = np.abs(v)
            cluster = smax(np.abs(z - tl_z) - L.get('tl_h', 0.038), smax((hw - L.get('tl_w', 0.42)) - av, av - (hw - 0.02), 0.02), 0.02)
            bar = np.abs(z - (tl_z + 0.012)) - 0.010
            return smax(np.minimum(cluster, bar), lim(u, n), 0.01)
        return lens, 0.016
    if style == 'clusters':
        def lens(u, v, z, n):
            av = np.abs(v)
            d = smax(np.abs(z - tl_z) - L.get('tl_h', 0.045), smax((hw - L.get('tl_w', 0.36)) - av, av - (hw - 0.02), 0.02), 0.025)
            return smax(d, lim(u, n), 0.01)
        return lens, 0.016
    if style in ('round', 'quad_round'):
        r = L.get('tl_r', 0.06)
        vs = [L.get('tl_v', hw - 0.22)] + ([L.get('tl_v2', hw - 0.40)] if style == 'quad_round' else [])

        def lens(u, v, z, n):
            av = np.abs(v)
            d = None
            for vc in vs:
                e = circle(av - vc, z - tl_z, r)
                d = e if d is None else np.minimum(d, e)
            return smax(d, lim(u, n), 0.005)
        return lens, 0.014
    if style == 'vertical':
        z0, z1 = L.get('tl_z0', s.tail_z - 0.32), L.get('tl_z1', s.tail_z + 0.18)

        def lens(u, v, z, n):
            av = np.abs(v)
            d = smax(band(z, z0, z1), smax((hw - L.get('tl_w', 0.16)) - av, av - (hw - 0.015), 0.02), 0.03)
            return smax(d, lim(u, n), 0.01)
        return lens, 0.014
    if style == 'blade':
        def lens(u, v, z, n):
            av = np.abs(v)
            d = smax(np.abs(z - tl_z) - L.get('tl_h', 0.012), av - (hw - 0.04), 0.01)
            return smax(d, lim(u, n), 0.01)
        return lens, 0.012
    return None


def tl_bars(b):
    """Light bars inside the tail lens (LED strips): thin horizontal lines."""
    s, L = b.s, b.look
    tl_z = L.get('tl_z', s.tail_z - 0.13)

    def f(u, v, z, n):
        d = None
        for dz in L.get('tl_bars', (0.0,)):
            e = np.abs(z - (tl_z + dz)) - 0.0045
            d = e if d is None else np.minimum(d, e)
        return d
    return f


# --- Front openings -------------------------------------------------------------------------------
def front_openings(b, on_front):
    s, L = b.s, b.look
    hw = s.width / 2
    out = []
    g = L.get('grille', 'none')
    lo_z = s.clearance
    if g == 'upper':
        gz0, gz1 = L.get('g_z0', s.nose_z + 0.03), L.get('g_z1', s.hood_front_z - 0.05)
        gv = L.get('g_v', 0.34)

        def grille(u, v, z, n):
            return smax(rbox2(v, z - (gz0 + gz1) / 2, gv, (gz1 - gz0) / 2, L.get('g_round', 0.035)), on_front(u, n), 0.004)
        out.append(Hole('Grille', grille, 0.03, 'Grille', margin=0.05))
    elif g == 'big':
        gz0, gz1 = L.get('g_z0', s.nose_z - 0.10), L.get('g_z1', s.hood_front_z - 0.03)
        gv = L.get('g_v', hw - 0.32)

        def grille(u, v, z, n):
            return smax(rbox2(v, z - (gz0 + gz1) / 2, gv, (gz1 - gz0) / 2, L.get('g_round', 0.04)), on_front(u, n), 0.004)
        out.append(Hole('Grille', grille, 0.035, 'Grille', margin=0.05))
        if L.get('g_surround'):
            def surround(u, v, z, n):
                d = rbox2(v, z - (gz0 + gz1) / 2, gv + 0.035, (gz1 - gz0) / 2 + 0.035, L.get('g_round', 0.04) + 0.03)
                return smax(smax(d, -grille(u, v, z, n), 0.002), on_front(u, n), 0.004)
            out.append(Hole('GrilleSurround', surround, 0.003, L['g_surround'], wall=L['g_surround'], margin=0.006))
    elif g == 'split':
        gz0, gz1 = L.get('g_z0', s.nose_z - 0.02), L.get('g_z1', s.hood_front_z - 0.035)
        gv = L.get('g_v', hw - 0.11)

        def grille(u, v, z, n):
            d = rbox2(v, z - (gz0 + gz1) / 2, gv, (gz1 - gz0) / 2, 0.025)
            d = smax(d, 0.035 - np.abs(v), 0.005)  # centre divider stays body colour
            return smax(d, on_front(u, n), 0.004)
        out.append(Hole('Grille', grille, 0.04, 'Grille', margin=0.05))
    elif g == 'oval':
        gz, gh = L.get('g_z', s.nose_z + 0.02), L.get('g_h', 0.075)
        gv = L.get('g_v', 0.30)

        def grille(u, v, z, n):
            return smax(rbox2(v, z - gz, gv, gh, gh * 0.95), on_front(u, n), 0.004)
        out.append(Hole('Grille', grille, 0.04, 'Grille', margin=0.05))
        if L.get('g_surround'):
            def surround(u, v, z, n):
                d = rbox2(v, z - gz, gv + 0.022, gh + 0.022, gh * 0.95 + 0.02)
                return smax(smax(d, -grille(u, v, z, n), 0.002), on_front(u, n), 0.004)
            out.append(Hole('GrilleSurround', surround, 0.002, L['g_surround'], wall=L['g_surround'], margin=0.005))
    elif g == 'slots':
        gz0, gz1 = L.get('g_z0', s.nose_z - 0.02), L.get('g_z1', s.hood_front_z - 0.06)
        gv, nslot = L.get('g_v', 0.36), L.get('g_slots', 7)
        pitch = 2 * gv / nslot

        def grille(u, v, z, n):
            ph = np.abs(((v + gv) / pitch) % 1.0 - 0.5) * pitch
            d = smax(ph - pitch * 0.3, smax(np.abs(v) - gv, band(z, gz0, gz1), 0.01), 0.008)
            return smax(d, on_front(u, n), 0.004)
        out.append(Hole('Grille', grille, 0.04, 'Grille', margin=0.05))

    intake = L.get('intake', 'center')
    iz = L.get('i_z', lo_z + 0.15)
    ih = L.get('i_h', 0.07)
    if intake in ('center', 'triple'):
        iv = L.get('i_v', 0.40)

        def ic(u, v, z, n):
            return smax(rbox2(v, z - iz, iv, ih, 0.05), on_front(u, n), 0.004)
        out.append(Hole('Intake', ic, 0.05, 'Grille', margin=0.06))
    if intake in ('triple', 'sides'):
        sv = L.get('is_v', hw - 0.26)

        def isd(u, v, z, n):
            return smax(rbox2(np.abs(v) - sv, z - (iz + 0.02), L.get('is_w', 0.10), ih + 0.015, 0.04), on_front(u, n), 0.004)
        out.append(Hole('IntakeSides', isd, 0.045, 'Grille', margin=0.06))
    if intake == 'wide':
        def iw(u, v, z, n):
            return smax(rbox2(v, z - iz, hw - L.get('i_in', 0.28), ih, 0.05), on_front(u, n), 0.004)
        out.append(Hole('Intake', iw, 0.05, 'Grille', margin=0.06))
    if intake == 'aero':
        # Big corner intakes with a vertical blade each, and a slim centre slot.
        def ia(u, v, z, n):
            av = np.abs(v)
            d = rbox2(av - (hw - 0.30), z - (iz + 0.03), 0.20, ih + 0.04, 0.06)
            d = smax(d, 0.012 - np.abs(av - (hw - 0.26)), 0.004)
            d = np.minimum(d, rbox2(v, z - (iz - 0.02), 0.30, 0.035, 0.03))
            return smax(d, on_front(u, n), 0.004)
        out.append(Hole('Intake', ia, 0.06, 'Grille', margin=0.07))
    if L.get('splitter', True):
        def splitter(u, v, z, n):
            return smax(z - (lo_z + L.get('split_h', 0.04)), (s.axle_f + 0.35) - u, 0.01)
        out.append(Hole('Splitter', splitter, 0.004, L.get('split_mat', 'Trim'), wall='Trim', margin=0.008))
    if L.get('plates', True) and L.get('plate_front', True):
        pz = L.get('pf_z', iz + ih + 0.075 if intake in ('center', 'triple', 'wide') else lo_z + 0.27)

        def plate(u, v, z, n):
            return smax(rbox2(v, z - pz, 0.26, 0.058, 0.012), on_front(u, n), 0.004)
        out.append(Hole('PlateFront', plate, 0.004, 'Plate', lod=1, wall='Trim', margin=0.008))
    if L.get('hood_vents'):
        hu = s.axle_f - L.get('hv_back', 0.05)

        def vents(u, v, z, n):
            av = np.abs(v)
            ph = np.abs(((u - hu) / 0.05) % 1.0 - 0.5) * 0.05
            d = smax(ph - 0.011, rbox2(u - hu, av - L.get('hv_v', 0.30), 0.13, 0.12, 0.04), 0.004)
            return smax(d, facing(nz_min=0.6)(n), 0.004)
        out.append(Hole('HoodVents', vents, 0.03, 'Grille', lod=1, margin=0.03))
    if L.get('scoop_mouth') and s.hood_scoop > 0:
        su = s.cowl_u + 0.55 + 0.30

        def mouth(u, v, z, n):
            hz = b.hood_z(np.array(s.cowl_u + 0.55))
            return smax(rbox2(v, z - (hz + s.hood_scoop * 0.55), 0.15, s.hood_scoop * 0.33, 0.02), smax((su - 0.08) - u, facing(nu_min=0.3)(n), 0.004), 0.004)
        out.append(Hole('ScoopMouth', mouth, 0.05, 'Grille', margin=0.05))
    return out


# --- Rear openings ------------------------------------------------------------------------------
def rear_openings(b, on_rear):
    s, L = b.s, b.look
    hw = s.width / 2
    out = []
    if L.get('diffuser', True):
        dz = L.get('diff_h', 0.19)

        def diffuser(u, v, z, n):
            return smax(z - (s.clearance + dz), u - (s.axle_r - 0.45), 0.01)
        out.append(Hole('Diffuser', diffuser, 0.006, L.get('diff_mat', 'Trim'), wall='Trim', margin=0.01))
    if L.get('plates', True):
        pz = L.get('pr_z', s.clearance + 0.37)

        def plate(u, v, z, n):
            return smax(rbox2(v, z - pz, 0.26, 0.065, 0.012), on_rear(u, n), 0.004)
        out.append(Hole('PlateRear', plate, 0.006, 'Plate', lod=1, wall='Trim', margin=0.01))
    if L.get('louvers'):
        lu0, lu1 = L['louvers']

        def louvers(u, v, z, n):
            ph = np.abs(((u - lu1) / 0.06) % 1.0 - 0.5) * 0.06
            d = smax(ph - 0.014, smax(band(u, lu1, lu0), np.abs(v) - L.get('lv_v', 0.42), 0.01), 0.004)
            return smax(d, facing(nz_min=0.5)(n), 0.004)
        out.append(Hole('Louvers', louvers, 0.04, 'Grille', lod=1, margin=0.04))
    if L.get('rear_vents'):
        def rv(u, v, z, n):
            av = np.abs(v)
            d = rbox2(av - (hw - 0.30), z - (s.tail_z - 0.32), 0.17, 0.06, 0.03)
            return smax(d, on_rear(u, n), 0.004)
        out.append(Hole('RearVents', rv, 0.04, 'Grille', lod=1, margin=0.05))
    return out


# --- Side trim -------------------------------------------------------------------------------------
def side_trim(b, side):
    s, L = b.s, b.look
    hw = s.width / 2
    out = []
    if L.get('sills', True):
        def sill(u, v, z, n):
            d = smax(z - (s.clearance + L.get('sill_h', 0.10)), smax((s.axle_r + s.tire_r + 0.07) - u, u - (s.axle_f - s.tire_r - 0.07), 0.02), 0.01)
            return d
        out.append(Hole('Sills', sill, 0.004, L.get('sill_mat', 'Trim'), wall='Trim', margin=0.008))
    if L.get('fender_vent'):
        vent_u = s.axle_f - s.tire_r - s.arch_gap - 0.17

        def vent(u, v, z, n):
            return smax(rbox2(u - vent_u, z - (s.shoulder_z - 0.17), 0.09, 0.028, 0.025), smax(0.5 - np.abs(v), side(n), 0.004), 0.004)
        out.append(Hole('FenderVent', vent, 0.02, 'Grille', lod=1, margin=0.02))
    if s.side_intake > 0:
        ui = s.axle_r + s.tire_r + s.arch_gap + 0.30
        zi = s.shoulder_z - 0.02

        def scoop_back(u, v, z, n):
            # The mesh at the back of the scoop: faces facing forward inside the intake.
            d = rbox2(u - (ui - 0.16), z - zi, 0.16, 0.12, 0.05)
            d = smax(d, (b.plan_width(u) - s.side_intake * 0.45) - np.abs(v), 0.01)
            return smax(d, facing(nu_min=0.35)(n), 0.004)
        out.append(Hole('SideIntake', scoop_back, 0.05, 'Grille', margin=0.05))
    if L.get('cladding'):
        def clad(u, v, z, n):
            low = z - (s.clearance + L.get('clad_h', 0.20))
            ring = np.minimum(b.arch_ring(u, v, z, s.axle_f, grow=0.004), b.arch_ring(u, v, z, s.axle_r, grow=0.004))
            return np.minimum(low, ring)
        out.append(Hole('Cladding', clad, 0.003, 'Cladding', wall='Cladding', margin=0.008))
    handles = L.get('handles', 'flush')
    if handles != 'none':
        hz = L.get('handle_z', s.shoulder_z + 0.035)
        us = L.get('handle_u', [s.cowl_u - 1.05] + ([s.cowl_u - 2.0] if s.doors >= 4 else []))

        def handle(u, v, z, n):
            d = None
            for hu in us:
                e = rbox2(u - hu, z - hz, 0.07, 0.012, 0.011)
                d = e if d is None else np.minimum(d, e)
            return smax(d, side(n), 0.004)
        out.append(Hole('Handles', handle, 0.006, 'Chrome' if handles == 'chrome' else 'Gloss', lod=1, wall='Gap', margin=0.008))
    if L.get('fuel_door', True):
        fu = L.get('fuel_u', s.axle_r + s.tire_r + s.arch_gap + 0.16)
        fz = L.get('fuel_z', s.shoulder_z - 0.04)

        def fuel(u, v, z, n):
            return smax(np.abs(rbox2(u - fu, z - fz, 0.075, 0.075, 0.025)) - 0.0022, smax(-v, side(n), 0.004), 0.002)
        out.append(Hole('FuelDoor', fuel, 0.008, 'Gap', lod=0, margin=0.004))
    return out


# --- Panel gaps -------------------------------------------------------------------------------------
GAP = 0.0024


def panel_gaps(b, side, top, rear):
    s, L = b.s, b.look
    hw = s.width / 2
    lines = []
    # Hood: a rounded rectangle on the top surfaces from the cowl to the nose.
    if s.style not in ('mid',):
        hu0 = s.cowl_u + L.get('hood_cowl', 0.06)
        hu1 = s.u_front - L.get('hood_front', 0.16)
        hv = L.get('hood_v', hw - 0.20)

        def hood(u, v, z, n):
            d = np.abs(rbox2(u - (hu0 + hu1) / 2, v, (hu1 - hu0) / 2, hv, 0.06)) - GAP
            return smax(d, smax(facing(nz_min=0.35)(n), s.shoulder_z - z, 0.004), 0.002)
        lines.append(hood)
    else:
        # Mid-engine: a front boot lid.
        hu0 = s.cowl_u + 0.10
        hu1 = s.u_front - 0.14

        def frunk(u, v, z, n):
            d = np.abs(rbox2(u - (hu0 + hu1) / 2, v, (hu1 - hu0) / 2, hw - 0.30, 0.08)) - GAP
            return smax(d, facing(nz_min=0.35)(n), 0.002)
        lines.append(frunk)
    # Doors.
    d0 = s.axle_f - s.tire_r - s.arch_gap - L.get('door_front', 0.10)
    d_end = s.axle_r + s.tire_r + s.arch_gap + L.get('door_rear', 0.08)
    z0 = s.clearance + L.get('door_bottom', 0.13)
    z1 = s.belt_z + 0.2
    if s.doors >= 4:
        bu = L.get('b_pillar', s.cowl_u - 1.12)
        doors = [(d0, bu - 0.03), (bu - 0.07, d_end)]
    else:
        doors = [(d0, max(d_end, d0 - L.get('door_len', 1.22)))]

    def door_gaps(u, v, z, n):
        d = None
        for a, c in doors:
            e = np.abs(rbox2(u - (a + c) / 2, z - (z0 + z1) / 2, (a - c) / 2, (z1 - z0) / 2, 0.05)) - GAP
            d = e if d is None else np.minimum(d, e)
        return smax(smax(d, z - (s.belt_z + 0.01), 0.002), side(n), 0.002)
    lines.append(door_gaps)
    # Trunk lid or tailgate.
    if s.style in ('fastback', 'notch'):
        tu0 = b.rear_base_u - 0.02
        tu1 = s.u_rear + 0.06

        def lid(u, v, z, n):
            d = np.abs(rbox2(u - (tu0 + tu1) / 2, v, (tu0 - tu1) / 2, hw - 0.24, 0.06)) - GAP
            return smax(d, facing(nz_min=0.3)(n), 0.002)
        lines.append(lid)
    elif s.squareback:
        gz0 = L.get('gate_z0', s.clearance + 0.40)
        gz1 = s.height - 0.03

        def gate(u, v, z, n):
            d = np.abs(rbox2(v, z - (gz0 + gz1) / 2, hw - L.get('gate_in', 0.17), (gz1 - gz0) / 2, 0.06)) - GAP
            return smax(d, facing(nu_max=-0.25)(n), 0.002)
        lines.append(gate)
    elif s.style == 'pickup':
        gz0 = s.clearance + 0.35

        def gate(u, v, z, n):
            d = np.abs(rbox2(v, z - (gz0 + s.belt_z) / 2, hw - 0.10, (s.belt_z - gz0) / 2, 0.03)) - GAP
            return smax(d, facing(nu_max=-0.4)(n), 0.002)
        lines.append(gate)

        def cab_gap(u, v, z, n):
            return smax(np.abs(u - (b.cab_back_u - 0.035)) - 0.008, smax(z - (s.belt_z + 0.05), side(n), 0.004), 0.002)
        lines.append(cab_gap)

    def gaps(u, v, z, n):
        d = None
        for f in lines:
            e = f(u, v, z, n)
            d = e if d is None else np.minimum(d, e)
        return d
    return [Hole('Gaps', gaps, 0.010, 'Gap', lod=0, margin=0.003)]
