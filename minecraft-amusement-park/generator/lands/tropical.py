"""Coral Cay: tropical adventure island with the Lagoona Splash water park, Temple of the Jade Idol,
the Tiki Tumba village and Mount Kahuna volcano."""
import math
import random

import numpy as np

from ..const import G
from ..geom import disc, circle, ring, ellipsoid, ball, line
from ..world import Builder, hash2, hash3
from ..font import giant_text
from .. import decor
from .. import track as T
from ..buildings import style, building, restroom, info_booth, food_kiosk, cone_roof, box_walls, hip_roof

TIKI = style(wall='bamboo_planks', base='mud_bricks', pillar='stripped_bamboo_block[axis=y]', trim='bamboo_mosaic',
             floor='bamboo_planks', ceiling='bamboo_planks', window='glass_pane', roof_stairs='bamboo_mosaic_stairs',
             roof_slab='bamboo_mosaic_slab', roof_block='hay_block[axis=y]', door='bamboo', sign_wood='bamboo',
             carpet='lime_carpet', counter='bamboo_block[axis=y]', counter_top='bamboo_slab[type=bottom]',
             shelf=('barrel[facing=up]', 'melon', 'bamboo_block[axis=y]'), light='lantern[hanging=true]',
             wall_light='torch' if False else 'lantern[hanging=false]', awning=['lime', 'yellow'],
             porch_floor='bamboo_planks', porch_post='bamboo_fence', porch_rail='bamboo_fence',
             porch_roof='bamboo_mosaic_slab[type=bottom]')
SPLASH = dict(TIKI, wall='smooth_sandstone', base='smooth_sandstone', pillar='cut_sandstone', trim='light_blue_concrete',
              roof_stairs='prismarine_stairs', roof_slab='prismarine_slab', roof_block='prismarine',
              awning=['light_blue', 'white'], carpet='light_blue_carpet')


def thatch_roof(w, x0, z0, x1, z1, y):
    """Steep stepped hay thatch."""
    k = 0
    while x0 - 1 + k <= x1 + 1 - k and z0 - 1 + k <= z1 + 1 - k:
        for x in range(x0 - 1 + k, x1 + 2 - k):
            for z in range(z0 - 1 + k, z1 + 2 - k):
                if x in (x0 - 1 + k, x1 + 1 - k) or z in (z0 - 1 + k, z1 + 1 - k) or k > 4:
                    w.set(x, y + k, z, 'hay_block[axis=y]')
        k += 1
    return y + k


