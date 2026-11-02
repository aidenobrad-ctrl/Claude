"""Street furniture, landscaping and small decorations."""
import math
import random

from .const import G
from .geom import disc, ring, circle, line, ellipsoid
from .world import DVEC, OPP, hash2, hash3, pick

FLOWERS = ['dandelion', 'poppy', 'blue_orchid', 'allium', 'azure_bluet', 'red_tulip', 'orange_tulip',
           'white_tulip', 'pink_tulip', 'oxeye_daisy', 'cornflower', 'lily_of_the_valley']
TALL_FLOWERS = ['sunflower', 'lilac', 'rose_bush', 'peony']

LAMP_LIGHTS = 0


def can_place(w, x, y, z, h=1):
    for k in range(h):
        if not w.is_air(x, y + k, z):
            return False
    return not w.is_air(x, y - 1, z)


# ---------------------------------------------------------------------------------------------
# Lamps
# ---------------------------------------------------------------------------------------------
def lamp(w, x, y, z, style='classic', arm=None):
    """Free-standing street lamp. arm: 'x' or 'z' adds a double arm with hanging lanterns."""
    if not can_place(w, x, y, z, 5):
        return False
    s = w.set
    if style == 'classic':
        s(x, y, z, 'polished_blackstone_wall')
        s(x, y + 1, z, 'polished_blackstone_wall')
        s(x, y + 2, z, 'dark_oak_fence')
        s(x, y + 3, z, 'dark_oak_fence')
        s(x, y + 4, z, 'dark_oak_fence')
        s(x, y + 5, z, 'lantern[hanging=false]')
        if arm:
            dx, dz = (1, 0) if arm == 'x' else (0, 1)
            for sg in (-1, 1):
                if w.is_air(x + dx * sg, y + 4, z + dz * sg) and w.is_air(x + dx * sg, y + 3, z + dz * sg):
                    s(x + dx * sg, y + 4, z + dz * sg, 'dark_oak_fence')
                    s(x + dx * sg, y + 3, z + dz * sg, 'lantern[hanging=true]')
    elif style == 'medieval':
        s(x, y, z, 'chiseled_stone_bricks')
        s(x, y + 1, z, 'stone_brick_wall')
        s(x, y + 2, z, 'stone_brick_wall')
        s(x, y + 3, z, 'spruce_fence')
        s(x, y + 4, z, 'spruce_fence')
        if arm:
            dx, dz = (1, 0) if arm == 'x' else (0, 1)
            for sg in (-1, 1):
                if w.is_air(x + dx * sg, y + 4, z + dz * sg) and w.is_air(x + dx * sg, y + 3, z + dz * sg):
                    s(x + dx * sg, y + 4, z + dz * sg, 'spruce_fence')
                    s(x + dx * sg, y + 3, z + dz * sg, 'lantern[hanging=true]')
        s(x, y + 5, z, 'lantern[hanging=false]')
    elif style == 'scifi':
        s(x, y, z, 'quartz_pillar[axis=y]')
        s(x, y + 1, z, 'iron_bars')
        s(x, y + 2, z, 'iron_bars')
        s(x, y + 3, z, 'iron_bars')
        s(x, y + 4, z, 'sea_lantern')
        s(x, y + 5, z, 'smooth_quartz_slab[type=bottom]')
        if arm:
            dx, dz = (1, 0) if arm == 'x' else (0, 1)
            for sg in (-1, 1):
                if w.is_air(x + dx * sg, y + 4, z + dz * sg):
                    s(x + dx * sg, y + 4, z + dz * sg, 'end_rod[facing=%s]' % (
                        ('east' if sg > 0 else 'west') if arm == 'x' else ('south' if sg > 0 else 'north')))
    elif style == 'western':
        s(x, y, z, 'stripped_spruce_log[axis=y]')
        for k in range(1, 5):
            s(x, y + k, z, 'spruce_fence')
        s(x, y + 5, z, 'lantern[hanging=false]')
        if arm:
            dx, dz = (1, 0) if arm == 'x' else (0, 1)
            if w.is_air(x + dx, y + 4, z + dz):
                s(x + dx, y + 4, z + dz, 'spruce_fence')
                s(x + dx, y + 3, z + dz, 'lantern[hanging=true]')
    elif style == 'tropical':
        s(x, y, z, 'bamboo_block[axis=y]')
        s(x, y + 1, z, 'bamboo_fence')
        s(x, y + 2, z, 'bamboo_fence')
        s(x, y + 3, z, 'bamboo_fence')
        s(x, y + 4, z, 'torch')
    elif style == 'cartoon':
        s(x, y, z, 'white_concrete')
        s(x, y + 1, z, 'cherry_fence')
        s(x, y + 2, z, 'cherry_fence')
        s(x, y + 3, z, 'cherry_fence')
        s(x, y + 4, z, 'pearlescent_froglight[axis=y]')
        s(x, y + 5, z, 'pink_carpet')
        if arm:
            dx, dz = (1, 0) if arm == 'x' else (0, 1)
            for sg in (-1, 1):
                if w.is_air(x + dx * sg, y + 3, z + dz * sg):
                    s(x + dx * sg, y + 3, z + dz * sg, 'cherry_fence')
                    s(x + dx * sg, y + 2, z + dz * sg, 'lantern[hanging=true]')
    elif style == 'pirate':
        s(x, y, z, 'cobblestone')
        s(x, y + 1, z, 'dark_oak_fence')
        s(x, y + 2, z, 'dark_oak_fence')
        s(x, y + 3, z, 'dark_oak_fence')
        s(x, y + 4, z, 'dark_oak_fence')
        dx, dz = (1, 0) if arm in (None, 'x') else (0, 1)
        if w.is_air(x + dx, y + 4, z + dz):
            s(x + dx, y + 4, z + dz, 'dark_oak_fence')
            s(x + dx, y + 3, z + dz, 'chain[axis=y]')
            s(x + dx, y + 2, z + dz, 'lantern[hanging=true]')
        s(x, y + 5, z, 'lantern[hanging=false]')
    elif style == 'hub':
        # grand double-head lamp
        s(x, y, z, 'polished_andesite')
        s(x, y + 1, z, 'andesite_wall')
        s(x, y + 2, z, 'dark_oak_fence')
        s(x, y + 3, z, 'dark_oak_fence')
        s(x, y + 4, z, 'dark_oak_fence')
        s(x, y + 5, z, 'dark_oak_fence')
        for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            if w.is_air(x + dx, y + 5, z + dz):
                s(x + dx, y + 5, z + dz, 'dark_oak_fence')
                s(x + dx, y + 4, z + dz, 'lantern[hanging=true]')
        s(x, y + 6, z, 'lantern[hanging=false]')
    return True


