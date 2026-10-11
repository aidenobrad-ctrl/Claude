"""Separate parts of a car: interior, wipers, wings and spoilers, chrome
bumpers, grille bars, roof rails and racks, bull bars, spare wheels, mud
flaps, roadster windshields, badges. Each is built per LOD where it matters
(<Name>_LOD0..2) or once for every LOD.

Positions come from the body (CarBody) so every part follows the car's
shape: surface points are found by marching rays into the body's field.
"""
import math

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

from . import meshops as mo
from .carbody import smax, rbox2
from .surfacenets import polygonize


def _obj(name, V, F, mats, mat_index=None, smooth=True, lod=None):
    if len(F) and (np.asarray(F).max() >= len(V) or np.asarray(F).min() < 0):
        raise ValueError(f'{name}: face index out of range ({np.asarray(F).max()} >= {len(V)})')
    me = bpy.data.meshes.new(name)
    me.from_pydata(np.asarray(V, dtype=np.float64).tolist(), [], np.asarray(F, dtype=np.int64).tolist())
    me.update()
    for m in mats:
        me.materials.append(m)
    if mat_index is not None and len(me.polygons):
        me.polygons.foreach_set('material_index', np.asarray(mat_index, dtype=np.int32))
    me.polygons.foreach_set('use_smooth', np.full(len(me.polygons), smooth, dtype=bool))
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    if lod is not None:
        ob.hide_render = lod > 0
    return ob


def _bm_obj(name, bm, mats, smooth=True, lod=None):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in mats:
        me.materials.append(m)
    for p in me.polygons:
        p.use_smooth = smooth
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    if lod is not None:
        ob.hide_render = lod > 0
    return ob


def _decimate(ob, target):
    n = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    if n <= target:
        return
    mod = ob.modifiers.new('Decimate', 'DECIMATE')
    mod.decimate_type = 'COLLAPSE'
    mod.ratio = max(target / n, 0.001)
    mod.use_collapse_triangulate = True
    with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
        bpy.ops.object.modifier_apply(modifier=mod.name)


def sweep(points, profile, closed_profile=True, normals=None, ups=None):
    """Sweep a 2D profile [(across, up), ...] along a 3D polyline.

    The profile's 'up' axis follows ups (per point) or world Z; 'across' is
    perpendicular to the path and up. Returns (V, F) with end caps.
    """
    P = np.asarray(points, dtype=np.float64)
    n = len(P)
    T = np.gradient(P, axis=0)
    T = mo.unit(T)
    U = np.tile([0.0, 0.0, 1.0], (n, 1)) if ups is None else mo.unit(np.asarray(ups, dtype=np.float64))
    A = mo.unit(np.cross(T, U))
    U = mo.unit(np.cross(A, T))
    prof = np.asarray(profile, dtype=np.float64)
    m = len(prof)
    V = (P[:, None, :] + prof[None, :, 0:1] * A[:, None, :] + prof[None, :, 1:2] * U[:, None, :]).reshape(-1, 3)
    F = []
    segs = m if closed_profile else m - 1
    for i in range(n - 1):
        for j in range(segs):
            a = i * m + j
            b = i * m + (j + 1) % m
            c = (i + 1) * m + (j + 1) % m
            d = (i + 1) * m + j
            F += [(a, d, c), (a, c, b)]
    if closed_profile:
        # Fan caps.
        c0 = len(V)
        c1 = c0 + 1
        V = np.concatenate([V, P[0:1], P[-1:]])
        for j in range(m):
            F.append((c0, (j + 1) % m, j))
            F.append((c1, (n - 1) * m + j, (n - 1) * m + (j + 1) % m))
    return V, np.asarray(F, dtype=np.int64)


def rounded_rect_profile(w, h, r, seg=3):
    """A closed rounded rectangle [(x, y), ...] of width w, height h, counter-clockwise."""
    pts = []
    for cx, cy, a0 in ((w / 2 - r, h / 2 - r, 0), (-w / 2 + r, h / 2 - r, 90), (-w / 2 + r, -h / 2 + r, 180), (w / 2 - r, -h / 2 + r, 270)):
        for k in range(seg + 1):
            a = math.radians(a0 + 90 * k / seg)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def circle_profile(r, seg=10):
    return [(r * math.cos(2 * math.pi * k / seg), r * math.sin(2 * math.pi * k / seg)) for k in range(seg)]


