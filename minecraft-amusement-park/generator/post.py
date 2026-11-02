"""Post-processing: compute the block states vanilla Minecraft would give connectable blocks.

Litematica places exact states, so fences, walls, panes, iron bars and stairs need the same
connection / shape properties the game would compute, otherwise the schematic verifier flags them.
"""
import numpy as np

from .world import parse_state, make_state, DIRS, DVEC

NON_FULL_KEYS = (
    'air', 'water', 'lava', 'stairs', 'slab', 'fence', '_wall', 'pane', 'bars', 'door', 'sign', 'banner',
    'torch', 'lantern', 'chain', 'rod', 'button', 'pressure_plate', 'carpet', 'flower', 'sapling', 'rail',
    'ladder', 'vine', 'leaves', 'pot', 'candle', 'cake', 'bed', 'chest', 'head', 'skull', 'campfire', 'anvil',
    'grindstone', 'lectern', 'hopper', 'scaffolding', 'snow', 'farmland', 'dirt_path', 'bell', 'lily_pad',
    'tall_grass', 'fern', 'bush', 'mushroom', 'coral', 'kelp', 'seagrass', 'sugar_cane', 'bamboo_sapling',
    'cactus', 'daylight', 'stonecutter', 'enchanting', 'brewing', 'pickle', 'tulip', 'orchid', 'allium',
    'bluet', 'daisy', 'poppy', 'dandelion', 'cornflower', 'lily_of', 'rose', 'peony', 'lilac', 'sunflower',
    'pumpkin', 'melon', 'jack_o', 'shulker', 'cauldron', 'composter', 'trapdoor', 'gate', 'button', 'lever',
    'tripwire', 'repeater', 'comparator', 'redstone_wire', 'cobweb', 'conduit', 'dripleaf', 'spore',
    'azalea', 'decorated_pot', 'amethyst_cluster', 'bud', 'frogspawn', 'sculk_vein', 'glow_lichen',
    'pointed_dripstone', 'end_rod', 'lightning', 'honey_block', 'soul_sand', 'mud', 'barrier', 'beacon',
    'sniffer', 'pitcher', 'torchflower', 'big_dripleaf', 'hanging', 'banner', 'item_frame', 'stem',
)

FULL_OVERRIDE = {'bamboo_block', 'stripped_bamboo_block', 'bamboo_planks', 'bamboo_mosaic', 'mud_bricks',
                 'packed_mud', 'mushroom_stem', 'red_mushroom_block', 'brown_mushroom_block',
                 'melon', 'pumpkin', 'carved_pumpkin', 'jack_o_lantern', 'muddy_mangrove_roots',
                 'sea_lantern', 'redstone_lamp', 'cracked_stone_bricks', 'tuff', 'soul_soil'}
NO_CONNECT = {'melon', 'pumpkin', 'carved_pumpkin', 'jack_o_lantern', 'barrier'}


def _base(n):
    return n[10:] if n.startswith('minecraft:') else n


def is_full(b):
    if b in FULL_OVERRIDE:
        return b not in NO_CONNECT
    if b.endswith('_glass') or b == 'glass' or b == 'tinted_glass':
        return True
    for k in NON_FULL_KEYS:
        if k in b:
            return False
    return True


def kind(b):
    if b.endswith('_fence') and 'nether' not in b:
        return 'wfence'
    if b == 'nether_brick_fence':
        return 'nfence'
    if b.endswith('_fence_gate'):
        return 'gate'
    if b.endswith('_wall') and not any(k in b for k in ('sign', 'torch', 'banner', 'fan', 'head', 'skull', 'hanging')):
        return 'wall'
    if b.endswith('glass_pane') or b == 'iron_bars':
        return 'pane'
    if b.endswith('_stairs'):
        return 'stairs'
    return None


