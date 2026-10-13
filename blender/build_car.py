"""Build a Halcyon car in Blender: body, glass, lights, trim, wheels and
parts with three LODs; export FBX for Unity and render review images.

  blender -b -P blender/build_car.py -- --car halden-aster-gt [--out DIR] [--sheet PNG] [--render PNG]
  (or, with the bpy module:  python blender/build_car.py -- --car ...)
  --car all builds every car in halcyon/cars.py.

Pipeline:
1. Sample the body's signed distance field on a grid, polygonize it with
   surface nets, triangulate, and project the vertices onto the exact surface.
2. Per LOD, cut every feature hole out of that mesh along its exact contour
   (meshops.cut), decimate what is left to the LOD's budget, and hang a
   dark wall from each hole's edge into the body.
3. Inserts: the surface under each hole, moved inward by the feature's depth
   (glass, grilles, trim), and crisp patches (light lenses with LED strips).
4. Wheels (LOD meshes under one pivot per wheel), callipers, exhausts, and
   the car's parts (interior, wipers, wings, bumpers, rails...).

The car faces -Y in Blender (Unity +Z after export), origin on the ground at
the middle of the wheelbase. Objects: <Part>_LOD0..2 meshes, Wheel_FL/FR/RL/RR
empties (pivot at the hub, axle along X) holding Wheel_xx_LOD0..2, and
Caliper_FL/FR/RL/RR (steer with the wheel, do not spin).
"""
import argparse
import math
import os
import sys
import time

import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from halcyon.carbody import CarBody, CarShape  # noqa: E402
from halcyon.surfacenets import polygonize  # noqa: E402
from halcyon import meshops as mo  # noqa: E402
from halcyon import materials as M  # noqa: E402
from halcyon import wheels as W  # noqa: E402
from halcyon import parts as P  # noqa: E402
from halcyon.cars import CARS  # noqa: E402

# Triangle budgets per LOD.
BODY_TRIS = (24000, 6000, 1600)
INSERT_EDGE = (0.05, 0.10, 0.20)    # target edge length for inserts, m
PATCH_EDGE = (0.016, 0.035, 0.08)


def args():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument('--car', default='halden-aster-gt')
    p.add_argument('--out', default=os.path.join(HERE, '..', 'unity', 'Assets', 'Halcyon', 'Art', 'Resources', 'Cars'))
    p.add_argument('--render', default='', help='hero renders: PNG path prefix')
    p.add_argument('--sheet', default='', help='six-view contact sheet PNG')
    p.add_argument('--voxel', type=float, default=0.012)
    p.add_argument('--samples', type=int, default=48)
    p.add_argument('--res', default='1280x720')
    p.add_argument('--views', default='front34,rear34')
    p.add_argument('--blend', default='')
    p.add_argument('--no-export', action='store_true')
    p.add_argument('--glb', default='', help='also export a GLB of one LOD for the web build to this folder')
    p.add_argument('--glb-lod', type=int, default=1)
    p.add_argument('--no-fbx', action='store_true', help='skip the FBX and style (with --glb)')
    p.add_argument('--engine', default='CYCLES')
    return p.parse_args(argv)


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


# --- Mesh helpers ------------------------------------------------------------------------------
def new_object(name, V, F, mats, mat_index=None, smooth=True, normals=None, uv=True):
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
    if uv and len(me.polygons):
        lay = me.uv_layers.new(name='UVMap')
        loops = np.empty(len(me.loops), dtype=np.int64)
        me.loops.foreach_get('vertex_index', loops)
        p = np.asarray(V)[loops]
        uvs = np.stack([p[:, 1] * 0.25 + 0.5, p[:, 2] * 0.25], axis=1).astype(np.float32)
        lay.data.foreach_set('uv', uvs.ravel())
    if normals is not None and smooth:
        me.normals_split_custom_set_from_vertices(np.asarray(normals).tolist())
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def read_mesh(ob):
    me = ob.data
    V = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get('co', V)
    me.calc_loop_triangles()
    T = np.empty(len(me.loop_triangles) * 3, dtype=np.int64)
    me.loop_triangles.foreach_get('vertices', T)
    mi = np.empty(len(me.loop_triangles), dtype=np.int64)
    me.loop_triangles.foreach_get('material_index', mi)
    return V.reshape(-1, 3), T.reshape(-1, 3), mi


def decimate_to(ob, target):
    n = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    if n <= target or target <= 0:
        return
    mod = ob.modifiers.new('Decimate', 'DECIMATE')
    mod.decimate_type = 'COLLAPSE'
    mod.ratio = max(target / n, 0.0005)
    mod.use_collapse_triangulate = True
    with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
        bpy.ops.object.modifier_apply(modifier=mod.name)