# ---------------------------------------------------------------------------------------------
# Benches, bins, tables
# ---------------------------------------------------------------------------------------------
def bench(w, x, y, z, look, mat='dark_oak', length=3, arms=True):
    """A bench whose occupants look toward `look`. It extends `length` blocks to the right of the viewer."""
    ldx, ldz = DVEC[look]
    ax, az = -ldz, ldx  # along-bench direction (perpendicular to look)
    cells = [(x + ax * i, z + az * i) for i in range(length)]
    ends = [(x - ax, z - az), (x + ax * length, z + az * length)]
    for cx, cz in cells + (ends if arms else []):
        if not can_place(w, cx, y, cz):
            return False
    back = OPP[look]
    for cx, cz in cells:
        w.set(cx, y, cz, f'{mat}_stairs[facing={back},half=bottom]')
    if arms:
        woods = ('oak', 'spruce', 'birch', 'jungle', 'acacia', 'dark_oak', 'mangrove', 'cherry', 'bamboo',
                 'crimson', 'warped')
        trap = f'{mat}_trapdoor' if mat in woods else 'iron_trapdoor'
        along = {(1, 0): 'east', (-1, 0): 'west', (0, 1): 'south', (0, -1): 'north'}[(ax, az)]
        w.set(ends[0][0], y, ends[0][1], f'{trap}[facing={OPP[along]},half=bottom,open=true]')
        w.set(ends[1][0], y, ends[1][1], f'{trap}[facing={along},half=bottom,open=true]')
    return True