# --- Interior ------------------------------------------------------------------------------------
def interior(body, mats, bb):
    """The cabin's inner surface (so open windows never show through the car), seats, dash, wheel."""
    s = body.s
    out = []
    if s.style == 'roadster':
        u0, u1 = s.cowl_u + 0.05, s.cowl_u - s.extra.get('cockpit_len', 1.15) - 0.05
    elif s.style == 'pickup':
        u0, u1 = s.cowl_u + 0.08, body.cab_back_u + 0.02
    elif s.squareback:
        u0, u1 = s.cowl_u + 0.08, s.u_rear + 0.05
    else:
        u0, u1 = s.cowl_u + 0.08, body.rear_base_u + 0.05
    z_floor = max(s.clearance + 0.12, s.belt_z - 0.48)
    inset = 0.035

    def field(x, y, z):
        # The air inside the cabin, as a solid: inside the body by `inset`, within the cabin box.
        u = -y + body.mid
        box = np.maximum(np.maximum(z_floor - z, u - u0), u1 - u)
        return np.maximum(body.base(u, x, z) + inset, box)

    h = 0.03
    lo = np.array([-s.width / 2, -(u0 - body.mid) - 0.05, z_floor - 0.05])
    hi = np.array([s.width / 2, -(u1 - body.mid) + 0.05, s.height + 0.02])
    n = np.ceil((hi - lo) / h).astype(int) + 1
    xs = lo[0] + np.arange(n[0]) * h
    ys = lo[1] + np.arange(n[1]) * h
    zs = lo[2] + np.arange(n[2]) * h
    f = np.empty(n, dtype=np.float32)
    Y, Z = np.meshgrid(ys, zs, indexing='ij')
    for a in range(n[0]):
        f[a] = field(np.full_like(Y, xs[a]), Y, Z)
    V, Q = polygonize(f, lo, h)
    if len(Q):
        F = mo.triangulate(V, Q)[:, ::-1]  # face inward
        for lod, target in ((0, 2500), (1, 700)):
            ob = _obj(f'Cabin_LOD{lod}', V, F, [mats['Interior']], lod=lod)
            _decimate(ob, target)
            out.append(ob)
    out += seats_and_dash(body, mats, z_floor)
    return out


def seats_and_dash(body, mats, z_floor):
    s = body.s
    out = []
    bm = bmesh.new()
    seat_z = z_floor + 0.06
    rows = [s.cowl_u - s.extra.get('seat_back', 0.95)]
    if s.doors >= 4 or s.style in ('wagon', 'suv', 'pickup'):
        rows.append(rows[0] - s.extra.get('row2', 0.88))
    cw = float(body.cabin_half_width(np.array(rows[0]), np.array(s.belt_z)))
    sv = min(0.36, cw - 0.25)
    # Seats sized to the cabin: the headrest stops well below the roof (or just above the belt in a roadster).
    top = s.belt_z + 0.24 if s.style == 'roadster' else min(s.height - 0.12, s.belt_z + 0.42)
    back_h = max(0.30, top - 0.14 - (seat_z + 0.10))
    for r, ru in enumerate(rows):
        for side in (1, -1):
            v = side * sv
            y = -(ru - body.mid)
            # Never taller than the roof (or rear window) above the backrest.
            hb = back_h
            if s.style != 'roadster':
                ub = ru - 0.30
                roof = float(body.roof_z(np.array(ub), np.array(v)))
                hb = min(hb, roof - 0.16 - 0.15 - (seat_z + 0.10))
            if hb < 0.18:
                continue
            _box(bm, (v, y, seat_z + 0.05), (0.44, 0.48, 0.11), 0)
            back = Matrix.Rotation(math.radians(-14), 3, 'X')
            _box(bm, (v, y + 0.22, seat_z + 0.10 + hb / 2), (0.44, 0.12, hb), 0, back)
            _box(bm, (v, y + 0.22 + hb * 0.24 + 0.02, seat_z + 0.10 + hb + 0.08), (0.24, 0.09, 0.15), 0, back)
    # Dashboard under the windshield, well inside the cabin.
    du0, du1 = s.cowl_u - 0.16, s.cowl_u - 0.58
    dz1 = min(s.belt_z + 0.02, float(body.windshield_line(np.array(du0))) - 0.06)
    dz0 = dz1 - 0.22
    _box(bm, (0.0, -((du0 + du1) / 2 - body.mid), (dz0 + dz1) / 2), (2 * cw - 0.12, du0 - du1, dz1 - dz0), 0)
    bmesh.ops.bevel(bm, geom=list(bm.edges), offset=0.03, segments=3, affect='EDGES')
    out.append(_bm_obj('Seats_LOD0', bm, [mats['Interior']], smooth=False, lod=0))
    # Steering wheel on the driver's side (left), facing the driver.
    sw = bmesh.new()
    bmesh.ops.create_circle(sw, cap_ends=False, radius=0.18, segments=24)
    ring = [v.co.copy() for v in sw.verts]
    sw.free()
    pts = [Vector((p.x, 0, p.y)) for p in ring] + [Vector((ring[0].x, 0, ring[0].y))]
    tilt = Matrix.Rotation(math.radians(-62), 3, 'X')
    centre = Vector((sv, -((s.cowl_u - 0.42) - body.mid), s.belt_z + 0.02))
    P3 = np.array([tuple(centre + tilt @ p) for p in pts])
    if s.style != 'roadster':
        # Under a low, raked screen the rim must stay clear of the glass: find the body's
        # surface above every point of the rim (the screen drops towards its sides).
        above = np.stack([P3[:, 0], P3[:, 1], np.full(len(P3), s.height + 0.3)], axis=1)
        hit, ok = body.surface_along(above, np.array([0.0, 0.0, -1.0]), tmax=s.height + 0.3, steps=160)
        if ok.any():
            P3[:, 2] -= max(0.0, float(np.max(P3[ok, 2] - (hit[ok, 2] - 0.05))))
    V, F = sweep(P3, circle_profile(0.018, 6), ups=np.tile(np.array(tuple(tilt @ Vector((0, 1, 0)))), (len(P3), 1)))
    out.append(_obj('SteeringWheel_LOD0', V, F, [mats['Interior']], lod=0))
    return out


