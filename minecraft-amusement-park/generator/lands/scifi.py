"""Nova Frontier: futuristic sci-fi land with the Hyperion coaster, Cosmic Voyager indoor coaster dome,
the Gravity Spire drop tower, a rocket landmark, a flying-saucer diner and shops."""
import math

import numpy as np

from ..const import G
from ..geom import disc, circle, ring, ball, sphere_shell, line
from ..world import Builder, hash2, hash3
from ..font import giant_text, text_width
from .. import decor
from .. import track as T
from ..buildings import style, building, restroom, info_booth, food_kiosk, cone_roof, flat_roof, box_walls, dome

SCI = style(wall='white_concrete', base='white_concrete', pillar='light_gray_concrete', trim='cyan_concrete',
            floor='smooth_quartz', ceiling='smooth_quartz', window='light_blue_stained_glass_pane',
            roof_stairs='quartz_stairs', roof_slab='smooth_quartz_slab', roof_block='smooth_quartz',
            door='iron' if False else 'birch', sign_wood='birch', light='sea_lantern', carpet='cyan_carpet',
            counter='white_concrete', counter_top='smooth_quartz_slab[type=bottom]',
            shelf=('light_gray_concrete', 'cyan_stained_glass'), parapet='light_gray_concrete',
            wall_light='end_rod[facing=up]', sign_color='cyan', sign_glow=True)

YS = G + 4  # Hyperion station track height


# =============================================================================================
# Hyperion
# =============================================================================================
def hyperion_track():
    t = T.Track(440, YS, 394, 0).mark('station_start')
    t.straight(36).mark('station_end')
    t.turn(90, 11)
    t.straight(4).mark('lift_start')
    t.climb_to(G + 64, 33, 14, 14).mark('lift_end')
    t.straight(2)
    t.turn(-180, 13, dy=-3, bank_deg=25).mark('drop_start')
    t.climb_to(G + 5, 62, 12, 18).mark('drop_end')
    t.straight(16).mark('tunnel1_end')
    t.loop(14, 6, right=True).mark('loop1')
    t.straight(4)
    t.turn(90, 16, dy=8, bank_deg=45).mark('turn1')
    t.straight(4)
    t.hill(18, 40).mark('hill1')
    t.turn(180, 15, dy=6, bank_deg=75).mark('overbank')
    t.straight(4)
    t.corkscrew(30, 6, right=True).mark('cork')
    t.straight(5)
    t.loop(12, 6, right=True).mark('loop2')
    t.straight(1)
    t.turn(-90, 10, dy=-6, bank_deg=40).mark('turn3')
    t.straight(14).mark('tunnel2_start')
    t.straight(22).mark('tunnel2_end')
    t.straight(14)
    t.turn(90, 12, bank_deg=40)
    t.straight(20)
    t.helix(1.5, 13, -8, 40, right=True).mark('helix')
    t.straight(3).mark('brakes')
    t.connect(440, YS, 394, 0, tension=1.2).mark('return')
    return t


def hyperion(w, paths):
    t = hyperion_track()
    fr = t.frames()
    m = t.marks
    tun1 = (m['drop_end'] - 60, m['tunnel1_end'])
    tun2 = (m['tunnel2_start'], m['tunnel2_end'])
    T.tunnel(w, fr, tun1[0], tun1[1], shell='gray_concrete', glass='cyan_stained_glass')
    T.tunnel(w, fr, tun2[0], tun2[1], shell='light_gray_concrete', glass='purple_stained_glass')
    station = (m['station_start'], m['station_end'])
    T.render(w, t, rail='white_concrete', spine='cyan_concrete', tie='light_gray_concrete',
             support='light_gray_concrete', support_every=9.0, no_support=[station],
             tunnel_ranges=[tun1, tun2])
    # lift-hill chain and side lights
    for i in range(m['lift_start'], m['lift_end'], 3):
        p, f, u, r = fr[i]
        c = p + u * 0.0
        w.set(int(round(c[0])), int(round(c[1])), int(round(c[2])), 'chain[axis=x]')
    for i in range(m['lift_start'], m['lift_end'], 25):
        p, f, u, r = fr[i]
        for s in (-2, 2):
            q = p + r * s
            q = (int(round(q[0])), int(round(q[1])), int(round(q[2])))
            if w.is_air(*q):
                w.set(*q, 'sea_lantern')
    station_building(w, paths, t)
    T.train(w, t, m['station_start'] + 170, cars=6, car_len=3, body='cyan_concrete', seat='quartz_stairs')
    w.label(505, 330, 'HYPERION COASTER', 'attraction')