def trash_can(w, x, y, z, style='classic'):
    if not can_place(w, x, y, z, 2):
        return False
    if style in ('classic', 'hub', 'scifi', 'cartoon'):
        w.set(x, y, z, 'cauldron')
        lid = {'classic': 'dark_oak_trapdoor', 'hub': 'dark_oak_trapdoor', 'scifi': 'iron_trapdoor',
               'cartoon': 'cherry_trapdoor'}[style]
        w.set(x, y + 1, z, f'{lid}[facing=north,half=bottom,open=false]')
    elif style in ('western', 'pirate'):
        w.set(x, y, z, 'barrel[facing=up,open=false]')
    else:
        w.set(x, y, z, 'composter[level=0]')
    return True


def umbrella_table(w, x, y, z, colors=('red_wool', 'white_wool'), wood='oak', chairs=True):
    """Patio umbrella over a small table with four chairs."""
    for dx in range(-2, 3):
        for dz in range(-2, 3):
            if not w.is_air(x + dx, y, z + dz) or not w.is_air(x + dx, y + 3, z + dz):
                return False
    w.set(x, y, z, f'{wood}_fence')
    w.set(x, y + 1, z, f'{wood}_fence')
    w.set(x, y + 2, z, f'{wood}_fence')
    w.set(x, y + 3, z, colors[0])
    for dx in range(-1, 2):
        for dz in range(-1, 2):
            if dx or dz:
                w.set(x + dx, y + 3, z + dz, colors[(abs(dx) + abs(dz)) % len(colors)])
    for dx, dz in ((2, 0), (-2, 0), (0, 2), (0, -2)):
        w.set(x + dx, y + 3, z + dz, colors[0].replace('_wool', '_carpet') if '_wool' in colors[0] else colors[0])
    if chairs:
        for d in ('north', 'south', 'east', 'west'):
            dx, dz = DVEC[d]
            w.set(x + dx, y, z + dz, f'{wood}_trapdoor[facing={d},half=top,open=false]')
            w.set(x + 2 * dx, y, z + 2 * dz, f'{wood}_stairs[facing={d},half=bottom]')
    return True


def table_set(w, x, y, z, wood='oak', top='white_carpet', seats=4, along='x'):
    """Indoor table (fence + pressure plate/carpet) with stair chairs."""
    w.set(x, y, z, f'{wood}_fence')
    w.set(x, y + 1, z, top)
    dirs = ['east', 'west'] if along == 'x' else ['north', 'south']
    if seats >= 4:
        dirs = ['north', 'south', 'east', 'west']
    for d in dirs:
        dx, dz = DVEC[d]
        if w.is_air(x + dx, y, z + dz):
            w.set(x + dx, y, z + dz, f'{wood}_stairs[facing={d},half=bottom]')


def picnic_table(w, x, y, z, along='x', wood='spruce', length=3):
    ax, az = (1, 0) if along == 'x' else (0, 1)
    px, pz = az, ax
    for i in range(length):
        for k in (-1, 0, 1):
            if not can_place(w, x + ax * i + px * k, y, z + az * i + pz * k):
                return False
    for i in range(length):
        cx, cz = x + ax * i, z + az * i
        w.set(cx, y, cz, f'{wood}_slab[type=top]')
        for k, d in ((-1, None), (1, None)):
            sx, sz = cx + px * k, cz + pz * k
            look = {(1, 0): 'west', (-1, 0): 'east', (0, 1): 'north', (0, -1): 'south'}[(px * k, pz * k)]
            w.set(sx, y, sz, f'{wood}_stairs[facing={OPP[look]},half=bottom]')
    return True


def deck_chair(w, x, y, z, look, wood='birch', towel='light_blue_carpet'):
    """Sun lounger: stairs headrest + slab body."""
    dx, dz = DVEC[look]
    w.set(x, y, z, f'{wood}_stairs[facing={OPP[look]},half=bottom]')
    w.set(x + dx, y, z + dz, f'{wood}_slab[type=bottom]')
    w.set(x + 2 * dx, y, z + 2 * dz, f'{wood}_slab[type=bottom]')


