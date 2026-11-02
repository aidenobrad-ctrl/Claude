"""Everspire Kingdom: fairytale castle, Royal Carousel, Hollowmoor Manor (haunted house), Tudor village."""
import math
import random

from ..const import G
from ..geom import disc, circle, ring, ball, ellipsoid, line, thick_line
from ..world import Builder, hash2, hash3, door as wdoor
from .. import decor
from ..buildings import (style, building, restroom, info_booth, food_kiosk, cone_roof, hip_roof, gable_roof,
                         box_walls, flat_roof)

WALL = 'calcite'
TRIM = 'smooth_stone'
ROOF = 'blue_concrete'
ROOF2 = 'light_blue_concrete'

TUDOR = style(wall='white_terracotta', base='cobblestone', pillar='stripped_dark_oak_log[axis=y]',
              trim='dark_oak_planks', floor='spruce_planks', ceiling='spruce_planks', window='glass_pane',
              roof_stairs='deepslate_tile_stairs', roof_slab='deepslate_tile_slab', roof_block='deepslate_tiles',
              door='dark_oak', sign_wood='dark_oak', awning=None, carpet='red_carpet',
              counter='stripped_spruce_wood[axis=y]', counter_top='spruce_slab[type=bottom]',
              shelf=('barrel[facing=up]', 'bookshelf', 'chest[facing=north]'))
TUDOR2 = dict(TUDOR, wall='smooth_sandstone', roof_stairs='spruce_stairs', roof_slab='spruce_slab',
              roof_block='spruce_planks', carpet='blue_carpet')
TUDOR3 = dict(TUDOR, wall='birch_planks', roof_stairs='dark_oak_stairs', roof_slab='dark_oak_slab',
              roof_block='dark_oak_planks', base='stone_bricks', carpet='green_carpet')


def cone_full(w, cx, cz, y0, r, h, block=ROOF, band=ROOF2, finial=True):
    """Stepped cone made of full blocks with a light accent band."""
    for k in range(h):
        rr = r * (1 - k / h)
        if rr < 0.4:
            break
        for (x, z) in disc(cx, cz, rr):
            w.set(x, y0 + k, z, band if k % 5 == 4 else block)
    top = y0 + h
    if finial:
        w.set(int(round(cx)), top, int(round(cz)), 'gold_block')
        w.set(int(round(cx)), top + 1, int(round(cz)), 'gold_block')
        w.set(int(round(cx)), top + 2, int(round(cz)), 'lightning_rod[facing=up]')
    return top


def round_tower(w, cx, cz, r, y0, h, roof_h=None, wall=WALL, windows=True, crenel=False, hollow=True,
                floor_every=6, banner=None):
    for k in range(h):
        y = y0 + k
        for (x, z) in circle(cx, cz, r):
            blk = wall
            if k == 0 or k == h - 1:
                blk = TRIM
            if windows and k % floor_every == 3 and k > 2:
                a = math.atan2(z - cz, x - cx)
                if int((a + math.pi) / (math.tau / 8)) % 2 == 0 and abs(math.sin(4 * a)) < 0.35:
                    blk = 'glass_pane'
            w.set(x, y, z, blk)
        if hollow:
            for (x, z) in disc(cx, cz, r - 1):
                w.set(x, y, z, 'spruce_planks' if k % floor_every == 0 else 'air')
            if k % floor_every == 4:
                w.set(int(cx), y, int(cz), 'lantern[hanging=true]')
    top = y0 + h
    if crenel:
        for (x, z) in circle(cx, cz, r + 1):
            w.set(x, top - 1, z, TRIM)
            if (x + z) % 2 == 0:
                w.set(x, top, z, wall)
    if roof_h:
        # flared skirt then cone
        for (x, z) in circle(cx, cz, r + 1):
            w.set(x, top, z, ROOF)
        cone_full(w, cx, cz, top + 1, r + 0.6, roof_h)
    if banner:
        for (dx, dz, f) in ((0, r + 1, 'south'), (0, -r - 1, 'north'), (r + 1, 0, 'east'), (-r - 1, 0, 'west')):
            w.set(int(cx) + dx, top - 4, int(cz) + dz, f'{banner}_wall_banner[facing={f}]')
    return top


def crenellated_wall(w, x0, z0, x1, z1, h, thick=3):
    """Straight curtain wall between two points (axis aligned) with a wall walk and merlons."""
    xs = range(min(x0, x1), max(x0, x1) + 1)
    zs = range(min(z0, z1), max(z0, z1) + 1)
    along_x = abs(x1 - x0) >= abs(z1 - z0)
    for x in xs:
        for z in zs:
            for t in range(thick):
                X = x if along_x else x + t - thick // 2
                Z = z + t - thick // 2 if along_x else z
                for k in range(1, h + 1):
                    blk = WALL if k not in (1, h) else TRIM
                    if k == h and t not in (0, thick - 1):
                        blk = 'stone_bricks'
                    w.set(X, G + k, Z, blk)
                if t in (0, thick - 1):
                    w.set(X, G + h + 1, Z, WALL if ((X + Z) % 2 == 0) else 'smooth_stone_slab[type=bottom]')
            if (x + z) % 9 == 0:
                for t in (0, thick - 1):
                    X = x if along_x else x + t - thick // 2
                    Z = z + t - thick // 2 if along_x else z
                    w.set(X, G + h - 4, Z, 'glass_pane')