def _box(bm, centre, size, mat, rot=Matrix.Identity(3)):
    sx, sy, sz = (x / 2 for x in size)
    c = Vector(centre)
    vs = [bm.verts.new(c + rot @ Vector((dx * sx, dy * sy, dz * sz))) for dx, dy, dz in ((-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1), (-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1))]
    for q in ((0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)):
        f = bm.faces.new([vs[i] for i in q])
        f.material_index = mat
    return vs


# --- Wipers ----------------------------------------------------------------------------------------
def wipers(body, mats):
    s = body.s
    if s.style == 'roadster' and not s.extra.get('wipers', True):
        return []
    out = []
    zb = s.cowl_z + 0.045
    blades = ((0.30, 0.62), (-0.22, 0.55))
    if s.style == 'mid':
        # A wide screen that wraps back at the sides: one central blade, as on most mid-engined cars.
        blades = ((0.27, 0.54),)
    for v0, length in blades:
        # Where the screen meets the cowl at the middle of the blade...
        vm = v0 - length / 2
        Pm = np.stack([[vm], [-((s.cowl_u + 0.9) - body.mid)], [zb]], axis=1)
        hm, okm = body.surface_along(Pm, np.array([0.0, 1.0, 0.0]), tmax=1.6, steps=60)
        if not okm.all():
            continue
        # ...a blade that is straight seen from above, resting on whatever lies under it
        # (the screen, or the cowl where a wrapped screen curves away behind it).
        xs = np.linspace(v0, v0 - length, 12)
        P0 = np.stack([xs, np.full(12, hm[0, 1] + 0.02), np.full(12, s.height + 0.3)], axis=1)
        hit, ok = body.surface_along(P0, np.array([0.0, 0.0, -1.0]), tmax=s.height + 0.3, steps=160)
        if not ok.all():
            continue
        N = mo.unit(mo.gradient(body.base_blender, hit, 0.002))
        path = hit + N * 0.016
        V, F = sweep(path, rounded_rect_profile(0.018, 0.012, 0.004, 1), ups=N)
        out.append(_obj(f'Wiper{len(out)}_LOD0', V, F, [mats['Gloss']], lod=0))
    return out


def build_parts(body, spec, mats, bb):
    out = []
    out += interior(body, mats, bb)
    out += wipers(body, mats)
    if body.look.get('rear_vents'):
        out += rear_vent_slats(body, spec, mats)
    for name in spec.get('parts', []):
        fn = PARTS.get(name if isinstance(name, str) else name[0])
        if fn is None:
            print('unknown part', name)
            continue
        args = {} if isinstance(name, str) else name[1]
        out += fn(body, spec, mats, **args) or []
    return out


PARTS = {}


# --- Surface helpers ----------------------------------------------------------------------------
def _bl(body, u, v, z):
    """Car frame -> Blender coordinates (arrays)."""
    return np.stack([np.asarray(v, dtype=np.float64) * np.ones_like(np.asarray(u, dtype=np.float64)),
                     -(np.asarray(u, dtype=np.float64) - body.mid) * np.ones_like(np.asarray(v, dtype=np.float64)),
                     np.asarray(z, dtype=np.float64) * np.ones_like(np.asarray(u, dtype=np.float64))], axis=-1)


def _hit(body, P, d, tmax=2.0, steps=80):
    """Surface points along rays from P (Blender) in direction d, with normals."""
    hit, ok = body.surface_along(P, d, tmax=tmax, steps=steps)
    N = mo.unit(mo.gradient(body.base_blender, hit, 0.002))
    return hit, N, ok


def front_path(body, zb, v_frac=0.86, n=33, out=0.04, wrap=0.25):
    """A path across the front of the car at height zb, `out` metres off the surface, wrapping round the corners."""
    s = body.s
    hw = s.width / 2
    vs = np.linspace(-hw * v_frac, hw * v_frac, n)
    P0 = _bl(body, np.full(n, s.u_front + 0.6), vs, np.full(n, zb))
    hit, N, ok = _hit(body, P0, np.array([0.0, 1.0, 0.0]), tmax=1.5, steps=60)
    pts = hit + N * out
    pts = pts[ok]
    if wrap > 0 and len(pts) > 2:
        # Continue round each corner along the side.
        ends = []
        for sign, end in ((-1, pts[0]), (1, pts[-1])):
            us = np.linspace(-(end[1]) + body.mid - 0.08, -(end[1]) + body.mid - 0.08 - wrap, 4)
            P1 = _bl(body, us, np.full(4, sign * (hw + 0.6)), np.full(4, zb))
            h1, n1, ok1 = _hit(body, P1, np.array([-sign, 0.0, 0.0]), tmax=1.0, steps=40)
            ends.append(h1[ok1] + n1[ok1] * out)
        pts = np.concatenate([ends[0][::-1], pts, ends[1]])
    return pts


def rear_path(body, zb, v_frac=0.86, n=33, out=0.04, wrap=0.25):
    s = body.s
    hw = s.width / 2
    vs = np.linspace(hw * v_frac, -hw * v_frac, n)
    P0 = _bl(body, np.full(n, s.u_rear - 0.6), vs, np.full(n, zb))
    hit, N, ok = _hit(body, P0, np.array([0.0, -1.0, 0.0]), tmax=1.5, steps=60)
    pts = hit[ok] + N[ok] * out
    if wrap > 0 and len(pts) > 2:
        ends = []
        for sign, end in ((1, pts[0]), (-1, pts[-1])):
            us = np.linspace(-(end[1]) + body.mid + 0.08, -(end[1]) + body.mid + 0.08 + wrap, 4)
            P1 = _bl(body, us, np.full(4, sign * (hw + 0.6)), np.full(4, zb))
            h1, n1, ok1 = _hit(body, P1, np.array([-sign, 0.0, 0.0]), tmax=1.0, steps=40)
            ends.append(h1[ok1] + n1[ok1] * out)
        pts = np.concatenate([ends[0][::-1], pts, ends[1]])
    return pts