# ---------------------------------------------------------------------------------------------
# Plants
# ---------------------------------------------------------------------------------------------
def flower_at(w, x, y, z, seed=0, palette=None):
    r = hash2(x, z, seed + 77)
    pal = palette or FLOWERS
    if r < 0.12 and palette is None:
        f = TALL_FLOWERS[int(hash2(x, z, seed + 5) * len(TALL_FLOWERS)) % len(TALL_FLOWERS)]
        if w.is_air(x, y + 1, z):
            w.set(x, y, z, f'{f}[half=lower]')
            w.set(x, y + 1, z, f'{f}[half=upper]')
            return
    w.set(x, y, z, pal[int(hash2(x, z, seed + 9) * len(pal)) % len(pal)])


def flower_bed(w, x0, z0, x1, z1, y=G, border='stone_brick_wall', density=0.8, seed=0, palette=None,
               soil='grass_block', hedge=None):
    """Rectangular raised bed: border wall/hedge around grass with flowers."""
    for x in range(x0, x1 + 1):
        for z in range(z0, z1 + 1):
            edge = x in (x0, x1) or z in (z0, z1)
            if edge and border:
                w.set(x, y, z, soil)
                if hedge:
                    w.set(x, y + 1, z, hedge)
                else:
                    w.set(x, y + 1, z, border)
            else:
                w.set(x, y, z, soil)
                if hash2(x, z, seed) < density and w.is_air(x, y + 1, z):
                    flower_at(w, x, y + 1, z, seed, palette)


def flower_patch(w, cells, y=G, density=0.7, seed=0, palette=None, soil='grass_block'):
    for (x, z) in cells:
        w.set(x, y, z, soil)
        if hash2(x, z, seed) < density and w.is_air(x, y + 1, z):
            flower_at(w, x, y + 1, z, seed, palette)


def hedge(w, cells, y, h=1, leaves='oak_leaves'):
    for (x, z) in cells:
        for k in range(h):
            w.set(x, y + k, z, f'{leaves}[persistent=true]')


def leaves(t):
    return f'{t}_leaves[persistent=true]'


def _blob(w, cx, cy, cz, rx, ry, rz, leaf, rnd, hollow_chance=0.0):
    for (x, y, z) in ellipsoid(cx, cy, cz, rx, ry, rz):
        dx, dy, dz = (x - cx) / rx, (y - cy) / ry, (z - cz) / rz
        d = dx * dx + dy * dy + dz * dz
        if d > 0.75 and rnd.random() < 0.35:
            continue
        w.set_if_air(x, y, z, leaf)