def _passage(w, paths, cx, z0, kz1):
    for z in range(z0 - 1, kz1 + 1):
        for x in range(cx - 4, cx + 5):
            for k in range(1, 10):
                w.set(x, G + k, z, 'air')
            w.set(x, G, z, 'polished_andesite' if (x + z) % 2 else 'stone_bricks')
            w.set(x, G + 10, z, 'stone_bricks')
            paths.mask[z, x] = True
        for x in (cx - 5, cx + 5):
            for k in range(1, 10):
                w.set(x, G + k, z, 'stone_bricks')
        if z % 4 == 0:
            w.set(cx, G + 9, z, 'lantern[hanging=true]')
            w.set(cx - 4, G + 4, z, 'wall_torch[facing=east]')
            w.set(cx + 4, G + 4, z, 'wall_torch[facing=west]')


def castle(w, paths):
    cx = 320
    x0, x1, z0, z1 = 286, 354, 124, 188
    # moat (ring of water around the walls)
    for x in range(x0 - 14, x1 + 15):
        for z in range(z0 - 12, z1 + 15):
            inner = (x0 - 6 <= x <= x1 + 6 and z0 - 6 <= z <= z1 + 6)
            outer = (x0 - 12 <= x <= x1 + 12 and z0 - 10 <= z <= z1 + 12)
            if outer and not inner:
                w.set(x, G - 2, z, 'mud')
                w.set(x, G - 1, z, 'water')
                w.set(x, G, z, 'water')
                if hash2(x, z, 5) < 0.04:
                    w.set(x, G + 1, z, 'lily_pad')
            elif inner:
                w.set(x, G, z, 'grass_block')
    # moat walls (stone edge)
    for x in range(x0 - 13, x1 + 14):
        for z in range(z0 - 11, z1 + 14):
            on_outer = (x in (x0 - 13, x1 + 13) and z0 - 11 <= z <= z1 + 13) or (z in (z0 - 11, z1 + 13) and x0 - 13 <= x <= x1 + 13)
            on_inner = (x in (x0 - 7, x1 + 7) and z0 - 7 <= z <= z1 + 7) or (z in (z0 - 7, z1 + 7) and x0 - 7 <= x <= x1 + 7)
            if on_outer or on_inner:
                w.set(x, G, z, 'stone_bricks')
                w.set(x, G - 1, z, 'stone_bricks')
                if on_outer:
                    w.set(x, G + 1, z, 'stone_brick_wall')
    # inner lawn/garden ring
    for x in range(x0 - 6, x1 + 7):
        for z in range(z0 - 6, z1 + 7):
            if not (x0 <= x <= x1 and z0 <= z <= z1):
                w.set(x, G, z, 'grass_block')
                if hash2(x, z, 8) < 0.3:
                    decor.flower_at(w, x, G + 1, z, 8, ['blue_orchid', 'white_tulip', 'cornflower', 'allium'])
    # courtyard paving
    for x in range(x0, x1 + 1):
        for z in range(z0, z1 + 1):
            w.set(x, G, z, 'stone_bricks' if (x + z) % 3 else 'polished_andesite')
            paths.mask[z, x] = True
    # curtain walls
    H = 14
    crenellated_wall(w, x0, z0, x1, z0, H)
    crenellated_wall(w, x0, z1, x1, z1, H)
    crenellated_wall(w, x0, z0, x0, z1, H)
    crenellated_wall(w, x1, z0, x1, z1, H)
    # corner towers
    for (tx, tz) in ((x0, z0), (x1, z0), (x0, z1), (x1, z1)):
        round_tower(w, tx, tz, 6, G + 1, 26, roof_h=15, banner='blue')
    # mid-wall towers
    for (tx, tz) in ((x0, (z0 + z1) // 2), (x1, (z0 + z1) // 2)):
        round_tower(w, tx, tz, 4, G + 1, 22, roof_h=11, banner='red')
    # ---------------- keep ----------------
    kx0, kx1, kz0, kz1 = 294, 346, 128, 160
    for k in range(1, 23):
        for x in range(kx0, kx1 + 1):
            for z in range(kz0, kz1 + 1):
                if x in (kx0, kx1) or z in (kz0, kz1):
                    blk = WALL if k % 7 else TRIM
                    if k % 7 in (3, 4, 5) and ((x - kx0) % 5 == 2 or (z - kz0) % 5 == 2) and not (x in (kx0, kx1) and z in (kz0, kz1)):
                        blk = 'glass_pane' if k % 7 != 5 else 'gold_block'
                    w.set(x, G + k, z, blk)
                elif k % 7 == 0:
                    w.set(x, G + k, z, 'spruce_planks')
                else:
                    w.set(x, G + k, z, 'air')
    # banquet hall interior on the ground floor of the keep
    for x in range(kx0 + 2, kx1 - 1, 5):
        for z in range(kz0 + 3, kz1 - 2):
            if abs(x - cx) < 6:
                continue
            w.set(x, G + 1, z, 'dark_oak_planks')
            w.set(x, G + 2, z, 'white_carpet' if z % 4 else 'candle[candles=3,lit=true]')
            w.set(x - 1, G + 1, z, 'dark_oak_stairs[facing=west,half=bottom]')
            w.set(x + 1, G + 1, z, 'dark_oak_stairs[facing=east,half=bottom]')
    for x in range(kx0 + 3, kx1 - 2, 6):
        for z in range(kz0 + 4, kz1 - 3, 6):
            w.set(x, G + 6, z, 'chain[axis=y]')
            w.set(x, G + 5, z, 'lantern[hanging=true]')
    for x in range(kx0 + 1, kx1):
        w.set(x, G + 1, kz0 + 1, 'red_carpet')
    _passage(w, paths, cx, z0, kz1)
    # mosaic murals in the passage (stained glass panels)
    for z in range(kz0 + 4, kz1 - 3, 8):
        for k in range(2, 8):
            for dz in range(0, 4):
                col = ['blue_stained_glass', 'yellow_stained_glass', 'red_stained_glass', 'light_blue_stained_glass'][(k + dz) % 4]
                w.set(cx - 5, G + k, z + dz, col)
                w.set(cx + 5, G + k, z + dz, col)
    # keep roofs and towers
    b = Builder(w, 0, G, 0, 0)
    for k in range(0, 9):
        for x in range(kx0 - 1 + k, kx1 + 2 - k):
            for z in (kz0 - 1 + k, kz1 + 1 - k):
                if kz0 - 1 + k <= kz1 + 1 - k:
                    w.set(x, G + 23 + k * 2, z, ROOF)
                    w.set(x, G + 24 + k * 2, z, ROOF if k % 2 else ROOF2)
        for z in range(kz0 - 1 + k, kz1 + 2 - k):
            for x in (kx0 - 1 + k, kx1 + 1 - k):
                w.set(x, G + 23 + k * 2, z, ROOF)
                w.set(x, G + 24 + k * 2, z, ROOF)
    for x in range(kx0 + 8, kx1 - 7):
        for z in range(kz0 + 8, kz1 - 7):
            w.set(x, G + 40, z, ROOF)
    # dormers on the keep roof
    for x in range(kx0 + 6, kx1 - 4, 10):
        for (z, f) in ((kz1 + 1 - 2, 'south'), (kz0 - 1 + 2, 'north')):
            for k in range(3):
                for dx in (-1, 0, 1):
                    w.set(x + dx, G + 27 + k, z, WALL)
            w.set(x, G + 28, z + (1 if f == 'south' else -1), 'glass_pane')
            w.set(x, G + 28, z, 'glass_pane')
            cone_full(w, x, z, G + 30, 2.2, 4, finial=False)
    # central great spire
    spx, spz = cx, (kz0 + kz1) // 2
    top = round_tower(w, spx, spz, 7, G + 1, 52, roof_h=None, banner='blue')
    for (x, z) in circle(spx, spz, 8):
        w.set(x, top, z, 'gold_block')
        w.set(x, top - 1, z, TRIM)
    top2 = round_tower(w, spx, spz, 5, top, 10, roof_h=None)
    cone_full(w, spx + 0.0, spz + 0.0, top2, 6.2, 22)
    # secondary spires on the keep
    for (tx, tz, r, h, rh) in ((kx0 + 4, kz0 + 4, 4, 34, 14), (kx1 - 4, kz0 + 4, 4, 34, 14),
                               (kx0 + 4, kz1 - 4, 4, 30, 13), (kx1 - 4, kz1 - 4, 4, 30, 13),
                               (cx - 14, kz1 - 2, 3, 38, 12), (cx + 14, kz1 - 2, 3, 38, 12)):
        round_tower(w, tx, tz, r, G + 1, h, roof_h=rh)
    _passage(w, paths, cx, z0, kz1)
    # ---------------- gatehouse (south) ----------------
    gz = z1
    for (tx) in (cx - 11, cx + 11):
        round_tower(w, tx, gz + 3, 5, G + 1, 24, roof_h=12, banner='red')
    for x in range(cx - 8, cx + 9):
        for z in range(gz - 2, gz + 7):
            for k in range(1, 19):
                edge = x in (cx - 8, cx + 8) or z in (gz - 2, gz + 6) or k in (1, 18)
                w.set(x, G + k, z, (WALL if k % 6 else TRIM) if edge else 'air')
            if (x + z) % 2 == 0:
                w.set(x, G + 19, z, WALL)
    for z in range(gz - 2, gz + 7):
        for x in range(cx - 4, cx + 5):
            for k in range(1, 10):
                w.set(x, G + k, z, 'air')
            w.set(x, G, z, 'polished_andesite')
            paths.mask[z, x] = True
    # portcullis (raised) and arch
    for x in range(cx - 4, cx + 5):
        w.set(x, G + 9, gz + 6, 'iron_bars')
        w.set(x, G + 9, gz + 7, 'quartz_stairs[facing=north,half=top]') if False else None
    for x in (cx - 4, cx + 4):
        w.set(x, G + 8, gz + 6, 'stone_brick_stairs[facing=%s,half=top]' % ('east' if x < cx else 'west'))
    for (dx, col) in ((-7, 'red'), (7, 'red'), (-2, 'blue'), (2, 'blue')):
        w.set(cx + dx, G + 15, gz + 7, f'{col}_wall_banner[facing=south]')
    # royal crest (stained glass rose window) above the gate
    for (dx, dy) in [(dx, dy) for dx in range(-3, 4) for dy in range(-3, 4) if dx * dx + dy * dy <= 10]:
        w.set(cx + dx, G + 13 + dy, gz + 6, 'blue_stained_glass' if (dx + dy) % 2 else 'yellow_stained_glass')
    w.set(cx, G + 13, gz + 6, 'gold_block')
    # drawbridge
    for z in range(gz + 7, gz + 21):
        for x in range(cx - 5, cx + 6):
            w.set(x, G, z, 'spruce_planks' if x not in (cx - 5, cx + 5) else 'dark_oak_planks')
            w.clear(x, G + 1, z, x, G + 3, z)
            paths.mask[z, x] = True
        for x in (cx - 5, cx + 5):
            w.set(x, G + 1, z, 'dark_oak_fence')
            if z % 4 == 0:
                w.set(x, G + 2, z, 'dark_oak_fence')
                w.set(x, G + 3, z, 'lantern')
    for x in (cx - 5, cx + 5):
        for k in range(1, 9):
            w.set(x, G + 9 - k, gz + 7, 'chain[axis=y]') if k > 1 else None
    # north (back) gate and bridge
    for z in range(z0 - 14, z0 + 1):
        for x in range(cx - 4, cx + 5):
            if z < z0 - 1:
                w.set(x, G, z, 'spruce_planks')
                w.clear(x, G + 1, z, x, G + 3, z)
            paths.mask[z, x] = True
        if z < z0 - 1:
            for x in (cx - 5, cx + 5):
                w.set(x, G, z, 'dark_oak_planks')
                w.set(x, G + 1, z, 'dark_oak_fence')
    for z in range(z0 - 2, z0 + 2):
        for x in range(cx - 4, cx + 5):
            for k in range(1, 10):
                w.set(x, G + k, z, 'air')
    # courtyard fountain & gardens
    decor.fountain(w, cx, 174, 5, rim='stone_bricks', rim_top='stone_brick_slab[type=bottom]', tiers=2,
                   finial='gold_block')
    for (bx0, bz0) in ((291, 166), (336, 166)):
        decor.flower_bed(w, bx0, bz0, bx0 + 12, bz0 + 16, border=None, hedge=decor.leaves('azalea'),
                         density=0.85, seed=bx0, palette=['blue_orchid', 'cornflower', 'white_tulip', 'allium'])
    for (x, z) in ((300, 164), (340, 164), (300, 184), (340, 184), (cx - 7, 182), (cx + 7, 182)):
        decor.lamp(w, x, G + 1, z, 'medieval')
    w.label(cx, 150, 'EVERSPIRE CASTLE', 'attraction')


# =============================================================================================
def carousel(w, cx, cz):
    """Royal Carousel: raised round platform, jumping horses on poles, striped canopy."""
    R = 11
    for (x, z) in disc(cx, cz, R + 4):
        w.set(x, G, z, 'stone_bricks' if hash2(x, z, 3) < 0.7 else 'polished_andesite')
        w.clear(x, G + 1, z, x, G + 16, z)
    for (x, z) in circle(cx, cz, R + 4):
        w.set(x, G + 1, z, 'dark_oak_fence')
    w.clear(cx, G + 1, cz + R + 4, cx, G + 1, cz + R + 4)
    w.set(cx, G + 1, cz + R + 4, 'dark_oak_fence_gate[facing=south,open=true,in_wall=false]')
    for (x, z) in disc(cx, cz, R):
        r = math.hypot(x - cx, z - cz)
        w.set(x, G + 1, z, 'stripped_oak_wood[axis=y]' if int(r) % 3 == 0 else 'spruce_planks')
    for (x, z) in circle(cx, cz, R + 0.5):
        dx, dz = x - cx, z - cz
        f = ('west' if dx > 0 else 'east') if abs(dx) >= abs(dz) else ('north' if dz > 0 else 'south')
        w.set(x, G + 1, z, f'spruce_stairs[facing={f},half=bottom]')
    # center drum with mirrors
    for k in range(2, 11):
        for (x, z) in circle(cx, cz, 3):
            w.set(x, G + k, z, 'glass' if k in (4, 5, 6) else ('gold_block' if k in (3, 7) else 'red_concrete'))
        for (x, z) in disc(cx, cz, 2):
            w.set(x, G + k, z, 'glowstone' if k == 5 else 'spruce_planks')
    # horses
    colors = ['white_concrete', 'black_concrete', 'brown_concrete', 'white_concrete', 'light_gray_concrete',
              'orange_terracotta']
    for ring_r, n in ((6.2, 10), (9.0, 14)):
        for i in range(n):
            a = i * math.tau / n
            bx, bz = int(round(cx + ring_r * math.cos(a))), int(round(cz + ring_r * math.sin(a)))
            tx, tz = -math.sin(a), math.cos(a)
            hx, hz = int(round(bx + tx)), int(round(bz + tz))
            tlx, tlz = int(round(bx - tx)), int(round(bz - tz))
            hy = G + 3 + (i % 2)
            body = colors[(i + int(ring_r)) % len(colors)]
            for y in range(G + 2, G + 10):
                if y != hy:
                    w.set(bx, y, bz, 'chain[axis=y]')
            w.set(bx, hy, bz, body)
            w.set(bx, hy + 1, bz, 'red_carpet' if i % 3 else 'blue_carpet')
            w.set(hx, hy, hz, body)
            w.set(hx, hy + 1, hz, body if 'white' not in body else 'quartz_block')
            w.set(tlx, hy, tlz, 'white_wool' if 'black' in body else 'brown_wool' if 'white' in body else 'black_wool')
            if hy > G + 3:
                w.set(hx, hy - 1, hz, 'birch_fence')
            # chariots between horses on the outer ring
            if ring_r > 8 and i % 7 == 3:
                w.set(tlx, G + 2, tlz, 'red_concrete')
    # canopy: striped cone + scalloped valance + lights
    top = G + 10
    for (x, z) in ring(cx, cz, R + 1.5, 3):
        w.set(x, top, z, 'spruce_planks')
    for (x, z) in circle(cx, cz, R + 1.5):
        w.set(x, top - 1, z, 'yellow_wool' if (x + z) % 2 else 'gold_block')
        if (x + z) % 3 == 0:
            w.set(x, top - 2, z, 'lantern[hanging=true]')
    for (x, z) in circle(cx, cz, R - 0.5):
        if (x * 3 + z) % 4 == 0:
            w.set(x, top - 1, z, 'glowstone')
    for k in range(1, 8):
        rr = (R + 2) * (1 - k / 8.0) + 0.5
        for (x, z) in disc(cx, cz, rr):
            a = math.atan2(z - cz, x - cx)
            w.set(x, top + k, z, 'red_concrete' if int((a + math.pi) / (math.tau / 16)) % 2 else 'white_concrete')
    w.set(cx, top + 8, cz, 'gold_block')
    w.set(cx, top + 9, cz, 'gold_block')
    for k in range(10, 13):
        w.set(cx, top + k, cz, 'oak_fence')
    for i in range(1, 4):
        w.set(cx + i, top + 12, cz, 'blue_wool')
        w.set(cx + i, top + 11, cz, 'yellow_wool')
    w.sign(cx + 2, G + 2, cz + R + 5, ['ROYAL', 'CAROUSEL', 'All ages', 'welcome!'], wood='dark_oak', facing='south')
    w.label(cx, cz, 'ROYAL CAROUSEL', 'attraction')


# =============================================================================================
def hollowmoor_manor(w, paths, x0=362, z0=30):
    """Family-friendly haunted mansion with a graveyard, an indoor 'doom buggy' ride and friendly ghosts."""
    st = style(wall='deepslate_bricks', base='cobbled_deepslate', pillar='polished_deepslate',
               trim='dark_oak_planks', floor='dark_oak_planks', ceiling='dark_oak_planks',
               window='purple_stained_glass_pane', roof_stairs='deepslate_tile_stairs',
               roof_slab='deepslate_tile_slab', roof_block='deepslate_tiles', door='dark_oak', sign_wood='dark_oak',
               light='soul_lantern[hanging=true]', carpet='purple_carpet', wall_light='soul_lantern[hanging=false]')
    Wd, Dp = 38, 26
    b = building(w, x0 + 8, z0 + 4, Wd, Dp, 'south', st, floors=3, fh=6, roof='hip', kind='plain',
                 awning=False, door_double=True, sign_lines=['HOLLOWMOOR', 'MANOR', '999 happy', 'haunts'])
    # ride track through the ground floor: black path with buggies
    track = []
    for lx in range(3, Wd - 3):
        track.append((lx, 4))
    for lz in range(4, Dp - 4):
        track.append((Wd - 4, lz))
    for lx in range(Wd - 4, 2, -1):
        track.append((lx, Dp - 5))
    for lz in range(Dp - 5, 9, -1):
        track.append((3, lz))
    for (lx, lz) in track:
        b.set(lx, -1, lz, 'black_concrete')
    for i, (lx, lz) in enumerate(track):
        if i % 9 == 0:
            b.set(lx, 0, lz, 'polished_blackstone_stairs[facing=south,half=bottom]')
            b.set(lx, 1, lz, 'purple_stained_glass_pane') if False else None
    # spooky rooms: library, ballroom with ghost dancers, portrait gallery
    for lx in range(6, Wd - 6, 2):
        b.set(lx, 0, 8, 'bookshelf')
        b.set(lx, 1, 8, 'bookshelf')
    for (lx, lz) in ((12, 14), (18, 16), (24, 13), (28, 17)):
        b.set(lx, 0, lz, 'white_stained_glass')
        b.set(lx, 1, lz, 'white_stained_glass')
        b.set(lx, 2, lz, 'white_wool')
        b.set(lx - 1, 1, lz, 'white_stained_glass_pane')
        b.set(lx + 1, 1, lz, 'white_stained_glass_pane')
    for lx in range(8, Wd - 8, 6):
        for lz in range(8, Dp - 6, 6):
            b.set(lx, 4, lz, 'chain[axis=y]')
            b.set(lx, 3, lz, 'soul_lantern[hanging=true]')
    for (lx, lz) in ((5, 6), (Wd - 6, 6), (5, Dp - 7), (Wd - 6, Dp - 7)):
        b.set(lx, 0, lz, 'cobweb')
        b.set(lx, 3, lz, 'cobweb')
    # ghosts peeking from upper windows
    for lx in range(2, Wd - 2, 6):
        b.set(lx, 7, 1, 'white_wool')
        b.set(lx, 13, 1, 'white_stained_glass')
    # corner tower with witch-hat roof
    tx, tz = b.xz(Wd - 1, 0)
    round_tower(w, tx, tz, 5, G + 1, 26, roof_h=None, wall='deepslate_bricks', windows=True)
    for k in range(14):
        rr = 6.5 * (1 - k / 14.0) + 0.3
        for (x, z) in disc(tx + (0.0 if k < 10 else (k - 10) * 0.4), tz, rr):
            w.set(x, G + 27 + k, z, 'deepslate_tiles' if k % 4 else 'purple_concrete')
    w.set(int(tx + 2), G + 41, tz, 'soul_lantern')
    # porch with jack-o-lanterns
    for lx in range(Wd // 2 - 6, Wd // 2 + 7):
        for lz in (-1, -2, -3):
            b.set(lx, -1, lz, 'dark_oak_planks')
        b.set(lx, 4, -3, 'deepslate_tile_slab[type=bottom]')
        b.set(lx, 4, -2, 'deepslate_tile_slab[type=bottom]')
        b.set(lx, 4, -1, 'deepslate_tile_slab[type=bottom]')
    for lx in (Wd // 2 - 6, Wd // 2 + 6):
        for ly in range(0, 4):
            b.set(lx, ly, -3, 'dark_oak_fence')
        b.set(lx, 0, -4, 'jack_o_lantern[facing=south]' if False else 'jack_o_lantern[facing=north]')
    # graveyard + iron fence + dead trees
    gx0, gz0, gx1, gz1 = x0 + 4, z0 + Dp + 10, x0 + Wd + 14, z0 + Dp + 30
    rnd = random.Random(7)
    for x in range(gx0, gx1 + 1):
        for z in range(gz0, gz1 + 1):
            w.set(x, G, z, 'podzol' if hash2(x, z, 2) < 0.6 else 'coarse_dirt')
            edge = x in (gx0, gx1) or z in (gz0, gz1)
            if edge:
                w.set(x, G + 1, z, 'iron_bars' if x % 4 else 'polished_blackstone_wall')
            elif (x - gx0) % 4 == 2 and (z - gz0) % 5 == 2 and not (abs(x - (gx0 + gx1) // 2) < 4):
                kind = rnd.choice(['stone_brick_wall', 'cobblestone_wall', 'mossy_cobblestone_wall', 'andesite_wall'])
                w.set(x, G + 1, z, kind)
                if rnd.random() < 0.5:
                    w.set(x, G + 2, z, 'stone_button[face=floor,facing=north,powered=false]' if False else kind)
                if rnd.random() < 0.3:
                    w.set(x, G + 1, z + 1, 'jack_o_lantern[facing=south]')
                elif rnd.random() < 0.3:
                    w.set(x, G + 1, z + 1, 'candle[candles=2,lit=true]')
    mid = (gx0 + gx1) // 2
    for z in range(z0 + Dp + 4, gz1 + 6):
        for x in range(mid - 2, mid + 3):
            w.set(x, G, z, 'cobbled_deepslate' if (x + z) % 2 else 'mossy_cobblestone')
            if z in (gz0, gz1):
                w.set(x, G + 1, z, 'air')
            paths.mask[z, x] = True
    for (tx2, tz2) in ((gx0 + 3, gz0 + 15), (gx1 - 4, gz0 + 5), (gx0 + 12, gz0 + 10)):
        for k in range(0, 7):
            w.set(tx2, G + 1 + k, tz2, 'dark_oak_log[axis=y]')
        for d, (dx, dz) in enumerate(((1, 0), (-1, 0), (0, 1))):
            for t in range(1, 4):
                w.set(tx2 + dx * t, G + 5 + t // 2, tz2 + dz * t, f'dark_oak_log[axis={"x" if dx else "z"}]')
    # friendly floating ghosts over the graveyard
    for (gxx, gzz, gy) in ((mid - 8, gz0 + 8, G + 6), (mid + 9, gz0 + 12, G + 8)):
        for (x, y, z) in ellipsoid(gxx, gy, gzz, 1.5, 2.2, 1.5):
            w.set(x, y, z, 'white_stained_glass')
        w.set(gxx, gy + 1, gzz - 2, 'black_concrete') if False else None
    w.sign(mid, G + 2, gz1 + 1, ['HOLLOWMOOR', 'MANOR', 'Ghosts welcome', 'Frights: mild'], wood='dark_oak',
           facing='south', color='purple', glow=True)
    w.label(x0 + 27, z0 + 18, 'HOLLOWMOOR MANOR (haunted house)', 'attraction')
    return (mid, gz1 + 2)


def dragon(w, cx, cz, y):
    """Stylised dragon sculpture perched on a rock."""
    # rock
    for (x, yy, z) in ellipsoid(cx, y, cz, 9, 5, 7):
        if yy >= G:
            w.set(x, yy, z, 'stone' if hash3(x, yy, z, 1) < 0.6 else ('mossy_cobblestone' if hash3(x, yy, z, 2) < 0.5 else 'andesite'))
    top = y + 5
    green, belly = 'green_concrete', 'lime_terracotta'
    for (x, yy, z) in ellipsoid(cx, top + 3, cz, 5, 2.6, 2.4):
        w.set(x, yy, z, green if yy > top + 2 else belly)
    # legs
    for (dx, dz) in ((-3, -2), (3, -2), (-3, 2), (3, 2)):
        for k in range(0, 3):
            w.set(cx + dx, top + k, cz + dz, green)
    # neck and head
    pts = thick_line((cx + 4, top + 4, cz), (cx + 9, top + 11, cz))
    for (x, yy, z) in pts:
        for (a, b2, c) in ball(x, yy, z, 1.0):
            w.set(a, b2, c, green)
    hx, hy, hz = cx + 10, top + 11, cz
    for x in range(hx - 1, hx + 4):
        for yy in range(hy - 1, hy + 2):
            for z in range(hz - 1, hz + 2):
                w.set(x, yy, z, green)
    w.set(hx + 3, hy - 1, hz, 'red_concrete')
    w.set(hx + 4, hy - 1, hz, 'shroomlight')
    w.set(hx + 1, hy + 1, hz - 2, 'magma_block')
    w.set(hx + 1, hy + 1, hz + 2, 'magma_block')
    w.set(hx, hy + 2, hz - 1, 'bone_block[axis=y]')
    w.set(hx, hy + 2, hz + 1, 'bone_block[axis=y]')
    # tail
    pts = thick_line((cx - 5, top + 3, cz), (cx - 13, top - 2, cz + 4))
    for (x, yy, z) in pts:
        w.set(x, yy, z, green)
    # spikes
    for x in range(cx - 4, cx + 5, 2):
        w.set(x, top + 6, cz, 'lime_concrete')
    # wings
    for side in (-1, 1):
        for i in range(1, 11):
            for j in range(0, 11 - i):
                x = cx - 2 + j // 2 + (i // 3)
                z = cz + side * (2 + i)
                yy = top + 5 + j // 2 + (i // 2)
                w.set(x, yy, z, 'green_terracotta' if j % 4 else green)


# =============================================================================================
def village(w, paths):
    # village street and plazas
    paths.draw([(222, 110), (245, 100), (395, 100), (418, 110)], 9, land='medieval')
    paths.draw([(320, 113), (320, 96)], 9, land='medieval')
    paths.draw([(320, 64), (320, 24)], 7, land='medieval')
    paths.draw([(320, 206), (262, 206), (262, 104)], 7, land='medieval')
    paths.draw([(320, 206), (378, 206), (378, 104)], 7, land='medieval')
    paths.draw([(255, 100), (252, 70)], 5, land='medieval')
    for (x, z) in disc(320, 80, 17):
        w.set(x, G, z, 'stone_bricks' if hash2(x, z, 1) < 0.6 else 'cobblestone')
        paths.mask[z, x] = True
    carousel(w, 320, 80)
    mx, mz = hollowmoor_manor(w, paths)
    paths.draw([(mx, mz), (mx, 100)], 5, land='medieval')
    # Dragon's Feast Hall (restaurant) - great timber hall
    hall = dict(TUDOR, wall='white_terracotta', base='stone_bricks', light='lantern[hanging=true]')
    building(w, 228, 30, 40, 24, 'south', hall, floors=1, fh=9, roof='gable_x', name="DRAGON'S FEAST HALL",
             kind='restaurant', awning=False, door_double=True, chimney=True,
             sign_lines=["DRAGON'S", 'FEAST HALL', 'Turkey legs', '& royal pies'])
    # Tudor shops along the village street (north side faces south, south side faces north)
    north_side = [(275, 12, 'SWEET SHOPPE', TUDOR), (290, 11, 'WIZARD WANDS', TUDOR2), (338, 12, 'ROYAL TAILOR', TUDOR3),
                  (353, 11, 'CURIO SHOP', TUDOR)]
    for (x0, wd, name, st) in north_side:
        tudor_building(w, x0, 84, wd, 11, 'south', st, floors=2, roof='steep_z', name=name, kind='shop',
                       awning=False, chimney=(x0 % 2 == 0))
    south_side = [(232, 11, 'ARMORY', TUDOR3), (246, 12, 'BAKERY', TUDOR2), (395, 12, 'TOY MAKER', TUDOR),
                  (395 - 15, 0, None, None)]
    for (x0, wd, name, st) in south_side:
        if not name:
            continue
        tudor_building(w, x0, 106, wd, 11, 'north', st, floors=2, roof='steep_z', name=name, kind='shop',
                       awning=False)
    # market stalls
    for row, z0 in enumerate((124, 134)):
        for i, x0 in enumerate(range(226, 256, 7)):
            _stall(w, x0, z0, ['red', 'yellow', 'blue', 'green', 'purple'][(i + row) % 5])
        for i, x0 in enumerate(range(386, 414, 7)):
            _stall(w, x0, z0, ['purple', 'orange', 'red', 'cyan'][(i + row) % 4])
    for (x, z) in ((240, 129), (400, 129)):
        decor.lamp(w, x, G + 1, z, 'medieval')
    w.label(240, 132, 'Market Square', 'small')
    # well
    for (x, z) in circle(300, 64, 2):
        w.set(x, G + 1, z, 'cobblestone')
    w.set(300, G, 64, 'water')
    for (x, z) in ((298, 64), (302, 64)):
        for k in range(2, 4):
            w.set(x, G + k, z, 'spruce_fence')
    for x in range(298, 303):
        w.set(x, G + 4, 64, 'spruce_slab[type=bottom]')
    w.set(300, G + 3, 64, 'chain[axis=y]')
    # services
    restroom(w, 236, 150, 'east', TUDOR3)
    info_booth(w, 290, 210, TUDOR)
    food_kiosk(w, 238, 172, 'east', 'TURKEY LEGS', ('red', 'yellow'), 'spruce', subtitle='ROASTED')
    food_kiosk(w, 395, 150, 'west', 'ROYAL TARTS', ('blue', 'yellow'), 'spruce', subtitle='& CIDER')
    tudor_building(w, 390, 168, 14, 12, 'west', TUDOR2, floors=2, roof='steep_z', name='ENCHANTED GIFTS',
                   kind='shop', awning=False)
    # Dragon's Rock
    dragon(w, 246, 196, G)
    w.label(246, 196, "Dragon's Rock", 'small')
    # banner posts along the drawbridge approach
    for z in range(206, 218, 4):
        decor.banner_post(w, 313, G + 1, z, 'blue', 'west')
        decor.banner_post(w, 327, G + 1, z, 'blue', 'east')
    decor.lawn_trees(w, 224, 22, 418, 218, ['oak', 'dark_oak', 'birch', 'spruce'], spacing=12, seed=50,
                     avoid=lambda x, z: paths.mask[z, x] or (264 <= x <= 376 and 104 <= z <= 205))
    w.label(320, 40, 'EVERSPIRE KINGDOM', 'land')


def timber(b, Wd, Dp, y0, y1, wall, post='stripped_dark_oak_log[axis=y]', brace='dark_oak_planks'):
    """Half-timbering: vertical posts every 3 blocks and diagonal braces on infill walls."""
    from ..world import norm
    wn = norm(wall)
    perim = [(x, 0) for x in range(Wd)] + [(x, Dp - 1) for x in range(Wd)] + \
            [(0, z) for z in range(1, Dp - 1)] + [(Wd - 1, z) for z in range(1, Dp - 1)]
    for (lx, lz) in perim:
        along = lx if lz in (0, Dp - 1) else lz
        for ly in range(y0, y1 + 1):
            if b.get(lx, ly, lz) != wn:
                continue
            if along % 3 == 0:
                b.set(lx, ly, lz, post)
            elif (along + ly) % 3 == 0 and ly == y0:
                b.set(lx, ly, lz, brace)


def tudor_building(w, x0, z0, wd, dp, facing, st, **kw):
    b = building(w, x0, z0, wd, dp, facing, st, **kw)
    fh = kw.get('fh', 5)
    floors = kw.get('floors', 2)
    if floors > 1:
        timber(b, wd, dp, fh, floors * fh - 2, st['wall'])
    return b


def _stall(w, x0, z0, color):
    for x in range(x0, x0 + 5):
        for z in range(z0, z0 + 4):
            w.set(x, G + 4, z, f'{color}_wool' if (x - x0) % 2 else 'white_wool')
    for (x, z) in ((x0, z0), (x0 + 4, z0), (x0, z0 + 3), (x0 + 4, z0 + 3)):
        for k in range(1, 4):
            w.set(x, G + k, z, 'spruce_fence')
    for x in range(x0 + 1, x0 + 4):
        w.set(x, G + 1, z0 + 1, 'barrel[facing=up]' if x % 2 else 'spruce_planks')
        w.set(x, G + 2, z0 + 1, ['melon', 'pumpkin', 'hay_block', 'cake'][(x + z0) % 4] if x % 2 == 0 else 'air')


def build(w, paths):
    castle(w, paths)
    village(w, paths)
