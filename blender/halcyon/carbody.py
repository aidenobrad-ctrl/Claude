"""Car bodies as smooth signed distance fields.

A car is sculpted from smooth profile functions instead of a control cage:
a lower body (plan shape with haunches, a shoulder crease, tucked sills,
hood and deck heights), a greenhouse (windshield, roof, and a fastback,
notchback, hatch, squareback or pickup-cab rear), wheel arches with lips or
flares, and mirrors. Body styles add their own volumes and cuts: a pickup
bed, a roadster cockpit with humps, mid-engine side intakes, hood scoops.

Features (glass, lights, grilles, intakes, trim, panel gaps) are regions on
the surface. The generator cuts them out of the painted body along their
exact contours and fills each with an insert of its own material, recessed
by the feature's depth (see build_car.py and meshops.py).

Car frame: u forward, v left, z up, metres, ground at z = 0. The body is
built around the wheelbase centre (self.mid); Blender coordinates are
x = v (left), y = -(u - mid) (the car faces -Y), z.
"""
from dataclasses import dataclass, field
import numpy as np


# --- Smooth operators ---------------------------------------------------------------
# Minimum body above a wheel arch, m (see CarBody.arch_lift).
ARCH_SKIN = 0.08


def smin(a, b, k):
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0.0, 1.0)
    return b + (a - b) * h - k * h * (1.0 - h)


def smax(a, b, k):
    return -smin(-a, -b, k)


def sstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def bump(x, w):
    """Smooth bump: 1 at 0, 0 beyond +-w (cosine window)."""
    t = np.clip(np.abs(x) / w, 0.0, 1.0)
    return 0.5 + 0.5 * np.cos(np.pi * t)


def rbox2(px, py, hx, hy, r):
    """2D rounded box SDF centred at the origin."""
    qx = np.abs(px) - hx + r
    qy = np.abs(py) - hy + r
    return np.sqrt(np.maximum(qx, 0) ** 2 + np.maximum(qy, 0) ** 2) + np.minimum(np.maximum(qx, qy), 0) - r


def band(x, lo, hi):
    """Negative inside [lo, hi] (a 1D slab)."""
    return np.maximum(lo - x, x - hi)


def circle(px, py, r):
    return np.sqrt(px * px + py * py) - r


def inter(*ds, k=0.01):
    d = ds[0]
    for e in ds[1:]:
        d = smax(d, e, k)
    return d


def union(*ds):
    d = ds[0]
    for e in ds[1:]:
        d = np.minimum(d, e)
    return d


