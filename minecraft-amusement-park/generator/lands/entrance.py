"""Grand Entrance: gate station, turnstiles, ticket booths, security, welcome sign, parking and tram stop."""
import math

from ..const import G
from ..geom import disc, circle, ring
from ..world import Builder, hash2
from ..font import giant_text, text_width
from .. import decor
from ..buildings import style, building, cone_roof, hip_roof, gable_roof, box_walls, food_kiosk, restroom

GX0, GX1 = 262, 378       # gate station footprint
GZ0, GZ1 = 612, 638
TUNNELS = [(287, 301), (339, 353)]


def gate_station(w, paths):
    """Victorian station building spanning the berm with two walk-through tunnels."""
    y = G + 1
    top = 17
    # body
    for x in range(GX0, GX1 + 1):
        for z in range(GZ0, GZ1 + 1):
            w.set(x, G, z, 'polished_andesite')
            edge = x in (GX0, GX1) or z in (GZ0, GZ1)
            for k in range(0, top):
                if edge:
                    blk = 'bricks'
                    if k in (0,):
                        blk = 'stone_bricks'
                    if k in (6, 12):
                        blk = 'smooth_quartz'
                    w.set(x, y + k, z, blk)
            w.set(x, y + top, z, 'smooth_quartz')
    # interior floors (upper levels hold the station offices)
    for x in range(GX0 + 1, GX1):
        for z in range(GZ0 + 1, GZ1):
            w.set(x, y + 6, z, 'spruce_planks')
            w.set(x, y + 12, z, 'spruce_planks')
    # windows on both long faces
    for x in range(GX0 + 2, GX1 - 1):
        if (x - GX0) % 4 == 2:
            for z in (GZ0, GZ1):
                for k in (8, 9, 10, 14, 15):
                    w.set(x, y + k, z, 'glass_pane')
    # tunnels
    for (tx0, tx1) in TUNNELS:
        for x in range(tx0, tx1 + 1):
            for z in range(GZ0, GZ1 + 1):
                for k in range(0, 9):
                    w.set(x, y + k, z, 'air')
                w.set(x, G, z, 'smooth_stone' if (x + z) % 4 else 'polished_andesite')
                paths.mask[z, x] = True
            for z in range(GZ0, GZ1 + 1):
                arch = 9 if tx0 + 2 <= x <= tx1 - 2 else 8
                w.set(x, y + 9, z, 'stone_bricks')
        for z in range(GZ0, GZ1 + 1):
            for k in range(0, 9):
                w.set(tx0 - 1, y + k, z, 'stone_bricks')
                w.set(tx1 + 1, y + k, z, 'stone_bricks')
            if (z - GZ0) % 4 == 2:
                w.set((tx0 + tx1) // 2, y + 8, z, 'lantern[hanging=true]')
                w.set(tx0 + 3, y + 8, z, 'lantern[hanging=true]')
                w.set(tx1 - 3, y + 8, z, 'lantern[hanging=true]')
        # arch trims on both faces
        for z in (GZ0 - 1, GZ1 + 1):
            for x in range(tx0 - 1, tx1 + 2):
                w.set(x, y + 9, z, 'smooth_quartz_slab[type=top]' if False else 'quartz_stairs[facing=%s,half=top]' % ('south' if z == GZ0 - 1 else 'north'))
        # attraction posters inside the tunnels
        for z in range(GZ0 + 3, GZ1 - 2, 6):
            w.sign(tx0, y + 2, z, ['COMING SOON', 'HYPERION', 'Loop the loop!', ''], wood='dark_oak', facing='east')
            w.sign(tx1, y + 2, z, ['NOW OPEN', 'GOLD RUSH GULCH', 'Log Flume', ''], wood='dark_oak', facing='west')
    # corner towers with conical roofs and clock faces
    for (tx, label) in ((GX0 + 7, 'W'), (GX1 - 7, 'E')):
        for x in range(tx - 7, tx + 8):
            for z in range(GZ0 - 2, GZ0 + 13):
                edge = x in (tx - 7, tx + 7) or z in (GZ0 - 2, GZ0 + 12)
                for k in range(0, 34):
                    if edge:
                        blk = 'bricks' if k % 11 not in (0, 10) else 'smooth_quartz'
                        w.set(x, y + k, z, blk)
                    elif k < top + 1:
                        pass
                    else:
                        w.set(x, y + k, z, 'air')
                w.set(x, y + 34, z, 'smooth_quartz')
        for k in range(18, 33, 3):
            for x in range(tx - 5, tx + 6, 2):
                w.set(x, y + k, GZ0 - 2, 'glass_pane')
                w.set(x, y + k + 1, GZ0 - 2, 'glass_pane')
        cone_roof(w, tx + 0.0, GZ0 + 5.0, y + 35, 9.5, 16, 'dark_prismarine_stairs', 'dark_prismarine',
                  finial=['gold_block', 'gold_block', 'lightning_rod[facing=up]'])
        # clock faces on the south (outside) and north (inside) faces
        for (cz, fz) in ((GZ1 + 1, 'south'),):
            pass
        for zf, step in ((GZ0 - 3, -1),):
            for (x, yy) in [(tx + dx, y + 26 + dy) for dx in range(-4, 5) for dy in range(-4, 5) if dx * dx + dy * dy <= 16]:
                w.set(x, yy, zf, 'white_concrete' if (x - tx) ** 2 + (yy - y - 26) ** 2 < 12 else 'gold_block')
            w.set(tx, y + 26, zf - 1, 'black_concrete') if False else None
            for k in range(0, 4):
                w.set(tx, y + 26 + k, zf, 'black_concrete')
            for k in range(0, 3):
                w.set(tx + k, y + 26, zf, 'black_concrete')
    # tower bodies continue down to the ground on the south side as well
    # roof over the station
    b = Builder(w, 0, y, 0, 0)
    hip_roof(b, GX0 + 15, GX1 - 15, GZ0, GZ1, top + 1, 'dark_prismarine_stairs', 1, steep=True,
             full='dark_prismarine', top='dark_prismarine_slab[type=bottom]')
    # dormer clock on the inside (north) face
    # train platform look on the park side
    for x in range(GX0, GX1 + 1):
        if not any(t0 <= x <= t1 for (t0, t1) in TUNNELS):
            w.set(x, y + 6, GZ0 - 1, 'dark_prismarine_stairs[facing=south,half=bottom]')
            w.set(x, y + 6, GZ0 - 2, 'dark_prismarine_stairs[facing=south,half=bottom]') if False else None
    # big lit lettering on the outside (south) face, centered above the tunnels
    text = 'WONDERHAVEN'
    giant_text(w, text, 320, y + 10, GZ1 + 1, 'south', 'redstone_lamp[lit=true]', scale=1, depth=1,
               back='redstone_block')
    # the backing redstone blocks need to sit in the wall: shift them into the wall plane
    giant_text(w, 'EST. 2026', 320, y + 2, GZ1 + 1, 'south', 'gold_block', scale=1) if False else None
    w.sign(320, y + 3, GZ1 + 1, ['', 'WONDERHAVEN', 'STATION', ''], wood='dark_oak', facing='south',
           color='yellow', glow=True)
    # station clock face on park side centre
    for (dx, dy) in [(dx, dy) for dx in range(-3, 4) for dy in range(-3, 4) if dx * dx + dy * dy <= 9]:
        w.set(320 + dx, y + 13 + dy, GZ0 - 1, 'white_concrete' if dx * dx + dy * dy < 7 else 'gold_block')
    for k in range(3):
        w.set(320, y + 13 + k, GZ0 - 1, 'black_concrete')
        w.set(320 + k, y + 13, GZ0 - 1, 'black_concrete')
    w.label(320, 626, 'Grand Gate', 'small')


def floral_star(w, cx, cz, r=10):
    """Big floral star bed in front of the gate (like a classic floral portrait)."""
    for x in range(cx - r - 1, cx + r + 2):
        for z in range(cz - r // 2 - 1, cz + r // 2 + 2):
            dx, dz = (x - cx), (z - cz) * 2.0
            d = math.hypot(dx, dz)
            if d > r + 0.5:
                continue
            a = math.atan2(dz, dx)
            star_r = r * (0.55 + 0.45 * abs(math.cos(2.5 * a)))
            w.set(x, G, z, 'grass_block')
            if d <= star_r * 0.75:
                w.set(x, G + 1, z, 'dandelion' if (x + z) % 2 else 'orange_tulip')
            elif d <= star_r:
                w.set(x, G + 1, z, 'red_tulip')
            else:
                w.set(x, G + 1, z, 'cornflower' if (x + z) % 2 else 'blue_orchid')
    for (x, z) in circle(cx, cz, r + 0.6):
        pass


def turnstiles(w, paths):
    z0, z1 = 643, 649
    for (tx0, tx1) in TUNNELS:
        for x in range(tx0 - 2, tx1 + 3):
            for z in range(z0, z1 + 1):
                w.set(x, G, z, 'smooth_stone')
                paths.mask[z, x] = True
        for x in range(tx0 - 2, tx1 + 3, 3):
            for z in range(z0, z1 + 1):
                w.set(x, G + 1, z, 'iron_bars' if z not in (z0, z1) else 'polished_blackstone_wall')
            w.set(x, G + 1, z0 + 3, 'polished_andesite')
            w.set(x, G + 2, z0 + 3, 'sea_lantern')
            w.set(x, G + 3, z0 + 3, 'smooth_stone_slab[type=bottom]')
        # canopy over turnstiles
        for x in range(tx0 - 3, tx1 + 4):
            for z in range(z0 - 1, z1 + 2):
                w.set(x, G + 5, z, 'white_wool' if (x // 2) % 2 else 'blue_wool')
        for x in (tx0 - 3, tx1 + 3):
            for z in (z0 - 1, z1 + 1):
                for k in range(1, 5):
                    w.set(x, G + k, z, 'polished_blackstone_wall')
        w.sign((tx0 + tx1) // 2, G + 4, z1 + 1, ['', 'ENTRANCE', 'Have tickets ready', ''], wood='dark_oak',
               facing='south', color='white', glow=True)


def ticket_booth_building(w, x0, z0, title):
    """Victorian ticket office with 6 windows facing south and a stanchion queue."""
    st = style(wall='bricks', pillar='quartz_pillar[axis=y]', trim='smooth_quartz', floor='dark_oak_planks',
               roof_stairs='dark_prismarine_stairs', roof_slab='dark_prismarine_slab', roof_block='dark_prismarine',
               door='dark_oak', sign_wood='dark_oak', counter='dark_oak_planks',
               counter_top='smooth_quartz_slab[type=bottom]')
    Wd, Dp = 34, 10
    b = Builder.for_rect(w, x0, z0, Wd, Dp, 'south', G + 1)
    b.clear(-1, 0, -4, Wd, 14, Dp)
    b.fill(0, -1, 0, Wd - 1, -1, Dp - 1, 'dark_oak_planks')
    box_walls(b, 0, Wd - 1, 0, Dp - 1, 0, 6, 'bricks', 'quartz_pillar[axis=y]')
    b.fill(1, 0, 1, Wd - 2, 5, Dp - 2, 'air')
    b.fill(0, 6, 0, Wd - 1, 6, Dp - 1, 'smooth_quartz')
    # windows: openings with counters
    for i in range(6):
        cx = 3 + i * 5 + 1
        for lx in (cx, cx + 1):
            b.set(lx, 0, 0, 'dark_oak_planks')
            b.set(lx, 1, 0, 'smooth_quartz_slab[type=bottom]')
            b.set(lx, 2, 0, 'glass_pane')
            b.set(lx, 3, 0, 'glass_pane')
            b.set(lx, 0, 1, 'dark_oak_stairs[facing=north,half=top]')
        b.set(cx, 2, 2, 'smithing_table') if False else None
        b.set(cx, 0, 3, 'dark_oak_stairs[facing=south,half=bottom]')   # clerk chair
        b.set(cx, 1, 1, 'lectern[facing=north,has_book=false,powered=false]') if False else None
        b.sign(cx, 4, -1, ['', 'WINDOW %d' % (i + 1), title if i % 3 else 'ANNUAL PASS', ''],
               wood='dark_oak', facing='north', color='white', glow=True)
        # stanchion queue lanes in front
        for lz in range(-2, -9, -1):
            for lx in (cx - 1, cx + 2):
                if lz == -8:
                    continue
                b.set(lx, -1, lz, 'polished_andesite')
                b.set(lx, 0, lz, 'polished_blackstone_wall' if lz % 2 == 0 else 'chain[axis=x]' if False else 'iron_bars')
    b.door(Wd // 2, 0, Dp - 1, 'south', 'dark_oak')
    hang = [(lx, 5, 4) for lx in range(3, Wd - 3, 4)]
    for p in hang:
        b.set(*p, 'lantern[hanging=true]')
    # awning over the windows
    for lx in range(0, Wd):
        b.set(lx, 5, -1, 'blue_wool' if lx % 2 else 'white_wool')
        b.set(lx, 4, -2, 'blue_wool' if lx % 2 else 'white_wool')
    hip_roof(b, 0, Wd - 1, 0, Dp - 1, 7, 'dark_prismarine_stairs', 1, full='dark_prismarine',
             top='dark_prismarine_slab[type=bottom]')
    b.sign(Wd // 2, 6, -1, ['', 'TICKETS', title, ''], wood='dark_oak', facing='north', color='yellow', glow=True)
    b.sign(Wd // 2 - 1, 6, -1, ['', 'TICKETS', title, ''], wood='dark_oak', facing='north', color='yellow', glow=True)


def security(w, paths, z0=680):
    """Bag-check tents with metal-detector arches across the approach."""
    for i, x0 in enumerate(range(266, 374, 9)):
        # tent canopy
        for x in range(x0, x0 + 8):
            for z in range(z0, z0 + 8):
                w.set(x, G + 5, z, 'white_wool' if (x + z) % 2 else 'light_blue_wool')
        for (x, z) in ((x0, z0), (x0 + 7, z0), (x0, z0 + 7), (x0 + 7, z0 + 7)):
            for k in range(1, 5):
                w.set(x, G + k, z, 'iron_bars')
        # metal detector arch
        ax = x0 + 3
        for k in range(1, 4):
            w.set(ax, G + k, z0 + 4, 'iron_block')
            w.set(ax + 2, G + k, z0 + 4, 'iron_block')
        w.set(ax, G + 4, z0 + 4, 'iron_block')
        w.set(ax + 1, G + 4, z0 + 4, 'iron_block')
        w.set(ax + 2, G + 4, z0 + 4, 'iron_block')
        w.set(ax + 1, G + 4, z0 + 3, 'redstone_lamp[lit=true]') if False else None
        # bag tables
        for z in range(z0 + 1, z0 + 4):
            w.set(x0 + 1, G + 1, z, 'smooth_stone_slab[type=top]')
            w.set(x0 + 6, G + 1, z, 'smooth_stone_slab[type=top]')
        w.set(x0 + 1, G + 2, z0 + 2, 'barrel[facing=up]') if i % 2 else None
        w.sign(x0 + 4, G + 4, z0 + 8, ['', 'SECURITY', 'Bag Check', ''], wood='birch', facing='south')
    w.label(320, z0 + 4, 'Security', 'small')


def welcome_sign(w, z=702):
    """Giant freestanding marquee sign: WELCOME TO / WONDERHAVEN, framed by two pylons."""
    y = G + 1
    tw = text_width('WONDERHAVEN', 2)
    x_left = 320 - tw // 2
    x_right = x_left + tw - 1
    for (px0, px1) in ((x_left - 14, x_left - 5), (x_right + 5, x_right + 14)):
        for x in range(px0, px1 + 1):
            for zz in range(z - 4, z + 2):
                edge = x in (px0, px1) or zz in (z - 4, z + 1)
                for k in range(0, 36):
                    if edge or k in (0, 35):
                        blk = 'quartz_bricks'
                        if k % 7 == 6:
                            blk = 'gold_block' if (x + zz) % 2 == 0 else 'smooth_quartz'
                        if 8 < k < 32 and (x - px0) % 3 == 1 and zz in (z - 4, z + 1) and k % 7 != 6:
                            blk = 'light_blue_stained_glass'
                        w.set(x, y + k, zz, blk)
                    else:
                        w.set(x, y + k, zz, 'sea_lantern' if k in (12, 24) else 'air')
        cx = (px0 + px1) / 2.0
        cone_roof(w, cx, z - 1.5, y + 36, 6.8, 10, 'quartz_stairs', 'quartz_block',
                  finial=['gold_block', 'gold_block', 'end_rod[facing=up]'])
    # beams
    for x in range(x_left - 5, x_right + 6):
        for zz in (z, z - 1, z - 2):
            w.set(x, y + 11, zz, 'quartz_block' if x % 6 else 'sea_lantern')
            w.set(x, y + 26, zz, 'quartz_block' if x % 6 else 'sea_lantern')
            w.set(x, y + 27, zz, 'quartz_slab[type=bottom]')
    # letters: lit marquee lamps with redstone blocks behind them
    giant_text(w, 'WONDERHAVEN', 320, y + 12, z, 'south', 'redstone_lamp[lit=true]', scale=2, depth=1,
               back='redstone_block')
    # back panel (seen when leaving) with a farewell message
    for x in range(x_left - 5, x_right + 6):
        for k in range(12, 26):
            w.set(x, y + k, z - 2, 'smooth_quartz')
    giant_text(w, 'SEE YOU SOON', 320, y + 16, z - 3, 'north', 'light_blue_concrete', scale=1)
    # WELCOME TO, on top
    giant_text(w, 'WELCOME TO', 320, y + 28, z, 'south', 'sea_lantern', scale=1, back='quartz_block')
    w.label(320, z + 1, 'WELCOME SIGN', 'attraction')


def parking(w, x0, x1, z0=650, z1=713, label='Parking'):
    """Parking lot: asphalt with bays, tree islands, light poles and parked cars."""
    colors = ['red', 'blue', 'white', 'black', 'gray', 'light_gray', 'yellow', 'green', 'cyan', 'orange', 'purple']
    for x in range(x0, x1 + 1):
        for z in range(z0, z1 + 1):
            w.set(x, G, z, 'gray_concrete' if hash2(x, z, 2) < 0.85 else 'gray_concrete_powder')
    rows = list(range(z0 + 3, z1 - 8, 16))
    for r, rz in enumerate(rows):
        # two facing rows of bays separated by a planted median
        for x in range(x0 + 2, x1 - 2):
            if (x - x0) % 4 == 0:
                for dz in range(0, 5):
                    w.set(x, G, rz + dz, 'white_concrete')
                    w.set(x, G, rz + 7 + dz, 'white_concrete')
            w.set(x, G, rz + 5, 'stone_bricks')
            w.set(x, G, rz + 6, 'grass_block')
            if (x - x0) % 20 == 10:
                decor.tree(w, x, G + 1, rz + 6, 'oak', 0.75)
            elif (x - x0) % 20 == 0:
                for k in range(1, 8):
                    w.set(x, G + k, rz + 6, 'iron_bars' if k > 1 else 'smooth_stone')
                w.set(x, G + 8, rz + 6, 'sea_lantern')
                w.set(x, G + 9, rz + 6, 'smooth_stone_slab[type=bottom]')
            else:
                w.set(x, G + 1, rz + 6, decor.leaves('azalea') if (x % 3) else 'air')
        # cars
        for x in range(x0 + 3, x1 - 3, 4):
            for (cz, face) in ((rz, 'n'), (rz + 7, 's')):
                if hash2(x, cz, 17) < 0.55:
                    _car(w, x, cz + (1 if face == 'n' else 0), colors[int(hash2(x, cz, 18) * len(colors))])
    # light poles along the edges
    w.label((x0 + x1) // 2, (z0 + z1) // 2, label, 'small')


def _car(w, x, z, color):
    y = G + 1
    body = f'{color}_concrete'
    for dz in range(4):
        for dx in range(2):
            wheel = dz in (0, 3)
            w.set(x + dx, y, z + dz, 'black_concrete' if wheel and False else body)
    for dx in range(2):
        w.set(x + dx, y + 1, z + 1, 'light_gray_stained_glass' if False else 'glass')
        w.set(x + dx, y + 1, z + 2, 'glass')
        w.set(x + dx, y + 1, z, f'{color}_carpet' if color not in ('gray', 'light_gray', 'black') else 'black_carpet')
        w.set(x + dx, y + 1, z + 3, f'{color}_carpet' if color not in ('gray', 'light_gray', 'black') else 'black_carpet')
        w.set(x + dx, y + 2, z + 1, 'smooth_stone_slab[type=bottom]' if False else f'{color}_carpet')
        w.set(x + dx, y + 2, z + 2, f'{color}_carpet')


def tram_stop(w):
    """Shuttle tram stop with shelters along the south edge."""
    for x in range(200, 441):
        for z in range(714, 720):
            w.set(x, G, z, 'gray_concrete' if z > 715 else 'smooth_stone')
    for x0 in (214, 250, 390, 420):
        for x in range(x0, x0 + 8):
            w.set(x, G + 4, 713, 'white_concrete')
            w.set(x, G + 4, 712, 'white_concrete')
            w.set(x, G + 4, 711, 'white_concrete')
        for x in (x0, x0 + 7):
            for k in range(1, 4):
                w.set(x, G + k, 711, 'iron_bars')
                w.set(x, G + k, 713, 'iron_bars')
        for x in range(x0 + 1, x0 + 7):
            w.set(x, G + 1, 712, 'quartz_stairs[facing=north,half=bottom]')
            w.set(x, G + 3, 711, 'light_blue_stained_glass_pane')
        w.sign(x0 + 4, G + 3, 714, ['', 'TRAM STOP', 'Parking & Hotels', ''], wood='birch', facing='south')
    # a parked shuttle tram
    for x in range(290, 340):
        for z in (716, 717, 718):
            seg = (x - 290) // 10
            blk = 'white_concrete' if (x - 290) % 10 else 'black_concrete'
            w.set(x, G + 1, z, blk)
            if z == 717 and (x - 290) % 10 not in (0,):
                w.set(x, G + 2, z, 'quartz_stairs[facing=south,half=bottom]')
            if z != 717 and (x - 290) % 10 in (1, 9):
                w.set(x, G + 2, z, 'iron_bars')
                w.set(x, G + 3, z, 'iron_bars')
        for z in (716, 717, 718):
            w.set(x, G + 4, z, 'light_blue_concrete' if x % 2 else 'white_concrete')


def plaza(w, paths):
    # paving for the whole entrance forecourt
    for x in range(232, 409):
        for z in range(640, 714):
            if abs(x - 320) < 30 and z < 650:
                blk = 'polished_diorite' if (x + z) % 2 else 'polished_andesite'
            else:
                r = math.hypot(x - 320, (z - 668) * 1.3)
                blk = 'polished_andesite' if int(r) % 5 else 'polished_granite'
                if (x - 320) % 12 == 0:
                    blk = 'polished_diorite'
            w.set(x, G, z, blk)
            paths.mask[z, x] = True
    decor.fountain(w, 320, 670, 9, tiers=3)
    floral_star(w, 320, 652, 12)
    # flower beds and lamps
    for (bx0, bz0) in ((240, 640), (382, 640)):
        decor.flower_bed(w, bx0, bz0, bx0 + 16, bz0 + 7, border='polished_blackstone_wall', density=0.9, seed=bx0)
    for x in range(236, 406, 12):
        for z in (676, 696):
            if math.hypot(x - 320, z - 670) > 12:
                decor.lamp(w, x, G + 1, z, 'classic', arm='x')
    for x in (234, 406):
        for z in range(644, 712, 10):
            if z % 20 == 4:
                decor.flagpole(w, x, G + 1, z, 12, ('blue_wool', 'yellow_wool'), 'east' if x < 320 else 'west')
            else:
                decor.lamp(w, x, G + 1, z, 'classic')
    for x in range(268, 374, 15):
        decor.bench(w, x, G + 1, 694, 'north', 'dark_oak', 3)
    food_kiosk(w, 236, 686, 'east', 'SNACKS', ('blue', 'white'), 'dark_oak', subtitle='& DRINKS')
    food_kiosk(w, 399, 686, 'west', 'SOUVENIRS', ('red', 'white'), 'dark_oak', subtitle='& PONCHOS')


def build(w, paths):
    plaza(w, paths)
    gate_station(w, paths)
    turnstiles(w, paths)
    ticket_booth_building(w, 238, 652, 'DAY TICKETS')
    ticket_booth_building(w, 368, 652, 'DAY TICKETS')
    security(w, paths)
    welcome_sign(w)
    parking(w, 18, 228, label='West Parking')
    parking(w, 412, 622, label='East Parking')
    tram_stop(w)
    restroom(w, 218, 641, 'east' if False else 'south', style(wall='bricks', trim='smooth_quartz', pillar='stone_bricks',
                                                             roof_stairs='dark_prismarine_stairs', roof_slab='dark_prismarine_slab',
                                                             roof_block='dark_prismarine', door='spruce', sign_wood='spruce',
                                                             ceiling='spruce_planks')) if False else None
    w.label(320, 690, 'GRAND ENTRANCE', 'land')
