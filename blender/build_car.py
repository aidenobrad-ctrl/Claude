"""Build a Halcyon car in Blender: body, glass, lights, wheels, materials;
export FBX for Unity and render a preview.

  blender -b -P blender/build_car.py -- --car halden-aster-gt [--out DIR] [--render PNG] [--voxel 0.012]
  (or, with the bpy module:  python blender/build_car.py -- --car ...)

The car faces -Y in Blender (Unity +Z after export), origin on the ground at
the middle of the wheelbase. Objects: Body, Wheel_FL/FR/RL/RR (pivot at the
hub, axle along X), Caliper_FL/FR/RL/RR (steer with the wheel, do not spin).
"""
import argparse
import math
import os
import sys
import time

import bpy
import bmesh
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from halcyon.carbody import CarBody, CarShape  # noqa: E402
from halcyon.surfacenets import normals, polygonize, sample_grid  # noqa: E402
from halcyon import materials as M  # noqa: E402
from halcyon import wheels as W  # noqa: E402
from halcyon.cars import CARS  # noqa: E402


def args():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument('--car', default='halden-aster-gt')
    p.add_argument('--out', default=os.path.join(HERE, '..', 'unity', 'Assets', 'Halcyon', 'Art', 'Cars'))
    p.add_argument('--render', default='')
    p.add_argument('--voxel', type=float, default=0.012)
    p.add_argument('--samples', type=int, default=48)
    p.add_argument('--res', default='1280x720')
    p.add_argument('--views', default='front34,rear34')
    p.add_argument('--blend', default='')
    return p.parse_args(argv)


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def body_meshes(body: CarBody, voxel: float, gap: float = 0.003):
    """Polygonize the painted body and one insert mesh per material (glass, lights, trim).

    The base field is sampled once; each feature's insert is the base shrunk
    by (depth - gap) inside the feature's region, so it sits just above the
    floor of the recess cut into the body.
    """
    s = body.s
    half_w = s.width / 2 + 0.14
    lo = np.array([-half_w, -(s.u_front - body.mid) - 0.08, 0.0])
    hi = np.array([half_w, -(s.u_rear - body.mid) + 0.08, s.height + 0.08])
    feats = body.features()
    t = time.time()
    n = np.ceil((hi - lo) / voxel).astype(int) + 1
    xs = lo[0] + np.arange(n[0]) * voxel
    ys = lo[1] + np.arange(n[1]) * voxel
    zs = lo[2] + np.arange(n[2]) * voxel
    base = np.empty(n, dtype=np.float32)
    regions = [np.empty(n, dtype=np.float32) for _ in feats]
    Y, Z = np.meshgrid(ys, zs, indexing='ij')
    U = -Y + body.mid
    for a in range(n[0]):
        V = np.full_like(Y, xs[a])
        base[a] = body.base(U, V, Z)
        for k, f in enumerate(feats):
            regions[k][a] = f.region(U, V, Z)
    print(f'fields sampled on {n[0]}x{n[1]}x{n[2]} in {time.time() - t:.1f} s')
    from halcyon.carbody import smax
    # Painted body: recess every feature.
    paint = base.copy()
    for f, R in zip(feats, regions):
        cut = smax(R, -(paint + f.depth), 0.004)
        paint = smax(paint, -cut, 0.004)
    out = {}

    def field_fn(kind, feat=None):
        def fn(x, y, z):
            u, v = -y + body.mid, x
            b = body.base(u, v, z)
            if kind == 'paint':
                d = b
                for f in feats:
                    cut = smax(f.region(u, v, z), -(d + f.depth), 0.004)
                    d = smax(d, -cut, 0.004)
                return d
            return smax(b + feat.depth - gap, feat.region(u, v, z), 0.003)
        return fn

    verts, quads = polygonize(paint, lo, voxel)
    out['Paint'] = (verts, quads, normals(field_fn('paint'), verts, eps=voxel * 0.25))
    for f, R in zip(feats, regions):
        ins = smax(base + f.depth - gap, R, 0.003)
        v2, q2 = polygonize(ins, lo, voxel)
        if not len(q2):
            continue
        nr = normals(field_fn('insert', f), v2, eps=voxel * 0.25)
        if f.material in out:
            pv, pq, pn = out[f.material]
            out[f.material] = (np.concatenate([pv, v2]), np.concatenate([pq, q2 + len(pv)]), np.concatenate([pn, nr]))
        else:
            out[f.material] = (v2, q2, nr)
    for k, (v_, q_, _) in out.items():
        print(f'  {k}: {len(v_)} verts, {len(q_)} quads')
    print(f'body meshes in {time.time() - t:.1f} s')
    return out