def fix_winding(ob, field, eps=0.0015):
    """Flip faces whose winding disagrees with the field's outward normal (engines cull back faces)."""
    import bmesh
    me = ob.data
    if not len(me.polygons):
        return 0
    ctr = np.empty(len(me.polygons) * 3)
    me.polygons.foreach_get('center', ctr)
    ctr = ctr.reshape(-1, 3)
    pn = np.empty(len(me.polygons) * 3)
    me.polygons.foreach_get('normal', pn)
    pn = pn.reshape(-1, 3)
    g = mo.gradient(field, ctr, eps)
    bad = np.nonzero((pn * g).sum(1) < 0)[0]
    if len(bad):
        bm = bmesh.new()
        bm.from_mesh(me)
        bm.faces.ensure_lookup_table()
        bmesh.ops.reverse_faces(bm, faces=[bm.faces[i] for i in bad])
        bm.to_mesh(me)
        bm.free()
    return len(bad)


def set_field_normals(ob, field, eps=0.0015):
    me = ob.data
    if not len(me.vertices):
        return
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get('co', co)
    co = co.reshape(-1, 3)
    nrm = mo.unit(mo.gradient(field, co, eps))
    me.polygons.foreach_set('use_smooth', np.ones(len(me.polygons), dtype=bool))
    me.normals_split_custom_set_from_vertices(nrm.tolist())


def tri_count(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons) if ob.type == 'MESH' else 0