def _smooth(pts, k=2):
    p = np.asarray(pts, dtype=np.float64)
    for _ in range(k):
        q = p.copy()
        q[1:-1] = (p[:-2] + 2 * p[1:-1] + p[2:]) / 4
        p = q
    return p


# --- Bumpers --------------------------------------------------------------------------------------
def bumper(body, spec, mats, end='front', z=None, h=0.09, depth=0.05, out=0.035, mat='Chrome', wrap=0.28, v_frac=0.88):
    s = body.s
    zb = z if z is not None else s.clearance + 0.28
    pts = front_path(body, zb, v_frac, out=out, wrap=wrap) if end == 'front' else rear_path(body, zb, v_frac, out=out, wrap=wrap)
    pts = _smooth(pts, 3)
    out_objs = []
    for lod, (seg, r) in enumerate(((3, 0.02), (1, 0.015))):
        V, F = sweep(pts, rounded_rect_profile(depth, h, min(r, depth / 2 - 0.002), seg))
        out_objs.append(_obj(f'Bumper{end.title()}_LOD{lod}', V, F, [mats[mat]], lod=lod))
    return out_objs


def rear_vent_slats(body, spec, mats, count=3, thick=0.012, depth=0.014, inset=0.014, mat='Gloss', lods=(0, 1)):
    """Horizontal slats across the two rear vents (look 'rear_vents' in features.py), so they read as vents, not holes."""
    s = body.s
    hw = s.width / 2
    zc = s.tail_z - 0.32
    out = []
    for lod in lods:
        Vs, Fs = [], []
        off = 0
        for side in (-1, 1):
            vc = side * (hw - 0.30)
            for k in range(count):
                zb = zc - 0.045 + 0.09 * (k + 0.5) / count
                vs = np.linspace(vc - 0.15, vc + 0.15, 9)
                P0 = _bl(body, np.full(9, s.u_rear - 0.6), vs, np.full(9, zb))
                hit, N, ok = _hit(body, P0, np.array([0.0, -1.0, 0.0]), tmax=1.5, steps=60)
                if ok.sum() < 3:
                    continue
                pts = _smooth(hit[ok] - N[ok] * inset, 1)
                V, F = sweep(pts, rounded_rect_profile(depth, thick, thick / 2 - 0.001, 1))
                Vs.append(V)
                Fs.append(F + off)
                off += len(V)
        if Vs:
            out.append(_obj(f'RearVentSlats_LOD{lod}', np.concatenate(Vs), np.concatenate(Fs), [mats[mat]], lod=lod))
    return out


def grille_bars(body, spec, mats, z0, z1, count, half=0.5, inset=0.012, thick=0.012, depth=0.02, mat='Chrome', lods=(0, 1), vertical=0):
    """Horizontal bars across the grille opening, following the nose (plus `vertical` upright bars: a lattice)."""
    s = body.s
    out = []
    for lod in lods:
        Vs, Fs = [], []
        off = 0
        for k in range(vertical):
            vb = -half + 2 * half * (k + 0.5) / vertical
            zs = np.linspace(z0, z1, 9)
            P0 = _bl(body, np.full(9, s.u_front + 0.6), np.full(9, vb), zs)
            hit, N, ok = _hit(body, P0, np.array([0.0, 1.0, 0.0]), tmax=1.5, steps=60)
            if ok.sum() < 3:
                continue
            pts = _smooth(hit[ok] - N[ok] * (inset + 0.004), 1)
            V, F = sweep(pts, rounded_rect_profile(depth * 0.8, thick * 0.8, thick * 0.4 - 0.001, 1), ups=np.tile([0.0, -1.0, 0.0], (len(pts), 1)))
            Vs.append(V)
            Fs.append(F + off)
            off += len(V)
        for k in range(count):
            zb = z0 + (z1 - z0) * (k + 0.5) / count
            vs = np.linspace(-half, half, 17)
            P0 = _bl(body, np.full(17, s.u_front + 0.6), vs, np.full(17, zb))
            hit, N, ok = _hit(body, P0, np.array([0.0, 1.0, 0.0]), tmax=1.5, steps=60)
            if ok.sum() < 3:
                continue
            pts = _smooth(hit[ok] - N[ok] * inset, 2)
            V, F = sweep(pts, rounded_rect_profile(depth, thick, thick / 2 - 0.001, 1 if lod else 2))
            Vs.append(V)
            Fs.append(F + off)
            off += len(V)
        if Vs:
            out.append(_obj(f'GrilleBars_LOD{lod}', np.concatenate(Vs), np.concatenate(Fs), [mats[mat]], lod=lod))
    return out