# ---------------------------------------------------------------------------------------------
# Water park
# ---------------------------------------------------------------------------------------------
def wave_pool(w, x0=30, x1=85, z0=535, z1=608):
    for x in range(x0, x1 + 1):
        for z in range(z0 - 12, z1 + 1):
            if z < z0 + 12:
                w.set(x, G, z, 'sand')
                w.clear(x, G + 1, z, x, G + 4, z)
                continue
            depth = 1 if z < z0 + 20 else 2 if z < z0 + 30 else 3 if z < z0 + 44 else 4
            wall = x in (x0, x1) or z == z1
            for k in range(depth):
                w.set(x, G - k, z, 'water')
            w.set(x, G - depth, z, 'light_blue_concrete' if depth > 1 else 'sand')
            if depth >= 3 and (x - x0) % 9 == 4 and (z - z0) % 9 == 4:
                w.set(x, G - depth, z, 'sea_lantern')
            if wall:
                for k in range(depth + 1):
                    w.set(x, G - k, z, 'smooth_sandstone')
                w.set(x, G + 1, z, 'smooth_sandstone_slab[type=bottom]')
            w.clear(x, G + 1, z, x, G + 4, z) if not wall else None
    # depth markers / lane ropes
    for x in range(x0 + 1, x1):
        if x % 2:
            w.set(x, G + 1, z0 + 30, 'yellow_carpet' if False else 'air')
    # wave machine house
    for x in range(x0, x1 + 1):
        for z in range(z1 + 1, z1 + 8):
            for y in range(G + 1, G + 9):
                edge = x in (x0, x1) or z in (z1 + 1, z1 + 7) or y == G + 8
                if edge:
                    blk = 'smooth_sandstone'
                    if z == z1 + 1 and G + 1 <= y <= G + 4 and (x - x0) % 4 != 0:
                        blk = 'iron_bars'
                    if y == G + 8:
                        blk = 'prismarine_bricks'
                    w.set(x, y, z, blk)
                else:
                    w.set(x, y, z, 'air')
            w.set(x, G, z, 'smooth_stone')
    giant_text(w, "SURF'S UP", (x0 + x1) // 2, G + 9, z1 + 1, 'north', 'light_blue_concrete', scale=1)
    # lifeguard towers
    for (lx, lz) in ((x0 - 1, z0 + 30), (x1 + 2, z0 + 40)):
        for y in range(G + 1, G + 4):
            w.set(lx, y, lz, 'white_concrete' if y < G + 3 else 'red_concrete')
        w.set(lx, G + 4, lz, 'quartz_stairs[facing=%s,half=bottom]' % ('west' if lx < x0 else 'east'))
        w.set(lx, G + 5, lz, 'red_wool')
    # loungers and umbrellas on the beach
    for x in range(x0 + 2, x1 - 2, 4):
        decor.deck_chair(w, x, G + 1, z0 - 10, 'south', 'birch')
        if (x // 4) % 2 == 0:
            for y in range(G + 1, G + 4):
                w.set(x + 2, y, z0 - 8, 'birch_fence')
            for dx in (-1, 0, 1):
                for dz in (-1, 0, 1):
                    w.set(x + 2 + dx, G + 4, z0 - 8 + dz, ['red_wool', 'white_wool', 'yellow_wool', 'light_blue_wool'][(x + dx + dz) % 4])
    w.label((x0 + x1) // 2, (z0 + z1) // 2, 'WAVE POOL', 'attraction')


def lazy_river(w, cx=120, cz=511):
    ro, ri = (24, 19), (17, 12)
    for x in range(cx - ro[0] - 2, cx + ro[0] + 3):
        for z in range(cz - ro[1] - 2, cz + ro[1] + 3):
            eo = ((x - cx) / ro[0]) ** 2 + ((z - cz) / ro[1]) ** 2
            ei = ((x - cx) / ri[0]) ** 2 + ((z - cz) / ri[1]) ** 2
            if eo <= 1.0 and ei > 1.0:
                w.set(x, G - 2, z, 'light_blue_concrete' if hash2(x, z, 1) > 0.05 else 'sea_lantern')
                w.set(x, G - 1, z, 'water')
                w.set(x, G, z, 'water')
                w.clear(x, G + 1, z, x, G + 3, z)
            elif eo <= 1.2 and ei > 0.75:
                w.set(x, G, z, 'smooth_sandstone')
                w.set(x, G - 1, z, 'smooth_sandstone')
            elif ei <= 0.75:
                w.set(x, G, z, 'sand' if ei > 0.5 else 'grass_block')
    # bridges
    for bz_dir in (-1, 1):
        for d in range(ri[1] - 1, ro[1] + 2):
            z = cz + bz_dir * d
            for x in range(cx - 2, cx + 3):
                w.set(x, G + 1, z, 'bamboo_planks')
                if x in (cx - 2, cx + 2):
                    w.set(x, G + 2, z, 'bamboo_fence')
    # island: tiki snack hut + palms
    for (x, z) in disc(cx, cz, 4):
        w.set(x, G, z, 'bamboo_planks')
    for (x, z) in ((cx - 3, cz - 3), (cx + 3, cz - 3), (cx - 3, cz + 3), (cx + 3, cz + 3)):
        for y in range(G + 1, G + 5):
            w.set(x, y, z, 'bamboo_block[axis=y]')
    thatch_roof(w, cx - 3, cz - 3, cx + 3, cz + 3, G + 5)
    w.set(cx, G + 1, cz, 'barrel[facing=up]')
    for (x, z) in ((cx - 7, cz - 2), (cx + 8, cz + 1), (cx - 2, cz + 7)):
        decor.tree(w, x, G + 1, z, 'palm', 0.9)
    # inner tubes floating
    for i in range(10):
        a = i * math.tau / 10
        x = int(round(cx + 20.5 * math.cos(a)))
        z = int(round(cz + 15.5 * math.sin(a)))
        if w.name(x, G, z) == 'water':
            w.set(x, G + 1, z, ['red_carpet', 'yellow_carpet', 'lime_carpet', 'pink_carpet'][i % 4] if False else
                  ['red_concrete', 'yellow_concrete', 'lime_concrete', 'pink_concrete'][i % 4])
    w.label(cx, cz, 'LAZY RIVER', 'attraction')


def slide_tracks(tx=100, tz=566, top=G + 25):
    a = T.Track(105, top, 565, 90)
    a.straight(8)
    a.helix(1.5, 7, -16, 0, right=True)
    a.connect(128, G + 1, 597, 180, tension=0.9, bank=False)
    a.straight(6)
    b = T.Track(94, top, 567, 270)
    b.straight(6)
    b.helix(1.25, 5, -15, 0, right=True)
    b.connect(82, G + 1, 546, 270, tension=0.9, bank=False)
    b.straight(5)
    c = T.Track(97, top, 571, 180)
    c.straight(1)
    c.pitch_to(-48, 6)
    drop = (top - (G + 1)) - 6 * (1 - math.cos(math.radians(48))) * 2
    c.straight(drop / math.sin(math.radians(48)))
    c.pitch_to(0, 6)
    c.straight(4)
    return a, b, c


def render_slide(w, t, glass, open_top=False):
    fr = t.frames()
    shell, core = set(), set()
    for (p, f, u, r) in fr:
        cen = tuple(int(round(v)) for v in p)
        core.add(cen)
        for du in (-1, 0, 1):
            for dv in (-1, 0, 1, 2) if open_top else (-1, 0, 1):
                if du == 0 and dv in (0, 1, 2):
                    continue
                if open_top and dv >= 1 and du == 0:
                    continue
                q = p + r * du + u * dv
                shell.add(tuple(int(round(v)) for v in q))
    for c in shell - core:
        if c[1] >= G:
            w.set(*c, glass)
    for c in core:
        w.set(*c, 'water')
        if open_top:
            w.set(c[0], c[1] + 1, c[2], 'air')
    return shell | core


def slide_tower(w, tx=100, tz=566, top=G + 25):
    # core and spiral stair
    for y in range(G + 1, top):
        for x in range(tx - 2, tx + 2):
            for z in range(tz - 2, tz + 2):
                w.set(x, y, z, 'jungle_planks' if (y - G) % 6 else 'jungle_log[axis=y]')
    ringc = []
    for x in range(tx - 4, tx + 4):
        ringc.append((x, tz - 4, 'east'))
    for z in range(tz - 4, tz + 4):
        ringc.append((tx + 4, z, 'south'))
    for x in range(tx + 4, tx - 4, -1):
        ringc.append((x, tz + 4, 'west'))
    for z in range(tz + 4, tz - 4, -1):
        ringc.append((tx - 4, z, 'north'))
    k = 0
    y = G + 1
    while y < top:
        x, z, f = ringc[k % len(ringc)]
        w.set(x, y, z, f'jungle_stairs[facing={f},half=bottom]')
        if y > G + 1:
            w.set(x, y - 1, z, 'jungle_planks')
        # inner landing between stair and core
        for (ix, iz) in ((x + (1 if x < tx - 2 else -1 if x > tx + 1 else 0), z + (1 if z < tz - 2 else -1 if z > tz + 1 else 0)),):
            pass
        y += 1
        k += 1
    # posts and railings
    for (x, z) in ((tx - 5, tz - 5), (tx + 4, tz - 5), (tx - 5, tz + 4), (tx + 4, tz + 4)):
        for yy in range(G + 1, top + 5):
            w.set(x, yy, z, 'jungle_log[axis=y]')
    # top platform
    for x in range(tx - 5, tx + 5):
        for z in range(tz - 5, tz + 5):
            w.set(x, top - 1, z, 'jungle_planks')
            w.clear(x, top, z, x, top + 3, z)
            if x in (tx - 5, tx + 4) or z in (tz - 5, tz + 4):
                w.set(x, top, z, 'jungle_fence')
    for (x, z) in ((tx - 5, tz - 5), (tx + 4, tz - 5), (tx - 5, tz + 4), (tx + 4, tz + 4)):
        for yy in range(top, top + 4):
            w.set(x, yy, z, 'jungle_log[axis=y]')
    thatch_roof(w, tx - 5, tz - 5, tx + 4, tz + 4, top + 4)
    # stair opening at the top
    x, z, f = ringc[(k - 1) % len(ringc)]
    w.set(x, top - 1, z, 'air')
    w.set(x, top, z, 'air')


def water_park(w, paths):
    # fence around the park
    x0, x1, z0, z1 = 24, 146, 470, 616
    for x in range(x0, x1 + 1):
        for z in (z0, z1):
            w.set(x, G + 1, z, 'bamboo_fence')
    for z in range(z0, z1 + 1):
        for x in (x0, x1):
            w.set(x, G + 1, z, 'bamboo_fence')
    # decking inside
    for x in range(x0 + 1, x1):
        for z in range(z0 + 1, z1):
            w.set(x, G, z, 'smooth_sandstone' if hash2(x, z, 2) < 0.75 else 'cut_sandstone')
            paths.mask[z, x] = True
    wave_pool(w)
    lazy_river(w)
    # splash pool for the tower slides
    for x in range(90, 140):
        for z in range(592, 613):
            edge = x in (90, 139) or z in (592, 612)
            for k in range(0, 3):
                w.set(x, G - k, z, 'smooth_sandstone' if edge else 'water')
            w.set(x, G - 3, z, 'light_blue_concrete')
            w.clear(x, G + 1, z, x, G + 3, z)
            if edge:
                w.set(x, G + 1, z, 'smooth_sandstone_slab[type=bottom]')
    slide_tower(w)
    a, b, c = slide_tracks()
    render_slide(w, a, 'purple_stained_glass')
    render_slide(w, b, 'orange_stained_glass')
    render_slide(w, c, 'light_blue_concrete', open_top=True)
    # start gates: fence gates hold the slide water back from the platform (fluids cannot pass them)
    top = G + 25
    for (x, z, f) in ((104, 565, 'east'), (95, 567, 'west'), (97, 570, 'south')):
        w.set(x, top, z, f'jungle_fence_gate[facing={f},open=false,in_wall=false]')
    for t in (a, b, c):
        fr = t.frames()
        p = fr[-1][0]
        for k in range(1, 4):
            q = tuple(int(round(v)) for v in p)
    # slide supports (jungle log columns under the tubes)
    for t in (a, b):
        fr = t.frames()
        for i in range(0, len(fr), 30):
            p = fr[i][0]
            x, y, z = int(round(p[0])), int(round(p[1])) - 2, int(round(p[2]))
            while y > G and w.is_air(x, y, z):
                w.set(x, y, z, 'jungle_log[axis=y]')
                y -= 1
    # splash pad for kids
    for x in range(30, 63):
        for z in range(478, 512):
            w.set(x, G, z, 'light_blue_terracotta' if (x + z) % 2 else 'cyan_terracotta')
    for (mx, mz, cap) in ((38, 486, 'red_mushroom_block'), (52, 492, 'brown_mushroom_block'), (42, 503, 'red_mushroom_block'),
                          (56, 504, 'red_mushroom_block')):
        for y in range(G + 1, G + 5):
            w.set(mx, y, mz, 'mushroom_stem')
        for (x, z) in disc(mx, mz, 3):
            w.set(x, G + 5, z, cap)
        for (x, z) in disc(mx, mz, 1.5):
            w.set(x, G + 6, z, cap)
        for (x, z) in circle(mx, mz, 2):
            if (x + z) % 2:
                w.set(x, G, z, 'water')
    # cabanas along the north beach
    for i, x in enumerate(range(32, 84, 8)):
        for (dx, dz) in ((0, 0), (5, 0), (0, 4), (5, 4)):
            for y in range(G + 1, G + 4):
                w.set(x + dx, y, 518 + dz, 'bamboo_fence')
        for dx in range(-1, 7):
            for dz in range(-1, 6):
                w.set(x + dx, G + 4, 518 + dz, ['white_wool', 'light_blue_wool', 'yellow_wool', 'lime_wool'][i % 4] if dz in (-1, 5) else 'white_wool')
        decor.deck_chair(w, x + 1, G + 1, 519, 'south', 'birch')
        decor.deck_chair(w, x + 4, G + 1, 519, 'south', 'birch')
    # changing rooms / restrooms and snack bar
    restroom(w, 120, 474, 'south' if False else 'north', SPLASH, roof='flat')
    food_kiosk(w, 100, 474, 'south' if False else 'north', 'SURF SHACK', ('light_blue', 'white'), 'bamboo',
               subtitle='SNACKS', wall='smooth_sandstone')
    # entrance arch
    for y in range(G + 1, G + 9):
        for z in (476, 486):
            w.set(x1, y, z, 'bamboo_block[axis=y]')
    for z in range(476, 487):
        w.set(x1, G + 9, z, 'light_blue_concrete')
        w.set(x1, G + 1, z, 'air') if 478 <= z <= 484 else None
    giant_text(w, 'LAGOONA SPLASH', x1 + 1, G + 11, 481, 'east', 'light_blue_concrete', scale=1, back='white_concrete')
    for z in range(477, 486):
        w.set(x1 + 1, G + 10, z, 'white_concrete') if False else None
    w.label(85, 545, 'LAGOONA SPLASH WATER PARK', 'attraction')
    w.label(110, 570, 'Slide Tower', 'small')


# ---------------------------------------------------------------------------------------------
# Temple, village, volcano
# ---------------------------------------------------------------------------------------------
def temple(w, cx=60, cz=446):
    tiers = 5
    for t in range(tiers):
        hx, hz = 26 - t * 4, 16 - t * 3
        for x in range(cx - hx, cx + hx + 1):
            for z in range(cz - hz, cz + hz + 1):
                for y in range(G + 1 + t * 4, G + 5 + t * 4):
                    edge = x in (cx - hx, cx + hx) or z in (cz - hz, cz + hz)
                    if edge or y == G + 4 + t * 4:
                        r = hash3(x, y, z, 6)
                        w.set(x, y, z, 'mossy_stone_bricks' if r < 0.45 else 'stone_bricks' if r < 0.75 else
                              'cracked_stone_bricks' if r < 0.9 else 'mossy_cobblestone')
        # vines down the east face
        for z in range(cz - hz, cz + hz + 1):
            if hash2(cx + hx, z, t) < 0.3:
                for y in range(G + 2 + t * 4, G + 4 + t * 4):
                    w.set(cx + hx + 1, y, z, 'vine[west=true]')
    # grand stair up the east face
    for k in range(tiers * 4):
        x = cx + 26 - k
        for z in range(cz - 3, cz + 4):
            w.set(x, G + 1 + k, z, 'stone_brick_stairs[facing=west,half=bottom]' if abs(z - cz) < 3 else 'mossy_stone_bricks')
            for y in range(G + 1, G + 1 + k):
                if w.is_air(x, y, z):
                    w.set(x, y, z, 'stone_bricks')
    # shrine on top with the Jade Idol
    ty = G + 1 + tiers * 4
    for x in range(cx - 4, cx + 5):
        for z in range(cz - 3, cz + 4):
            edge = x in (cx - 4, cx + 4) or z in (cz - 3, cz + 3)
            for y in range(ty, ty + 5):
                if edge and not (x == cx + 4 and abs(z - cz) <= 1 and y < ty + 3):
                    w.set(x, y, z, 'chiseled_stone_bricks' if y == ty + 4 else 'mossy_stone_bricks')
    for x in range(cx - 5, cx + 6):
        for z in range(cz - 4, cz + 5):
            w.set(x, ty + 5, z, 'mossy_stone_bricks' if (x + z) % 3 else 'stone_brick_slab[type=bottom]')
    w.set(cx, ty, cz, 'gold_block')
    w.set(cx, ty + 1, cz, 'emerald_block')
    w.set(cx, ty + 2, cz, 'green_glazed_terracotta[facing=east]')
    w.set(cx, ty + 3, cz, 'emerald_block')
    for z in (cz - 1, cz + 1):
        w.set(cx + 3, ty, z, 'campfire[lit=true,signal_fire=false,facing=east]')
    # ride entrance at the base (dark doorway)
    for z in range(cz - 2, cz + 3):
        for y in range(G + 1, G + 4):
            w.set(cx + 26, y, z + 8, 'air') if False else None
    for x in range(cx + 18, cx + 27):
        for z in range(cz + 8, cz + 13):
            for y in range(G + 1, G + 4):
                w.set(x, y, z, 'air')
    for z in range(cz + 8, cz + 13):
        w.set(cx + 27, G + 4, z, 'mossy_stone_brick_stairs[facing=west,half=top]') if False else None
    for (x, z) in ((cx + 27, cz + 7), (cx + 27, cz + 13)):
        for y in range(G + 1, G + 5):
            w.set(x, y, z, 'stone_brick_wall')
        w.set(x, G + 5, z, 'torch')
    w.sign(cx + 27, G + 3, cz + 10, ['TEMPLE OF THE', 'JADE IDOL', 'Adventure Ride', 'Beware!'], wood='jungle',
           facing='east')
    # jungle overgrowth
    for (x, z) in ((cx - 26, cz - 18), (cx + 20, cz - 18), (cx - 28, cz + 14), (cx + 30, cz - 10)):
        decor.tree(w, x, G + 1, z, 'jungle', 0.9)
    w.label(cx, cz, 'TEMPLE OF THE JADE IDOL', 'attraction')


def volcano(w, cx=184, cz=578, R=30, Hh=40):
    for x in range(cx - R - 1, cx + R + 2):
        for z in range(cz - R - 1, cz + R + 2):
            d = math.hypot(x - cx, z - cz)
            if d > R:
                continue
            h = Hh * (1 - (d / R) ** 0.9) + (hash2(x // 2, z // 2, 3) - 0.5) * 2
            if d < 7:
                h = Hh - 4
            h = int(max(1, h))
            nb = []
            for (dx, dz) in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                dd = math.hypot(x + dx - cx, z + dz - cz)
                nb.append(0 if dd > R else int(max(1, Hh * (1 - (dd / R) ** 0.9))))
            ybot = max(G + 1, G + min(nb) - 2)
            for y in range(ybot, G + h + 1):
                r = hash3(x, y, z, 4)
                w.set(x, y, z, 'basalt[axis=y]' if r < 0.3 else 'blackstone' if r < 0.55 else 'tuff' if r < 0.8 else 'stone')
            top = G + h
            if d > R * 0.7:
                w.set(x, top, z, 'grass_block' if hash2(x, z, 5) < 0.7 else 'moss_block')
    # crater with glowing lava
    for (x, z) in disc(cx, cz, 6.5):
        for y in range(G + Hh - 6, G + Hh + 2):
            w.set(x, y, z, 'air')
        w.set(x, G + Hh - 7, z, 'magma_block')
        w.set(x, G + Hh - 6, z, 'shroomlight' if (x + z) % 3 == 0 else 'orange_stained_glass')
    for (x, z) in circle(cx, cz, 7):
        for y in range(G + Hh - 7, G + Hh - 2):
            w.set(x, y, z, 'blackstone')
    # lava streams down the slopes (magma + glowing shroomlight)
    for a in (0.6, 2.4, 4.0):
        for d in range(7, R - 3):
            x, z = int(round(cx + d * math.cos(a))), int(round(cz + d * math.sin(a)))
            y = G + Hh
            while y > G and w.is_air(x, y, z):
                y -= 1
            w.set(x, y, z, 'magma_block' if d % 3 else 'shroomlight')
    # jungle ring
    for i in range(14):
        a = i * math.tau / 14
        x, z = int(round(cx + (R + 3) * math.cos(a))), int(round(cz + (R + 3) * math.sin(a)))
        if 22 < x < 216 and 424 < z < 616 and w.is_air(x, G + 1, z) and w.name(x, G, z) == 'grass_block':
            decor.tree(w, x, G + 1, z, 'palm' if i % 2 else 'jungle_small', 1.0)
    w.label(cx, cz, 'MOUNT KAHUNA', 'attraction')


def village(w, paths):
    paths.draw([(120, 418), (138, 436), (200, 436), (218, 422)], 9, land='tropical')
    paths.draw([(216, 469), (180, 469), (150, 481), (147, 481)], 9, land='tropical')
    paths.draw([(138, 436), (130, 465), (147, 481)], 7, land='tropical')
    paths.draw([(88, 446), (110, 446), (138, 436)], 7, land='tropical')
    building(w, 158, 444, 28, 18, 'north', TIKI, floors=1, fh=6, roof='hip', name='TIKI TUMBA GRILL',
             kind='restaurant', door_double=True, porch=0,
             sign_lines=['TIKI TUMBA', 'GRILL', 'Island BBQ', '& Smoothies'])
    building(w, 190, 444, 14, 12, 'north', TIKI, floors=2, roof='hip', name='TIKI TREASURES', kind='shop')
    building(w, 190, 478, 14, 12, 'north', dict(TIKI, wall='bamboo_mosaic'), floors=1, fh=6, roof='hip',
             name='ISLAND OUTFITTERS', kind='shop')
    restroom(w, 168, 478, 'north', TIKI)
    food_kiosk(w, 152, 420 + 2, 'south' if False else 'north', 'PINEAPPLE WHIP', ('yellow', 'lime'), 'bamboo',
               subtitle='DOLE-ICIOUS') if False else food_kiosk(w, 206, 452, 'west', 'PINEAPPLE WHIP',
                                                               ('yellow', 'lime'), 'bamboo', subtitle='FROZEN')
    info_booth(w, 150, 452, TIKI, title='ISLAND INFO')
    # stilt huts by a jungle pond
    pond = [(x, z) for (x, z) in disc(186, 515, 13)]
    decor.pond(w, pond, depth=2, edge='mud', seed=33)
    for (hx, hz) in ((176, 506), (194, 522)):
        for (x, z) in ((hx, hz), (hx + 5, hz), (hx, hz + 5), (hx + 5, hz + 5)):
            for y in range(G - 1, G + 4):
                w.set(x, y, z, 'jungle_log[axis=y]')
        for x in range(hx, hx + 6):
            for z in range(hz, hz + 6):
                w.set(x, G + 3, z, 'bamboo_planks')
                if x in (hx, hx + 5) or z in (hz, hz + 5):
                    w.set(x, G + 4, z, 'bamboo_fence')
        thatch_roof(w, hx, hz, hx + 5, hz + 5, G + 7)
        for (x, z) in ((hx, hz), (hx + 5, hz), (hx, hz + 5), (hx + 5, hz + 5)):
            for y in range(G + 4, G + 7):
                w.set(x, y, z, 'jungle_log[axis=y]')
    # rope bridge between the huts
    for i, x in enumerate(range(182, 194)):
        z = 511 + (i * 11) // 12
        w.set(x, G + 3, z, 'bamboo_slab[type=bottom]')
    decor.lawn_trees(w, 150, 424, 216, 545, ['palm', 'jungle_small', 'palm'], spacing=9, seed=90,
                     avoid=lambda x, z: paths.mask[z, x])
    decor.lawn_trees(w, 24, 424, 146, 468, ['palm', 'jungle_small'], spacing=9, seed=91,
                     avoid=lambda x, z: paths.mask[z, x])
    w.label(186, 436, 'CORAL CAY', 'land')


def build(w, paths):
    temple(w)
    water_park(w, paths)
    volcano(w)
    village(w, paths)
