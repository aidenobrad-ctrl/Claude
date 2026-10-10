"""Wheels: tyre (lathed profile with tread grooves), multi-spoke rim with a
concave face, lug nuts and centre cap, brake disc, and a calliper that steers
but does not spin. Axle along Blender X; the outboard face looks +X on the
left side of the car (+X) and -X on the right."""
import math

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector


def lathe(bm, profile, segments, mat=0, a0=0.0, a1=2 * math.pi, closed=True):
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
            f = bm.faces.new((r0[j], r1[j], r1[j + 1], r0[j + 1]))
            f.material_index = mat
            f.smooth = True
            faces.append(f)
    return faces


def box(bm, center, size, rot=Matrix.Identity(3), mat=0):
    """Axis box of size (sx, sy, sz) rotated by rot about its centre."""
    sx, sy, sz = (x / 2 for x in size)
    c = Vector(center)
    vs = []
    for dx, dy, dz in ((-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1), (-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)):
        vs.append(bm.verts.new(c + rot @ Vector((dx * sx, dy * sy, dz * sz))))
    quads = ((0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7))
    for q in quads:
        f = bm.faces.new([vs[i] for i in q])
        f.material_index = mat
    return vs


def tyre_profile(radius, width, rim_r):
    hw = width / 2
    side = radius - rim_r
    pts = [(-hw + 0.012, rim_r + 0.004), (-hw - 0.004, rim_r + side * 0.45), (-hw + 0.004, radius - 0.022), (-hw + 0.02, radius - 0.004)]
    # Tread with four circumferential grooves.
    grooves = [-0.62, -0.22, 0.22, 0.62]
    tread = [(-hw + 0.03, radius)]
    for g in grooves:
        a = g * (hw - 0.03)
        tread += [(a - 0.006, radius), (a - 0.004, radius - 0.007), (a + 0.004, radius - 0.007), (a + 0.006, radius)]
    tread.append((hw - 0.03, radius))
    right = [(-a, r) for a, r in reversed(pts)]
    return pts + tread + right