def postprocess(world, log=print):
    pal = world.pal
    info = []  # per palette id: (base, props, kind)
    for s in pal:
        n, p = parse_state(s)
        b = _base(n)
        info.append((b, p, kind(b)))
    full = [is_full(i[0]) for i in info]
    kinds = [i[2] for i in info]

    def ensure():
        while len(info) < len(pal):
            n, p = parse_state(pal[len(info)])
            bb = _base(n)
            info.append((bb, p, kind(bb)))
            full.append(is_full(bb))
            kinds.append(kind(bb))

    def connects(kind_self, nid, d_to_neighbor):
        if nid >= len(info):
            ensure()
        nb, np_, nk = info[nid]
        if nid == 0:
            return False
        if kind_self == 'wfence':
            if nk == 'wfence':
                return True
            if nk == 'gate':
                f = np_.get('facing', 'north')
                return (f in ('north', 'south')) == (d_to_neighbor in ('east', 'west'))
            return bool(full[nid])
        if kind_self == 'nfence':
            return nk == 'nfence' or bool(full[nid])
        if kind_self == 'wall':
            if nk in ('wall', 'pane'):
                return True
            if nk == 'gate':
                f = np_.get('facing', 'north')
                return (f in ('north', 'south')) == (d_to_neighbor in ('east', 'west'))
            return bool(full[nid])
        if kind_self == 'pane':
            return nk in ('pane', 'wall') or bool(full[nid])
        return False

    a = world.a
    H, D, W = a.shape
    changed = 0
    for k in ('wfence', 'nfence', 'wall', 'pane'):
        ids = [i for i, kk in enumerate(kinds) if kk == k]
        if not ids:
            continue
        mask = np.isin(a, np.array(ids, dtype=a.dtype))
        ys, zs, xs = np.nonzero(mask)
        for y, z, x in zip(ys.tolist(), zs.tolist(), xs.tolist()):
            sid = int(a[y, z, x])
            b, props, _ = info[sid]
            newp = dict(props)
            con = {}
            for d in DIRS:
                dx, dz = DVEC[d]
                nx, nz = x + dx, z + dz
                nid = int(a[y, nz, nx]) if 0 <= nx < W and 0 <= nz < D else 0
                con[d] = connects(k, nid, d)
            if k == 'wall':
                above = int(a[y + 1, z, x]) if y + 1 < H else 0
                if above >= len(info):
                    ensure()
                ab, ap, ak = info[above]
                above_full = bool(full[above]) and above != 0
                for d in DIRS:
                    if con[d]:
                        tall = above_full or (ak == 'wall' and ap.get(d, 'none') != 'none')
                        newp[d] = 'tall' if tall else 'low'
                    else:
                        newp[d] = 'none'
                straight = (con['north'] and con['south'] and not con['east'] and not con['west']) or \
                           (con['east'] and con['west'] and not con['north'] and not con['south'])
                post_forced = above != 0 and not above_full and ak != 'wall' and \
                    any(t in ab for t in ('torch', 'lantern', 'sign', 'banner', 'pressure_plate', 'rod', 'candle',
                                          'pot', 'skull', 'head', 'campfire', 'button', 'carpet', 'chain', 'fence'))
                if ak == 'wall' and ap.get('up') == 'true':
                    post_forced = True
                newp['up'] = 'true' if (not straight or post_forced) else 'false'
            else:
                for d in DIRS:
                    newp[d] = 'true' if con[d] else 'false'
            ns = make_state('minecraft:' + b, newp)
            if ns != pal[sid]:
                a[y, z, x] = world.id(ns)
                changed += 1
        ensure()

    # ---- stairs shapes (vanilla StairsBlock.getStairsShape) -------------------
    sids = [i for i, kk in enumerate(kinds) if kk == 'stairs']
    if sids:
        mask = np.isin(a, np.array(sids, dtype=a.dtype))
        ys, zs, xs = np.nonzero(mask)
        pts = list(zip(ys.tolist(), zs.tolist(), xs.tolist()))

        def st(y, z, x):
            if not (0 <= x < W and 0 <= z < D and 0 <= y < H):
                return None
            i = int(a[y, z, x])
            if i >= len(kinds):
                ensure()
            if kinds[i] != 'stairs':
                return None
            p = info[i][1]
            return p.get('facing', 'north'), p.get('half', 'bottom')

        def ccw(d):
            return DIRS[(DIRS.index(d) + 3) % 4]

        def axis(d):
            return 'z' if d in ('north', 'south') else 'x'

        def opp(d):
            return DIRS[(DIRS.index(d) + 2) % 4]

        new_shapes = []
        for y, z, x in pts:
            f, h = st(y, z, x)

            def different(dirn):
                dx, dz = DVEC[dirn]
                n = st(y, z + dz, x + dx)
                return n is None or n[0] != f or n[1] != h

            shape = 'straight'
            dx, dz = DVEC[f]
            back = st(y, z + dz, x + dx)
            done = False
            if back is not None and back[1] == h:
                bf = back[0]
                if axis(bf) != axis(f) and different(opp(bf)):
                    shape = 'outer_left' if bf == ccw(f) else 'outer_right'
                    done = True
            if not done:
                front = st(y, z - dz, x - dx)
                if front is not None and front[1] == h:
                    ff = front[0]
                    if axis(ff) != axis(f) and different(ff):
                        shape = 'inner_left' if ff == ccw(f) else 'inner_right'
            new_shapes.append(shape)
        for (y, z, x), shape in zip(pts, new_shapes):
            sid = int(a[y, z, x])
            b, props, _ = info[sid]
            if props.get('shape', 'straight') != shape:
                np_ = dict(props)
                np_['shape'] = shape
                a[y, z, x] = world.id(make_state('minecraft:' + b, np_))
                changed += 1
    log(f'  post-process updated {changed} block states')
    return changed