# --- Parameters ------------------------------------------------------------------------
@dataclass
class CarShape:
    """Dimensions from the car spec plus styling, all in metres."""
    length: float = 4.38
    width: float = 1.86
    height: float = 1.30
    wheelbase: float = 2.47
    track_f: float = 1.59
    track_r: float = 1.60
    tire_r: float = 0.335
    tire_w_f: float = 0.255
    tire_w_r: float = 0.275
    clearance: float = 0.12
    # fastback, notch (sedan), hatch, wagon, suv, pickup, mid (mid-engine), roadster
    style: str = 'fastback'
    doors: int = 2
    front_overhang: float = 0.88
    # Side profile: heights above the ground; u positions relative to the front axle where noted.
    nose_z: float = 0.46          # height of the nose tip
    hood_front_z: float = 0.66    # hood height at the front edge
    cowl_back: float = 0.62       # windshield base behind the front axle
    cowl_z: float = 0.86
    windshield_slope: float = 0.58  # rise per run
    roof_len: float = 0.72        # flat-ish roof length (fastback, notch, mid, pickup)
    roof_drop: float = 0.05       # roof sag from front to back
    rear_slope: float = 0.36      # rear window rise per run
    rear_base: float = None       # squareback styles: rear glass base this far ahead of the tail
    deck_z: float = 0.93          # rear window base height / deck height
    tail_z: float = 0.90
    belt_z: float = 0.81
    shoulder_z: float = 0.70      # height of the side crease
    # Sections and plan.
    shoulder_out: float = 0.018   # how far the crease stands out
    shoulder_inset: float = 0.075  # greenhouse inset from the body side at the beltline
    tumblehome: float = 0.42      # greenhouse side lean (run per rise)
    haunch_r: float = 0.05        # rear fender bulge
    haunch_f: float = 0.03        # front fender bulge
    fender_crown: float = 0.035   # fenders stand above the hood
    crown: float = 0.05           # hood/deck crown across the width
    roof_crown: float = 0.07
    sill_tuck: float = 0.075
    upper_tuck: float = 0.05
    nose_taper: float = 0.12      # plan narrowing at the nose
    tail_taper: float = 0.05
    nose_lean: float = 0.55       # front face run per rise above the nose tip
    nose_under: float = 0.9       # undercut below the nose tip
    nose_round: float = 0.30      # plan rounding of the nose corners
    tail_round: float = 0.15
    tail_lean: float = 0.06
    edge_k: float = 0.04          # body edge rounding (smaller = crisper, boxier)
    cabin_taper_f: float = 0.03
    cabin_taper_r: float = 0.07
    arch_gap: float = 0.04
    arch_lip: float = 0.012
    arch_style: str = 'lip'       # lip, flare, plain
    ducktail: float = 0.03
    mirror: bool = True
    mirror_scale: float = 1.0
    # Style extras.
    bed_len: float = 1.7          # pickup bed inner length
    bed_floor: float = 0.9        # pickup bed floor height
    hood_scoop: float = 0.0       # scoop height (0 = none)
    hood_bulge: float = 0.012
    side_intake: float = 0.0      # mid-engine side intake depth (0 = none)
    humps: float = 0.0            # roadster headrest humps height
    extra: dict = field(default_factory=dict)

    @property
    def u_front(self):
        return self.length / 2

    @property
    def u_rear(self):
        return -self.length / 2

    @property
    def axle_f(self):
        return self.u_front - self.front_overhang

    @property
    def axle_r(self):
        return self.axle_f - self.wheelbase

    @property
    def cowl_u(self):
        return self.axle_f - self.cowl_back

    @property
    def squareback(self):
        return self.style in ('hatch', 'wagon', 'suv')