# --- Wings and spoilers -----------------------------------------------------------------------------
def airfoil(chord, thick=0.12, camber=0.05, n=14):
    """Closed airfoil outline [(x, y), ...] (x along the chord, leading edge at 0), cambered down (downforce)."""
    xs = 0.5 * (1 - np.cos(np.linspace(0, np.pi, n)))
    yt = 5 * thick * (0.2969 * np.sqrt(xs) - 0.126 * xs - 0.3516 * xs ** 2 + 0.2843 * xs ** 3 - 0.1036 * xs ** 4)
    yc = -camber * 4 * xs * (1 - xs)
    upper = [(x * chord, (c + t) * chord) for x, c, t in zip(xs, yc, yt)]
    lower = [(x * chord, (c - t) * chord) for x, c, t in zip(xs[::-1], yc[::-1], yt[::-1])]
    return upper + lower[1:-1]


def wing(body, spec, mats, u_from_tail=0.20, height=0.25, span=None, chord=0.26, angle=8.0, mat='Carbon', uprights='pedestal', post_v=0.42, plates=True, plate_h=0.16, gurney=True):
    """A rear wing on two uprights standing on the deck or tailgate."""
    s = body.s
    span = span or s.width - 0.16
    uc = s.u_rear + u_from_tail
    # Deck height under the wing centre.
    P0 = _bl(body, np.array([uc, uc]), np.array([post_v, -post_v]), np.array([s.height + 0.5] * 2))
    hit, N, ok = _hit(body, P0, np.array([0.0, 0.0, -1.0]), tmax=s.height + 0.5, steps=80)
    z_deck = float(hit[ok][:, 2].max()) if ok.any() else s.deck_z
    zw = z_deck + height
    prof = airfoil(chord)
    a = math.radians(angle)
    ca, sa = math.cos(a), math.sin(a)
    # Profile in (u, z): leading edge forward (+u), chord running back; pitched nose-down by `angle`.
    pts2 = [(-(x - chord / 2) * ca + y * sa * 0, y + (x - chord / 2) * sa) for x, y in prof]
    out = []
    for lod in (0, 1, 2):
        Vs, Fs = [], []
        # Main plane: the profile extruded across the car.
        nv = len(pts2)
        V = []
        for x in (-span / 2, span / 2):
            for pu, pz in pts2:
                V.append((x, -(uc + pu - body.mid), zw + pz))
        V = np.array(V)
        F = []
        for j in range(nv):
            a0, b0 = j, (j + 1) % nv
            F += [(a0, b0, nv + b0), (a0, nv + b0, nv + a0)]
        # Caps.
        for j in range(1, nv - 1):
            F += [(0, j + 1, j), (nv, nv + j, nv + j + 1)]
        V, F = np.array(V), np.array(F, dtype=np.int64)
        Vs.append(V)
        Fs.append(F)
        if plates:
            for side in (-1, 1):
                x = side * (span / 2 + 0.004)
                c = (x, -(uc - body.mid), zw - plate_h * 0.35)
                bm = bmesh.new()
                _box(bm, c, (0.008, chord * 1.35, plate_h), 0)
                me = bpy.data.meshes.new('tmp')
                bm.to_mesh(me)
                bm.free()
                pv = np.array([v.co[:] for v in me.vertices])
                pf = np.array([list(p.vertices) for p in me.polygons])
                bpy.data.meshes.remove(me)
                tf = np.concatenate([pf[:, [0, 1, 2]], pf[:, [0, 2, 3]]])
                Vs.append(pv)
                Fs.append(tf)
        if uprights != 'none':
            for side in (-1, 1):
                x = side * post_v
                top = zw - 0.01
                bot = z_deck - 0.02
                bm = bmesh.new()
                _box(bm, (x, -(uc - body.mid) + 0.02, (top + bot) / 2), (0.012, chord * 0.55, top - bot), 0)
                me = bpy.data.meshes.new('tmp')
                bm.to_mesh(me)
                bm.free()
                pv = np.array([v.co[:] for v in me.vertices])
                pf = np.array([list(p.vertices) for p in me.polygons])
                bpy.data.meshes.remove(me)
                tf = np.concatenate([pf[:, [0, 1, 2]], pf[:, [0, 2, 3]]])
                Vs.append(pv)
                Fs.append(tf)
        V, F, _ = mo.merge(list(zip(Vs, Fs)))
        assert F.max() < len(V)
        out.append(_obj(f'Wing_LOD{lod}', V, F, [mats[mat]], smooth=False, lod=lod))
    return out


def roof_spoiler(body, spec, mats, chord=0.22, drop=12.0, mat='Paint', span_in=0.10):
    """A lip off the back of a hatch roof."""
    s = body.s
    u0 = body.roof_rear_u + 0.02
    cw = float(body.cabin_half_width(np.array(u0), np.array(s.height - 0.05)))
    span = 2 * (cw - span_in)
    P0 = _bl(body, np.array([u0]), np.array([0.0]), np.array([s.height + 0.4]))
    hit, N, ok = _hit(body, P0, np.array([0.0, 0.0, -1.0]), tmax=1.0, steps=40)
    zr = float(hit[0, 2]) if ok[0] else s.height
    a = math.radians(drop)
    prof = [(0.0, 0.0), (-chord, -chord * math.sin(a) + 0.01), (-chord, -chord * math.sin(a) - 0.008), (0.0, -0.035)]
    out = []
    for lod in (0, 1):
        V = []
        for x in (-span / 2, span / 2):
            for pu, pz in prof:
                V.append((x, -(u0 + pu - body.mid), zr + pz - 0.004))
        nv = len(prof)
        F = []
        for j in range(nv):
            a0, b0 = j, (j + 1) % nv
            F += [(a0, b0, nv + b0), (a0, nv + b0, nv + a0)]
        F += [(0, 2, 1), (0, 3, 2), (nv, nv + 1, nv + 2), (nv, nv + 2, nv + 3)]
        out.append(_obj(f'RoofSpoiler_LOD{lod}', np.array(V), np.array(F, dtype=np.int64), [mats[mat]], smooth=False, lod=lod))
    return out