# --- Body ----------------------------------------------------------------------------------------
class BodyBuilder:
    def __init__(self, body: CarBody, mats, voxel):
        self.body = body
        self.mats = mats
        self.voxel = voxel
        self.field = body.base_blender

    def region(self, fn):
        """A car-frame region fn(u, v, z, n) as fn(P, N) in Blender coordinates."""
        mid = self.body.mid

        def f(P, N):
            return fn(-P[:, 1] + mid, P[:, 0], P[:, 2], (-N[:, 1], N[:, 0], N[:, 2]))
        return f

    def base_mesh(self):
        b = self.body
        s = b.s
        t = time.time()
        half_w = s.width / 2 + 0.14
        lo = np.array([-half_w, -(s.u_front - b.mid) - 0.08, 0.0])
        hi = np.array([half_w, -(s.u_rear - b.mid) + 0.08, s.height + 0.12])
        h = self.voxel
        n = np.ceil((hi - lo) / h).astype(int) + 1
        xs = lo[0] + np.arange(n[0]) * h
        ys = lo[1] + np.arange(n[1]) * h
        zs = lo[2] + np.arange(n[2]) * h
        f = np.empty(n, dtype=np.float32)
        Y, Z = np.meshgrid(ys, zs, indexing='ij')
        U = -Y + b.mid
        for a in range(n[0]):
            f[a] = b.base(U, np.full_like(Y, xs[a]), Z)
        print(f'field sampled on {n[0]}x{n[1]}x{n[2]} in {time.time() - t:.1f} s')
        V, Q = polygonize(f, lo, h)
        F = mo.triangulate(V, Q)
        V = mo.project(self.field, V, iters=3, max_step=h * 0.5)
        N = mo.unit(mo.gradient(self.field, V, 0.0015))
        print(f'base mesh: {len(V)} verts, {len(F)} tris in {time.time() - t:.1f} s')
        self.V, self.F, self.N = V, F, N
        return V, F, N

    def cut_holes(self, holes):
        V, F, N = self.V, self.F, self.N
        removed = np.zeros(len(F), dtype=bool)
        for h in holes:
            V, F, inside, parent, N = mo.cut(V, F, self.region(h.region), N, surface=self.field)
            removed = removed[parent] | inside
        keep = ~removed
        used = np.unique(F[keep])
        remap = np.full(len(V), -1, dtype=np.int64)
        remap[used] = np.arange(len(used))
        return V[used], remap[F[keep]], N[used]

    def crisp(self, region, extra_splits=()):
        """The base surface inside a region, cut crisply along its contour; returns (V, F, N, material slot per face)."""
        V, F, N = self.V, self.F, self.N
        rf = self.region(region)
        val = rf(V, N)
        near = (val[F] < 0.03).any(axis=1)
        if not near.any():
            return None
        Vs, Fs = mo.subset(V, F, near)
        Ns = N[np.unique(F[near])]
        Vs, Fs, inside, _, Ns = mo.cut(Vs, Fs, rf, Ns, surface=self.field)
        used = np.unique(Fs[inside])
        if len(used) == 0:
            return None
        remap = np.full(len(Vs), -1, dtype=np.int64)
        remap[used] = np.arange(len(used))
        Vs, Fs, Ns = Vs[used], remap[Fs[inside]], Ns[used]
        slot = np.zeros(len(Fs), dtype=np.int64)
        for k, sr in enumerate(extra_splits):
            Vs, Fs, ins, parent, Ns = mo.cut(Vs, Fs, self.region(sr), Ns, surface=self.field)
            slot = slot[parent]
            slot[ins] = k + 1
        return Vs, Fs, Ns, slot

    def margin_patch(self, region, margin):
        """The base surface where region < margin (boundary left jagged; it hides behind the body)."""
        V, F, N = self.V, self.F, self.N
        val = self.region(region)(V, N)
        sel = (val[F] < margin).any(axis=1)
        if not sel.any():
            return None
        used = np.unique(F[sel])
        remap = np.full(len(V), -1, dtype=np.int64)
        remap[used] = np.arange(len(used))
        return V[used], remap[F[sel]], N[used]

    def build_lod(self, lod, feats):
        mats = self.mats
        holes = [f for f in feats if f.kind == 'hole' and f.lod >= lod]
        patches = [f for f in feats if f.kind == 'patch' and f.lod >= lod]
        t = time.time()
        V, F, N = self.cut_holes(holes)
        body = new_object(f'Body_LOD{lod}', V, F, [mats['Paint']])
        decimate_to(body, BODY_TRIS[lod])
        fix_winding(body, self.field)
        set_field_normals(body, self.field)
        objs = [body]
        # Walls into every hole, classified by the hole each boundary vertex lies on.
        Vb, Fb, _ = read_mesh(body)
        Nb = mo.unit(mo.gradient(self.field, Vb, 0.0015))
        be = mo.boundary_edges(Fb)
        if len(be):
            vals = np.stack([np.abs(self.region(h.region)(Vb[be[:, 0]] * 0.5 + Vb[be[:, 1]] * 0.5, Nb[be[:, 0]])) for h in holes], axis=1)
            owner = np.argmin(vals, axis=1)
            wall_mats = sorted({h.wall for h in holes})
            WV, WF, WM = [], [], []
            off = 0
            for k, h in enumerate(holes):
                sel = owner == k
                if not sel.any():
                    continue
                vw, fw = wall_from_edges(Vb, Nb, be[sel], h.depth + 0.004)
                WV.append(vw)
                WF.append(fw + off)
                WM.append(np.full(len(fw), wall_mats.index(h.wall)))
                off += len(vw)
            if WV:
                w = new_object(f'Walls_LOD{lod}', np.concatenate(WV), np.concatenate(WF), [mats[m] for m in wall_mats], np.concatenate(WM), smooth=False, uv=False)
                objs.append(w)
        # Inserts under the holes, grouped by material.
        groups = {}
        for h in holes:
            mp = self.margin_patch(h.region, h.margin)
            if mp is None:
                continue
            v, f, n = mp
            groups.setdefault(h.material, []).append((v - n * h.depth, f, n))
        for mat, parts in groups.items():
            v, f, _ = mo.merge([(p[0], p[1]) for p in parts])
            ob = new_object(f'{mat}_LOD{lod}', v, f, [mats[mat]])
            area = mo.area(v, f).sum()
            decimate_to(ob, int(max(60, area / (0.433 * INSERT_EDGE[lod] ** 2))))
            fix_winding(ob, self.field)
            set_field_normals(ob, self.field)
            objs.append(ob)
        # Crisp patches (lenses) with their material splits.
        for p in patches:
            res = self.crisp(p.region, [sr for sr, _ in p.splits])
            if res is None:
                continue
            v, f, n, slot = res
            names = [p.material] + [m for _, m in p.splits]
            ob = new_object(f'{p.name}_LOD{lod}', v - n * p.depth, f, [mats[m] for m in names], slot)
            if not p.splits:
                area = mo.area(v, f).sum()
                decimate_to(ob, int(max(40, area / (0.433 * PATCH_EDGE[lod] ** 2))))
            fix_winding(ob, self.field)
            set_field_normals(ob, self.field)
            objs.append(ob)
        for ob in objs:
            ob.hide_render = lod > 0
        print(f'LOD{lod}: ' + ', '.join(f'{o.name} {tri_count(o)}' for o in objs) + f' ({time.time() - t:.1f} s)')
        return objs


