"""Starlight Plaza: the central hub with the Wonder Spire landmark, reflecting pool and gardens."""
import math

from ..const import G, HUB
from ..geom import disc, ring, circle, sphere_shell, ball
from ..world import hash2, Builder
from .. import decor
from ..buildings import style, restroom, info_booth, food_kiosk, cone_roof, hip_roof, building

CX, CZ = HUB


def _polar(r, a):
    return CX + r * math.cos(a), CZ + r * math.sin(a)


RADIAL_ANGLES = [i * math.pi / 4 for i in range(8)]  # 0 = east, pi/2 = south


def in_radial(x, z, half_width=5.0):
    dx, dz = x - CX, z - CZ
    for a in RADIAL_ANGLES:
        ux, uz = math.cos(a), math.sin(a)
        along = dx * ux + dz * uz
        perp = abs(-dx * uz + dz * ux)
        if along > 0 and perp <= half_width:
            return True
    return False


def plaza(w, paths):
    # ---------- paving rings ----------
    for x in range(CX - 56, CX + 57):
        for z in range(CZ - 56, CZ + 57):
            r = math.hypot(x - CX, z - CZ)
            a = math.atan2(z - CZ, x - CX)
            if r <= 11.5:
                blk = 'polished_diorite' if int(r) % 3 else 'chiseled_quartz_block'
            elif r <= 18.5:
                continue  # pool handled below
            elif r <= 20.5:
                continue  # rim
            elif r <= 34.5:
                band = int(r) % 4
                spoke = (int((a + math.pi) / (math.pi / 16)) % 2 == 0)
                if band == 0:
                    blk = 'polished_granite'
                elif r > 33.5:
                    blk = 'polished_diorite'
                else:
                    blk = 'polished_andesite' if spoke else 'polished_diorite'
            elif r <= 48.5:
                if in_radial(x, z, 5.5):
                    blk = 'polished_andesite' if not in_radial(x, z, 4.5) else ('polished_diorite' if int(r) % 4 == 0 else 'smooth_stone')
                else:
                    continue  # gardens
            elif r <= 54.5:
                blk = 'polished_andesite' if int(r) % 3 else 'polished_diorite'
                if r > 53.5:
                    blk = 'polished_granite'
            else:
                continue
            w.set(x, G, z, blk)
            paths.mask[z, x] = True

    # ---------- reflecting pool ring (r 12..18) with bridges ----------
    for (x, z) in ring(CX, CZ, 18.5, 11.5):
        bridge = any(abs((x - CX) * math.sin(a) - (z - CZ) * math.cos(a)) <= 2.5 and
                     ((x - CX) * math.cos(a) + (z - CZ) * math.sin(a)) > 0 for a in RADIAL_ANGLES[::2])
        if bridge:
            w.set(x, G, z, 'smooth_quartz')
            w.set(x, G - 1, z, 'quartz_bricks')
            continue
        w.set(x, G - 2, z, 'prismarine_bricks')
        w.set(x, G - 1, z, 'water')
        w.set(x, G, z, 'water')
        if hash2(x, z, 4) < 0.08:
            w.set(x, G - 2, z, 'sea_lantern')
    for (x, z) in ring(CX, CZ, 20.5, 18.5):
        w.set(x, G, z, 'quartz_bricks')
        w.set(x, G - 1, z, 'quartz_bricks')
        bridge = any(abs((x - CX) * math.sin(a) - (z - CZ) * math.cos(a)) <= 2.5 and
                     ((x - CX) * math.cos(a) + (z - CZ) * math.sin(a)) > 0 for a in RADIAL_ANGLES[::2])
        if not bridge:
            w.set(x, G + 1, z, 'smooth_quartz_slab[type=bottom]')
    for (x, z) in ring(CX, CZ, 12.4, 11.5):
        bridge = any(abs((x - CX) * math.sin(a) - (z - CZ) * math.cos(a)) <= 2.5 and
                     ((x - CX) * math.cos(a) + (z - CZ) * math.sin(a)) > 0 for a in RADIAL_ANGLES[::2])
        if not bridge:
            w.set(x, G + 1, z, 'quartz_slab[type=bottom]')
    # bridge railings
    for a in RADIAL_ANGLES[::2]:
        ux, uz = math.cos(a), math.sin(a)
        px, pz = -uz, ux
        for t in range(12, 20):
            for side in (-3, 3):
                X = int(round(CX + ux * t + px * side))
                Z = int(round(CZ + uz * t + pz * side))
                w.set(X, G, Z, 'quartz_bricks')
                w.set(X, G + 1, Z, 'quartz_slab[type=bottom]' if t % 3 else 'quartz_pillar[axis=y]')
                if t % 3 == 0:
                    w.set(X, G + 2, Z, 'lantern')
    # jets in the pool: little fountains
    for i in range(16):
        a = i * math.tau / 16 + math.tau / 32
        X, Z = _polar(15.2, a)
        X, Z = int(round(X)), int(round(Z))
        if w.name(X, G, Z) == 'water':
            w.set(X, G, Z, 'prismarine_wall')
            w.set(X, G - 1, Z, 'prismarine_bricks')
            w.set(X, G + 1, Z, 'sea_lantern')
            w.set(X, G + 2, Z, 'prismarine_wall')

    # ---------- gardens (r 35..48 between radials) ----------
    for x in range(CX - 49, CX + 50):
        for z in range(CZ - 49, CZ + 50):
            r = math.hypot(x - CX, z - CZ)
            if 34.5 < r <= 48.5 and not in_radial(x, z, 5.5):
                a = (math.atan2(z - CZ, x - CX) + math.tau) % math.tau
                if r < 35.6 or r > 47.5 or in_radial(x, z, 6.6):
                    w.set(x, G, z, 'grass_block')
                    w.set(x, G + 1, z, decor.leaves('azalea' if int(a * 20) % 5 else 'flowering_azalea'))
                else:
                    w.set(x, G, z, 'grass_block')
                    # concentric flower bands in alternating colours
                    band = int((r - 35) / 2.4) % 4
                    pal = [['red_tulip', 'poppy'], ['dandelion', 'oxeye_daisy'], ['blue_orchid', 'cornflower'],
                           ['pink_tulip', 'allium']][band]
                    if hash2(x, z, 12) < 0.85:
                        w.set(x, G + 1, z, pal[int(hash2(x, z, 13) * 2)])
    # trees and topiaries ring
    for i in range(8):
        a = i * math.tau / 8 + math.tau / 16
        X, Z = _polar(42, a)
        X, Z = int(round(X)), int(round(Z))
        for dx in range(-2, 3):
            for dz in range(-2, 3):
                w.set(X + dx, G + 1, Z + dz, 'air')
                w.set(X + dx, G, Z + dz, 'grass_block')
        decor.tree(w, X, G + 1, Z, 'cherry', 1.15, seed=i)
        for s in (-1, 1):
            X2, Z2 = _polar(41, a + s * 0.17)
            decor.tree(w, int(round(X2)), G + 1, int(round(Z2)), 'topiary', 1.0, seed=i * 3 + s)

    # ---------- lamps + benches on the plaza ----------
    for i in range(24):
        a = i * math.tau / 24
        X, Z = _polar(33, a)
        decor.lamp(w, int(round(X)), G + 1, int(round(Z)), 'hub')
    for i in range(8):
        a = i * math.tau / 8 + math.tau / 16
        X, Z = _polar(23, a)
        X, Z = int(round(X)), int(round(Z))
        # bench facing the pool (inward)
        dx, dz = CX - X, CZ - Z
        look = ('east' if dx > 0 else 'west') if abs(dx) > abs(dz) else ('south' if dz > 0 else 'north')
        decor.bench(w, X, G + 1, Z, look, 'dark_oak', 3)
    # outer promenade lamps
    for i in range(32):
        a = i * math.tau / 32 + math.tau / 64
        X, Z = _polar(54, a)
        if not in_radial(int(round(X)), int(round(Z)), 6):
            decor.lamp(w, int(round(X)), G + 1, int(round(Z)), 'hub')