def tree(w, x, y, z, kind='oak', size=1.0, seed=None):
    """Grow a decorative tree with its base at (x, y, z). Returns False if blocked."""
    rnd = random.Random(seed if seed is not None else (x * 73856093) ^ (z * 19349663))
    s = size
    if kind in ('oak', 'birch', 'cherry', 'jungle_small', 'mangrove'):
        log = {'oak': 'oak_log', 'birch': 'birch_log', 'cherry': 'cherry_log', 'jungle_small': 'jungle_log',
               'mangrove': 'mangrove_log'}[kind]
        lf = leaves({'oak': 'oak', 'birch': 'birch', 'cherry': 'cherry', 'jungle_small': 'jungle',
                     'mangrove': 'mangrove'}[kind])
        h = int(round((5 + rnd.randint(0, 2)) * s))
        if kind == 'birch':
            h += 1
        for k in range(h):
            w.set(x, y + k, z, f'{log}[axis=y]')
        r = (2.6 if kind != 'cherry' else 3.4) * s
        _blob(w, x, y + h - 1, z, r, r * 0.75, r, lf, rnd)
        if kind == 'cherry':
            for (px, pz) in disc(x, z, 3):
                if rnd.random() < 0.35 and w.is_air(px, y, pz) and w.name(px, y - 1, pz) == 'grass_block':
                    w.set(px, y, pz, f'pink_petals[facing=north,flower_amount={rnd.randint(1, 4)}]')
        # branches
        if s > 1.1:
            for d in rnd.sample(['north', 'east', 'south', 'west'], 2):
                dx, dz = DVEC[d]
                ax = 'x' if dx else 'z'
                w.set(x + dx, y + h - 3, z + dz, f'{log}[axis={ax}]')
                _blob(w, x + 2 * dx, y + h - 2, z + 2 * dz, 1.8, 1.4, 1.8, lf, rnd)
    elif kind in ('spruce', 'pine'):
        h = int(round((9 + rnd.randint(0, 3)) * s))
        for k in range(h):
            w.set(x, y + k, z, 'spruce_log[axis=y]')
        lf = leaves('spruce')
        base = 2 if kind == 'spruce' else h - 5
        maxr = 3.2 * s if kind == 'spruce' else 2.2 * s
        for k in range(base, h + 1):
            t = (k - base) / max(1, (h - base))
            r = maxr * (1 - t) + 0.6
            if (k - base) % 2 == 1:
                r *= 0.7
            for (px, pz) in disc(x, z, r):
                w.set_if_air(px, y + k, pz, lf)
        w.set_if_air(x, y + h + 1, z, lf)
    elif kind == 'palm':
        h = int(round((7 + rnd.randint(0, 3)) * s))
        ang = rnd.random() * math.tau
        lx, lz = math.cos(ang), math.sin(ang)
        top = None
        for k in range(h):
            off = 2.5 * (k / h) ** 2
            px, pz = int(round(x + lx * off)), int(round(z + lz * off))
            w.set(px, y + k, pz, 'jungle_log[axis=y]')
            top = (px, y + k, pz)
        tx, ty, tz = top
        lf = leaves('jungle')
        w.set(tx, ty + 1, tz, lf)
        for i in range(8):
            a = i * math.tau / 8 + rnd.random() * 0.3
            L = 4 + rnd.randint(0, 1)
            for k in range(1, L + 1):
                fx = int(round(tx + math.cos(a) * k))
                fz = int(round(tz + math.sin(a) * k))
                fy = ty + 1 - (1 if k > L - 2 else 0) - (1 if k == L else 0)
                w.set_if_air(fx, fy, fz, lf)
        # coconuts
        for d in rnd.sample(['north', 'east', 'south', 'west'], 2):
            dx, dz = DVEC[d]
            w.set_if_air(tx + dx, ty - 1, tz + dz, f'cocoa[age=2,facing={OPP[d]}]')
    elif kind == 'acacia':
        h = int(round(5 * s)) + rnd.randint(0, 1)
        d = rnd.choice(['north', 'east', 'south', 'west'])
        dx, dz = DVEC[d]
        cx, cz = x, z
        for k in range(h):
            if k == h // 2:
                cx += dx
                cz += dz
            w.set(cx, y + k, cz, 'acacia_log[axis=y]')
        lf = leaves('acacia')
        for (px, pz) in disc(cx, cz, 3.3 * s):
            w.set_if_air(px, y + h, pz, lf)
        for (px, pz) in disc(cx, cz, 1.8 * s):
            w.set_if_air(px, y + h + 1, pz, lf)
    elif kind == 'dark_oak':
        h = int(round(6 * s)) + rnd.randint(0, 1)
        for k in range(h):
            for ox, oz in ((0, 0), (1, 0), (0, 1), (1, 1)):
                w.set(x + ox, y + k, z + oz, 'dark_oak_log[axis=y]')
        lf = leaves('dark_oak')
        _blob(w, x + 0.5, y + h, z + 0.5, 4.2 * s, 2.4 * s, 4.2 * s, lf, rnd)
    elif kind == 'topiary':
        w.set(x, y, z, 'oak_log[axis=y]')
        w.set(x, y + 1, z, 'oak_log[axis=y]')
        for (px, py, pz) in ellipsoid(x, y + 3, z, 1.6, 1.6, 1.6):
            w.set_if_air(px, py, pz, leaves('azalea'))
    elif kind == 'cone_topiary':
        w.set(x, y, z, 'spruce_log[axis=y]')
        for k in range(1, 6):
            r = max(0.4, 1.8 - k * 0.32)
            for (px, pz) in disc(x, z, r):
                w.set_if_air(px, y + k, pz, leaves('spruce'))
    elif kind == 'bush':
        lf = leaves(rnd.choice(['oak', 'azalea', 'flowering_azalea', 'birch']))
        for (px, py, pz) in ellipsoid(x, y, z, 1.6 * s, 1.2 * s, 1.6 * s):
            if py >= y:
                w.set_if_air(px, py, pz, lf)
    elif kind == 'jungle':
        h = int(round(12 * s))
        for k in range(h):
            for ox, oz in ((0, 0), (1, 0), (0, 1), (1, 1)):
                w.set(x + ox, y + k, z + oz, 'jungle_log[axis=y]')
        lf = leaves('jungle')
        _blob(w, x + 0.5, y + h, z + 0.5, 4.5 * s, 2.5 * s, 4.5 * s, lf, rnd)
        for k in range(3, h - 2, 3):
            d = rnd.choice(['north', 'east', 'south', 'west'])
            dx, dz = DVEC[d]
            w.set_if_air(x + (2 if dx > 0 else -1 if dx < 0 else 0), y + k, z + (2 if dz > 0 else -1 if dz < 0 else 0),
                         f'vine[{d if False else OPP[d]}=true]' if False else lf)
    return True