# --- Roof gear ---------------------------------------------------------------------------------------
def roof_rails(body, spec, mats, inset=0.10, lift=0.045, mat='Gloss'):
    s = body.s
    u0 = body.roof_front_u - 0.12
    u1 = body.roof_rear_u + (0.0 if s.squareback else 0.1)
    us = np.linspace(u0, u1, 16)
    out = []
    Vs, Fs = [], []
    for side in (-1, 1):
        cw = body.cabin_half_width(us, np.full(16, s.height - 0.04))
        P0 = _bl(body, us, side * (cw - inset), np.full(16, s.height + 0.5))
        hit, N, ok = _hit(body, P0, np.array([0.0, 0.0, -1.0]), tmax=1.0, steps=40)
        pts = hit[ok] + np.array([0, 0, lift])
        if len(pts) < 3:
            continue
        pts = _smooth(pts, 2)
        V, F = sweep(pts, rounded_rect_profile(0.03, 0.024, 0.01, 2))
        Vs.append(V)
        Fs.append(F)
        # Feet at both ends.
        for p in (pts[0], pts[-1]):
            bm = bmesh.new()
            _box(bm, (p[0], p[1], p[2] - lift / 2), (0.032, 0.07, lift), 0)
            me = bpy.data.meshes.new('tmp')
            bm.to_mesh(me)
            bm.free()
            pv = np.array([v.co[:] for v in me.vertices])
            pf = np.array([list(q.vertices) for q in me.polygons])
            bpy.data.meshes.remove(me)
            Vs.append(pv)
            Fs.append(np.concatenate([pf[:, [0, 1, 2]], pf[:, [0, 2, 3]]]))
    if Vs:
        V, F, _ = mo.merge(list(zip(Vs, Fs)))
        out.append(_obj('RoofRails_LOD0', V, F, [mats[mat]], lod=0))
        out.append(_obj('RoofRails_LOD1', V, F, [mats[mat]], lod=1))
    return out


def _tube_mesh(paths, r=0.02, seg=8):
    Vs, Fs = [], []
    for p in paths:
        V, F = sweep(np.asarray(p, dtype=np.float64), circle_profile(r, seg))
        Vs.append(V)
        Fs.append(F)
    V, F, _ = mo.merge(list(zip(Vs, Fs)))
    return V, F


def roof_rack(body, spec, mats, lift=0.09, mat='Trim'):
    s = body.s
    u0 = body.roof_front_u - 0.10
    u1 = body.roof_rear_u + 0.02
    cw = float(body.cabin_half_width(np.array((u0 + u1) / 2), np.array(s.height - 0.05))) - 0.06
    zt = s.height + lift
    Y = lambda u: -(u - body.mid)
    paths = [
        [(-cw, Y(u0), zt), (cw, Y(u0), zt)],
        [(-cw, Y(u1), zt), (cw, Y(u1), zt)],
        [(cw, Y(u0), zt), (cw, Y(u1), zt)],
        [(-cw, Y(u0), zt), (-cw, Y(u1), zt)],
    ]
    for k in range(1, 5):
        u = u0 + (u1 - u0) * k / 5
        paths.append([(-cw, Y(u), zt), (cw, Y(u), zt)])
    for u in (u0 + 0.05, u1 - 0.05):
        for side in (-1, 1):
            paths.append([(side * cw, Y(u), zt), (side * (cw + 0.02), Y(u), s.height - 0.06)])
    out = []
    for lod, seg in ((0, 8), (1, 4)):
        V, F = _tube_mesh([np.array(p) for p in paths], 0.016, seg)
        out.append(_obj(f'RoofRack_LOD{lod}', V, F, [mats[mat]], lod=lod))
    return out


def bull_bar(body, spec, mats, mat='Trim', z0=None, z1=None, half=None):
    s = body.s
    z0 = z0 if z0 is not None else s.clearance + 0.20
    z1 = z1 if z1 is not None else s.hood_front_z - 0.02
    half = half or s.width * 0.28
    vs = np.array([-half, -half, half, half])
    zs = np.array([z0, z1, z1, z0])
    P0 = _bl(body, np.full(4, s.u_front + 0.6), vs, zs)
    hit, N, ok = _hit(body, P0, np.array([0.0, 1.0, 0.0]), tmax=1.5, steps=60)
    yf = float(hit[ok][:, 1].min()) - 0.12 if ok.any() else -(s.u_front - body.mid) - 0.12
    loop = [(-half, yf, z0), (-half, yf, z1 - 0.06), (-half + 0.06, yf - 0.01, z1), (half - 0.06, yf - 0.01, z1), (half, yf, z1 - 0.06), (half, yf, z0)]
    cross = [(-half, yf, (z0 + z1) / 2), (half, yf, (z0 + z1) / 2)]
    inner = [(-half * 0.45, yf - 0.005, z0 + 0.02), (-half * 0.45, yf - 0.005, z1 + 0.01)], [(half * 0.45, yf - 0.005, z0 + 0.02), (half * 0.45, yf - 0.005, z1 + 0.01)]
    out = []
    for lod, seg in ((0, 12), (1, 6)):
        paths = [_resample(np.array(loop), 14), np.array(cross)] + [np.array(p) for p in inner]
        V, F = _tube_mesh(paths, 0.03, seg)
        out.append(_obj(f'BullBar_LOD{lod}', V, F, [mats[mat]], lod=lod))
    return out