def station_building(w, paths, t):
    x_track = 440
    z0, z1 = 356, 396
    # raised platforms on both sides of the track
    for z in range(z0, z1 + 1):
        for x in list(range(431, 439)) + list(range(442, 449)):
            for y in range(G + 1, YS + 1):
                w.set(x, y, z, 'light_gray_concrete' if y < YS else ('white_concrete' if (x + z) % 2 else 'smooth_quartz'))
        # safety line
        w.set(438, YS, z, 'yellow_concrete')
        w.set(442, YS, z, 'yellow_concrete')
    # under-track pit
    for z in range(z0, z1 + 1):
        for y in range(G + 1, YS - 1):
            w.set(x_track, y, z, 'gray_concrete')
            w.set(x_track - 1, y, z, 'gray_concrete')
            w.set(x_track + 1, y, z, 'gray_concrete')
    # columns and roof
    for z in range(z0, z1 + 1, 6):
        for x in (431, 448):
            for y in range(YS + 1, G + 13):
                w.set(x, y, z, 'quartz_pillar[axis=y]')
    for z in range(z0 - 2, z1 + 3):
        for x in range(428, 452):
            edge = x in (428, 451) or z in (z0 - 2, z1 + 2)
            w.set(x, G + 13, z, 'white_concrete' if not edge else 'cyan_concrete')
            if not edge and (x - 428) % 6 == 3 and (z - z0) % 4 == 0:
                w.set(x, G + 13, z, 'cyan_stained_glass')
        w.set(427, G + 12, z, 'quartz_stairs[facing=east,half=top]')
        w.set(452, G + 12, z, 'quartz_stairs[facing=west,half=top]')
    for z in range(z0, z1 + 1, 3):
        for x in (434, 446):
            w.set(x, G + 12, z, 'sea_lantern')
    # operator booth on the unload side
    for x in range(444, 448):
        for z in range(z0 + 2, z0 + 6):
            edge = x in (444, 447) or z in (z0 + 2, z0 + 5)
            for y in range(YS + 1, YS + 4):
                if edge:
                    w.set(x, y, z, 'light_blue_stained_glass' if y > YS + 1 else 'white_concrete')
            w.set(x, YS + 4, z, 'smooth_quartz_slab[type=bottom]')
    w.set(445, YS + 1, z0 + 3, 'lectern[facing=west,has_book=false,powered=false]')
    w.set(446, YS + 1, z0 + 4, 'white_concrete')
    w.set(446, YS + 2, z0 + 4, 'stone_button[face=floor,facing=north,powered=false]')
    # air gates along the load platform
    for z in range(z0 + 3, z1 - 2, 4):
        w.set(437, YS + 1, z, 'iron_bars')
    # entrance stairs from the queue (west)
    for k in range(4):
        for z in range(z1 - 2, z1 + 1):
            w.set(430 - k, G + 4 - k, z, 'quartz_stairs[facing=east,half=bottom]')
            for y in range(G + 1, G + 4 - k):
                w.set(430 - k, y, z, 'light_gray_concrete')
    # exit footbridge from the unload platform over the returning track
    zb = (z1 - 4, z1 - 2)
    for z in range(zb[0], zb[1] + 1):
        for k in range(3):
            x = 449 + k
            w.set(x, YS + 1 + k, z, 'quartz_stairs[facing=east,half=bottom]')
            for y in range(G + 1, YS + 1 + k):
                w.set(x, y, z, 'light_gray_concrete')
        for x in range(452, 469):
            w.set(x, YS + 3, z, 'smooth_quartz')
        for k in range(6):
            x = 469 + k
            w.set(x, YS + 2 - k, z, 'quartz_stairs[facing=west,half=bottom]')
            for y in range(G + 1, YS + 2 - k):
                w.set(x, y, z, 'light_gray_concrete')
    for x in range(449, 475):
        for z in (zb[0] - 1, zb[1] + 1):
            yb = YS + 4 if 452 <= x <= 468 else (YS + 2 + (x - 449) if x < 452 else YS + 3 - (x - 469))
            w.set(x, yb, z, 'white_stained_glass_pane')
    for x in (452, 468):
        for z in (zb[0] - 1, zb[1] + 1):
            for y in range(G + 1, YS + 3):
                w.set(x, y, z, 'quartz_pillar[axis=y]')
    for x in (456, 464):
        w.set(x, YS + 5, zb[0] - 1, 'end_rod[facing=up]')
        w.set(x, YS + 5, zb[1] + 1, 'end_rod[facing=up]')
    # switchback queue west of the station
    for x in range(422, 431):
        for z in range(z0 - 2, z1 + 1):
            w.set(x, G, z, 'light_gray_concrete')
            paths.mask[z, x] = True
    for lane, x in enumerate(range(423, 428, 2)):
        for z in range(z0 + (2 if lane % 2 == 0 else 0), z1 - (0 if lane % 2 == 0 else 2)):
            w.set(x, G + 1, z, 'iron_bars')
    for z in range(z0 - 2, z1 + 1, 6):
        for x in (421, 430):
            for y in range(G + 1, G + 7):
                w.set(x, y, z, 'iron_bars')
    for x in range(421, 431):
        for z in range(z0 - 3, z1 + 2):
            if w.is_air(x, G + 7, z):
                w.set(x, G + 7, z, 'white_stained_glass' if (x + z) % 3 else 'cyan_stained_glass')
    # big letters on the roof facing the hub (west)
    for z in range(358, 395):
        for y in range(G + 14, G + 23):
            w.set(441, y, z, 'white_concrete' if y in (G + 14, G + 22) else 'air')
        w.set(441, G + 14, z, 'light_gray_concrete')
    giant_text(w, 'HYPERION', 440, G + 15, 376, 'west', 'cyan_concrete', scale=1, back='sea_lantern')
    w.sign(421, G + 3, 355, ['HYPERION', 'Height: 64 blocks', 'Loops: 2 + cork', 'Wait: 25 min'],
           wood='birch', facing='north', color='cyan', glow=True)
    w.sign(475, G + 3, z1 - 1, ['', 'EXIT', '& PHOTOS', ''], wood='birch', facing='east', color='cyan', glow=True)
    paths.draw([(424, 320), (425, 352)], 7, land='scifi_s')
    paths.draw([(475, 393), (477, 414), (424, 414), (422, 420)], 5, land='scifi_s')


