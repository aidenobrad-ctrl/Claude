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


# ---------------------------------------------------------------------------------------------
# Support / integrity pass: make sure nothing would pop off when the game updates it
# ---------------------------------------------------------------------------------------------
SOIL = {'grass_block', 'dirt', 'coarse_dirt', 'podzol', 'rooted_dirt', 'moss_block', 'mud', 'farmland', 'mycelium'}
PLANTS = {'dandelion', 'poppy', 'blue_orchid', 'allium', 'azure_bluet', 'red_tulip', 'orange_tulip', 'white_tulip',
          'pink_tulip', 'oxeye_daisy', 'cornflower', 'lily_of_the_valley', 'fern', 'pink_petals', 'torchflower',
          'sweet_berry_bush'}
TALL = {'sunflower', 'lilac', 'rose_bush', 'peony', 'tall_grass', 'large_fern'}
GRAVITY_FIX = {'sand': 'sandstone', 'red_sand': 'red_sandstone', 'gravel': 'cobblestone'}


def fix_support(world, log=print):
    from .world import DVEC, OPP
    a = world.a
    H, D, W = a.shape
    pal = world.pal
    info = [(_base(parse_state(s)[0]), parse_state(s)[1]) for s in pal]

    def nm(x, y, z):
        if 0 <= x < W and 0 <= y < H and 0 <= z < D:
            i = int(a[y, z, x])
            if i >= len(info):
                info.extend((_base(parse_state(s)[0]), parse_state(s)[1]) for s in pal[len(info):])
            return info[i][0]
        return 'air'

    def solid(x, y, z):
        b = nm(x, y, z)
        return b != 'air' and b != 'water' and b != 'lava'

    targets = []
    for i, (b, p) in enumerate(info):
        if (b in PLANTS or b in TALL or b in ('dead_bush', 'cactus', 'sugar_cane', 'lily_pad', 'cocoa', 'vine', 'ladder')
                or b.endswith('_carpet') or b in ('lantern', 'soul_lantern', 'torch', 'wall_torch', 'cake', 'rail', 'bell')
                or b.endswith('_door') or b.endswith('_wall_sign') or b.endswith('_wall_banner') or b.endswith('_sign')
                or b.endswith('_banner') or b.endswith('pressure_plate') or b.endswith('_button') or b == 'candle'
                or b.endswith('_candle') or b == 'tripwire_hook' or b in GRAVITY_FIX or b.endswith('concrete_powder')):
            targets.append(i)
    mask = np.isin(a, np.array(targets, dtype=a.dtype))
    ys, zs, xs = np.nonzero(mask)
    removed = fixed = 0
    stats = {}
    for y, z, x in zip(ys.tolist(), zs.tolist(), xs.tolist()):
        i = int(a[y, z, x])
        b, p = info[i]
        below = nm(x, y - 1, z)
        new = None  # None = keep, 'air' = remove, other = replace
        if b in PLANTS:
            if below not in SOIL:
                new = 'air'
        elif b in TALL:
            half = p.get('half', 'lower')
            if half == 'lower':
                if below not in SOIL or nm(x, y + 1, z) != b:
                    new = 'air'
            elif below != b:
                new = 'air'
        elif b == 'dead_bush':
            if below not in SOIL and below not in ('sand', 'red_sand') and 'terracotta' not in below:
                new = 'air'
        elif b == 'cactus':
            if below not in ('sand', 'red_sand', 'cactus') or any(solid(x + dx, y, z + dz) for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                new = 'air'
        elif b == 'sugar_cane':
            ok = below == 'sugar_cane' or (below in SOIL | {'sand', 'red_sand'} and
                                          any(nm(x + dx, y - 1, z + dz) == 'water' for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1))))
            if not ok:
                new = 'air'
        elif b == 'lily_pad':
            if below != 'water':
                new = 'air'
        elif b.endswith('_carpet') or b in ('cake', 'rail', 'candle') or b.endswith('_candle') or \
                b.endswith('pressure_plate'):
            if not solid(x, y - 1, z):
                new = 'air'
        elif b in ('lantern', 'soul_lantern'):
            hanging = p.get('hanging', 'false') == 'true'
            up, dn = solid(x, y + 1, z), solid(x, y - 1, z)
            if hanging and not up:
                new = f'{b}[hanging=false]' if dn else 'air'
            elif not hanging and not dn:
                new = f'{b}[hanging=true]' if up else 'air'
        elif b == 'torch':
            if not solid(x, y - 1, z):
                new = 'air'
        elif b.endswith('_wall_sign') and 'hanging' not in b:
            f = p.get('facing', 'north')
            dx, dz = DVEC[f]
            if not solid(x - dx, y, z - dz):
                # free-standing information sign: convert to a standing sign on a wooden post
                wood = b[:-len('_wall_sign')]
                rot = {'south': 0, 'west': 4, 'north': 8, 'east': 12}[f]
                depth = 0
                while depth < 7 and not solid(x, y - 1 - depth, z):
                    depth += 1
                if depth >= 7:
                    new = 'air'
                else:
                    post = f'{wood}_fence' if wood not in ('crimson', 'warped') else 'oak_fence'
                    for k in range(depth):
                        a[y - 1 - k, z, x] = world.id(post)
                    new = f'{wood}_sign[rotation={rot}]'
        elif b in ('wall_torch', 'ladder', 'tripwire_hook') or b.endswith('_wall_banner'):
            f = p.get('facing', 'north')
            dx, dz = DVEC[f]
            if not solid(x - dx, y, z - dz):
                new = 'air'
        elif b == 'cocoa':
            f = p.get('facing', 'north')
            dx, dz = DVEC[f]
            if nm(x + dx, y, z + dz) not in ('jungle_log', 'jungle_wood', 'stripped_jungle_log'):
                new = 'air'
        elif b == 'vine':
            if not any(p.get(d) == 'true' and solid(x + DVEC[d][0], y, z + DVEC[d][1]) for d in DVEC):
                new = 'air'
        elif b.endswith('_sign') and 'wall' not in b and 'hanging' not in b:
            if not solid(x, y - 1, z):
                new = 'air'
        elif b.endswith('_banner') and 'wall' not in b:
            if not solid(x, y - 1, z):
                new = 'air'
        elif b.endswith('_button'):
            face = p.get('face', 'wall')
            if face == 'floor' and not solid(x, y - 1, z):
                new = 'air'
        elif b == 'bell':
            if p.get('attachment') == 'floor' and not solid(x, y - 1, z):
                new = 'air'
        elif b.endswith('_door'):
            half = p.get('half', 'lower')
            if half == 'lower':
                if not nm(x, y + 1, z).endswith('_door') or not solid(x, y - 1, z):
                    new = 'air'
            elif not nm(x, y - 1, z).endswith('_door'):
                new = 'air'
        elif b in GRAVITY_FIX or b.endswith('concrete_powder'):
            if nm(x, y - 1, z) in ('air', 'water') and y > 6:
                new = GRAVITY_FIX.get(b, b.replace('_powder', ''))
        if new is not None:
            if new == 'air':
                removed += 1
            else:
                fixed += 1
            stats[(b, new.split('[')[0])] = stats.get((b, new.split('[')[0]), 0) + 1
            a[y, z, x] = world.id(new)
            if (x, y, z) in world.tiles and 'sign' not in new:
                del world.tiles[(x, y, z)]
    # a second sweep removes upper halves orphaned by the first sweep
    for y, z, x in zip(ys.tolist(), zs.tolist(), xs.tolist()):
        b = nm(x, y, z)
        if (b.endswith('_door') or b in TALL) and info[int(a[y, z, x])][1].get('half') == 'upper' and nm(x, y - 1, z) != b:
            a[y, z, x] = 0
            removed += 1
    log(f'  support pass: removed {removed} unsupported blocks, adjusted {fixed}')
    top = sorted(stats.items(), key=lambda kv: -kv[1])[:12]
    log('    ' + ', '.join(f'{a_}->{b_}:{n}' for (a_, b_), n in top))
    return removed, fixed