def _resample(pts, n):
    p = np.asarray(pts, dtype=np.float64)
    d = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(p, axis=0), axis=1))])
    t = np.linspace(0, d[-1], n)
    return np.stack([np.interp(t, d, p[:, k]) for k in range(3)], axis=1)


def side_steps(body, spec, mats, mat='Trim', z=None, out_v=0.02):
    s = body.s
    z = z if z is not None else s.clearance + 0.06
    u0 = s.axle_f - s.tire_r - s.arch_gap - 0.06
    u1 = s.axle_r + s.tire_r + s.arch_gap + 0.06
    objs = []
    for lod in (0, 1):
        Vs, Fs = [], []
        for side in (-1, 1):
            hw = float(body.half_width(np.array((u0 + u1) / 2), np.array(s.clearance + 0.12)))
            x = side * (hw + out_v)
            pts = np.array([(x, -(u - body.mid), z) for u in np.linspace(u0, u1, 6)])
            V, F = sweep(pts, rounded_rect_profile(0.13, 0.035, 0.012, 2 if lod == 0 else 1))
            Vs.append(V)
            Fs.append(F)
        V, F, _ = mo.merge(list(zip(Vs, Fs)))
        objs.append(_obj(f'SideSteps_LOD{lod}', V, F, [mats[mat]], lod=lod))
    return objs


def spare_wheel(body, spec, mats, z=None):
    from .wheels import wheel_bmesh
    s = body.s
    z = z if z is not None else s.tail_z - 0.30
    P0 = _bl(body, np.array([s.u_rear - 0.6]), np.array([0.0]), np.array([z]))
    hit, N, ok = _hit(body, P0, np.array([0.0, -1.0, 0.0]), tmax=1.0, steps=40)
    y = float(hit[0, 1]) if ok[0] else -(s.u_rear - body.mid)
    w = s.tire_w_r
    out = []
    for lod in (0, 1, 2):
        bm = wheel_bmesh(s.tire_r, w, {**spec, 'calipers': False}, outward=1, lod=min(2, lod + 1))
        # Axle along X -> along +Y (pointing back), outboard face looking back.
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(-90), 3, 'Z'))
        bmesh.ops.translate(bm, verts=bm.verts, vec=Vector((0.0, y + w / 2 + 0.03, z)))
        ob = _bm_obj(f'Spare_LOD{lod}', bm, [mats['Rubber'], mats['Rim'], mats['Brake'], mats['Dark']], lod=lod)
        out.append(ob)
    return out


def mud_flaps(body, spec, mats, mat='Accent', h=0.30, w=0.26):
    s = body.s
    objs = []
    for lod in (0, 1):
        Vs, Fs = [], []
        for axle, track in ((s.axle_f, s.track_f), (s.axle_r, s.track_r)):
            for side in (-1, 1):
                u = axle - s.tire_r - 0.07
                x = side * track / 2
                bm = bmesh.new()
                _box(bm, (x, -(u - body.mid), 0.06 + h / 2), (w, 0.012, h), 0)
                me = bpy.data.meshes.new('tmp')
                bm.to_mesh(me)
                bm.free()
                pv = np.array([v.co[:] for v in me.vertices])
                pf = np.array([list(q.vertices) for q in me.polygons])
                bpy.data.meshes.remove(me)
                Vs.append(pv)
                Fs.append(np.concatenate([pf[:, [0, 1, 2]], pf[:, [0, 2, 3]]]))
        V, F, _ = mo.merge(list(zip(Vs, Fs)))
        objs.append(_obj(f'MudFlaps_LOD{lod}', V, F, [mats[mat]], smooth=False, lod=lod))
    return objs


def snorkel(body, spec, mats, mat='Trim'):
    s = body.s
    side = -1  # right side
    u_base = s.axle_f - 0.05
    hw = float(body.half_width(np.array(u_base), np.array(s.shoulder_z)))
    pts = [
        (side * (hw + 0.04), -(u_base - body.mid), s.shoulder_z - 0.05),
        (side * (hw + 0.05), -(u_base - 0.25 - body.mid), s.belt_z),
    ]
    # Up the A-pillar.
    for f in np.linspace(0, 1, 5):
        z = s.belt_z + f * (s.height - 0.05 - s.belt_z)
        u = float(s.cowl_u - (z - s.cowl_z) / s.windshield_slope) + 0.02
        cw = float(body.cabin_half_width(np.array(u), np.array(z)))
        pts.append((side * (cw + 0.07), -(u - body.mid), z))
    pts.append((side * (pts[-1][0] * side), pts[-1][1] - 0.12, pts[-1][2] + 0.02))
    out = []
    for lod, seg in ((0, 12), (1, 6)):
        V, F = _tube_mesh([_resample(np.array(pts), 16)], 0.045, seg)
        out.append(_obj(f'Snorkel_LOD{lod}', V, F, [mats[mat]], lod=lod))
    return out