def wheel_mesh(name, radius, width, spec, mats, outward=1):
    rim_r = radius - max(0.075, radius * 0.27)
    bm = bmesh.new()
    lathe(bm, tyre_profile(radius, width, rim_r), 96, mat=0)
    # Rim barrel: lip at the outboard face, then the inner barrel.
    face_a = width / 2 - 0.018
    barrel = [(face_a + 0.006, rim_r + 0.012), (face_a + 0.01, rim_r - 0.004), (face_a - 0.004, rim_r - 0.016), (-width / 2 + 0.02, rim_r - 0.02), (-width / 2 + 0.005, rim_r - 0.008)]
    lathe(bm, barrel, 72, mat=1)
    # Spokes: a concave face, pairs of spokes from the hub to the barrel.
    n_spokes = spec.get('spokes', 10)
    hub_r = 0.068
    hub_a = face_a - 0.055
    for k in range(n_spokes):
        th = 2 * math.pi * k / n_spokes
        r0, r1 = hub_r - 0.005, rim_r - 0.012
        a0, a1 = hub_a, face_a - 0.006
        steps = 4
        prev = None
        for t in range(steps + 1):
            f = t / steps
            r = r0 + (r1 - r0) * f
            a = a0 + (a1 - a0) * math.sqrt(f)
            w = (0.034 - 0.014 * f) * (1.0 if spec.get('spoke_style', 'split') != 'blade' else 1.6)
            thick = 0.022 - 0.006 * f
            c, s = math.cos(th), math.sin(th)
            # Spoke cross-section: rectangle in the plane normal to the radial direction.
            radial = Vector((0, c, s))
            tang = Vector((0, -s, c))
            ax = Vector((1, 0, 0))
            centre = ax * a + radial * r
            ring = [bm.verts.new(centre + tang * (w / 2) * sx + ax * (thick / 2) * sy) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
            if prev:
                for j in range(4):
                    fc = bm.faces.new((prev[j], prev[(j + 1) % 4], ring[(j + 1) % 4], ring[j]))
                    fc.material_index = 1
                    fc.smooth = True
            else:
                fc = bm.faces.new(list(reversed(ring)))
                fc.material_index = 1
            prev = ring
        fc = bm.faces.new(prev)
        fc.material_index = 1
    # Hub, centre cap and five lug nuts.
    lathe(bm, [(hub_a - 0.03, hub_r), (hub_a + 0.012, hub_r), (hub_a + 0.016, hub_r - 0.01), (hub_a + 0.02, 0.03), (hub_a + 0.024, 0.0)], 48, mat=1)
    for k in range(5):
        th = 2 * math.pi * k / 5 + 0.3
        c, s = math.cos(th), math.sin(th)
        lathe_nut = [(hub_a + 0.012, 0.0095), (hub_a + 0.03, 0.0095), (hub_a + 0.034, 0.006), (hub_a + 0.035, 0.0)]
        nut_faces = lathe(bm, lathe_nut, 6, mat=1)
        offset = Vector((0, 0.045 * c, 0.045 * s))
        vs = {v for f in nut_faces for v in f.verts}
        bmesh.ops.translate(bm, verts=list(vs), vec=offset)
    # Brake disc behind the spokes.
    disc_r = radius * 0.53
    disc_a = face_a - 0.11
    lathe(bm, [(disc_a - 0.014, 0.085), (disc_a - 0.014, disc_r), (disc_a + 0.014, disc_r), (disc_a + 0.014, 0.085)], 64, mat=2)
    if outward < 0:
        bmesh.ops.scale(bm, vec=(-1, 1, 1), verts=bm.verts)
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in (mats['Rubber'], mats['Rim'], mats['Brake']):
        me.materials.append(m)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def caliper_mesh(name, radius, width, spec, mats, outward=1, front=True):
    bm = bmesh.new()
    face_a = width / 2 - 0.018
    disc_a = face_a - 0.11
    disc_r = radius * 0.53
    # An arc-shaped block straddling the disc at the rear-top.
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


def build_wheels(shape, spec, mats):
    out = []
    mid = (shape.axle_f + shape.axle_r) / 2
    for front in (True, False):
        axle = shape.axle_f if front else shape.axle_r
        track = shape.track_f if front else shape.track_r
        width = shape.tire_w_f if front else shape.tire_w_r
        for side, tag in ((1, 'L'), (-1, 'R')):
            key = ('F' if front else 'R') + tag
            loc = Vector((side * track / 2, -(axle - mid), shape.tire_r))
            w = wheel_mesh(f'Wheel_{key}', shape.tire_r, width, spec, mats, outward=side)
            w.location = loc
            c = caliper_mesh(f'Caliper_{key}', shape.tire_r, width, spec, mats, outward=side, front=front)
            c.location = loc
            # The calliper sits behind the axle: mirror front/rear placement in y.
            out += [w, c]
    return out


def build_exhausts(shape, spec, mats, body):
    """Tailpipes: chrome tubes poking out of the diffuser (count from the spec)."""
    n = spec.get('exhausts', 2)
    if n <= 0:
        return None
    bm = bmesh.new()
    mid = (shape.axle_f + shape.axle_r) / 2
    z = shape.clearance + 0.13
    xs = {1: [0.42], 2: [-0.42, 0.42], 4: [-0.48, -0.36, 0.36, 0.48]}.get(n, [-0.42, 0.42])
    for x in xs:
        # Find the rear surface at this height and poke 3 cm out of it.
        u = shape.u_rear + 0.4
        while u > shape.u_rear - 0.2 and body.sdf(np.array([u]), np.array([x]), np.array([z]))[0] < 0:
            u -= 0.005
        # A tube with a rolled lip, open at the back so it reads as a pipe.
        prof = [(-0.07, 0.029), (0.0, 0.029), (0.003, 0.034), (0.0, 0.040), (-0.012, 0.042), (-0.12, 0.042)]
        faces = lathe(bm, prof, 32, mat=0)
        vs = {vv for f in faces for vv in f.verts}
        rot = Matrix.Rotation(math.radians(90), 3, 'Z')
        for vv in vs:
            vv.co = rot @ vv.co
        # Blender y = -(u - mid); tube axis along +Y (pointing back).
        bmesh.ops.translate(bm, verts=list(vs), vec=Vector((x, -(u - mid) + 0.03, z)))
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    me = bpy.data.meshes.new('Exhaust')
    bm.to_mesh(me)
    bm.free()
    me.materials.append(mats['Chrome'])
    for p in me.polygons:
        p.use_smooth = True
    ob = bpy.data.objects.new('Exhaust', me)
    bpy.context.scene.collection.objects.link(ob)
    return ob