def wonder_spire(w):
    """The park's central landmark: a 90-block spire with a glowing globe."""
    y0 = G + 1
    # raised base platform r 11
    for (x, z) in disc(CX, CZ, 11):
        w.set(x, G + 1, z, 'polished_diorite')
    for (x, z) in circle(CX, CZ, 11):
        w.set(x, G + 1, z, 'quartz_stairs[facing=north,half=bottom]')
    # fix stair facings to face outward-inward properly: steps up toward center
    for (x, z) in circle(CX, CZ, 11):
        dx, dz = x - CX, z - CZ
        f = ('west' if dx > 0 else 'east') if abs(dx) >= abs(dz) else ('north' if dz > 0 else 'south')
        w.set(x, G + 1, z, f'quartz_stairs[facing={f},half=bottom]')
    y0 = G + 2
    # pavilion of 8 pillars at r=9 with arch ring
    for i in range(8):
        a = i * math.tau / 8 + math.tau / 16
        X, Z = int(round(CX + 9 * math.cos(a))), int(round(CZ + 9 * math.sin(a)))
        for k in range(0, 9):
            w.set(X, y0 + k, Z, 'quartz_pillar[axis=y]')
        w.set(X, y0 + 9, Z, 'chiseled_quartz_block')
        w.set(X, y0 + 10, Z, 'gold_block')
        w.set(X, y0 + 11, Z, 'lantern')
    for (x, z) in ring(CX, CZ, 10.4, 7.6):
        w.set(x, y0 + 9, z, 'smooth_quartz')
        w.set(x, y0 + 8, z, 'smooth_quartz_slab[type=top]')
    for (x, z) in circle(CX, CZ, 10.4):
        w.set(x, y0 + 10, z, 'quartz_slab[type=bottom]')
    for (x, z) in circle(CX, CZ, 8.0):
        w.set(x, y0 + 10, z, 'gold_block')
    # core tower: tapered cylinder
    def tower_r(y):
        if y < 30:
            return 5.5
        if y < 46:
            return 4.5
        return 3.5
    top_core = 54
    for k in range(0, top_core):
        r = tower_r(k)
        for (x, z) in circle(CX, CZ, r):
            a = math.atan2(z - CZ, x - CX)
            stripe = int((a + math.pi) / (math.tau / 16)) % 2 == 0
            blk = 'quartz_bricks'
            if k % 10 == 9:
                blk = 'sea_lantern'
            elif stripe and 3 < k % 10 < 8:
                blk = 'light_blue_stained_glass'
            elif k % 10 == 8:
                blk = 'smooth_quartz'
            w.set(x, y0 + k, z, blk)
        for (x, z) in disc(CX, CZ, r - 1):
            w.set(x, y0 + k, z, 'air')
        if k % 10 == 0:
            for (x, z) in disc(CX, CZ, r - 1):
                w.set(x, y0 + k - 1 if k else y0, z, 'smooth_quartz')
    # interior: a little lobby with a glowing centre and spiral stairs
    for k in range(0, top_core - 1):
        a = k * 0.55
        X, Z = int(round(CX + 2.5 * math.cos(a))), int(round(CZ + 2.5 * math.sin(a)))
        w.set(X, y0 + k, Z, 'quartz_block')
        w.set(CX, y0 + k, CZ, 'quartz_pillar[axis=y]')
    # entrances on 4 sides
    for d, (dx, dz) in (('north', (0, -1)), ('south', (0, 1)), ('east', (1, 0)), ('west', (-1, 0))):
        for t in range(4, 7):
            X, Z = CX + dx * t, CZ + dz * t
            for k in range(0, 4):
                if dx == 0:
                    for o in (-1, 0, 1):
                        w.set(X + o, y0 + k, Z, 'air')
                else:
                    for o in (-1, 0, 1):
                        w.set(X, y0 + k, Z + o, 'air')
        X, Z = CX + dx * 6, CZ + dz * 6
        if dx == 0:
            for o in (-2, 2):
                w.set(X + o, y0 + 4, Z + dz, 'lantern[hanging=true]') if w.is_air(X + o, y0 + 4, Z + dz) else None
    # balconies
    for by in (24, 44):
        r = tower_r(by) + 2.5
        for (x, z) in ring(CX, CZ, r, tower_r(by) - 0.5):
            w.set(x, y0 + by, z, 'smooth_quartz')
        for (x, z) in circle(CX, CZ, r):
            w.set(x, y0 + by + 1, z, 'white_stained_glass_pane')
        for (x, z) in circle(CX, CZ, r + 1):
            w.set(x, y0 + by - 1, z, 'quartz_stairs[facing=north,half=top]')
        for (x, z) in circle(CX, CZ, r + 1):
            dx, dz = x - CX, z - CZ
            f = ('west' if dx > 0 else 'east') if abs(dx) >= abs(dz) else ('north' if dz > 0 else 'south')
            w.set(x, y0 + by, z, f'quartz_stairs[facing={f},half=top]')
    # flying buttresses from the pavilion to the tower
    for i in range(8):
        a = i * math.tau / 8 + math.tau / 16
        for t in range(0, 12):
            r = 9 - t * 0.33
            X, Z = int(round(CX + r * math.cos(a))), int(round(CZ + r * math.sin(a)))
            w.set(X, y0 + 11 + t, Z, 'quartz_block' if t < 11 else 'gold_block')
    # the Wonder Orb
    oy = y0 + top_core + 7
    for (x, y, z) in sphere_shell(CX, oy, CZ, 7.5, 1.0):
        lat = y - oy
        lon = math.atan2(z - CZ, x - CX)
        if lat % 4 == 0 or int((lon + math.pi) / (math.tau / 12) + 0.15) != int((lon + math.pi) / (math.tau / 12) - 0.15):
            w.set(x, y, z, 'gold_block')
        else:
            w.set(x, y, z, 'light_blue_stained_glass')
    for (x, y, z) in ball(CX, oy, CZ, 2.5):
        w.set(x, y, z, 'glowstone')
    for k in range(oy - 7 - (y0 + top_core) + 1):
        w.set(CX, y0 + top_core + k, CZ, 'quartz_pillar[axis=y]')
    # orb collar
    for (x, z) in ring(CX, CZ, 4.5, 2.5):
        w.set(x, oy - 7, z, 'gold_block')
    # needle spire above the orb
    sy = oy + 8
    cone_roof(w, CX + 0.0, CZ + 0.0, sy, 3.2, 20, 'quartz_stairs', 'quartz_block')
    for k in range(5):
        w.set(CX, sy + 20 + k, CZ, 'quartz_pillar[axis=y]' if k < 3 else 'gold_block')
    w.set(CX, sy + 25, CZ, 'lightning_rod[facing=up]')
    # star points at the base of the needle
    for d in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        for t in range(1, 6):
            w.set(CX + d[0] * (3 + t), sy + 1 - (t // 2), CZ + d[1] * (3 + t), 'gold_block' if t == 5 else 'quartz_block')
        w.set(CX + d[0] * 9, sy - 2, CZ + d[1] * 9, 'end_rod[facing=down]')
    w.label(CX, CZ, 'WONDER SPIRE', 'attraction')


def quadrants(w, paths):
    """Gardens, services and food in the four corners of the hub cell."""
    st = style(wall='quartz_bricks', pillar='quartz_pillar[axis=y]', trim='smooth_quartz', floor='polished_diorite',
               roof_stairs='blue_terracotta' if False else 'prismarine_brick_stairs', roof_slab='prismarine_brick_slab',
               roof_block='prismarine_bricks', door='birch', sign_wood='birch', ceiling='smooth_quartz',
               counter='quartz_bricks', counter_top='smooth_quartz_slab[type=bottom]')
    # restrooms
    restroom(w, 368, 236, 'south', st)
    restroom(w, 262, 395, 'north', st)
    info_booth(w, 384, 384, st, title='GUEST INFO')
    info_booth(w, 256, 256, st, title='GUEST INFO')
    # food kiosks along the outer promenade
    food_kiosk(w, 248, 300, 'east', 'PRETZELS', ('blue', 'white'), 'birch', subtitle='& LEMONADE')
    food_kiosk(w, 387, 335, 'west', 'ICE CREAM', ('pink', 'white'), 'birch', subtitle='SUNDAES')
    food_kiosk(w, 296, 250, 'south', 'POPCORN', ('red', 'white'), 'birch', subtitle='FRESH!')
    food_kiosk(w, 334, 386, 'north', 'CHURROS', ('orange', 'white'), 'birch', subtitle='& COCOA')

    # NE: topiary garden with hedge maze
    mx0, mz0 = 380, 262
    for x in range(mx0, mx0 + 25):
        for z in range(mz0 - 25, mz0):
            if paths.mask[z, x]:
                continue
            w.set(x, G, z, 'grass_block')
            lx, lz = x - mx0, z - (mz0 - 25)
            wall = (lx % 4 == 0 or lz % 4 == 0) and not (hash2(lx // 4 * 4 + 1, lz // 4 * 4 + 1, 3) < 0.45 and
                                                        (lx % 4 != 0 and lz % 4 == 0 or lz % 4 != 0 and lx % 4 == 0))
            if lx in (0, 24) or lz in (0, 24):
                wall = not (12 <= lx <= 13 or 12 <= lz <= 13)
            if wall:
                w.set(x, G + 1, z, decor.leaves('oak'))
                w.set(x, G + 2, z, decor.leaves('oak'))
            else:
                w.set(x, G, z, 'dirt_path' if False else 'moss_block')
    decor.tree(w, mx0 + 14, G + 1, mz0 - 10, 'cone_topiary', 1.0) if w.is_air(mx0 + 14, G + 1, mz0 - 10) else None
    w.label(mx0 + 12, mz0 - 12, 'Hedge Maze', 'small')

    # NW: rose garden pond with gazebo
    pc = [(x, z) for (x, z) in disc(258, 280, 9)]
    decor.pond(w, pc, depth=2, seed=4)
    gx, gz = 270, 262
    for (x, z) in disc(gx, gz, 4):
        w.set(x, G, z, 'polished_diorite')
    for i in range(8):
        a = i * math.tau / 8
        X, Z = int(round(gx + 3.6 * math.cos(a))), int(round(gz + 3.6 * math.sin(a)))
        for k in range(1, 5):
            w.set(X, G + k, Z, 'birch_fence')
    cone_roof(w, gx + 0.0, gz + 0.0, G + 5, 5.0, 4, 'prismarine_brick_stairs', 'prismarine_bricks',
              finial=['lightning_rod[facing=up]'])
    w.set(gx, G + 4, gz, 'lantern[hanging=true]')
    decor.flower_patch(w, [(x, z) for (x, z) in disc(250, 255, 6) if not paths.mask[z, x]], density=0.8,
                       palette=['rose_bush' if False else 'poppy', 'red_tulip', 'pink_tulip', 'white_tulip'])
    w.label(262, 270, 'Rose Garden', 'small')

    # SW + SE: picnic lawns with trees and flower beds
    for (x0, z0) in ((228, 345), (380, 345), (345, 380), (262, 380)):
        pass
    decor.lawn_trees(w, 224, 224, 300, 300, ['oak', 'birch', 'cherry'], spacing=11, seed=1,
                     avoid=lambda x, z: math.hypot(x - CX, z - CZ) < 60 or paths.mask[z, x])
    decor.lawn_trees(w, 340, 224, 416, 300, ['oak', 'birch', 'cherry'], spacing=11, seed=2,
                     avoid=lambda x, z: math.hypot(x - CX, z - CZ) < 60 or paths.mask[z, x])
    decor.lawn_trees(w, 224, 340, 300, 416, ['oak', 'birch', 'cherry'], spacing=11, seed=3,
                     avoid=lambda x, z: math.hypot(x - CX, z - CZ) < 60 or paths.mask[z, x])
    decor.lawn_trees(w, 340, 340, 416, 416, ['oak', 'birch', 'cherry'], spacing=11, seed=4,
                     avoid=lambda x, z: math.hypot(x - CX, z - CZ) < 60 or paths.mask[z, x])


def build(w, paths):
    # radial walkways out of the hub to every land
    paths.draw([(CX, CZ - 50), (CX, 216)], 11, land='hub')                 # north -> Everspire Kingdom
    paths.draw([(CX + 50, CZ), (424, CZ)], 11, land='hub')                 # east -> Nova Frontier (Hyperion)
    paths.draw([(CX - 50, CZ), (216, CZ)], 11, land='hub')                 # west -> Blackwater Cove
    d = 50 / math.sqrt(2)
    paths.draw([(CX + d, CZ - d), (422, 218)], 9, land='hub')             # NE
    paths.draw([(CX - d, CZ - d), (218, 218)], 9, land='hub')             # NW
    paths.draw([(CX - d, CZ + d), (218, 422)], 9, land='hub')             # SW
    paths.draw([(CX + d, CZ + d), (422, 422)], 9, land='hub')             # SE
    paths.draw([(CX, CZ + 50), (CX, 424)], 13, land='hub')                 # south -> Grand Avenue
    plaza(w, paths)
    wonder_spire(w)
    quadrants(w, paths)
    w.label(CX, CZ + 75, 'STARLIGHT PLAZA', 'land')
