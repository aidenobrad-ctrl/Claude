"""Wheels: tyre (lathed profile with tread grooves), rim with a style
(split spokes, five spokes, multi-spoke, mesh, steel, aero disc, beadlock),
lug nuts and centre cap, brake disc, and a calliper that steers but does not
spin. Axle along Blender X; the outboard face looks +X on the left side of
the car (+X) and -X on the right.

Each wheel is an empty `Wheel_FL` (pivot at the hub) holding LOD meshes
`Wheel_FL_LOD0..2`; each calliper is `Caliper_FL` (one mesh, all LODs).
"""
import math

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

# Segments around the wheel per LOD: tyre, barrel, disc.
SEGMENTS = ((72, 64, 48), (36, 28, 20), (16, 12, 0))


def lathe(bm, profile, segments, mat=0, a0=0.0, a1=2 * math.pi, closed=True, smooth=True):
    """Revolve [(axial, radius), ...] around X. Returns the new faces."""
    rings = []
    n = segments if closed else segments + 1
    for k in range(n):
        th = a0 + (a1 - a0) * k / segments
        c, s = math.cos(th), math.sin(th)
        rings.append([bm.verts.new((a, r * c, r * s)) for a, r in profile])
    faces = []
    for k in range(segments):
        r0 = rings[k]
        r1 = rings[(k + 1) % n] if closed else rings[k + 1]
        for j in range(len(profile) - 1):
            if profile[j][1] == 0 and profile[j + 1][1] == 0:
                continue
            try:
                f = bm.faces.new((r0[j], r1[j], r1[j + 1], r0[j + 1]))
            except ValueError:
                continue
            f.material_index = mat
            f.smooth = smooth
            faces.append(f)
    return faces


def tyre_profile(radius, width, rim_r, lod=0):
    hw = width / 2
    side = radius - rim_r
    pts = [(-hw + 0.012, rim_r + 0.004), (-hw - 0.004, rim_r + side * 0.45), (-hw + 0.004, radius - 0.022), (-hw + 0.02, radius - 0.004)]
    tread = [(-hw + 0.03, radius)]
    grooves = [-0.45, 0.0, 0.45] if lod == 0 else []
    for g in grooves:
        a = g * (hw - 0.03)
        tread += [(a - 0.006, radius), (a - 0.004, radius - 0.007), (a + 0.004, radius - 0.007), (a + 0.006, radius)]
    tread.append((hw - 0.03, radius))
    if lod == 2:
        pts = [(-hw + 0.01, rim_r), (-hw, rim_r + side * 0.5), (-hw + 0.02, radius - 0.003)]
    right = [(-a, r) for a, r in reversed(pts)]
    return pts + tread + right


