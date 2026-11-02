"""Flat rides: Ferris wheel, teacups, swinging pirate ship, big-top tent, bumper cars."""
import math

from .const import G
from .geom import disc, circle, ring, line, ellipsoid, thick_line
from .world import hash2, DVEC
from . import decor

RAINBOW = ['red', 'orange', 'yellow', 'lime', 'light_blue', 'blue', 'purple', 'magenta', 'pink', 'cyan']


def ferris_wheel(w, cx, cz, R=30, n_gondolas=18, name='JOLLY WHEEL'):
    """Ferris wheel in the x-y plane (faces south/north). Returns axle height."""
    cy = G + 1 + R + 7
    planes = (cz - 3, cz + 3)
    # rims, lattice and spokes in both planes
    for zp in planes:
        for x in range(cx - R - 1, cx + R + 2):
            for y in range(cy - R - 1, cy + R + 2):
                d = math.hypot(x - cx, y - cy)
                a = math.atan2(y - cy, x - cx)
                seg = int(((a + math.pi) / math.tau) * 20) % 20
                col = RAINBOW[seg % len(RAINBOW)]
                if R - 0.5 <= d < R + 0.5:
                    w.set(x, y, zp, 'ochre_froglight[axis=y]' if (int(math.degrees(a) + 360) % 9 == 0) else f'{col}_concrete')
                elif R - 3.5 <= d < R - 2.5:
                    w.set(x, y, zp, 'white_concrete')
                elif R - 2.5 <= d < R - 0.5:
                    if int(math.degrees(a) + 360) % 15 in (0, 1):
                        w.set(x, y, zp, 'white_concrete')
                elif d <= 3.5:
                    w.set(x, y, zp, 'gold_block' if d > 2 else 'white_concrete')
        for i in range(18):
            a = i * math.tau / 18
            p0 = (cx + 3 * math.cos(a), cy + 3 * math.sin(a), zp)
            p1 = (cx + (R - 3) * math.cos(a), cy + (R - 3) * math.sin(a), zp)
            for (x, y, z) in line(p0, p1):
                w.set(x, y, z, 'light_gray_concrete')
    # axle
    for z in range(cz - 6, cz + 7):
        w.set(cx, cy, z, 'iron_block')
    # A-frame legs
    for zp in (cz - 5, cz + 5):
        for sx in (-1, 1):
            foot = (cx + sx * 17, G + 1, zp)
            for (x, y, z) in line((cx, cy, zp), foot):
                w.set(x, y, z, 'white_concrete')
                w.set(x + sx, y, z, 'white_concrete')
            w.set(foot[0], G, zp, 'polished_andesite')
            w.set(foot[0] + sx, G, zp, 'polished_andesite')
        for k in range(1, 4):
            yb = cy - k * 9
            t = (cy - yb) / (cy - G - 1)
            xa, xb = int(round(cx - 17 * t)), int(round(cx + 17 * t))
            for x in range(xa, xb + 1):
                w.set(x, yb, zp, 'white_concrete')
    # gondolas
    for i in range(n_gondolas):
        a = i * math.tau / n_gondolas
        ax, ay = int(round(cx + R * math.cos(a))), int(round(cy + R * math.sin(a)))
        col = RAINBOW[i % len(RAINBOW)]
        for z in range(cz - 2, cz + 3):
            w.set(ax, ay, z, 'iron_block')
        w.set(ax, ay - 1, cz, 'chain[axis=y]')
        for x in range(ax - 1, ax + 2):
            for z in range(cz - 2, cz + 3):
                w.set(x, ay - 2, z, f'{col}_concrete')
                w.set(x, ay - 5, z, f'{col}_concrete')
                for y in (ay - 3, ay - 4):
                    edge = x in (ax - 1, ax + 1) or z in (cz - 2, cz + 2)
                    corner = x in (ax - 1, ax + 1) and z in (cz - 2, cz + 2)
                    if corner:
                        w.set(x, y, z, f'{col}_concrete')
                    elif edge:
                        w.set(x, y, z, 'glass_pane' if y == ay - 3 else f'{col}_concrete')
                    else:
                        w.set(x, y, z, 'air')
        w.set(ax, ay - 4, cz - 1, 'quartz_stairs[facing=north,half=bottom]')
        w.set(ax, ay - 4, cz + 1, 'quartz_stairs[facing=south,half=bottom]')
    # boarding platform under the lowest gondola
    by = cy - R - 5
    for x in range(cx - 8, cx + 9):
        for z in range(cz + 3, cz + 12):
            for y in range(G + 1, by):
                w.set(x, y, z, 'white_concrete')
            w.set(x, by - 1, z, 'pink_concrete' if (x + z) % 2 else 'white_concrete')
    for k in range(by - G - 1):
        for x in range(cx - 2, cx + 3):
            w.set(x, G + 1 + k, cz + 12 + (by - G - 2 - k), f'quartz_stairs[facing=north,half=bottom]')
    w.sign(cx + 4, by + 1, cz + 12, ['', name, 'Ferris Wheel', ''], wood='cherry', facing='south', color='magenta', glow=True)
    w.label(cx, cz, name + ' (Ferris wheel)', 'attraction')
    return cy