def roadster_screen(body, spec, mats, top=None, frame='Chrome'):
    """A roadster's windshield: a bowed glass panel with a frame."""
    s = body.s
    top = top or s.height
    rows, cols = 6, 15
    base_hw = float(body.plan_width(np.array(s.cowl_u))) - 0.12
    V = []
    for i in range(rows):
        f = i / (rows - 1)
        z = s.cowl_z - 0.02 + f * (top - s.cowl_z + 0.02)
        u = s.cowl_u - (z - s.cowl_z) / s.windshield_slope
        hwk = base_hw - 0.05 * f
        for j in range(cols):
            g = j / (cols - 1) * 2 - 1
            v = g * hwk
            uu = u - 0.10 * g * g
            V.append((v, -(uu - body.mid), z))
    V = np.array(V)
    F = []
    for i in range(rows - 1):
        for j in range(cols - 1):
            a = i * cols + j
            F += [(a, a + 1, a + cols + 1), (a, a + cols + 1, a + cols)]
    F = np.array(F, dtype=np.int64)
    # Double-sided: a second copy with flipped faces, 6 mm behind.
    V2 = V + np.array([0, 0.006, 0])
    VV = np.concatenate([V, V2])
    FF = np.concatenate([F, F[:, ::-1] + len(V)])
    out = [_obj('Screen_LOD0', VV, FF, [mats['Glass']], lod=0), _obj('Screen_LOD1', VV, FF, [mats['Glass']], lod=1)]
    # Frame round the top and sides.
    edge = [V[i * cols] for i in range(rows)][::-1] + [V[(rows - 1) * cols + j] for j in range(cols)] + [V[i * cols + cols - 1] for i in range(rows)][::-1][::-1]
    edge = np.array([V[i * cols] for i in range(rows - 1, -1, -1)][::-1])
    left = np.array([V[i * cols] for i in range(rows)])
    topl = np.array([V[(rows - 1) * cols + j] for j in range(cols)])
    right = np.array([V[i * cols + cols - 1] for i in range(rows)])[::-1]
    loop = np.concatenate([left, topl[1:], right[1:]])
    for lod, seg in ((0, 8), (1, 4)):
        Vf, Ff = sweep(loop + np.array([0, 0.003, 0]), circle_profile(0.011, seg))
        out.append(_obj(f'ScreenFrame_LOD{lod}', Vf, Ff, [mats[frame]], lod=lod))
    return out


def badge(body, spec, mats, mat='Chrome', r=0.035, front_z=None, rear_z=None):
    s = body.s
    out = []
    pts = []
    if front_z is not None:
        P0 = _bl(body, np.array([s.u_front + 0.6]), np.array([0.0]), np.array([front_z]))
        h, n, ok = _hit(body, P0, np.array([0.0, 1.0, 0.0]), tmax=1.5, steps=60)
        if ok[0]:
            pts.append((h[0], n[0]))
    if rear_z is not None:
        P0 = _bl(body, np.array([s.u_rear - 0.6]), np.array([0.0]), np.array([rear_z]))
        h, n, ok = _hit(body, P0, np.array([0.0, -1.0, 0.0]), tmax=1.5, steps=60)
        if ok[0]:
            pts.append((h[0], n[0]))
    Vs, Fs = [], []
    for p, nrm in pts:
        a = p - nrm * 0.004
        b = p + nrm * 0.006
        V, F = sweep(np.array([a, b]), circle_profile(r, 20), ups=np.tile(np.cross(nrm, [1, 0, 0]) if abs(nrm[0]) < 0.9 else [0, 0, 1], (2, 1)))
        Vs.append(V)
        Fs.append(F)
    if Vs:
        V, F, _ = mo.merge(list(zip(Vs, Fs)))
        out.append(_obj('Badges_LOD0', V, F, [mats[mat]], smooth=False, lod=0))
    return out


def rear_wiper(body, spec, mats):
    s = body.s
    z = s.deck_z + 0.08
    P0 = _bl(body, np.array([s.u_rear - 0.6]), np.array([0.0]), np.array([z]))
    h, n, ok = _hit(body, P0, np.array([0.0, -1.0, 0.0]), tmax=1.5, steps=60)
    if not ok[0]:
        return []
    p0 = h[0] + n[0] * 0.015
    pts = [p0 + np.array([-0.45 * t, 0.0, 0.12 * t]) for t in np.linspace(0, 1, 6)]
    hit2, n2, ok2 = _hit(body, np.array(pts) + np.array([0, 0.6, 0]), np.array([0.0, -1.0, 0.0]), tmax=1.5, steps=60)
    pts = hit2[ok2] + n2[ok2] * 0.015
    if len(pts) < 2:
        return []
    V, F = sweep(pts, rounded_rect_profile(0.016, 0.011, 0.004, 1), ups=n2[ok2])
    return [_obj('RearWiper_LOD0', V, F, [mats['Gloss']], lod=0)]


PARTS.update({
    'bumper': bumper,
    'grille_bars': grille_bars,
    'wing': wing,
    'roof_spoiler': roof_spoiler,
    'roof_rails': roof_rails,
    'roof_rack': roof_rack,
    'bull_bar': bull_bar,
    'side_steps': side_steps,
    'spare_wheel': spare_wheel,
    'mud_flaps': mud_flaps,
    'snorkel': snorkel,
    'roadster_screen': roadster_screen,
    'badge': badge,
    'rear_wiper': rear_wiper,
})