# Triangle budget per part and LOD, as decimation ratios of the polygonized mesh.
LOD_RATIOS = {
    'Paint': (0.10, 0.032, 0.01),
    'Glass': (0.06, 0.025, 0.01),
    'Trim': (0.025, 0.012, 0.006),
    'LightFront': (0.15, 0.06, 0.025),
    'LightRear': (0.15, 0.06, 0.025),
}


def body_fields(body):
    """Blender-space field functions per material, for normals after decimation."""
    from halcyon.carbody import smax
    feats = body.features()

    def paint(x, y, z):
        u, v = -y + body.mid, x
        d = body.base(u, v, z)
        for f in feats:
            cut = smax(f.region(u, v, z), -(d + f.depth), 0.004)
            d = smax(d, -cut, 0.004)
        return d

    def inserts(material):
        fs = [f for f in feats if f.material == material]

        def fn(x, y, z):
            u, v = -y + body.mid, x
            b = body.base(u, v, z)
            d = None
            for f in fs:
                e = smax(b + f.depth - 0.003, f.region(u, v, z), 0.003)
                d = e if d is None else np.minimum(d, e)
            return d
        return fn

    return {'Paint': paint, 'Glass': inserts('Glass'), 'Trim': inserts('Trim'), 'LightFront': inserts('LightFront'), 'LightRear': inserts('LightRear')}


def decimate(ob, ratio):
    if ratio >= 1:
        return
    mod = ob.modifiers.new('Decimate', 'DECIMATE')
    mod.decimate_type = 'COLLAPSE'
    mod.ratio = ratio
    mod.use_collapse_triangulate = True
    with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
        bpy.ops.object.modifier_apply(modifier=mod.name)


def field_normals(ob, field, eps=0.002):
    """Smooth shading from the exact field gradient, whatever the mesh density."""
    me = ob.data
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get('co', co)
    co = co.reshape(-1, 3)
    nrm = normals(field, co, eps=eps)
    me.polygons.foreach_set('use_smooth', np.ones(len(me.polygons), dtype=bool))
    me.normals_split_custom_set_from_vertices(nrm.tolist())


def make_object(name, verts, quads, nrm, mat_index, mats):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts.tolist(), [], quads.tolist())
    me.update()
    for m in mats:
        me.materials.append(m)
    me.polygons.foreach_set('material_index', mat_index.astype(np.int32))
    me.polygons.foreach_set('use_smooth', np.ones(len(me.polygons), dtype=bool))
    if nrm is not None:
        me.normals_split_custom_set_from_vertices(nrm.tolist())
    # A simple planar UV (side projection) so engines get a UV set.
    uv = me.uv_layers.new(name='UVMap')
    loops = np.empty(len(me.loops), dtype=np.int64)
    me.loops.foreach_get('vertex_index', loops)
    p = verts[loops]
    uvs = np.stack([p[:, 1] * 0.25 + 0.5, p[:, 2] * 0.25], axis=1).astype(np.float32)
    uv.data.foreach_set('uv', uvs.ravel())
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def build(spec_id: str, voxel: float):
    spec = CARS[spec_id]
    shape = spec['shape']
    body = CarBody(shape)
    mats = M.car_materials(spec)
    meshes = body_meshes(body, voxel)
    names = {'Paint': 'Body', 'Glass': 'Glass', 'Trim': 'Trim', 'LightFront': 'LightsFront', 'LightRear': 'LightsRear'}
    body_ob = None
    fields = body_fields(body)
    for key, (v_, q_, n_) in meshes.items():
        for lod, ratio in enumerate(LOD_RATIOS[key]):
            ob = make_object(f'{names[key]}_LOD{lod}', v_, q_, None, np.zeros(len(q_), dtype=np.int32), [mats[key]])
            decimate(ob, ratio)
            field_normals(ob, fields[key])
            ob.hide_render = lod > 0
            if key == 'Paint' and lod == 0:
                body_ob = ob
    wheels = W.build_wheels(shape, spec, mats)
    W.build_exhausts(shape, spec, mats, body)
    tris = {}
    for ob in bpy.context.scene.objects:
        if ob.type == 'MESH':
            n = sum(len(p.vertices) - 2 for p in ob.data.polygons)
            lod = ob.name[-1] if '_LOD' in ob.name else '0'
            tris[lod] = tris.get(lod, 0) + n
    print('triangles per LOD (wheels and exhausts count in every LOD):', tris)
    return body, body_ob, wheels