def _spoke(bm, th0, twist, r0, r1, a0, a1, w0, w1, t0, t1, steps, mat):
    prev = None
    for t in range(steps + 1):
        f = t / steps
        r = r0 + (r1 - r0) * f
        a = a0 + (a1 - a0) * math.sqrt(f)
        w = w0 + (w1 - w0) * f
        thick = t0 + (t1 - t0) * f
        th = th0 + twist * f
        c, s = math.cos(th), math.sin(th)
        radial = Vector((0, c, s))
        tang = Vector((0, -s, c))
        ax = Vector((1, 0, 0))
        centre = ax * a + radial * r
        ring = [bm.verts.new(centre + tang * (w / 2) * sx + ax * (thick / 2) * sy) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
        if prev:
            for j in range(4):
                fc = bm.faces.new((prev[j], prev[(j + 1) % 4], ring[(j + 1) % 4], ring[j]))
                fc.material_index = mat
                fc.smooth = True
        else:
            fc = bm.faces.new(list(reversed(ring)))
            fc.material_index = mat
        prev = ring
    fc = bm.faces.new(prev)
    fc.material_index = mat


def wheel_bmesh(radius, width, spec, outward=1, lod=0):
    """Materials: 0 rubber, 1 rim, 2 brake disc, 3 dark (rim barrel inside)."""
    seg_t, seg_b, seg_d = SEGMENTS[lod]
    rim_r = radius - max(0.075, radius * spec.get('sidewall', 0.27))
    bm = bmesh.new()
    lathe(bm, tyre_profile(radius, width, rim_r, lod), seg_t, mat=0)
    face_a = width / 2 - 0.018
    barrel = [(face_a + 0.006, rim_r + 0.012), (face_a + 0.01, rim_r - 0.004), (face_a - 0.004, rim_r - 0.016), (-width / 2 + 0.02, rim_r - 0.02), (-width / 2 + 0.005, rim_r - 0.008)]
    lathe(bm, barrel, seg_b, mat=1)
    style = spec.get('rim_style', 'split')
    hub_r = 0.068
    hub_a = face_a - spec.get('dish', 0.055)
    r1 = rim_r - 0.012
    if lod == 2 or style in ('steel', 'aero'):
        # A face disc: dished steel, aero cover, or the far-LOD stand-in for spokes.
        if style == 'steel':
            prof = [(hub_a + 0.03, 0.0), (hub_a + 0.03, 0.09), (hub_a + 0.012, 0.11), (hub_a + 0.004, r1 * 0.72), (face_a - 0.02, r1 * 0.80), (face_a - 0.006, r1), (face_a - 0.03, r1)]
        elif style == 'aero':
            prof = [(face_a - 0.012, 0.0), (face_a - 0.014, 0.05), (face_a - 0.022, r1 * 0.85), (face_a - 0.010, r1), (face_a - 0.04, r1)]
        else:
            prof = [(hub_a + 0.02, 0.0), (hub_a + 0.016, hub_r), (face_a - 0.03, r1 * 0.9), (face_a - 0.008, r1), (face_a - 0.04, r1)]
        lathe(bm, prof, max(seg_b, 12), mat=1 if style != 'dark' else 3)
        if style in ('steel', 'aero') and lod < 2:
            # Cut-outs read as dark slots: small dark patches on the face.
            n_h = spec.get('spokes', 6 if style == 'steel' else 5)
            for k in range(n_h):
                th = 2 * math.pi * k / n_h
                rr = r1 * (0.55 if style == 'steel' else 0.62)
                prof_h = [(face_a - 0.02 + (0.008 if style == 'aero' else -0.006) + 0.002, 0.0), (face_a - 0.02 + 0.002, 0.026 if style == 'steel' else 0.03)]
                fs = lathe(bm, [(prof_h[0][0], 0.0), (prof_h[0][0], prof_h[1][1])], 10, mat=3)
                vs = {v for f in fs for v in f.verts}
                bmesh.ops.translate(bm, verts=list(vs), vec=Vector((0.0, rr * math.cos(th), rr * math.sin(th))))
    else:
        n = spec.get('spokes', 10)
        steps = 4 if lod == 0 else 2
        if style == 'split':
            for k in range(n):
                th = 2 * math.pi * k / n
                _spoke(bm, th, 0.0, hub_r - 0.005, r1, hub_a, face_a - 0.006, 0.034, 0.020, 0.022, 0.016, steps, 1)
        elif style == 'five':
            for k in range(n):
                th = 2 * math.pi * k / n
                _spoke(bm, th, 0.0, hub_r - 0.005, r1, hub_a, face_a - 0.006, 0.075, 0.05, 0.03, 0.022, steps, 1)
        elif style == 'multi':
            for k in range(n):
                th = 2 * math.pi * k / n
                _spoke(bm, th, 0.18, hub_r - 0.01, r1, hub_a, face_a - 0.006, 0.016, 0.010, 0.012, 0.010, steps, 1)
        elif style == 'mesh':
            for k in range(n):
                th = 2 * math.pi * k / n
                for tw in (0.32, -0.32):
                    _spoke(bm, th, tw, hub_r - 0.005, r1, hub_a, face_a - 0.008, 0.016, 0.012, 0.016, 0.012, steps, 1)
        elif style == 'beadlock':
            for k in range(n):
                th = 2 * math.pi * k / n
                _spoke(bm, th, 0.0, hub_r - 0.005, r1 * 0.82, hub_a, face_a - 0.02, 0.06, 0.07, 0.03, 0.03, steps, 1)
            # The beadlock ring with bolts.
            lathe(bm, [(face_a - 0.01, r1 * 0.80), (face_a + 0.012, r1 * 0.82), (face_a + 0.014, r1 + 0.01), (face_a - 0.01, r1 + 0.012)], seg_b, mat=1)
            if lod == 0:
                for k in range(16):
                    th = 2 * math.pi * k / 16
                    fs = lathe(bm, [(face_a + 0.012, 0.0055), (face_a + 0.02, 0.0055), (face_a + 0.022, 0.0)], 6, mat=1)
                    vs = {v for f in fs for v in f.verts}
                    rr = (r1 * 0.82 + r1 + 0.01) / 2
                    bmesh.ops.translate(bm, verts=list(vs), vec=Vector((0, rr * math.cos(th), rr * math.sin(th))))
        # Dark barrel inside so the gaps between spokes read as depth.
        lathe(bm, [(hub_a - 0.06, r1 * 0.98), (hub_a - 0.06, hub_r + 0.01)], seg_b, mat=3)
    if lod < 2:
        # Hub, centre cap and lug nuts.
        lathe(bm, [(hub_a - 0.03, hub_r), (hub_a + 0.012, hub_r), (hub_a + 0.016, hub_r - 0.01), (hub_a + 0.02, 0.03), (hub_a + 0.024, 0.0)], 48 if lod == 0 else 16, mat=1)
    if lod == 0 and style not in ('aero',):
        n_lug = spec.get('lugs', 5)
        for k in range(n_lug):
            th = 2 * math.pi * k / n_lug + 0.3
            c, s = math.cos(th), math.sin(th)
            nut = [(hub_a + 0.012, 0.0095), (hub_a + 0.03, 0.0095), (hub_a + 0.034, 0.006), (hub_a + 0.035, 0.0)]
            fs = lathe(bm, nut, 6, mat=1)
            vs = {v for f in fs for v in f.verts}
            bmesh.ops.translate(bm, verts=list(vs), vec=Vector((0, 0.045 * c, 0.045 * s)))
    if seg_d and style not in ('aero', 'steel'):
        disc_r = radius * spec.get('disc', 0.53)
        disc_a = face_a - 0.11
        lathe(bm, [(disc_a - 0.014, 0.085), (disc_a - 0.014, disc_r), (disc_a + 0.014, disc_r), (disc_a + 0.014, 0.085)], seg_d, mat=2)
    if outward < 0:
        bmesh.ops.scale(bm, vec=(-1, 1, 1), verts=bm.verts)
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    return bm


def wheel_object(name, radius, width, spec, mats, outward=1, lod=0):
    bm = wheel_bmesh(radius, width, spec, outward, lod)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in (mats['Rubber'], mats['Rim'], mats['Brake'], mats['Dark']):
        me.materials.append(m)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def caliper_mesh(name, radius, width, spec, mats, outward=1, front=True):
    bm = bmesh.new()
    face_a = width / 2 - 0.018
    disc_a = face_a - 0.11
    disc_r = radius * spec.get('disc', 0.53)
    prof = [(disc_a - 0.035, disc_r - 0.06), (disc_a + 0.035, disc_r - 0.06), (disc_a + 0.04, disc_r + 0.012), (disc_a - 0.035, disc_r + 0.012)]
    span = math.radians(48 if front else 40)
    centre = math.radians(118)
    lathe(bm, prof + [prof[0]], 10, mat=0, a0=centre - span / 2, a1=centre + span / 2, closed=False)
    if outward < 0:
        bmesh.ops.scale(bm, vec=(-1, 1, 1), verts=bm.verts)
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(mats['Caliper'])
    for p in me.polygons:
        p.use_smooth = True
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def build_wheels(shape, spec, mats, mid):
    out = []
    for front in (True, False):
        axle = shape.axle_f if front else shape.axle_r
        track = shape.track_f if front else shape.track_r
        width = shape.tire_w_f if front else shape.tire_w_r
        for side, tag in ((1, 'L'), (-1, 'R')):
            key = ('F' if front else 'R') + tag
            loc = Vector((side * track / 2, -(axle - mid), shape.tire_r))
            pivot = bpy.data.objects.new(f'Wheel_{key}', None)
            bpy.context.scene.collection.objects.link(pivot)
            pivot.location = loc
            for lod in range(3):
                w = wheel_object(f'Wheel_{key}_LOD{lod}', shape.tire_r, width, spec, mats, outward=side, lod=lod)
                w.parent = pivot
                w.hide_render = lod > 0
            if spec.get('calipers', True) and spec.get('rim_style', 'split') not in ('steel', 'aero'):
                c = caliper_mesh(f'Caliper_{key}', shape.tire_r, width, spec, mats, outward=side, front=front)
                c.location = loc
                out.append(c)
            out.append(pivot)
    return out


def build_exhausts(shape, spec, mats, body, mid):
    """Tailpipes poking out of the rear (count and layout from the spec)."""
    n = spec.get('exhausts', 2)
    if n <= 0:
        return None
    bm = bmesh.new()
    z = shape.clearance + spec.get('exhaust_z', 0.13)
    layouts = {
        1: [spec.get('exhaust_v', 0.42)],
        2: spec.get('exhaust_vs', [-0.42, 0.42]),
        4: [-0.48, -0.36, 0.36, 0.48],
    }
    xs = spec['exhaust_vs'] if 'exhaust_vs' in spec else layouts.get(n, [-0.42, 0.42])
    r = spec.get('exhaust_r', 0.04)
    for x in xs:
        u = shape.u_rear + 0.5
        while u > shape.u_rear - 0.3 and body.base(np.array([u]), np.array([x]), np.array([z]))[0] < 0:
            u -= 0.005
        prof = [(-0.07, r - 0.011), (0.0, r - 0.011), (0.003, r - 0.006), (0.0, r), (-0.012, r + 0.002), (-0.14, r + 0.002)]
        faces = lathe(bm, prof, 24, mat=0)
        vs = {vv for f in faces for vv in f.verts}
        rot = Matrix.Rotation(math.radians(90), 3, 'Z')
        for vv in vs:
            vv.co = rot @ vv.co
        bmesh.ops.translate(bm, verts=list(vs), vec=Vector((x, -(u - mid) + 0.03, z)))
    me = bpy.data.meshes.new('Exhaust')
    bm.to_mesh(me)
    bm.free()
    me.materials.append(mats['Chrome'])
    for p in me.polygons:
        p.use_smooth = True
    ob = bpy.data.objects.new('Exhaust', me)
    bpy.context.scene.collection.objects.link(ob)
    return ob