def teacup(w, cx, cz, y, color):
    for (x, z) in disc(cx, cz, 2.6):
        w.set(x, y - 1, z, 'white_concrete')
    for (x, z) in circle(cx, cz, 2.6):
        w.set(x, y, z, f'{color}_concrete')
        w.set(x, y + 1, z, f'{color}_terracotta' if (x + z) % 2 else f'{color}_concrete')
        w.set(x, y + 2, z, 'smooth_quartz_slab[type=bottom]')
    for (x, z) in disc(cx, cz, 1.6):
        dx, dz = x - cx, z - cz
        if (dx, dz) == (0, 0):
            w.set(x, y, z, 'white_concrete')
            w.set(x, y + 1, z, 'stone_button[face=floor,facing=north,powered=false]')
        else:
            f = 'east' if dx < 0 else 'west' if dx > 0 else 'south' if dz < 0 else 'north'
            f = {'east': 'west', 'west': 'east', 'north': 'south', 'south': 'north'}[f]
            w.set(x, y, z, f'quartz_stairs[facing={f},half=bottom]')
    w.set(cx + 3, y + 1, cz, f'{color}_concrete')
    w.set(cx + 3, y, cz, 'air')


def teacups(w, cx, cz, name='TOPSY TEACUPS'):
    R = 18
    for (x, z) in disc(cx, cz, R):
        w.set(x, G, z, 'pink_concrete' if ((x // 2) + (z // 2)) % 2 else 'white_concrete')
        w.clear(x, G + 1, z, x, G + 14, z)
    for (x, z) in circle(cx, cz, R):
        w.set(x, G + 1, z, 'white_concrete' if (x + z) % 2 else 'magenta_concrete')
    for k in (-1, 0, 1):
        w.set(cx + k, G + 1, cz + R, 'air')
    cols = ['red', 'yellow', 'blue', 'lime', 'purple', 'orange', 'pink', 'cyan', 'light_blue']
    n = 0
    for t in range(3):
        a = t * math.tau / 3 + 0.3
        tx, tz = int(round(cx + 9.5 * math.cos(a))), int(round(cz + 9.5 * math.sin(a)))
        for (x, z) in disc(tx, tz, 7):
            w.set(x, G, z, 'light_blue_concrete')
        for (x, z) in circle(tx, tz, 7):
            w.set(x, G, z, 'yellow_concrete')
        for c in range(3):
            b = c * math.tau / 3 + t
            ux, uz = int(round(tx + 3.6 * math.cos(b))), int(round(tz + 3.6 * math.sin(b)))
            teacup(w, ux, uz, G + 1, cols[n % len(cols)])
            n += 1
    # giant teapot centrepiece
    for (x, y, z) in ellipsoid(cx, G + 4, cz, 3.5, 3.0, 3.5):
        if y >= G + 1:
            w.set(x, y, z, 'white_concrete' if (y - G) % 3 else 'pink_concrete')
    for (x, z) in disc(cx, cz, 2):
        w.set(x, G + 7, z, 'pink_concrete')
    w.set(cx, G + 8, cz, 'gold_block')
    for (x, y, z) in thick_line((cx + 3, G + 3, cz), (cx + 6, G + 7, cz)):
        w.set(x, y, z, 'white_concrete')
    for (x, y) in ((cx - 4, G + 3), (cx - 5, G + 4), (cx - 5, G + 5), (cx - 4, G + 6)):
        w.set(x, y, cz, 'pink_concrete')
    # pergola posts + string lights to a centre mast
    for k in range(G + 9, G + 15):
        w.set(cx, k, cz, 'cherry_fence')
    w.set(cx, G + 15, cz, 'ochre_froglight[axis=y]')
    for i in range(8):
        a = i * math.tau / 8
        px, pz = int(round(cx + (R - 1) * math.cos(a))), int(round(cz + (R - 1) * math.sin(a)))
        for k in range(1, 9):
            w.set(px, G + k, pz, 'cherry_fence')
        w.set(px, G + 9, pz, 'lantern')
        decor.string_lights(w, (px, G + 9, pz), (cx, G + 14, cz), sag=2, every=2)
    w.sign(cx, G + 2, cz + R + 1, ['', name, 'Spin your cup!', ''], wood='cherry', facing='south', color='magenta', glow=True)
    w.label(cx, cz, name, 'attraction')


def swinging_ship(w, cx, cz, angle_deg=32, name="KRAKEN'S REVENGE"):
    """Swinging pirate-ship ride: A-frames, axle, and a hull frozen mid-swing (axle along z)."""
    ay = G + 24
    L = 15
    for zp in (cz - 7, cz + 7):
        for sx in (-1, 1):
            for (x, y, z) in line((cx, ay, zp), (cx + sx * 13, G + 1, zp)):
                w.set(x, y, z, 'dark_oak_log[axis=y]')
            w.set(cx + sx * 13, G, zp, 'stone_bricks')
        for yb in (G + 9, G + 16):
            t = (ay - yb) / (ay - G - 1)
            for x in range(int(cx - 13 * t), int(cx + 13 * t) + 1):
                w.set(x, yb, zp, 'dark_oak_planks')
    for z in range(cz - 7, cz + 8):
        w.set(cx, ay, z, 'iron_block')
    th = math.radians(angle_deg)
    ca, sa = math.cos(th), math.sin(th)

    def place(u, v, wz, blk):
        # local u along hull (x), v up; hull hangs L below the axle, rotated by angle about the axle
        lx, ly = u, v - L
        x = cx + lx * ca - ly * sa
        y = ay + lx * sa + ly * ca
        w.set(int(round(x)), int(round(y)), cz + wz, blk)

    for ui in range(-22, 23):
        u = ui / 2.0
        bottom = -3.2 + 3.0 * (u / 11.0) ** 2
        half = 3.2 - 1.6 * abs(u) / 11.0
        for wi in range(-4, 5):
            if abs(wi) > half + 0.2:
                continue
            for vi in range(int(math.floor(bottom * 2)), 3):
                v = vi / 2.0
                edge = abs(wi) >= half - 0.8 or v <= bottom + 0.6
                if edge:
                    blk = 'dark_oak_planks' if v > bottom + 1.2 else 'spruce_planks'
                    if -0.5 <= v <= 0.0:
                        blk = 'red_concrete'
                    place(u, v, wi, blk)
                elif v >= 1.0:
                    place(u, v, wi, 'spruce_slab[type=bottom]')
        if ui % 4 == 0 and abs(u) < 9:
            for wi in (-2, -1, 1, 2):
                place(u, 1.5, wi, 'dark_oak_stairs[facing=east,half=bottom]')
    # swing arms from the axle to the hull
    for side in (-6, 6):
        for t in [i / 20 for i in range(21)]:
            for u0 in (-6.0, 6.0):
                place(u0 * t, 2.0 + (L - 2.0) * (1 - t) + 0.0, side if abs(side) <= 7 else 0, 'iron_bars') if False else None
        for (u0) in (-6.0, 6.0):
            for i in range(26):
                t = i / 25
                lx, ly = u0 * (1 - t), (-L + 2) * (1 - t)
                x = cx + lx * ca - ly * sa
                y = ay + lx * sa + ly * ca
                w.set(int(round(x)), int(round(y)), cz + side, 'iron_block' if False else 'gray_concrete')
    # mast and skull sail
    for v in range(3, 12):
        place(0, v, 0, 'spruce_log[axis=y]')
    for v in range(5, 11):
        for wi in range(-3, 4):
            place(0.6, v, wi, 'black_wool' if not (v in (8, 9) and abs(wi) == 1) else 'white_wool')
    # loading platform
    for x in range(cx - 9, cx + 10):
        for z in range(cz - 6, cz + 7):
            if w.is_air(x, G + 1, z):
                w.set(x, G, z, 'spruce_planks')
    w.sign(cx, G + 2, cz + 9, ['', name, 'Swinging Ship', ''], wood='dark_oak', facing='south')
    w.label(cx, cz, name + ' (swing ship)', 'attraction')


def big_top(w, cx, cz, R=22, name='BIG TOP CIRCUS', door='west'):
    wall_h = 8
    for (x, z) in disc(cx, cz, R + 1):
        w.set(x, G, z, 'sand' if math.hypot(x - cx, z - cz) < 7 else 'spruce_planks')
        w.clear(x, G + 1, z, x, G + 30, z)
    for (x, z) in circle(cx, cz, R):
        a = math.atan2(z - cz, x - cx)
        stripe = int((a + math.pi) / (math.tau / 32)) % 2
        for y in range(G + 1, G + 1 + wall_h):
            w.set(x, y, z, 'red_wool' if stripe else 'white_wool')
    # roof cone with stripes
    H = 16
    for k in range(H):
        rr = (R + 1.5) * (1 - k / H)
        for (x, z) in disc(cx, cz, rr):
            if math.hypot(x - cx, z - cz) < rr - 1.6 and k < H - 2:
                continue
            a = math.atan2(z - cz, x - cx)
            stripe = int((a + math.pi) / (math.tau / 16)) % 2
            w.set(x, G + 1 + wall_h + k, z, 'red_wool' if stripe else 'yellow_wool')
    for (x, z) in circle(cx, cz, R + 1.5):
        w.set(x, G + wall_h, z, 'blue_wool' if (x + z) % 2 else 'yellow_wool')
    for y in range(G + 1, G + wall_h + H + 4):
        w.set(cx, y, cz, 'stripped_oak_log[axis=y]' if y < G + wall_h + H else 'oak_fence')
    for i in range(1, 4):
        w.set(cx + i, G + wall_h + H + 3, cz, 'red_wool')
    # ring, bleachers, trapeze
    for (x, z) in circle(cx, cz, 7):
        w.set(x, G + 1, z, 'red_concrete')
    for rr in range(10, R - 1):
        hgt = (rr - 10) // 2 + 1
        for (x, z) in circle(cx, cz, rr):
            dx, dz = x - cx, z - cz
            if (door == 'west' and dx < 0 and abs(dz) <= 3):
                continue
            f = ('east' if dx > 0 else 'west') if abs(dx) >= abs(dz) else ('south' if dz > 0 else 'north')
            for y in range(G + 1, G + hgt):
                w.set(x, y, z, 'spruce_planks')
            if (rr - 10) % 2 == 0:
                w.set(x, G + hgt, z, f'spruce_stairs[facing={f},half=bottom]')
            else:
                w.set(x, G + hgt, z, 'spruce_planks')
    for sx in (-5, 5):
        for y in range(G + 1, G + 16):
            w.set(cx + sx, y, cz, 'iron_bars')
    for x in range(cx - 5, cx + 6):
        w.set(x, G + 16, cz, 'iron_bars')
    w.set(cx - 1, G + 12, cz, 'chain[axis=y]')
    w.set(cx - 1, G + 11, cz, 'spruce_slab[type=bottom]')
    for i in range(8):
        a = i * math.tau / 8
        x, z = int(round(cx + (R - 2) * math.cos(a))), int(round(cz + (R - 2) * math.sin(a)))
        w.set(x, G + wall_h + 2, z, 'glowstone')
    # entrance
    dx, dz = DVEC[door]
    for t in range(R - 1, R + 2):
        for o in range(-3, 4):
            x, z = cx + dx * t + (o if dz else 0), cz + dz * t + (o if dx else 0)
            for y in range(G + 1, G + 6):
                w.set(x, y, z, 'air')
            w.set(x, G, z, 'spruce_planks')
    w.sign(cx + dx * (R + 2), G + 6, cz + dz * (R + 2) + 1, ['', name, 'Shows hourly!', ''], wood='oak',
           facing=door, color='red', glow=True)
    w.label(cx, cz, name, 'attraction')


def bumper_cars(w, x0, z0, wd=30, dp=22, name='BUMPER BONANZA'):
    for x in range(x0, x0 + wd):
        for z in range(z0, z0 + dp):
            edge = x in (x0, x0 + wd - 1) or z in (z0, z0 + dp - 1)
            w.set(x, G, z, 'light_gray_concrete' if not edge else 'yellow_concrete')
            w.clear(x, G + 1, z, x, G + 9, z)
            if edge:
                w.set(x, G + 1, z, 'black_wool')
            w.set(x, G + 7, z, 'iron_bars' if not edge else ('red_concrete' if (x + z) % 2 else 'yellow_concrete'))
            w.set(x, G + 8, z, 'smooth_stone_slab[type=bottom]' if not edge else ('red_concrete' if (x + z) % 2 else 'white_concrete'))
    for (x, z) in ((x0, z0), (x0 + wd - 1, z0), (x0, z0 + dp - 1), (x0 + wd - 1, z0 + dp - 1),
                   (x0 + wd // 2, z0), (x0 + wd // 2, z0 + dp - 1)):
        for y in range(G + 1, G + 7):
            w.set(x, y, z, 'white_concrete')
    for x in range(x0 + 4, x0 + wd - 3, 6):
        for z in range(z0 + 4, z0 + dp - 3, 6):
            w.set(x, G + 6, z, 'sea_lantern') if False else None
            w.set(x, G + 7, z, 'glowstone')
    cols = ['red', 'blue', 'yellow', 'lime', 'purple', 'orange', 'pink', 'cyan']
    for i in range(10):
        x = x0 + 3 + int(hash2(i, 1, 5) * (wd - 7))
        z = z0 + 3 + int(hash2(i, 2, 5) * (dp - 7))
        c = cols[i % len(cols)]
        if not w.is_air(x, G + 1, z) or not w.is_air(x + 1, G + 1, z + 2):
            continue
        for dz in range(3):
            for dx in range(2):
                w.set(x + dx, G + 1, z + dz, f'{c}_concrete' if dz else 'black_concrete')
        w.set(x, G + 2, z + 2, 'quartz_stairs[facing=south,half=bottom]')
        w.set(x + 1, G + 2, z + 2, 'quartz_stairs[facing=south,half=bottom]')
        for y in range(G + 2, G + 7):
            w.set(x + 1, y, z + 1, 'iron_bars')
    w.sign(x0 + wd // 2 - 1, G + 6, z0 - 1, ['', name, 'Bumper Cars', ''], wood='cherry', facing='north',
           color='yellow', glow=True)
    w.label(x0 + wd // 2, z0 + dp // 2, name, 'attraction')


def rocket_spinner(w, cx, cz, name='ASTRO SPINNER', arms=8, R=9):
    """Classic rocket spinner: rockets on radial arms around a tall central column, frozen mid-ride."""
    for (x, z) in disc(cx, cz, R + 3):
        w.set(x, G, z, 'light_gray_concrete' if (x + z) % 2 else 'white_concrete')
        w.clear(x, G + 1, z, x, G + 20, z)
    for (x, z) in circle(cx, cz, R + 3):
        w.set(x, G + 1, z, 'iron_bars')
    w.set(cx, G + 1, cz + R + 3, 'air')
    # central column with rings of light
    for y in range(G + 1, G + 15):
        for (x, z) in disc(cx, cz, 1.5):
            w.set(x, y, z, 'sea_lantern' if (y - G) % 4 == 0 else 'white_concrete')
    for (x, z) in disc(cx, cz, 3.2):
        w.set(x, G + 15, z, 'cyan_concrete')
    for y in range(G + 16, G + 20):
        w.set(cx, y, cz, 'iron_bars')
    for (x, y, z) in ellipsoid(cx, G + 21, cz, 2.2, 2.2, 2.2):
        w.set(x, y, z, 'yellow_stained_glass' if (x + y + z) % 3 else 'gold_block')
    w.set(cx, G + 21, cz, 'glowstone')
    cols = ['red', 'blue', 'yellow', 'lime', 'purple', 'orange', 'cyan', 'magenta']
    for i in range(arms):
        a = i * math.tau / arms
        ex, ez = cx + R * math.cos(a), cz + R * math.sin(a)
        ey = G + 8 + (i % 2) * 2
        for (x, y, z) in line((cx, G + 13, cz), (ex, ey + 1, ez)):
            w.set(x, y, z, 'light_gray_concrete')
        tx, tz = -math.sin(a), math.cos(a)
        bx, bz = int(round(ex)), int(round(ez))
        c = cols[i % len(cols)]
        for t in (-1, 0, 1):
            w.set(int(round(ex + tx * t)), ey, int(round(ez + tz * t)), f'{c}_concrete')
        w.set(int(round(ex + tx * 2)), ey, int(round(ez + tz * 2)), 'white_concrete')
        w.set(int(round(ex - tx * 2)), ey, int(round(ez - tz * 2)), 'shroomlight')
        w.set(bx, ey + 1, bz, 'quartz_stairs[facing=north,half=bottom]')
    w.sign(cx, G + 2, cz + R + 4, ['', name, 'Pilot your rocket!', ''], wood='birch', facing='south', color='cyan', glow=True)
    w.label(cx, cz, name, 'attraction')