def lawn_trees(w, x0, z0, x1, z1, kinds, spacing=9, seed=0, y=G + 1, avoid=None):
    """Scatter trees on grass in a rectangle, only where there is grass and free air."""
    for x in range(x0, x1 + 1, spacing):
        for z in range(z0, z1 + 1, spacing):
            jx = x + int(hash2(x, z, seed) * spacing * 0.6)
            jz = z + int(hash2(z, x, seed + 1) * spacing * 0.6)
            if not (x0 <= jx <= x1 and z0 <= jz <= z1):
                continue
            ok = True
            for dx in range(-2, 3):
                for dz in range(-2, 3):
                    if w.name(jx + dx, y - 1, jz + dz) != 'grass_block' or not w.is_air(jx + dx, y, jz + dz):
                        ok = False
            if not ok:
                continue
            if avoid and avoid(jx, jz):
                continue
            k = kinds[int(hash2(jx, jz, seed + 3) * len(kinds)) % len(kinds)]
            tree(w, jx, y, jz, k, 0.9 + hash2(jx, jz, seed + 4) * 0.4)


# ---------------------------------------------------------------------------------------------
# Water features
# ---------------------------------------------------------------------------------------------
def fountain(w, cx, cz, r=6, y=G, rim='quartz_bricks', rim_top='smooth_quartz_slab[type=bottom]',
             tiers=2, finial='sea_lantern', bowl='quartz_block'):
    """Round fountain: sunken pool, rim, tiered bowls with water and a lit finial."""
    for (x, z) in disc(cx, cz, r - 1):
        w.set(x, y - 1, z, 'smooth_stone')
        w.set(x, y, z, 'water')
        w.clear(x, y + 1, z, x, y + 3, z)
    for (x, z) in circle(cx, cz, r):
        w.set(x, y - 1, z, rim)
        w.set(x, y, z, rim)
        w.set(x, y + 1, z, rim_top)
    # center pedestal + tiers
    yy = y
    rr = max(1.5, r * 0.45)
    for t in range(tiers):
        h = 3 if t == 0 else 2
        for k in range(h):
            w.set(cx, yy + k, cz, 'quartz_pillar[axis=y]')
        yy += h
        for (x, z) in disc(cx, cz, rr):
            w.set(x, yy, z, bowl)
        for (x, z) in circle(cx, cz, rr):
            w.set(x, yy + 1, z, 'smooth_quartz_slab[type=bottom]')
        for (x, z) in disc(cx, cz, rr - 1):
            if (x, z) != (cx, cz):
                w.set(x, yy + 1, z, 'water')
        w.set(cx, yy + 1, cz, 'quartz_pillar[axis=y]')
        yy += 1
        rr = max(1.2, rr * 0.55)
    w.set(cx, yy + 1, cz, 'quartz_pillar[axis=y]')
    w.set(cx, yy + 2, cz, finial)
    w.set(cx, yy + 3, cz, 'end_rod[facing=up]')
    # underwater lights
    for (x, z) in circle(cx, cz, r - 2):
        if hash2(x, z, 11) < 0.25:
            w.set(x, y - 1, z, 'sea_lantern')