def wall_from_edges(V, N, be, depth):
    """Quads hanging from directed boundary edges (a -> b) into the surface."""
    verts = np.unique(be)
    loc = np.full(int(be.max()) + 1, -1, dtype=np.int64)
    loc[verts] = np.arange(len(verts))
    top = V[verts]
    bot = top - N[verts] * depth
    m = len(verts)
    a, b = loc[be[:, 0]], loc[be[:, 1]]
    F = np.concatenate([np.stack([b, a, a + m], 1), np.stack([b, a + m, b + m], 1)])
    return np.concatenate([top, bot]), F


def build(spec_id, voxel):
    spec = CARS[spec_id]
    shape = spec['shape']
    body = CarBody(shape, spec.get('look', {}))
    mats = M.car_materials(spec)
    bb = BodyBuilder(body, mats, voxel)
    bb.base_mesh()
    feats = body.features()
    for lod in range(3):
        bb.build_lod(lod, feats)
    W.build_wheels(shape, spec, mats, body.mid)
    W.build_exhausts(shape, spec, mats, body, body.mid)
    P.build_parts(body, spec, mats, bb)
    tris = {}
    for ob in bpy.context.scene.objects:
        if ob.type == 'MESH':
            lod = ob.name[-1] if '_LOD' in ob.name else 'all'
            tris[lod] = tris.get(lod, 0) + tri_count(ob)
    print('triangles per LOD (\'all\' counts in every LOD):', tris)
    return body


def export_style(spec_id, path):
    """The car's colours and finishes for the game (Resources/Cars/<id>.style.json)."""
    import json
    spec = CARS[spec_id]
    hexs = lambda k, d: '#%06X' % spec.get(k, d)
    style = {
        'id': spec_id,
        'name': spec['name'],
        'paint': hexs('paint', 0xC7262E),
        'accent': hexs('accent', 0x111111),
        'rim': hexs('rim', 0x9A9FA6),
        'caliper': hexs('caliper', 0xD92B1C),
        'interior': hexs('interior', 0x1A1A1C),
        'metallic': spec.get('metallic', 0.55),
        'paintRoughness': spec.get('paint_rough', 0.32),
        'accentMetallic': spec.get('accent_metallic', 0.3),
        'rimMetallic': spec.get('rim_metallic', 1.0),
        'rimRoughness': spec.get('rim_rough', 0.24),
    }
    with open(path, 'w') as f:
        json.dump(style, f, indent=2)
        f.write('\n')


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