def export_fbx(path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=False,
        apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',
        axis_up='Y',
        bake_space_transform=True,
        mesh_smooth_type='OFF',
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        object_types={'MESH', 'EMPTY'},
        path_mode='STRIP',
    )


def stage(shape: CarShape):
    """Ground, sky and sun for preview renders."""
    sc = bpy.context.scene
    world = bpy.data.worlds.new('Sky')
    sc.world = world
    world.use_nodes = True
    nt = world.node_tree
    nt.nodes.clear()
    sky = nt.nodes.new('ShaderNodeTexSky')
    sky.sky_type = 'SINGLE_SCATTERING' if hasattr(sky, 'sky_type') and 'SINGLE_SCATTERING' in [e.identifier for e in sky.bl_rna.properties['sky_type'].enum_items] else sky.sky_type
    try:
        sky.sun_elevation = math.radians(28)
        sky.sun_rotation = math.radians(215)
    except AttributeError:
        pass
    bg = nt.nodes.new('ShaderNodeBackground')
    bg.inputs['Strength'].default_value = 0.22
    out = nt.nodes.new('ShaderNodeOutputWorld')
    nt.links.new(sky.outputs['Color'], bg.inputs['Color'])
    nt.links.new(bg.outputs['Background'], out.inputs['Surface'])
    # Ground: a large warm-grey plane.
    bpy.ops.mesh.primitive_plane_add(size=60, location=(0, 0, 0))
    ground = bpy.context.active_object
    ground.name = 'Ground'
    gm = bpy.data.materials.new('Ground')
    gm.use_nodes = True
    bsdf = gm.node_tree.nodes['Principled BSDF']
    bsdf.inputs['Base Color'].default_value = (0.11, 0.105, 0.10, 1)
    bsdf.inputs['Roughness'].default_value = 0.85
    ground.data.materials.append(gm)
    # Sun.
    sun_data = bpy.data.lights.new('Sun', 'SUN')
    sun_data.energy = 3.2
    sun_data.angle = math.radians(1.5)
    sun = bpy.data.objects.new('Sun', sun_data)
    sun.rotation_euler = (math.radians(55), 0, math.radians(35))
    sc.collection.objects.link(sun)
    return ground, sun


def render_views(path, shape, res, samples, views):
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    w, h = (int(x) for x in res.split('x'))
    sc.render.resolution_x = w
    sc.render.resolution_y = h
    for vt in ('Khronos PBR Neutral', 'AgX', 'Filmic', 'Standard'):
        try:
            sc.view_settings.view_transform = vt
            break
        except TypeError:
            continue
    cam_data = bpy.data.cameras.new('Cam')
    cam_data.lens = 50
    cam = bpy.data.objects.new('Cam', cam_data)
    sc.collection.objects.link(cam)
    sc.camera = cam
    poses = {
        'front34': (5.2, -6.0, 1.55),
        'rear34': (-5.0, 6.2, 1.9),
        'side': (7.5, 0.0, 1.0),
        'top': (0.01, 0.0, 9.0),
        'front': (0.0, -8.5, 0.9),
        'rear': (0.0, 8.5, 1.1),
    }
    out = []
    for name in views.split(','):
        x, y, z = poses[name]
        cam.location = (x, y, z)
        target = np.array([0.0, 0.0, 0.55])
        d = target - np.array([x, y, z])
        yaw = math.atan2(d[1], d[0]) - math.pi / 2
        pitch = math.atan2(d[2], math.hypot(d[0], d[1]))
        cam.rotation_euler = (math.pi / 2 + pitch, 0, yaw)
        p = path.replace('.png', f'-{name}.png')
        sc.render.filepath = p
        t = time.time()
        bpy.ops.render.render(write_still=True)
        print(f'rendered {p} in {time.time() - t:.0f} s')
        out.append(p)
    return out


def main():
    a = args()
    reset_scene()
    body, body_ob, wheels = build(a.car, a.voxel)
    fbx = os.path.join(a.out, f'{a.car}.fbx')
    export_fbx(fbx)
    print(f'exported {fbx}')
    if a.blend:
        bpy.ops.wm.save_as_mainfile(filepath=a.blend)
    if a.render:
        stage(body.s)
        render_views(a.render, body.s, a.res, a.samples, a.views)


if __name__ == '__main__':
    main()