def pond(w, cells, depth=2, y=G, edge='mossy_cobblestone', bed='sand', lily=0.05, seed=0, reeds=True):
    """Natural pond over a set of (x,z) cells."""
    cs = set(cells)
    for (x, z) in cs:
        edge_cell = any((x + dx, z + dz) not in cs for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1)))
        d = 1 if edge_cell else depth
        w.clear(x, y - d + 1, z, x, y + 3, z)
        for k in range(d):
            w.set(x, y - k, z, 'water')
        w.set(x, y - d, z, bed if not edge_cell else 'clay')
        if not edge_cell and hash2(x, z, seed) < lily:
            w.set(x, y + 1, z, 'lily_pad')
    # shore
    for (x, z) in cs:
        for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, nz = x + dx, z + dz
            if (nx, nz) not in cs:
                nm = w.name(nx, y, nz)
                if nm in ('grass_block', 'air', 'dirt'):
                    w.set(nx, y, nz, edge if hash2(nx, nz, seed + 1) < 0.5 else 'grass_block')
                    w.set(nx, y - 1, nz, 'dirt')
                    if reeds and hash2(nx, nz, seed + 2) < 0.15 and w.is_air(nx, y + 1, nz):
                        w.set(nx, y + 1, nz, 'sugar_cane')
                        if w.name(nx, y, nz) != 'grass_block':
                            w.set(nx, y, nz, 'grass_block')


# ---------------------------------------------------------------------------------------------
# Flags, signs, misc
# ---------------------------------------------------------------------------------------------
def flagpole(w, x, y, z, h=12, flag=('red_wool', 'white_wool'), direction='east', pole='iron_bars'):
    for k in range(h):
        w.set(x, y + k, z, pole)
    w.set(x, y + h, z, 'lightning_rod[facing=up]')
    dx, dz = DVEC[direction]
    for i in range(1, 5):
        for j in range(3):
            c = flag[(j + (i // 2)) % len(flag)] if len(flag) > 1 else flag[0]
            w.set(x + dx * i, y + h - 1 - j, z + dz * i, c)


def banner_post(w, x, y, z, color='red', facing='south', h=4, post='dark_oak_fence'):
    for k in range(h):
        w.set(x, y + k, z, post)
    w.set(x, y + h, z, 'lantern')
    dx, dz = DVEC[facing]
    w.set(x + dx, y + h - 1, z + dz, f'{color}_wall_banner[facing={facing}]')


def potted(w, x, y, z, seed=0):
    plants = ['potted_red_tulip', 'potted_azure_bluet', 'potted_fern', 'potted_allium', 'potted_cornflower',
              'potted_oxeye_daisy', 'potted_orange_tulip', 'potted_pink_tulip', 'potted_azalea_bush',
              'potted_flowering_azalea_bush', 'potted_blue_orchid', 'potted_lily_of_the_valley']
    w.set(x, y, z, plants[int(hash2(x, z, seed) * len(plants)) % len(plants)])


def string_lights(w, p0, p1, sag=2, light='lantern[hanging=true]', every=3, wire='chain[axis=x]'):
    """A sagging line of hanging lanterns between two points (p0,p1 = (x,y,z) attach points)."""
    x0, y0, z0 = p0
    x1, y1, z1 = p1
    n = int(max(abs(x1 - x0), abs(z1 - z0)))
    if n < 2:
        return
    for i in range(1, n):
        t = i / n
        x = int(round(x0 + (x1 - x0) * t))
        z = int(round(z0 + (z1 - z0) * t))
        yy = int(round(y0 + (y1 - y0) * t - sag * 4 * t * (1 - t)))
        if i % every == 0 and w.is_air(x, yy, z):
            w.set(x, yy, z, light)


def lit_bollard(w, x, y, z, style='classic'):
    if not can_place(w, x, y, z):
        return
    w.set(x, y, z, {'classic': 'polished_blackstone_wall', 'scifi': 'quartz_pillar[axis=y]',
                    'western': 'spruce_fence', 'tropical': 'bamboo_fence', 'cartoon': 'white_concrete',
                    'pirate': 'dark_oak_fence', 'medieval': 'stone_brick_wall', 'hub': 'andesite_wall'}.get(style, 'stone_brick_wall'))
    if w.is_air(x, y + 1, z):
        w.set(x, y + 1, z, 'lantern' if style != 'scifi' else 'end_rod[facing=up]')
