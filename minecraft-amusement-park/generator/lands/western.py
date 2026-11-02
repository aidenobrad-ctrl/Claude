"""Dusty Gulch: Wild-West town, Gold Rush Gulch log flume mountain and the Dust Devil Speedway go-karts."""
import math
import random

import numpy as np

from ..const import G
from ..geom import disc, circle, ring, catmull_rom, line
from ..world import Builder, hash2, hash3
from ..font import giant_text
from .. import decor
from ..buildings import style, building, restroom, info_booth, food_kiosk, cone_roof, box_walls, gable_roof

WEST = style(wall='spruce_planks', base='spruce_planks', pillar='stripped_spruce_log[axis=y]',
             trim='stripped_spruce_log[axis=x]', floor='spruce_planks', ceiling='spruce_planks',
             window='glass_pane', roof_stairs='spruce_stairs', roof_slab='spruce_slab', roof_block='spruce_planks',
             door='spruce', sign_wood='spruce', carpet='brown_carpet', counter='stripped_spruce_wood[axis=y]',
             counter_top='spruce_slab[type=bottom]', shelf=('barrel[facing=up]', 'chest[facing=north]', 'hay_block[axis=y]'),
             porch_floor='spruce_planks', porch_roof='spruce_slab[type=bottom]', porch_post='spruce_fence',
             porch_rail='spruce_fence')
WALLS = ['spruce_planks', 'oak_planks', 'dark_oak_planks', 'birch_planks', 'stripped_spruce_wood[axis=y]',
         'red_terracotta', 'orange_terracotta', 'jungle_planks', 'acacia_planks', 'white_terracotta']

STRATA = ['orange_terracotta', 'terracotta', 'red_terracotta', 'orange_terracotta', 'yellow_terracotta',
          'white_terracotta', 'terracotta', 'brown_terracotta', 'light_gray_terracotta', 'orange_terracotta',
          'red_terracotta', 'terracotta']

# ---------------------------------------------------------------------------------------------
# Mountain
# ---------------------------------------------------------------------------------------------
BUMPS = [(62, 72, 40, 22), (98, 50, 26, 14), (44, 96, 24, 14), (96, 96, 20, 13), (36, 52, 18, 12)]
MC, MR = (70, 72), (52, 46)