# --- The body --------------------------------------------------------------------------------
class CarBody:
    def __init__(self, s: CarShape, look: dict = None):
        self.s = s
        self.look = look or {}
        self.mid = (s.axle_f + s.axle_r) / 2
        self.roof_front_u = s.cowl_u - (s.height - s.cowl_z) / s.windshield_slope
        if s.style == 'pickup':
            self.cab_back_u = s.u_rear + 0.06 + s.bed_len + 0.08
            self.roof_rear_u = self.cab_back_u + 0.07
            self.rear_base_u = self.cab_back_u
        elif s.rear_base is not None:
            self.rear_base_u = s.u_rear + s.rear_base
            self.roof_rear_u = self.rear_base_u + (s.height - s.roof_drop - s.deck_z) / s.rear_slope
        else:
            self.roof_rear_u = self.roof_front_u - s.roof_len
            self.rear_base_u = self.roof_rear_u - (s.height - s.roof_drop - s.deck_z) / s.rear_slope
        self.roof_len = self.roof_front_u - self.roof_rear_u

    # Lower body ------------------------------------------------------------------------------
    def plan_width(self, u):
        """Half width at the shoulder crease along the car (plan view)."""
        s = self.s
        hw0 = s.width / 2 - 0.045
        hw = hw0 - 0.025 + s.haunch_r * bump(u - s.axle_r, 0.8) + s.haunch_f * bump(u - s.axle_f, 0.7)
        nose = sstep(s.axle_f + 0.30, s.u_front, u)
        tail = sstep(s.axle_r - 0.55, s.u_rear, u)
        return hw - s.nose_taper * nose ** 2 - s.tail_taper * tail ** 2

    def half_width(self, u, z):
        """Body half width at height z: tucked at the sill, out at the crease, in above it."""
        s = self.s
        hw = self.plan_width(u)
        below = np.clip((s.shoulder_z - z) / (s.shoulder_z - s.clearance), 0.0, 1.0)
        above = np.clip((z - s.shoulder_z) / 0.25, 0.0, 1.0)
        crease = s.shoulder_out * np.maximum(0.0, 1 - np.abs(z - s.shoulder_z) / 0.06) ** 2
        return hw - s.sill_tuck * below ** 1.7 - s.upper_tuck * above ** 1.3 + crease

    def hood_z(self, u):
        s = self.s
        hood_t = np.clip((s.u_front - 0.12 - u) / (s.u_front - 0.12 - s.cowl_u), 0.0, 1.0)
        return s.hood_front_z + (s.cowl_z - 0.03 - s.hood_front_z) * (1 - (1 - hood_t) ** 1.7)

    def top_z(self, u, v):
        s = self.s
        hood = self.hood_z(u)
        # Deck from the cabin to the tail, with a ducktail lip.
        deck_t = np.clip((u - s.u_rear) / 0.75, 0.0, 1.0)
        deck = s.tail_z + (s.deck_z - s.tail_z) * deck_t + s.ducktail * bump(u - (s.u_rear + 0.10), 0.16)
        belt = s.belt_z + 0.02
        w_hood = sstep(s.cowl_u - 0.12, s.cowl_u + 0.12, u)
        if s.squareback or s.style == 'pickup':
            w_deck = 0.0 * u
            if s.style == 'pickup':
                belt = belt + 0.0 * u
        else:
            w_deck = sstep(self.rear_base_u + 0.2, self.rear_base_u - 0.25, u)
        z = belt * (1 - w_hood) * (1 - w_deck) + hood * w_hood + deck * w_deck
        # Crown across the width; fenders stand proud over the wheels; a hood bulge.
        hw = s.width / 2
        crown = s.crown * (v / hw) ** 2
        fender = s.fender_crown * sstep(0.42, 0.72, np.abs(v)) * (bump(u - s.axle_f, 0.8) + 0.9 * bump(u - s.axle_r, 0.85))
        bulge = s.hood_bulge * bump(v, 0.32) * sstep(s.u_front - 0.1, s.axle_f, u) * w_hood
        z = z - crown + fender + bulge
        return z + self.arch_lift(u, v, z)

    def arch_lift(self, u, v, z):
        """
        How far the top surface must rise over the wheels to keep ARCH_SKIN of
        body above each arch: a thinner skin than about two voxels tears when
        it is polygonized. Zero wherever the fender is already high enough.
        """
        s = self.s
        r = s.tire_r + s.arch_gap
        lift = 0.0 * z
        for axle, track, tw in ((s.axle_f, s.track_f, s.tire_w_f), (s.axle_r, s.track_r, s.tire_w_r)):
            du = u - axle
            edge = s.tire_r + np.sqrt(np.maximum(r * r - du * du, 0.0)) + ARCH_SKIN
            inner = track / 2 - tw / 2 - 0.08
            weight = bump(du, r + 0.18) * sstep(inner - 0.12, inner + 0.04, np.abs(v))
            k = 0.006
            need = k * np.log1p(np.exp(np.clip((edge - z) / k, -60.0, 60.0)))  # a smooth max(0, edge - z)
            lift = np.maximum(lift, need * weight)
        return lift

    def bottom_z(self, u):
        s = self.s
        front_rise = 0.04 * sstep(s.axle_f + 0.25, s.u_front, u)
        rear_rise = 0.08 * sstep(s.axle_r - 0.35, s.u_rear, u)
        return s.clearance + front_rise + rear_rise

    def nose_u(self, z, v):
        s = self.s
        hw = s.width / 2
        below = np.minimum(z - s.nose_z, 0.0)
        above = np.maximum(z - s.nose_z, 0.0)
        return s.u_front - s.nose_under * below ** 2 / 0.32 - s.nose_lean * above - s.nose_round * (v / hw) ** 4

    def tail_u(self, z, v):
        s = self.s
        hw = s.width / 2
        below = np.minimum(z - (s.clearance + 0.2), 0.0)
        return s.u_rear + 0.35 * below ** 2 / 0.2 + s.tail_lean * np.maximum(z - s.tail_z + 0.18, 0.0) + s.tail_round * (v / hw) ** 4

    def lower(self, u, v, z):
        s = self.s
        d_side = np.abs(v) - self.half_width(u, z)
        d_top = z - self.top_z(u, v)
        d = smax(d_side, d_top, s.edge_k)
        d = smax(d, self.bottom_z(u) - z, 0.025)
        return d

    # Greenhouse -------------------------------------------------------------------------------
    def windshield_line(self, u):
        s = self.s
        return s.cowl_z + (s.cowl_u - u) * s.windshield_slope

    def rear_line(self, u):
        s = self.s
        if s.style == 'pickup':
            return s.belt_z + (u - self.cab_back_u) * 25.0
        return s.deck_z + (u - self.rear_base_u) * s.rear_slope

    def roof_arc(self, u):
        s = self.s
        mid = (self.roof_front_u + self.roof_rear_u) / 2
        half = self.roof_len / 2 + 0.3
        sag = s.roof_drop * (mid - u) / max(self.roof_len, 0.1)
        return s.height - 0.04 * ((u - mid) / half) ** 2 - np.maximum(sag, 0) * 0.5

    def roof_z(self, u, v):
        s = self.s
        k_rear = 0.10 if s.squareback or s.style == 'pickup' else 0.14
        z = smin(self.windshield_line(u), self.roof_arc(u), 0.12)
        z = smin(z, self.rear_line(u), k_rear)
        cw = np.maximum(self.cabin_half_width(u, s.height), 0.3)
        return z - s.roof_crown * (v / cw) ** 2

    def cabin_half_width(self, u, z):
        s = self.s
        base = self.plan_width(u) - 0.06 - s.shoulder_inset
        # Narrower towards the windshield base and the rear deck (teardrop plan).
        base = base - s.cabin_taper_f * sstep(self.roof_front_u, s.cowl_u + 0.2, u) - s.cabin_taper_r * sstep(self.roof_rear_u, self.rear_base_u - 0.1, u)
        return base - np.maximum(z - s.belt_z, 0.0) * s.tumblehome

    def cabin(self, u, v, z):
        s = self.s
        d = smax(np.abs(v) - self.cabin_half_width(u, z), z - self.roof_z(u, v), 0.045)
        d = smax(d, s.belt_z - 0.25 - z, 0.02)
        # The greenhouse ends inside the body: no slab running out of the nose or tail.
        d = smax(d, u - (s.cowl_u + 0.25), 0.02)
        if s.style == 'pickup':
            d = smax(d, self.cab_back_u - u, 0.03)
        elif not s.squareback:
            d = smax(d, (self.rear_base_u - 0.25) - u, 0.02)
        return d

    # Arches and details -------------------------------------------------------------------
    def _arch(self, u, v, z, axle, track, tw):
        s = self.s
        r = s.tire_r + s.arch_gap
        cyl = np.sqrt((u - axle) ** 2 + (z - s.tire_r) ** 2) - r
        inner = track / 2 - tw / 2 - 0.08
        return smax(cyl, inner - np.abs(v), 0.02), cyl

    def arches(self, u, v, z):
        s = self.s
        a1, _ = self._arch(u, v, z, s.axle_f, s.track_f, s.tire_w_f)
        a2, _ = self._arch(u, v, z, s.axle_r, s.track_r, s.tire_w_r)
        return np.minimum(a1, a2)

    def arch_ring(self, u, v, z, axle, grow=0.0):
        """Distance to the torus-like lip around one arch."""
        s = self.s
        flare = s.arch_style == 'flare'
        r = s.tire_r + s.arch_gap + (0.035 if flare else 0.02)
        tube = (0.05 if flare else 0.026) + grow
        side = self.plan_width(axle) - (0.01 if flare else 0.035)
        q = np.sqrt((u - axle) ** 2 + (z - s.tire_r) ** 2) - r
        if flare:
            # Flat-faced flare: a rounded rectangle in section.
            ring = rbox2(q, np.abs(v) - side, tube * 0.9, tube * 0.55, 0.018)
        else:
            ring = np.sqrt(q ** 2 + (np.abs(v) - side) ** 2) - tube
        return smax(ring, max(s.tire_r * 0.35, s.clearance + 0.09) - z, 0.02)  # not below the sill

    def arch_lips(self, u, v, z):
        s = self.s
        return np.minimum(self.arch_ring(u, v, z, s.axle_f), self.arch_ring(u, v, z, s.axle_r))

    def mirror_frame(self):
        s = self.s
        m = s.mirror_scale
        cu = s.cowl_u - 0.17 + (0.05 if s.style in ('suv', 'pickup') else 0.0)
        cz = s.belt_z + 0.075 * m
        side = float(self.half_width(np.array(cu), np.array(s.belt_z))) - 0.03
        cv = side + 0.13 * m
        return cu, cv, cz, side

    def mirrors(self, u, v, z):
        s = self.s
        m = s.mirror_scale
        cu, cv, cz, side = self.mirror_frame()
        av = np.abs(v)
        # Head: a rounded box, tapered towards the front, with a flat back face.
        q = rbox2(av - cv, z - cz, 0.085 * m, 0.048 * m, 0.035 * m)
        front = (u - (cu + 0.02)) - 0.09 * m + 0.35 * np.maximum(np.abs(av - cv) - 0.02, 0)
        head = smax(q, smax(front, (cu - 0.035 * m) - u, 0.01), 0.012)
        # Stalk from the door to the head's inner end.
        stalk = rbox2(u - (cu - 0.005), z - (cz - 0.035 * m), 0.035 * m, 0.014 * m, 0.01)
        stalk = smax(stalk, np.abs(av - (side + cv - 0.05 * m) / 2) - (cv - 0.05 * m - side) / 2 - 0.02, 0.01)
        return smin(head, stalk, 0.02)

    # Style volumes ----------------------------------------------------------------------------
    def additions(self, u, v, z):
        """Extra volumes blended onto the body: hood scoops, roadster humps."""
        s = self.s
        out = []
        if s.hood_scoop > 0:
            su = s.cowl_u + 0.55
            hz = self.hood_z(np.array(su)) + 0.0
            scoop = smax(rbox2(u - su, v, 0.30, 0.20, 0.08), z - (hz + s.hood_scoop - 0.025 * (u - su) / 0.3), 0.03)
            scoop = smax(scoop, hz - 0.05 - z, 0.02)
            out.append((scoop, 0.04))
        if s.humps > 0:
            for side in (-1, 1):
                hv = side * s.extra.get('hump_v', 0.33)
                hu = s.cowl_u - s.extra.get('hump_back', 1.05)
                q = np.stack([(u - (hu - 0.35)) / 0.62, (v - hv) / 0.17, (z - s.belt_z) / s.humps])
                e = (np.sqrt((q ** 2).sum(axis=0)) - 1.0) * min(0.17, s.humps)
                # Rounded front face, long taper to the back.
                e = smax(e, u - (hu + 0.05), 0.08)
                out.append((e, 0.06))
        return out

    def cuts(self, u, v, z):
        """Volumes removed from the body: pickup bed, roadster cockpit, side intakes."""
        s = self.s
        out = []
        if s.style == 'pickup':
            u0 = s.u_rear + 0.06
            u1 = self.cab_back_u - 0.07
            hv = self.plan_width(np.array((u0 + u1) / 2)) - 0.075
            bed = smax(rbox2(u - (u0 + u1) / 2, v, (u1 - u0) / 2, hv, 0.03), s.bed_floor - z, 0.02)
            out.append((bed, 0.012))
        if s.style == 'roadster':
            c0 = s.cowl_u - 0.02
            c1 = s.cowl_u - s.extra.get('cockpit_len', 1.15)
            hv = s.width / 2 - 0.16
            pit = smax(rbox2(u - (c0 + c1) / 2, v, (c0 - c1) / 2, hv, 0.22), s.belt_z - 0.42 - z, 0.03)
            out.append((pit, 0.03))
        if s.side_intake > 0:
            ui = s.axle_r + s.tire_r + s.arch_gap + 0.30
            zi = s.shoulder_z - 0.02
            depth = s.side_intake * sstep(ui + 0.28, ui - 0.22, u)
            hw = self.plan_width(u)
            scoop = smax(rbox2(u - ui, z - zi, 0.30, 0.15, 0.08), (hw - depth) - np.abs(v), 0.04)
            out.append((scoop, 0.03))
        return out

    def base(self, u, v, z):
        """The body before features are cut in."""
        s = self.s
        if s.style == 'roadster':
            d = self.lower(u, v, z)
        else:
            d = smin(self.lower(u, v, z), self.cabin(u, v, z), 0.035)
        for a, k in self.additions(u, v, z):
            d = smin(d, a, k)
        # Nose and tail caps for everything (the squareback greenhouse ends at the tail).
        d = smax(d, u - self.nose_u(z, v), 0.06)
        d = smax(d, self.tail_u(z, v) - u, 0.05)
        for c, k in self.cuts(u, v, z):
            d = smax(d, -c, k)
        if s.arch_lip > 0:
            d = smin(d, self.arch_lips(u, v, z), 0.04)
        if s.mirror:
            d = smin(d, self.mirrors(u, v, z), 0.012)
        return smax(d, -self.arches(u, v, z), 0.010)

    def base_blender(self, x, y, z):
        """Field in Blender coordinates (x left, y back, z up), origin mid-wheelbase."""
        return self.base(-y + self.mid, x, z)

    def to_blender(self, u, v, z):
        return np.stack([v, -(u - self.mid), z], axis=-1)

    def surface_along(self, P, d, tmax=1.0, steps=40):
        """First crossing of the body along rays P + t d (Blender coordinates), by marching then bisection."""
        P = np.asarray(P, dtype=np.float64)
        d = np.asarray(d, dtype=np.float64) * np.ones_like(P)
        f = lambda Q: self.base_blender(Q[:, 0], Q[:, 1], Q[:, 2])
        t0 = np.zeros(len(P))
        t1 = np.full(len(P), np.nan)
        prev = f(P)
        dt = tmax / steps
        for k in range(1, steps + 1):
            cur = f(P + d * (k * dt))
            hit = np.isnan(t1) & (prev > 0) & (cur <= 0)
            t0 = np.where(np.isnan(t1) & ~hit, k * dt, t0)
            t1 = np.where(hit, k * dt, t1)
            prev = cur
        ok = ~np.isnan(t1)
        lo = np.where(ok, t0, 0)
        hi = np.where(ok, t1, 0)
        for _ in range(20):
            m = (lo + hi) / 2
            inside = f(P + d * m[:, None]) <= 0
            hi = np.where(inside, m, hi)
            lo = np.where(inside, lo, m)
        return P + d * hi[:, None], ok

    # Features ------------------------------------------------------------------------------
    def features(self):
        if getattr(self, '_features', None) is None:
            from .features import make_features
            self._features = make_features(self)
        return self._features


class Feature:
    """A region cut out of the painted body and filled with an insert of another material.

    depth: how far the insert sits below the body surface.
    lod: the coarsest LOD that still has this feature (0 = LOD0 only).
    insert: False for a pure groove (a panel gap): the wall and a dark floor only.
    """

    def __init__(self, name, material, region, depth, lod=2, insert=True, wall='Gap', margin=None):
        self.name = name
        self.material = material
        self.region = region
        self.depth = depth
        self.lod = lod
        self.insert = insert
        self.wall = wall
        self.margin = margin if margin is not None else depth * 1.5 + 0.006