def export_glb(path, lod):
    """The car for the web build: one LOD of every part, the wheels and callipers under
    their hub empties, the materials by name (the game swaps in its own)."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for ob in bpy.context.scene.objects:
        n = ob.name
        k = n.rfind('_LOD')
        if ob.type == 'EMPTY' or (ob.type == 'MESH' and (k < 0 or n[k + 4:] == str(lod))):
            ob.select_set(True)
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format='GLB',
        use_selection=True,
        export_apply=True,
        export_yup=True,
        export_texcoords=False,
        export_normals=True,
        export_materials='EXPORT',
        export_cameras=False,
        export_lights=False,
        export_extras=False,
    )


# --- Review renders ----------------------------------------------------------------------------
def stage(body):
    sc = bpy.context.scene
    world = bpy.data.worlds.new('Sky')
    sc.world = world
    world.use_nodes = True
    nt = world.node_tree
    nt.nodes.clear()
    sky = nt.nodes.new('ShaderNodeTexSky')
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
    bpy.ops.mesh.primitive_plane_add(size=80, location=(0, 0, 0))
    ground = bpy.context.active_object
    ground.name = 'Ground'
    gm = bpy.data.materials.new('Ground')
    gm.use_nodes = True
    bsdf = gm.node_tree.nodes['Principled BSDF']
    bsdf.inputs['Base Color'].default_value = (0.11, 0.105, 0.10, 1)
    bsdf.inputs['Roughness'].default_value = 0.85
    ground.data.materials.append(gm)
    sun_data = bpy.data.lights.new('Sun', 'SUN')
    sun_data.energy = 3.2
    sun_data.angle = math.radians(1.5)
    sun = bpy.data.objects.new('Sun', sun_data)
    sun.rotation_euler = (math.radians(55), 0, math.radians(35))
    sc.collection.objects.link(sun)


POSES = {
    'front34': ((5.2, -6.0, 1.55), 50),
    'rear34': ((-5.0, 6.2, 1.9), 50),
    'side': ((7.5, 0.0, 1.0), 50),
    'front': ((0.0, -8.5, 0.9), 55),
    'rear': ((0.0, 8.5, 1.1), 55),
    'top': ((0.01, 0.0, 9.5), 50),
    'low': ((3.4, -4.6, 0.45), 35),
}


def setup_render(res, samples, engine):
    sc = bpy.context.scene
    if engine == 'WORKBENCH':
        sc.render.engine = 'BLENDER_WORKBENCH'
        sc.display.shading.light = 'STUDIO'
        sc.display.shading.color_type = 'MATERIAL'
        sc.display.shading.show_shadows = True
        sc.display.shading.show_cavity = True
    else:
        sc.render.engine = 'CYCLES'
        sc.cycles.device = 'CPU'
        sc.cycles.samples = samples
        sc.cycles.use_denoising = True
    w, h = (int(x) for x in res.split('x'))
    sc.render.resolution_x = w
    sc.render.resolution_y = h
    for vt in ('AgX', 'Filmic', 'Standard'):
        try:
            sc.view_settings.view_transform = vt
            break
        except TypeError:
            continue
    for look in ('AgX - Punchy', 'AgX - Medium High Contrast'):
        try:
            sc.view_settings.look = look
            break
        except TypeError:
            continue


def render_views(prefix, body, views):
    sc = bpy.context.scene
    cam = sc.camera
    if cam is None:
        cam_data = bpy.data.cameras.new('Cam')
        cam = bpy.data.objects.new('Cam', cam_data)
        sc.collection.objects.link(cam)
        sc.camera = cam
    s = body.s
    scale = max(1.0, s.length / 4.4)
    out = []
    for name in views:
        (x, y, z), lens = POSES[name]
        cam.data.lens = lens
        cam.location = (x * scale, y * scale, z * (scale if name == 'top' else 1.0) + (s.height - 1.3) * 0.5)
        target = np.array([0.0, 0.0, s.height * 0.42])
        d = target - np.array(cam.location)
        yaw = math.atan2(d[1], d[0]) - math.pi / 2
        pitch = math.atan2(d[2], math.hypot(d[0], d[1]))
        cam.rotation_euler = (math.pi / 2 + pitch, 0, yaw)
        p = f'{prefix}-{name}.png'
        sc.render.filepath = p
        t = time.time()
        bpy.ops.render.render(write_still=True)
        print(f'rendered {p} in {time.time() - t:.0f} s')
        out.append(p)
    return out


def contact_sheet(paths, out, title):
    from PIL import Image, ImageDraw
    ims = [Image.open(p).convert('RGB') for p in paths]
    w, h = ims[0].size
    cols = 3
    rows = (len(ims) + cols - 1) // cols
    sheet = Image.new('RGB', (w * cols, h * rows + 28), (20, 20, 22))
    d = ImageDraw.Draw(sheet)
    d.text((8, 7), title, fill=(230, 230, 230))
    for i, im in enumerate(ims):
        sheet.paste(im, ((i % cols) * w, 28 + (i // cols) * h))
    sheet.save(out)
    print('contact sheet', out)


def main():
    a = args()
    cars = list(CARS) if a.car == 'all' else a.car.split(',')
    for car in cars:
        t = time.time()
        reset_scene()
        body = build(car, a.voxel)
        if a.glb:
            glb = os.path.join(a.glb, f'{car}.glb')
            export_glb(glb, a.glb_lod)
            print(f'exported {glb}')
        if not a.no_export and not a.no_fbx:
            fbx = os.path.join(a.out, f'{car}.fbx')
            export_fbx(fbx)
            export_style(car, os.path.join(a.out, f'{car}.style.json'))
            print(f'exported {fbx}')
        if a.blend:
            bpy.ops.wm.save_as_mainfile(filepath=a.blend.replace('{car}', car))
        if a.render or a.sheet:
            stage(body)
        if a.sheet:
            setup_render('640x360', max(16, a.samples // 2), a.engine)
            base = a.sheet.replace('{car}', car).replace('.png', '')
            paths = render_views(base, body, ['front34', 'rear34', 'side', 'front', 'rear', 'top'])
            contact_sheet(paths, base + '.png', f"{CARS[car]['name']}  ({car})")
            for p in paths:
                os.remove(p)
        if a.render:
            setup_render(a.res, a.samples, a.engine)
            render_views(a.render.replace('{car}', car).replace('.png', ''), body, a.views.split(','))
        print(f'{car} done in {time.time() - t:.0f} s')


if __name__ == '__main__':
    main()