def mountain_height(x, z):
    ex = ((x - MC[0]) / MR[0]) ** 2 + ((z - MC[1]) / MR[1]) ** 2
    if ex >= 1.0:
        return 0
    fall = 1.0 - ex ** 1.5
    h = 0.0
    for (bx, bz, bh, bs) in BUMPS:
        h = max(h, bh * math.exp(-((x - bx) ** 2 + (z - bz) ** 2) / (2 * bs * bs)))
    h = h * fall + 3 * fall
    h += (hash2(x // 3, z // 3, 77) - 0.5) * 2.5
    return max(0, int(round(h)))


def build_mountain(w):
    x0, x1 = 22, 124
    z0, z1 = 24, 120
    hm = {}
    for x in range(x0 - 1, x1 + 2):
        for z in range(z0 - 1, z1 + 2):
            hm[(x, z)] = mountain_height(x, z)
    for x in range(x0, x1 + 1):
        for z in range(z0, z1 + 1):
            h = hm[(x, z)]
            if h <= 0:
                continue
            nmin = min(hm[(x + 1, z)], hm[(x - 1, z)], hm[(x, z + 1)], hm[(x, z - 1)])
            ybot = max(G + 1, G + nmin - 3)
            for y in range(ybot, G + h + 1):
                w.set(x, y, z, STRATA[(y + int(hash2(x // 6, z // 6, 3) * 2)) % len(STRATA)])
            top = G + h
            r = hash2(x, z, 12)
            w.set(x, top, z, 'coarse_dirt' if r < 0.35 else 'red_sand' if r < 0.55 else
                  'terracotta' if r < 0.75 else 'grass_block' if h < 12 else 'orange_terracotta')
            w.set(x, G, z, 'coarse_dirt')
    return hm


def mountain_dressing(w, hm):
    for (x, z), h in hm.items():
        if h <= 2:
            continue
        top = G + h
        if not w.is_air(x, top + 1, z):
            continue
        nm = w.name(x, top, z)
        r = hash2(x, z, 20)
        if nm == 'red_sand' and r < 0.08 and all(w.is_air(x + dx, top + 1, z + dz) for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1))):
            for k in range(1, 3 + int(r * 20) % 2):
                w.set(x, top + k, z, 'cactus[age=0]')
        elif nm in ('coarse_dirt', 'red_sand', 'terracotta') and r < 0.12:
            w.set(x, top + 1, z, 'dead_bush')
        elif nm == 'grass_block' and r < 0.05:
            decor.tree(w, x, top + 1, z, 'acacia', 0.8)


# ---------------------------------------------------------------------------------------------
# Log flume
# ---------------------------------------------------------------------------------------------
FLUME = [(92, 122, G), (40, 122, G), (40, 108, G), (40, 78, G + 30, 'lift'), (68, 78, G + 28),
         (68, 90, G + 22), (102, 90, G + 21), (102, 112, G, 'drop'), (102, 120, G)]


def _seg_cells(p, q):
    (x0, z0), (x1, z1) = p, q
    n = max(abs(x1 - x0), abs(z1 - z0))
    dx = (x1 - x0) // n if n else 0
    dz = (z1 - z0) // n if n else 0
    return [(x0 + dx * i, z0 + dz * i) for i in range(n + 1)], (dx, dz)


def log_flume(w, paths, hm):
    cells_all = []
    for i in range(len(FLUME) - 1):
        a, b = FLUME[i], FLUME[i + 1]
        kind = b[3] if len(b) > 3 else 'run'
        cells, (dx, dz) = _seg_cells((a[0], a[1]), (b[0], b[1]))
        n = len(cells) - 1
        for k, (x, z) in enumerate(cells):
            t = k / n if n else 0
            yw = int(round(a[2] + (b[2] - a[2]) * t))
            cells_all.append((x, z, yw, dx, dz, kind, k == 0 or k == n))
    # carve the corridor (tunnels through the mountain) and build the channel
    for (x, z, yw, dx, dz, kind, corner) in cells_all:
        px, pz = -dz, dx
        offs = range(-3, 4)
        for o in offs:
            for (cx, cz) in ((x + px * o, z + pz * o),) + (((x + dx * o, z + dz * o),) if corner else ()):
                for y in range(yw, yw + 6):
                    if y > G or abs(o) <= 2:
                        w.set(cx, y, cz, 'air')
    for (x, z, yw, dx, dz, kind, corner) in cells_all:
        px, pz = -dz, dx
        offsets = [(x + px * o, z + pz * o, o) for o in range(-2, 3)]
        if corner:
            offsets += [(x + ax, z + az, max(abs(ax), abs(az))) for ax in range(-2, 3) for az in range(-2, 3)]
        for (cx, cz, o) in offsets:
            if kind == 'lift':
                # dry conveyor ramp
                if abs(o) <= 1:
                    face = 'north' if dz < 0 else 'south' if dz > 0 else 'east' if dx > 0 else 'west'
                    w.set(cx, yw - 1, cz, 'spruce_planks')
                    w.set(cx, yw, cz, f'spruce_stairs[facing={face},half=bottom]' if o else 'black_concrete')
                else:
                    w.set(cx, yw - 1, cz, 'spruce_planks')
                    w.set(cx, yw, cz, 'stripped_spruce_log[axis=y]')
                    w.set(cx, yw + 1, cz, 'spruce_fence')
                continue
            if abs(o) <= 1:
                w.set(cx, yw - 1, cz, 'spruce_planks')
                w.set(cx, yw, cz, 'water')
            else:
                w.set(cx, yw - 1, cz, 'spruce_planks')
                w.set(cx, yw, cz, 'stripped_spruce_log[axis=y]')
                w.set(cx, yw + 1, cz, 'stripped_spruce_log[axis=y]' if kind == 'drop' else 'spruce_planks')
                if kind == 'drop':
                    w.set(cx, yw + 2, cz, 'spruce_fence')
    # make sure no wall block sits inside the water of a neighbouring cell (corners)
    for (x, z, yw, dx, dz, kind, corner) in cells_all:
        if kind != 'lift':
            px, pz = -dz, dx
            for o in (-1, 0, 1):
                w.set(x + px * o, yw, z + pz * o, 'water')
                w.set(x + px * o, yw + 1, z + pz * o, 'air')
    # trestle supports where the channel is above ground
    for i, (x, z, yw, dx, dz, kind, corner) in enumerate(cells_all):
        if i % 4:
            continue
        px, pz = -dz, dx
        for o in (-2, 2):
            cx, cz = x + px * o, z + pz * o
            y = yw - 2
            while y > G and w.is_air(cx, y, cz):
                w.set(cx, y, cz, 'spruce_log[axis=y]' if o else 'spruce_fence')
                y -= 1
        if yw - 2 > G + 3:
            for y in range(G + 2, yw - 2, 4):
                for o in range(-1, 2):
                    cx, cz = x + px * o, z + pz * o
                    if w.is_air(cx, y, cz):
                        w.set(cx, y, cz, 'spruce_fence')
    # splash pool
    for x in range(90, 115):
        for z in range(108, 126):
            edge = x in (90, 114) or z in (108, 125)
            w.set(x, G - 2, z, 'spruce_planks' if not edge else 'stripped_spruce_log[axis=y]')
            if edge:
                for y in range(G - 1, G + 1):
                    w.set(x, y, z, 'stripped_spruce_log[axis=y]')
                w.set(x, G + 1, z, 'spruce_fence')
            else:
                w.set(x, G - 1, z, 'water')
                w.set(x, G, z, 'water')
                w.clear(x, G + 1, z, x, G + 4, z)
    for (x, z) in ((101, 108), (102, 108), (103, 108)):
        w.set(x, G + 1, z, 'air')
        w.set(x, G, z, 'water')
        w.set(x, G - 1, z, 'water')
    for z in (121, 122, 123):
        w.set(90, G, z, 'water')
        w.set(90, G + 1, z, 'air')
    # station: covered loading dock
    for x in range(56, 88):
        for z in range(117, 131):
            if z >= 125:
                w.set(x, G, z, 'spruce_planks')
                paths.mask[z, x] = True
            w.set(x, G + 6, z, 'spruce_slab[type=bottom]' if (x + z) % 2 else 'spruce_planks')
        for z in (117, 130):
            for y in range(G + 1, G + 6):
                if x % 6 == 2:
                    w.set(x, y, z, 'stripped_spruce_log[axis=y]')
            w.set(x, G + 5, z, 'spruce_stairs[facing=%s,half=top]' % ('south' if z == 117 else 'north'))
        if x % 6 == 5:
            w.set(x, G + 5, 124, 'lantern[hanging=true]')
    # station platform edge: remove the inner wall so riders can step in
    for x in range(60, 84):
        w.set(x, G + 1, 124, 'spruce_trapdoor[facing=north,half=bottom,open=false]' if False else 'air')
        w.set(x, G, 124, 'spruce_slab[type=top]')
    # log boats in the station
    for bx in (64, 74):
        for x in range(bx, bx + 5):
            w.set(x, G, 122, 'stripped_spruce_log[axis=x]')
        w.set(bx + 1, G + 1, 122, 'spruce_stairs[facing=east,half=bottom]')
        w.set(bx + 3, G + 1, 122, 'spruce_stairs[facing=east,half=bottom]')
        w.set(bx + 4, G + 1, 122, 'spruce_trapdoor[facing=west,half=bottom,open=true]')
    # queue corral
    for x in range(58, 86):
        for z in range(131, 139):
            w.set(x, G, z, 'spruce_planks' if z == 131 else 'coarse_dirt')
            paths.mask[z, x] = True
            if (z - 131) % 2 == 0 and z > 131 and not (x in (58, 59) and (z // 2) % 2 or x in (84, 85) and not (z // 2) % 2):
                w.set(x, G + 1, z, 'spruce_fence')
    # billboard with giant letters on the mountain flank
    for x in range(44, 101):
        for y in range(G + 9, G + 18):
            if x in (44, 100) or y in (G + 9, G + 17):
                w.set(x, y, 113, 'dark_oak_planks')
            else:
                w.set(x, y, 113, 'spruce_planks')
    for x in (46, 72, 98):
        for y in range(G + 1, G + 9):
            if w.is_air(x, y, 113) or w.name(x, y, 113).endswith('terracotta'):
                w.set(x, y, 113, 'spruce_log[axis=y]')
    giant_text(w, 'GOLD RUSH', 72, G + 10, 114, 'south', 'yellow_terracotta', scale=1)
    w.sign(70, G + 3, 139, ['GOLD RUSH', 'GULCH', 'Log Flume', 'You WILL get wet!'], wood='spruce',
           facing='south')
    # splash-zone viewing deck
    for x in range(90, 115):
        for z in range(126, 131):
            w.set(x, G, z, 'spruce_planks')
            paths.mask[z, x] = True
        w.set(x, G + 1, 130, 'spruce_fence' if x % 6 else 'lantern')
    w.label(72, 72, 'GOLD RUSH GULCH (log flume)', 'attraction')
    paths.draw([(70, 139), (70, 160)], 5, land='western')
    paths.draw([(102, 131), (102, 160)], 5, land='western')


# ---------------------------------------------------------------------------------------------
# Go-karts
# ---------------------------------------------------------------------------------------------
KART_PTS = [(142, 40), (175, 34), (206, 40), (210, 62), (188, 72), (206, 90), (198, 108), (160, 108),
            (148, 92), (170, 80), (152, 66), (138, 56)]


def go_karts(w, paths):
    pts = catmull_rom([(x, 0, z) for (x, z) in KART_PTS], samples_per_unit=4, closed=True)
    pts2 = [(p[0], p[2]) for p in pts]
    arr = np.array(pts2)
    # rasterise by distance to centreline
    x0, x1 = int(arr[:, 0].min()) - 9, int(arr[:, 0].max()) + 9
    z0, z1 = int(arr[:, 1].min()) - 9, int(arr[:, 1].max()) + 9
    arc = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(arr, axis=0), axis=1))])
    for x in range(x0, x1 + 1):
        for z in range(z0, z1 + 1):
            d = np.sqrt(((arr - (x, z)) ** 2).sum(axis=1))
            i = int(d.argmin())
            dm = d[i]
            if dm <= 3.6:
                w.set(x, G, z, 'gray_concrete' if hash2(x, z, 4) < 0.8 else 'black_concrete')
                paths.mask[z, x] = True
            elif dm <= 4.6:
                w.set(x, G, z, 'red_concrete' if int(arc[i] / 2) % 2 else 'white_concrete')
                paths.mask[z, x] = True
            elif dm <= 6.0:
                w.set(x, G, z, 'coarse_dirt' if hash2(x, z, 5) < 0.6 else 'gravel')
                paths.mask[z, x] = True
            elif dm <= 7.0:
                w.set(x, G, z, 'coarse_dirt')
                w.set(x, G + 1, z, 'black_wool' if int(arc[i]) % 3 else 'white_wool')
                paths.mask[z, x] = True
    # start / finish line (checkered) on the south straight
    sx = 182
    for z in range(102, 116):
        for x in (sx, sx + 1):
            d = np.sqrt(((arr - (x, z)) ** 2).sum(axis=1)).min()
            if d <= 3.6:
                w.set(x, G, z, 'white_concrete' if (x + z) % 2 else 'black_concrete')
    for z in (102, 115):
        for y in range(G + 1, G + 7):
            w.set(sx, y, z, 'iron_bars' if y < G + 6 else 'black_concrete')
    for z in range(102, 116):
        w.set(sx, G + 7, z, 'black_concrete' if z % 2 else 'white_concrete')
    # karts on the grid
    colors = ['red', 'yellow', 'blue', 'lime', 'orange', 'purple']
    for i in range(6):
        kx = sx - 6 - (i // 2) * 5
        kz = 105 + (i % 2) * 3
        _kart(w, kx, kz, colors[i])
    # pit garage + grandstand south of the start straight
    gst = dict(WEST, wall='light_gray_concrete', pillar='gray_concrete', trim='red_concrete',
               roof_block='red_concrete', parapet='white_concrete')
    building(w, 134, 117, 18, 9, 'north', gst, floors=1, fh=5, roof='flat', name='PIT GARAGE', kind='plain',
             awning=False, door_double=True)
    for x in range(156, 198):
        for k in range(4):
            z = 117 + k
            w.set(x, G + 1 + k, z, 'spruce_stairs[facing=south,half=bottom]')
            for y in range(G + 1, G + 1 + k):
                w.set(x, y, z, 'spruce_planks')
        for y in range(G + 1, G + 9):
            if x % 8 == 0:
                w.set(x, y, 121, 'stripped_spruce_log[axis=y]')
        for z in range(117, 122):
            w.set(x, G + 9, z, 'red_wool' if z % 2 else 'white_wool')
    # control tower
    for y in range(G + 1, G + 12):
        for x in range(200, 205):
            for z in range(117, 122):
                edge = x in (200, 204) or z in (117, 121)
                if edge:
                    w.set(x, y, z, 'glass' if y > G + 8 else 'spruce_planks')
    for x in range(199, 206):
        for z in range(116, 123):
            w.set(x, G + 12, z, 'red_concrete')
    w.door(202, G + 1, 121, 'south', 'spruce')
    w.sign(202, G + 4, 122, ['DUST DEVIL', 'SPEEDWAY', 'Go-Karts', 'Min 120 cm'], wood='spruce', facing='south')
    # infield decor
    for (x, z) in ((176, 92), (186, 54), (156, 80)):
        if not paths.mask[z, x]:
            decor.tree(w, x, G + 1, z, 'acacia', 1.0)
    w.label(178, 70, 'DUST DEVIL SPEEDWAY (go-karts)', 'attraction')
    paths.draw([(176, 160), (176, 123)], 5, land='western')


def _kart(w, x, z, color):
    y = G + 1
    for dz in range(3):
        for dx in range(2):
            corner = dz in (0, 2)
            w.set(x + dx, y, z + dz, 'black_concrete' if corner and False else f'{color}_concrete')
    w.set(x, y + 1, z + 1, 'spruce_stairs[facing=west,half=bottom]')
    w.set(x + 1, y + 1, z + 1, 'black_carpet')
    w.set(x, y + 1, z + 2, 'black_carpet')
    w.set(x, y + 1, z, 'black_carpet')


# ---------------------------------------------------------------------------------------------
# Town
# ---------------------------------------------------------------------------------------------
NORTH = [(10, 'GENERAL STORE', 'shop'), (14, 'RUSTY SPUR', 'restaurant'), (9, 'SHERIFF', 'office'),
         (11, 'ASSAY OFFICE', 'shop'), (10, 'BARBER', 'shop'), (12, 'HOTEL', 'office'), (9, 'TELEGRAPH', 'office'),
         (12, 'TRADING POST', 'shop')]
SOUTH = [(12, 'BANK', 'office'), (10, 'BLACKSMITH', 'shop'), (14, 'PECOS BBQ', 'restaurant'),
         (10, 'PHOTO PARLOR', 'shop'), (11, 'CANDY CO', 'shop'), (10, 'LEATHER', 'shop'), (12, 'MERCANTILE', 'shop')]


def town(w, paths):
    paths.draw([(218, 218), (205, 170), (190, 165), (34, 165)], 11, land='western')
    paths.draw([(222, 110), (214, 124), (200, 132), (186, 140), (184, 165)], 7, land='western')
    paths.draw([(120, 165), (120, 222)], 7, land='western')
    rnd = random.Random(9)
    gaps = [(66, 74), (98, 106), (170, 192)]
    x = 36
    for i, (wd, name, kind) in enumerate(NORTH):
        for (g0, g1) in gaps:
            if x <= g1 and x + wd - 1 >= g0:
                x = g1 + 1
        if x + wd > 214:
            break
        st = dict(WEST, wall=WALLS[i % len(WALLS)])
        building(w, x, 141, wd, 14, 'south', st, floors=2 if wd >= 11 else 1, fh=5, roof='low_gable_behind',
                 name=name, kind=kind, awning=False, false_front=3, porch=3, door_double=wd >= 12)
        x += wd + 2
    x = 36
    for i, (wd, name, kind) in enumerate(SOUTH):
        if x + wd > 214:
            break
        if 114 <= x <= 126 or x <= 120 <= x + wd:
            x = 127
        st = dict(WEST, wall=WALLS[(i + 4) % len(WALLS)])
        building(w, x, 175, wd, 14, 'north', st, floors=2 if wd >= 12 else 1, fh=5, roof='low_gable_behind',
                 name=name, kind=kind, awning=False, false_front=3, porch=3, door_double=wd >= 12)
        x += wd + 2
    # saloon balcony
    # water tower landmark
    tx, tz = 206, 196
    for (dx, dz) in ((-3, -3), (3, -3), (-3, 3), (3, 3)):
        for y in range(G + 1, G + 14):
            w.set(tx + dx, y, tz + dz, 'spruce_log[axis=y]')
    for y in range(G + 3, G + 14, 5):
        for t in range(-3, 4):
            w.set(tx + t, y, tz - 3, 'spruce_fence')
            w.set(tx + t, y, tz + 3, 'spruce_fence')
    for k in range(9):
        for (x2, z2) in circle(tx, tz, 4.5):
            w.set(x2, G + 14 + k, z2, 'spruce_planks' if k % 3 else 'stripped_dark_oak_log[axis=y]')
    for (x2, z2) in disc(tx, tz, 4.5):
        w.set(x2, G + 14, z2, 'spruce_planks')
    cone_roof(w, tx + 0.0, tz + 0.0, G + 23, 5.5, 4, 'spruce_stairs', 'spruce_planks')
    giant_text(w, 'DG', tx, G + 17, tz + 5, 'south', 'white_terracotta', scale=1)
    # windmill
    mx, mz = 34, 200
    for y in range(G + 1, G + 16):
        w.set(mx, y, mz, 'spruce_log[axis=y]')
        if y < G + 10:
            for (dx, dz) in ((1, 1), (-1, 1), (1, -1), (-1, -1)):
                if (y - G) % 3 == 0:
                    w.set(mx + dx, y, mz + dz, 'spruce_fence')
    for a in range(12):
        ang = a * math.tau / 12
        for t in range(1, 6):
            w.set(mx, G + 16 + int(round(t * math.sin(ang))), mz + int(round(t * math.cos(ang))), 'white_wool' if t > 1 else 'spruce_planks')
    w.set(mx, G + 16, mz, 'spruce_planks')
    # props along the street: hay, barrels, wagons, cacti planters, hitching posts
    for xx in range(40, 212, 9):
        for zz in (158, 172):
            if w.is_air(xx, G + 1, zz) and w.name(xx, G, zz) not in ('air',):
                r = rnd.random()
                if r < 0.3:
                    w.set(xx, G + 1, zz, 'hay_block[axis=y]')
                elif r < 0.55:
                    w.set(xx, G + 1, zz, 'barrel[facing=up]')
                elif r < 0.7:
                    w.set(xx, G + 1, zz, 'spruce_fence')
                    w.set(xx + 1, G + 1, zz, 'spruce_fence') if w.is_air(xx + 1, G + 1, zz) else None
    # covered wagon
    _wagon(w, 160, 198)
    _wagon(w, 60, 200)
    # corral and services
    for x2 in range(130, 150):
        for z2 in range(194, 214):
            if x2 in (130, 149) or z2 in (194, 213):
                w.set(x2, G + 1, z2, 'oak_fence')
            else:
                w.set(x2, G, z2, 'coarse_dirt' if hash2(x2, z2, 1) < 0.5 else 'dirt')
    w.set(140, G + 1, 194, 'oak_fence_gate[facing=north,open=false,in_wall=false]')
    for (hx, hz) in ((135, 200), (142, 205), (137, 209)):
        w.set(hx, G + 1, hz, 'hay_block[axis=y]')
    restroom(w, 80, 194, 'north', WEST)
    info_booth(w, 104, 200, WEST, title='TOWN INFO')
    food_kiosk(w, 186, 194, 'west', 'SARSAPARILLA', ('brown', 'white'), 'spruce', subtitle='COLD DRINKS')
    food_kiosk(w, 20 + 22, 132, 'south', 'CHURRO CART', ('orange', 'white'), 'spruce', subtitle='CINNAMON')
    decor.lawn_trees(w, 24, 186, 216, 216, ['acacia', 'spruce'], spacing=12, seed=60,
                     avoid=lambda x, z: paths.mask[z, x])
    for (cx, cz) in ((24, 140), (130, 130), (150, 125), (210, 130)):
        if w.is_air(cx, G + 1, cz):
            w.set(cx, G, cz, 'red_sand')
            for k in range(1, 4):
                w.set(cx, G + k, cz, 'cactus[age=0]')
    w.label(120, 165, 'DUSTY GULCH', 'land')


def _wagon(w, x, z):
    y = G + 1
    for dz in range(5):
        for dx in range(3):
            w.set(x + dx, y + 1, z + dz, 'spruce_planks')
    for (dx, dz) in ((-1, 0), (3, 0), (-1, 4), (3, 4)):
        w.set(x + dx, y, z + dz, 'dark_oak_trapdoor[facing=north,half=bottom,open=true]' if False else 'spruce_fence')
        w.set(x + dx, y + 1, z + dz, 'dark_oak_planks')
    for dz in range(5):
        for dx in (0, 2):
            w.set(x + dx, y + 2, z + dz, 'white_wool')
            w.set(x + dx, y + 3, z + dz, 'white_wool')
        w.set(x + 1, y + 4, z + dz, 'white_wool')


def build(w, paths):
    hm = build_mountain(w)
    log_flume(w, paths, hm)
    mountain_dressing(w, hm)
    go_karts(w, paths)
    town(w, paths)