# =============================================================================================
# Cosmic Voyager (indoor coaster inside a giant dome)
# =============================================================================================
DC = (545, 78)
DR, DRY = 42, 36


def voyager_track():
    t = T.Track(528, G + 3, 104, 90).mark('st0')
    t.straight(20).mark('st1')
    t.turn(-90, 8)
    t.straight(1).mark('lift0')
    t.climb_to(G + 21, 35, 7, 7).mark('lift1')
    t.straight(1)
    t.turn(-180, 7, dy=-1, bank_deg=30)
    t.climb_to(G + 13, 35, 7, 7)
    t.turn(180, 10, dy=-1, bank_deg=45)
    t.straight(4)
    t.turn(90, 8, dy=-4, bank_deg=40)
    t.straight(18)
    t.helix(0.75, 7, -5, 35, right=True)
    t.straight(2)
    t.connect(520, G + 3, 104, 90, tension=1.1).mark('ret')
    t.straight(8)
    return t


def cosmic_voyager(w, paths):
    cx, cz = DC
    # floor
    for (x, z) in disc(cx, cz, DR):
        w.set(x, G, z, 'black_concrete' if hash2(x, z, 3) > 0.02 else 'sea_lantern')
        w.clear(x, G + 1, z, x, G + DRY + 1, z)
    # double shell: white panels outside, black starfield inside
    R = DR
    for x in range(cx - R - 1, cx + R + 2):
        for z in range(cz - R - 1, cz + R + 2):
            dxz = math.hypot(x - cx, z - cz)
            if dxz > R + 0.5:
                continue
            for k in range(0, DRY + 2):
                d = math.sqrt((dxz / R) ** 2 + (k / DRY) ** 2)
                if 1 - 1.15 / R < d <= 1.0:
                    lon = math.atan2(z - cz, x - cx)
                    rib = abs(((lon / (math.tau / 20)) % 1) - 0.5) > 0.42
                    blk = 'white_concrete'
                    if rib or k % 9 == 8:
                        blk = 'light_gray_concrete'
                    if 2 <= k <= 4 and not rib:
                        blk = 'cyan_stained_glass'
                    if k == 0:
                        blk = 'light_gray_concrete'
                    w.set(x, G + 1 + k, z, blk)
                elif 1 - 2.3 / R < d <= 1 - 1.15 / R and k > 4:
                    w.set(x, G + 1 + k, z, 'sea_lantern' if hash3(x, k, z, 4) < 0.04 else 'black_concrete')
    # top oculus ring light
    for (x, z) in ring(cx, cz, 4.5, 2.5):
        w.set(x, G + 1 + DRY, z, 'sea_lantern')
    for (x, z) in disc(cx, cz, 2.5):
        w.set(x, G + 1 + DRY, z, 'cyan_stained_glass')
    # coaster
    t = voyager_track()
    fr = t.frames()
    st = (t.marks['st0'], t.marks['st1'])
    cells, _, _ = T.render(w, t, rail='purple_concrete', spine='black_concrete', tie='gray_concrete',
                           support='gray_concrete', support_every=6.0, no_support=[st])
    # light-ring launch tunnel after the station
    for i in range(t.marks['st1'] + 10, t.marks['st1'] + 60, 10):
        p, f, u, r = fr[i]
        for du in (-3, 3):
            for dv in range(-1, 4):
                q = p + r * du + u * dv
                q = tuple(int(round(v)) for v in q)
                if q not in cells:
                    w.set(*q, 'sea_lantern' if dv in (-1, 3) or True else 'air')
        for du in range(-3, 4):
            q = p + r * du + u * 4
            q = tuple(int(round(v)) for v in q)
            if q not in cells:
                w.set(*q, 'sea_lantern')
    T.train(w, t, st[0] + 95, cars=3, car_len=2, body='purple_concrete', seat='polished_blackstone_stairs')
    # station platforms
    for x in range(524, 552):
        for z in range(106, 111):
            for y in range(G + 1, G + 3):
                w.set(x, y, z, 'gray_concrete')
            w.set(x, G + 3, z, 'black_concrete' if z > 106 else 'yellow_concrete')
        for z in range(97, 102):
            for y in range(G + 1, G + 3):
                w.set(x, y, z, 'gray_concrete')
            w.set(x, G + 3, z, 'black_concrete' if z < 101 else 'yellow_concrete')
    for x in range(524, 552, 4):
        w.set(x, G + 9, 108, 'sea_lantern')
        for y in range(G + 4, G + 9):
            w.set(x, y, 110, 'iron_bars')
    # planets and floating decor (only where they clear the track)
    track_pts = np.array(list(cells.keys()), dtype=float)

    def clear_of_track(px, py, pz, r):
        d = np.sqrt(((track_pts - np.array([px, py, pz])) ** 2).sum(axis=1))
        return d.min() > r + 3

    planets = [((532, G + 25, 72), 5, 'orange_terracotta', 'yellow_terracotta', True),
               ((563, G + 18, 92), 4, 'blue_concrete', 'light_blue_concrete', False),
               ((525, G + 10, 66), 3, 'red_terracotta', 'orange_terracotta', False),
               ((560, G + 27, 70), 3, 'lime_concrete', 'green_concrete', False),
               ((545, G + 30, 88), 2.5, 'white_concrete', 'light_gray_concrete', False)]
    for (px, py, pz), r, c1, c2, ringed in planets:
        if not clear_of_track(px, py, pz, r + (4 if ringed else 0)):
            continue
        for (x, y, z) in ball(px, py, pz, r):
            w.set(x, y, z, c1 if (y + int(hash3(x, y, z, 2) * 2)) % 3 else c2)
        if ringed:
            for (x, z) in ring(px, pz, r + 3.5, r + 1.5):
                w.set(x, py, z, 'yellow_stained_glass' if (x + z) % 2 else 'orange_stained_glass')
        for y in range(py + int(r) + 1, G + DRY):
            if w.is_air(px, y, pz):
                w.set(px, y, pz, 'chain[axis=y]')
            else:
                break
    # entrance portal on the south side
    pz0 = cz + DR - 3
    for x in range(cx - 8, cx + 9):
        for z in range(pz0, pz0 + 12):
            edge = x in (cx - 8, cx + 8)
            for y in range(G + 1, G + 11):
                if edge or y == G + 10:
                    w.set(x, y, z, 'white_concrete' if y != G + 10 else 'light_gray_concrete')
                else:
                    w.set(x, y, z, 'air')
            w.set(x, G, z, 'black_concrete' if (x + z) % 2 else 'gray_concrete')
            paths.mask[z, x] = True
        w.set(x, G + 11, pz0 + 11, 'cyan_concrete')
    for z in range(pz0, pz0 + 12, 2):
        w.set(cx - 7, G + 9, z, 'sea_lantern')
        w.set(cx + 7, G + 9, z, 'sea_lantern')
    for x in range(cx - 7, cx + 8):
        w.set(x, G + 10, pz0 + 11, 'sea_lantern')
    # queue switchbacks between the portal and the station platform
    for x in range(cx - 7, cx + 8, 3):
        for z in range(111, pz0 + 8):
            if z % 6 != (0 if (x // 3) % 2 else 3):
                w.set(x, G + 1, z, 'iron_bars')
    for x in range(524, 552):
        for z in range(111, 119):
            if w.name(x, G, z) == 'black_concrete' and hash2(x, z, 9) < 0.08:
                w.set(x, G, z, 'sea_lantern')
    giant_text(w, 'COSMIC VOYAGER', cx, G + 13, pz0 + 11, 'south', 'cyan_concrete', scale=1, back='sea_lantern')
    for x in range(cx - 42, cx + 43):
        pass
    w.sign(cx - 3, G + 4, pz0 + 12, ['COSMIC', 'VOYAGER', 'Indoor Space', 'Coaster'], wood='birch',
           facing='south', color='cyan', glow=True)
    w.sign(cx + 3, G + 4, pz0 + 12, ['WAIT TIME', '20 min', 'Lap bars', 'Min 107cm'], wood='birch',
           facing='south', color='cyan', glow=True)
    w.label(cx, cz, 'COSMIC VOYAGER (indoor)', 'attraction')


# =============================================================================================
# Gravity Spire drop tower
# =============================================================================================
def gravity_spire(w, paths, cx=456, cz=62):
    top = G + 1 + 100
    # base plaza
    for (x, z) in disc(cx, cz, 13):
        w.set(x, G, z, 'light_gray_concrete' if int(math.hypot(x - cx, z - cz)) % 3 else 'cyan_concrete')
        paths.mask[z, x] = True
    for (x, z) in ring(cx, cz, 9.5, 6.5):
        w.set(x, G + 1, z, 'smooth_quartz')
    for (x, z) in circle(cx, cz, 9.5):
        w.set(x, G + 2, z, 'iron_bars')
    # loading gondola at the bottom (second set of seats) and core
    for y in range(G + 1, top):
        for x in range(cx - 2, cx + 3):
            for z in range(cz - 2, cz + 3):
                edge = x in (cx - 2, cx + 2) or z in (cz - 2, cz + 2)
                corner = x in (cx - 2, cx + 2) and z in (cz - 2, cz + 2)
                if not edge:
                    if (y - G) % 5 == 0:
                        w.set(x, y, z, 'sea_lantern')
                    continue
                blk = 'white_concrete'
                if corner:
                    blk = 'light_gray_concrete'
                elif (x == cx or z == cz):
                    blk = 'cyan_stained_glass'
                if (y - G) % 12 == 0:
                    blk = 'sea_lantern' if not corner else 'cyan_concrete'
                w.set(x, y, z, blk)
    # carriage frozen near the top
    def carriage(yc, colour):
        for x in range(cx - 5, cx + 6):
            for z in range(cz - 5, cz + 6):
                if abs(x - cx) <= 2 and abs(z - cz) <= 2:
                    continue
                w.set(x, yc - 1, z, 'light_gray_concrete' if max(abs(x - cx), abs(z - cz)) < 5 else colour)
                if max(abs(x - cx), abs(z - cz)) == 5:
                    w.set(x, yc + 2, z, colour)
        for (x, z, f) in [(cx + dx, cz - 3, 'south') for dx in (-2, -1, 1, 2)] + \
                         [(cx + dx, cz + 3, 'north') for dx in (-2, -1, 1, 2)] + \
                         [(cx - 3, cz + dz, 'east') for dz in (-2, -1, 1, 2)] + \
                         [(cx + 3, cz + dz, 'west') for dz in (-2, -1, 1, 2)]:
            w.set(x, yc, z, f'quartz_stairs[facing={f},half=bottom]')
            w.set(x, yc + 1, z, 'iron_bars')
        for (x, z) in ((cx - 5, cz - 5), (cx + 5, cz - 5), (cx - 5, cz + 5), (cx + 5, cz + 5)):
            w.set(x, yc, z, 'sea_lantern')
            w.set(x, yc + 1, z, 'sea_lantern')
    carriage(G + 86, 'cyan_concrete')
    carriage(G + 3, 'purple_concrete')
    for x in range(cx - 5, cx + 6):
        for z in range(cz - 5, cz + 6):
            if max(abs(x - cx), abs(z - cz)) > 2 and w.is_air(x, G + 1, z):
                w.set(x, G + 1, z, 'smooth_quartz')
    # crown
    for x in range(cx - 4, cx + 5):
        for z in range(cz - 4, cz + 5):
            w.set(x, top, z, 'cyan_concrete' if max(abs(x - cx), abs(z - cz)) == 4 else 'white_concrete')
    for (x, z) in ((cx - 4, cz - 4), (cx + 4, cz - 4), (cx - 4, cz + 4), (cx + 4, cz + 4)):
        for y in range(top + 1, top + 5):
            w.set(x, y, z, 'iron_bars')
        w.set(x, top + 5, z, 'sea_lantern')
        w.set(x, top + 6, z, 'lightning_rod[facing=up]')
    # beacon with cyan beam
    for x in range(cx - 1, cx + 2):
        for z in range(cz - 1, cz + 2):
            w.set(x, top + 1, z, 'iron_block')
    w.set(cx, top + 2, cz, 'beacon')
    w.set(cx, top + 3, cz, 'cyan_stained_glass')
    # queue building
    b = building(w, cx - 10, cz + 14, 21, 9, 'south', SCI, floors=1, fh=5, roof='flat', name='GRAVITY SPIRE',
                 kind='plain', awning=False, door_double=True,
                 sign_lines=['GRAVITY SPIRE', '100 block drop', 'Min height', '122 cm'])
    w.label(cx, cz, 'GRAVITY SPIRE drop tower', 'attraction')


# =============================================================================================
# Rocket landmark, saucer diner, other buildings
# =============================================================================================
def rocket(w, cx, cz):
    for (x, z) in disc(cx, cz, 15):
        r = math.hypot(x - cx, z - cz)
        w.set(x, G, z, 'yellow_concrete' if 13.5 < r <= 14.5 and int(math.degrees(math.atan2(z - cz, x - cx)) / 10) % 2 else
              ('light_gray_concrete' if int(r) % 4 else 'white_concrete'))
    for (x, z) in disc(cx, cz, 7):
        w.set(x, G + 1, z, 'smooth_stone')
    # engine bells and exhaust glow
    for (ox, oz) in ((0, 0), (2, 0), (-2, 0), (0, 2), (0, -2)):
        w.set(cx + ox, G + 2, cz + oz, 'shroomlight')
        w.set(cx + ox, G + 3, cz + oz, 'iron_block')
        w.set(cx + ox, G + 4, cz + oz, 'iron_block')
    body0, body1 = G + 5, G + 38
    for y in range(body0, body1):
        for (x, z) in disc(cx, cz, 4):
            d = math.hypot(x - cx, z - cz)
            if d > 3.2:
                blk = 'white_concrete'
                if (y - body0) % 11 in (0, 1):
                    blk = 'red_concrete'
                if (y - body0) % 11 == 6 and (x == cx or z == cz):
                    blk = 'light_blue_stained_glass'
                w.set(x, y, z, blk)
    # nose cone
    for k in range(12):
        r = 4 * (1 - k / 12.0) + 0.3
        for (x, z) in disc(cx, cz, r):
            w.set(x, body1 + k, z, 'red_concrete' if k < 10 else 'white_concrete')
    w.set(cx, body1 + 12, cz, 'lightning_rod[facing=up]')
    # fins
    for (dx, dz) in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        for k in range(0, 14):
            reach = 4 + max(0, 6 - k // 2)
            for t in range(4, reach + 1):
                w.set(cx + dx * t, body0 + k, cz + dz * t, 'red_concrete')
    # gantry tower
    gx, gz = cx + 9, cz - 9
    for y in range(G + 1, body1 + 4):
        for (x, z) in ((gx, gz), (gx + 3, gz), (gx, gz + 3), (gx + 3, gz + 3)):
            w.set(x, y, z, 'light_gray_concrete')
        if (y - G) % 6 == 0:
            for t in range(4):
                w.set(gx + t, y, gz, 'iron_bars')
                w.set(gx + t, y, gz + 3, 'iron_bars')
                w.set(gx, y, gz + t, 'iron_bars')
                w.set(gx + 3, y, gz + t, 'iron_bars')
        if (y - G) % 12 == 6:
            w.set(gx, y + 1, gz, 'sea_lantern')
    for t in range(1, 6):
        w.set(gx - t + 1, G + 30, gz + t - 1 + 1, 'light_gray_concrete')
    w.label(cx, cz - 18, 'Rocket Plaza', 'small')


def orbit_diner(w, cx, cz):
    # ground floor glass rotunda
    for (x, z) in disc(cx, cz, 11):
        w.set(x, G, z, 'smooth_quartz')
        w.clear(x, G + 1, z, x, G + 14, z)
    for (x, z) in circle(cx, cz, 11):
        for y in range(G + 1, G + 6):
            a = math.atan2(z - cz, x - cx)
            w.set(x, y, z, 'white_concrete' if y in (G + 1, G + 5) or int(math.degrees(a) / 15) % 3 == 0 else 'light_blue_stained_glass')
    for k in range(-1, 2):
        w.clear(cx + k, G + 1, cz + 11, cx + k, G + 3, cz + 11)
    w.clear(cx - 11, G + 1, cz, cx - 11, G + 3, cz)
    # saucer roof
    for (x, z) in disc(cx, cz, 16):
        d = math.hypot(x - cx, z - cz)
        h = 2.5 * math.sqrt(max(0, 1 - (d / 16) ** 2))
        for y in range(G + 6, G + 6 + int(round(h)) + 1):
            w.set(x, y, z, 'smooth_quartz')
        if 15.2 < d <= 16:
            w.set(x, G + 6, z, 'cyan_concrete' if (x + z) % 3 else 'sea_lantern')
    for (x, y, z) in sphere_shell(cx, G + 8, cz, 6, 1):
        if y > G + 8:
            w.set(x, y, z, 'light_blue_stained_glass')
    w.set(cx, G + 15, cz, 'iron_bars')
    w.set(cx, G + 16, cz, 'iron_bars')
    w.set(cx, G + 17, cz, 'sea_lantern')
    # interior: counter ring + tables
    for (x, z) in ring(cx, cz, 4.5, 3.5):
        w.set(x, G + 1, z, 'white_concrete')
        w.set(x, G + 2, z, 'smooth_quartz_slab[type=bottom]')
    for (x, z) in disc(cx, cz, 3):
        w.set(x, G + 1, z, 'smoker[facing=north,lit=true]' if (x + z) % 3 == 0 else 'air')
    for i in range(10):
        a = i * math.tau / 10
        x, z = int(round(cx + 8 * math.cos(a))), int(round(cz + 8 * math.sin(a)))
        decor.table_set(w, x, G + 1, z, 'birch', 'cyan_carpet', 2, along='x' if i % 2 else 'z')
    for (x, z) in circle(cx, cz, 9):
        if (x + z) % 4 == 0:
            w.set(x, G + 5, z, 'sea_lantern')
    w.sign(cx, G + 4, cz + 12, ['ORBIT DINER', 'Burgers - Shakes', 'Cosmic Fries', 'OPEN'], wood='birch',
           facing='south', color='cyan', glow=True)
    w.label(cx, cz, 'Orbit Diner', 'small')


def nova_north(w, paths):
    # walkways
    paths.draw([(422, 218), (470, 178), (492, 158)], 9, land='scifi_n')
    paths.draw([(512, 140), (545, 124)], 9, land='scifi_n')
    paths.draw([(490, 140), (468, 118), (418, 110)], 7, land='scifi_n')
    paths.draw([(468, 118), (457, 86)], 5, land='scifi_n')
    paths.draw([(515, 156), (560, 176), (573, 176)], 7, land='scifi_n')
    paths.draw([(500, 165), (500, 222), (600, 230)], 7, land='scifi_n')
    paths.draw([(600, 226), (600, 418)], 7, land='scifi_s')
    paths.draw([(424, 228), (600, 228)], 6, land='scifi_s')
    rocket(w, 500, 150)
    cosmic_voyager(w, paths)
    gravity_spire(w, paths)
    orbit_diner(w, 584, 176)
    # shops
    building(w, 430, 160, 22, 14, 'east', SCI, floors=2, roof='flat', name='GALACTIC GOODS', kind='shop',
             awning=False, door_double=True, goods=['cyan_stained_glass', 'light_gray_concrete', 'end_rod[facing=up]'])
    building(w, 520, 180, 20, 14, 'north', SCI, floors=1, fh=6, roof='flat', name='ROBO ARCADE', kind='shop',
             awning=False, door_double=True, goods=['jukebox', 'note_block', 'target', 'redstone_lamp[lit=true]'])
    building(w, 440, 130, 16, 12, 'south', SCI, floors=2, roof='flat', name='STAR TRADERS', kind='shop', awning=False)
    restroom(w, 598, 120, 'west', SCI, roof='flat')
    info_booth(w, 484, 186, SCI, title='GUEST INFO')
    food_kiosk(w, 528, 140, 'west', 'COSMIC CONES', ('cyan', 'white'), 'birch', subtitle='ICE CREAM',
               wall='white_concrete')
    food_kiosk(w, 470, 140, 'east', 'SPACE DOGS', ('purple', 'white'), 'birch', subtitle='& SODAS',
               wall='white_concrete')
    # landscaping: lit planters and kinetic sculptures
    for (x, z) in ((480, 200), (560, 200), (610, 60), (430, 40), (600, 30)):
        if w.is_air(x, G + 1, z):
            for y in range(G + 1, G + 9):
                w.set(x, y, z, 'iron_bars')
            for (sx, sy, sz) in ball(x, G + 10, z, 1.6):
                w.set(sx, sy, sz, 'cyan_stained_glass')
            w.set(x, G + 10, z, 'sea_lantern')
    decor.lawn_trees(w, 425, 25, 615, 215, ['birch', 'cone_topiary', 'birch'], spacing=12, seed=40,
                     avoid=lambda x, z: paths.mask[z, x] or math.hypot(x - DC[0], z - DC[1]) < DR + 4)
    w.label(520, 205, 'NOVA FRONTIER', 'land')


def nova_south_extras(w, paths):
    building(w, 586, 330, 24, 16, 'west', SCI, floors=2, roof='flat', name='HYPERION OUTPOST', kind='shop',
             awning=False, door_double=True)
    restroom(w, 586, 372, 'west', SCI, roof='flat')
    food_kiosk(w, 590, 300, 'west', 'STARPORT SNACKS', ('cyan', 'white'), 'birch', subtitle='PRETZELS',
               wall='white_concrete')
    decor.lawn_trees(w, 425, 236, 615, 415, ['birch', 'cone_topiary'], spacing=13, seed=41,
                     avoid=lambda x, z: paths.mask[z, x])


def maintenance_bay(w):
    st = dict(SCI, wall='light_gray_concrete', base='light_gray_concrete', pillar='gray_concrete', trim='yellow_concrete')
    b = building(w, 582, 394, 14, 12, 'west', st, floors=1, fh=7, roof='flat', kind='plain', awning=False,
                 sign_lines=['RIDE', 'MAINTENANCE', 'Cast Members', 'Only'])
    for lz in range(2, 11, 2):
        b.set(11, 0, lz, 'anvil[facing=north]' if lz % 4 else 'smithing_table')
        b.set(11, 1, lz, 'chain[axis=y]')
    for lz in range(3, 10, 3):
        b.set(5, 0, lz, 'cyan_concrete')
        b.set(6, 0, lz, 'white_concrete')


def build(w, paths):
    maintenance_bay(w)
    nova_north(w, paths)
    hyperion(w, paths)
    nova_south_extras(w, paths)
