"""Blackwater Cove: pirate lagoon with a galleon, Skull Rock, Fort Cutlass, lighthouse and harbor town."""
import math
import random

from ..const import G
from ..geom import disc, circle, ring, ellipsoid, ball, line
from ..world import Builder, hash2, hash3
from ..font import giant_text
from .. import decor
from .. import rides
from ..buildings import style, building, restroom, info_booth, food_kiosk, cone_roof, box_walls

HARBOR = style(wall='spruce_planks', base='cobblestone', pillar='stripped_dark_oak_log[axis=y]',
               trim='dark_oak_planks', floor='dark_oak_planks', ceiling='spruce_planks', window='glass_pane',
               roof_stairs='dark_oak_stairs', roof_slab='dark_oak_slab', roof_block='dark_oak_planks',
               door='dark_oak', sign_wood='dark_oak', carpet='red_carpet', counter='dark_oak_planks',
               counter_top='dark_oak_slab[type=bottom]', shelf=('barrel[facing=up]', 'chest[facing=north]', 'bookshelf'),
               porch_floor='dark_oak_planks', porch_post='dark_oak_fence', porch_rail='dark_oak_fence',
               porch_roof='dark_oak_slab[type=bottom]')
WALLS = ['spruce_planks', 'oak_planks', 'birch_planks', 'stripped_spruce_wood[axis=y]', 'mossy_cobblestone',
         'light_gray_terracotta', 'cyan_terracotta']

LC = (82, 322)


def in_lagoon(x, z):
    n = (hash2(x // 6, z // 6, 90) - 0.5) * 0.22 + (hash2(x // 3, z // 3, 91) - 0.5) * 0.08
    return ((x - LC[0]) / 62.0) ** 2 + ((z - LC[1]) / 80.0) ** 2 < 1.0 + n


def lagoon(w, paths):
    cells = set()
    for x in range(20, 150):
        for z in range(236, 410):
            if in_lagoon(x, z):
                cells.add((x, z))
    for (x, z) in cells:
        edge = sum((x + dx, z + dz) in cells for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1), (2, 0), (-2, 0), (0, 2), (0, -2)))
        depth = 1 if edge < 6 else 2 if edge < 8 else 3
        for k in range(depth):
            w.set(x, G - k, z, 'water')
        w.set(x, G - depth, z, 'sand' if hash2(x, z, 3) < 0.7 else 'gravel')
        if depth == 3 and hash2(x, z, 5) < 0.02:
            w.set(x, G - 2, z, 'seagrass') if False else w.set(x, G - 3, z, 'sea_lantern')
        if depth >= 2 and hash2(x, z, 6) < 0.03:
            w.set(x, G - depth + 1, z, 'seagrass')
    # beaches
    for (x, z) in list(cells):
        for dx in range(-4, 5):
            for dz in range(-4, 5):
                X, Z = x + dx, z + dz
                if (X, Z) in cells or abs(dx) + abs(dz) > 5:
                    continue
                if w.name(X, G, Z) == 'grass_block':
                    w.set(X, G, Z, 'sand' if abs(dx) + abs(dz) <= 3 else ('sand' if hash2(X, Z, 7) < 0.5 else 'grass_block'))
    return cells


def galleon(w, x0, z0, L=46, beam=13):
    """Three-masted galleon moored with its bow pointing east. x0,z0 = stern-centre."""
    cz = z0
    half0 = beam / 2.0

    def half_at(u, y):
        t = u / L
        if t < 0.08:
            h = half0 * (0.85 + t * 1.8)
        elif t < 0.7:
            h = half0
        else:
            h = half0 * max(0.0, 1 - ((t - 0.7) / 0.3) ** 1.6)
        lower = 0.55 + 0.45 * min(1.0, (y - (G - 2)) / 6.0)
        return h * lower

    deck = G + 4
    for u in range(0, L):
        for y in range(G - 2, deck + 1):
            h = half_at(u, y)
            if h < 0.5:
                continue
            for dz in range(-int(h + 0.5), int(h + 0.5) + 1):
                x, z = x0 + u, cz + dz
                edge = abs(dz) >= h - 0.6 or y == G - 2
                if y == deck:
                    w.set(x, y, z, 'dark_oak_planks' if edge else 'spruce_planks')
                elif edge:
                    blk = 'dark_oak_planks'
                    if y == G + 2:
                        blk = 'yellow_terracotta'
                    if y == G + 1 and u % 5 == 2 and 6 < u < L - 8 and abs(dz) >= h - 0.6:
                        blk = 'black_concrete'
                    w.set(x, y, z, blk)
                else:
                    w.set(x, y, z, 'air' if y > G - 1 else 'dark_oak_planks')
        # bulwark railing
        h = half_at(u, deck)
        if h >= 0.5:
            for dz in (-int(h + 0.5), int(h + 0.5)):
                w.set(x0 + u, deck + 1, cz + dz, 'dark_oak_fence' if u % 4 else 'dark_oak_planks')
    # sterncastle with captain's cabin
    for u in range(0, 10):
        h = int(half_at(u, deck) + 0.5)
        for dz in range(-h, h + 1):
            for y in range(deck + 1, deck + 5):
                edge = abs(dz) == h or u in (0, 9)
                if edge:
                    blk = 'dark_oak_planks'
                    if y in (deck + 2, deck + 3) and (u == 0 and dz % 2 == 0 or abs(dz) == h and u % 3 == 1):
                        blk = 'yellow_stained_glass_pane'
                    w.set(x0 + u, y, cz + dz, blk)
                else:
                    w.set(x0 + u, y, cz + dz, 'air')
            w.set(x0 + u, deck + 5, cz + dz, 'spruce_planks')
            if abs(dz) == h or u == 0:
                w.set(x0 + u, deck + 6, cz + dz, 'dark_oak_fence')
    w.door(x0 + 9, deck + 1, cz, 'east', 'dark_oak')
    w.set(x0 + 4, deck + 1, cz, 'red_carpet')
    w.set(x0 + 3, deck + 1, cz - 2, 'chest[facing=east,type=single]')
    w.set(x0 + 3, deck + 1, cz + 2, 'cartography_table')
    w.set(x0 + 5, deck + 4, cz, 'lantern[hanging=true]')
    # ship's wheel on the poop deck
    w.set(x0 + 7, deck + 6, cz, 'spruce_fence')
    w.set(x0 + 7, deck + 7, cz, 'dark_oak_trapdoor[facing=west,half=bottom,open=true]')
    for (dz) in (-2, 2):
        w.set(x0 + 1, deck + 6, cz + dz, 'lantern')
    # forecastle
    for u in range(L - 12, L - 3):
        h = int(half_at(u, deck) + 0.5)
        for dz in range(-h, h + 1):
            w.set(x0 + u, deck + 2, cz + dz, 'spruce_planks')
            if abs(dz) == h:
                w.set(x0 + u, deck + 1, cz + dz, 'dark_oak_planks')
                w.set(x0 + u, deck + 3, cz + dz, 'dark_oak_fence')
    # bowsprit
    for (x, y, z) in line((x0 + L - 3, deck + 2, cz), (x0 + L + 9, deck + 7, cz)):
        w.set(x, y, z, 'dark_oak_log[axis=x]')
    # figurehead
    w.set(x0 + L, deck - 1, cz, 'gold_block')
    w.set(x0 + L - 1, deck, cz, 'gold_block')
    # masts, yards, sails, crow's nest, flags
    for (u, h, nsail) in ((13, 30, 3), (26, 36, 4), (37, 26, 3)):
        mx = x0 + u
        for y in range(G - 1, deck + h):
            w.set(mx, y, cz, 'dark_oak_log[axis=y]')
        for s in range(nsail):
            ys = deck + 6 + s * 7
            span = 9 - s * 2
            for dz in range(-span, span + 1):
                w.set(mx, ys + 5, cz + dz, 'dark_oak_fence')
            for y in range(ys, ys + 5):
                bow = 1 if 0 < y - ys < 4 else 0
                for dz in range(-span + 1, span):
                    if abs(dz) < span - 1 or y > ys:
                        w.set(mx + 1 + bow * (1 if abs(dz) < span - 3 else 0), y, cz + dz, 'white_wool')
        top = deck + h
        for (x, z) in circle(mx, cz, 2):
            w.set(x, top - 6, z, 'dark_oak_planks')
            w.set(x, top - 5, z, 'dark_oak_fence')
        # jolly roger
        for i in range(1, 6):
            for j in range(4):
                w.set(mx, top - 1 - j, cz + i, 'black_wool')
        for (i, j) in ((2, 1), (3, 1), (4, 1), (2, 2), (4, 2), (3, 3)):
            w.set(mx, top - 1 - j, cz + i + 0, 'white_wool')
    # cannons on deck
    for u in range(12, L - 12, 6):
        for side in (-1, 1):
            h = int(half_at(u, deck) + 0.5)
            w.set(x0 + u, deck + 1, cz + side * (h - 1), 'black_concrete')
            w.set(x0 + u, deck + 1, cz + side * (h - 2), 'spruce_trapdoor[facing=north,half=bottom,open=false]')
    w.label(x0 + L // 2, cz, 'THE CRIMSON KRAKEN (galleon)', 'attraction')


def skull_rock(w, cx, cz):
    for (x, y, z) in ellipsoid(cx, G, cz, 14, 8, 12):
        if y >= G - 2:
            r = hash3(x, y, z, 2)
            w.set(x, y, z, 'stone' if r < 0.5 else 'andesite' if r < 0.75 else 'mossy_cobblestone')
    sy = G + 12
    for (x, y, z) in ellipsoid(cx, sy, cz, 9, 10, 9):
        w.set(x, y, z, 'calcite' if hash3(x, y, z, 3) < 0.85 else 'diorite')
    # jaw
    for (x, y, z) in ellipsoid(cx + 2, sy - 8, cz, 7, 4, 7):
        if y >= G + 1:
            w.set(x, y, z, 'calcite')
    face = cx + 9
    # eye sockets (glowing deep inside)
    for ez in (cz - 4, cz + 4):
        for (x, y, z) in ellipsoid(face - 1, sy + 2, ez, 3, 2.6, 2.4):
            if x >= face - 3:
                w.set(x, y, z, 'black_concrete' if x <= face - 3 else 'air')
        w.set(face - 4, sy + 2, ez, 'shroomlight')
    # nose
    for k in range(3):
        for dz in range(-k, k + 1):
            w.set(face - 1, sy - 1 - (2 - k), cz + dz, 'air')
            w.set(face - 2, sy - 1 - (2 - k), cz + dz, 'black_concrete')
    # teeth
    for dz in range(-5, 6):
        w.set(face, sy - 5, cz + dz, 'bone_block[axis=y]' if dz % 2 else 'black_concrete')
        w.set(face - 1, sy - 5, cz + dz, 'black_concrete')
        w.set(face, sy - 6, cz + dz, 'bone_block[axis=y]' if dz % 2 == 0 else 'black_concrete')
    # pirate cave entrance at the waterline
    for x in range(face - 6, face + 2):
        for y in range(G + 1, G + 4):
            for z in range(cz - 2, cz + 3):
                w.set(x, y, z, 'air')
    for z in range(cz - 2, cz + 3):
        w.set(face - 6, G + 1, z, 'chest[facing=east,type=single]' if z == cz else 'gold_block' if z % 2 else 'air')
    w.label(cx, cz, 'SKULL ROCK', 'attraction')


def fort(w, x0, z0, x1, z1):
    for x in range(x0, x1 + 1):
        for z in range(z0, z1 + 1):
            edge = x in (x0, x1) or z in (z0, z1)
            w.set(x, G, z, 'cobblestone' if hash2(x, z, 1) < 0.6 else 'gravel')
            if edge:
                for y in range(G + 1, G + 6):
                    w.set(x, y, z, 'stone_bricks' if hash3(x, y, z, 4) < 0.6 else 'mossy_stone_bricks')
                if (x + z) % 2 == 0:
                    w.set(x, G + 6, z, 'stone_bricks')
    for (bx, bz) in ((x0, z0), (x1, z0), (x0, z1), (x1, z1)):
        for (x, z) in disc(bx, bz, 3.5):
            for y in range(G + 1, G + 8):
                w.set(x, y, z, 'cobblestone' if y < G + 7 else 'mossy_cobblestone')
        for (x, z) in circle(bx, bz, 3.5):
            if (x + z) % 2:
                w.set(x, G + 8, z, 'cobblestone_wall')
    # gate
    gx = (x0 + x1) // 2
    for x in range(gx - 2, gx + 3):
        for y in range(G + 1, G + 5):
            w.set(x, y, z1, 'air')
    # cannons on the lagoon-facing wall (south) parapet
    for x in range(x0 + 4, x1 - 3, 5):
        if abs(x - gx) < 4:
            continue
        w.set(x, G + 6, z1, 'black_concrete')
        w.set(x, G + 6, z1 - 1, 'polished_blackstone')
    # barracks & flag
    st = dict(HARBOR, wall='stone_bricks', base='cobblestone')
    building(w, x0 + 4, z0 + 3, 16, 9, 'south', st, floors=1, fh=5, roof='gable_x', name='FORT CUTLASS',
             kind='plain', awning=False)
    fx, fz = x1 - 8, z0 + 7
    for y in range(G + 1, G + 20):
        w.set(fx, y, fz, 'dark_oak_fence')
    for i in range(1, 7):
        for j in range(4):
            w.set(fx + i, G + 19 - j, fz, 'black_wool')
    for (i, j) in ((3, 1), (4, 1), (3, 2), (4, 2)):
        w.set(fx + i, G + 19 - j, fz, 'white_wool')
    w.label((x0 + x1) // 2, (z0 + z1) // 2, 'Fort Cutlass', 'small')


def lighthouse(w, cx, cz):
    for k in range(30):
        for (x, z) in circle(cx, cz, 4 if k < 20 else 3):
            w.set(x, G + 1 + k, z, 'red_concrete' if (k // 5) % 2 else 'white_concrete')
        for (x, z) in disc(cx, cz, 3 if k < 20 else 2):
            w.set(x, G + 1 + k, z, 'air')
    for (x, z) in disc(cx, cz, 4.5):
        w.set(x, G + 31, z, 'smooth_stone')
    for (x, z) in circle(cx, cz, 4.5):
        w.set(x, G + 32, z, 'iron_bars')
    for k in range(32, 36):
        for (x, z) in circle(cx, cz, 2.5):
            w.set(x, G + k, z, 'glass')
    w.set(cx, G + 33, cz, 'glowstone')
    w.set(cx, G + 34, cz, 'glowstone')
    cone_roof(w, cx + 0.0, cz + 0.0, G + 36, 3.5, 4, 'red_nether_brick_stairs', 'red_nether_bricks',
              finial=['lightning_rod[facing=up]'])
    for k in range(30):
        a = k * 0.6
        w.set(int(round(cx + 2 * math.cos(a))), G + 1 + k, int(round(cz + 2 * math.sin(a))), 'spruce_planks')
    w.door(cx, G + 1, cz + 4, 'south', 'dark_oak')
    w.label(cx, cz, 'Lighthouse', 'small')


def harbor(w, paths):
    paths.draw([(120, 218), (150, 236), (166, 250), (166, 402), (120, 422)], 9, land='pirate')
    paths.draw([(216, 320), (166, 320)], 9, land='pirate')
    # boardwalk along the shore
    for z in range(250, 400):
        for x in range(152, 160):
            if w.name(x, G, z) in ('sand', 'grass_block', 'water'):
                w.set(x, G, z, 'spruce_planks' if x % 4 else 'stripped_spruce_log[axis=z]')
                paths.mask[z, x] = True
    # piers
    for (pz, px1) in ((300, 98), (345, 122)):
        for x in range(px1, 160):
            for z in range(pz - 2, pz + 3):
                w.set(x, G + 1 if w.name(x, G, z) == 'water' else G, z, 'spruce_planks')
                if w.name(x, G, z) == 'water' and z in (pz - 2, pz + 2) and x % 4 == 0:
                    for y in range(G - 3, G + 1):
                        if w.name(x, y, z) in ('water', 'sand', 'gravel'):
                            w.set(x, y, z, 'spruce_log[axis=y]')
                    w.set(x, G + 2, z, 'spruce_fence')
                    w.set(x, G + 3, z, 'lantern')
            paths.mask[pz - 2:pz + 3, x] = True
    # harbour shops (east side of the harbour street, facing west)
    shops = [(272, 16, 'SALTY BARNACLE', 'restaurant', 2), (292, 12, "CAPTAIN'S LOOT", 'shop', 2),
             (330, 14, 'MAP MAKER', 'shop', 2), (348, 12, 'ROPE & ANCHOR', 'shop', 1),
             (364, 14, "DAVY'S LOCKER", 'shop', 2), (382, 12, 'HARBOR MASTER', 'office', 2)]
    for i, (z0, wd, name, kind, fl) in enumerate(shops):
        st = dict(HARBOR, wall=WALLS[i % len(WALLS)])
        building(w, 174, z0, wd, 16, 'west', st, floors=fl, fh=5, roof='steep_z' if wd <= 12 else 'gable_z',
                 name=name, kind=kind, awning=False, porch=2, door_double=wd >= 14)
    restroom(w, 196, 380, 'west', HARBOR)
    info_booth(w, 182, 312, HARBOR, title='HARBOR INFO')
    food_kiosk(w, 174, 250, 'west', 'GROG & JUICE', ('black', 'white'), 'dark_oak', subtitle='SODA FOUNTAIN')
    food_kiosk(w, 186, 404, 'west', 'FISH & CHIPS', ('blue', 'white'), 'dark_oak', subtitle='HOT!')
    # crates, barrels, nets and anchors on the boardwalk
    rnd = random.Random(12)
    for z in range(254, 398, 7):
        x = 153
        if w.is_air(x, G + 1, z):
            w.set(x, G + 1, z, rnd.choice(['barrel[facing=up]', 'barrel[facing=up]', 'spruce_planks', 'chest[facing=east,type=single]']))
            if rnd.random() < 0.4:
                w.set(x, G + 2, z, 'barrel[facing=up]')


def build(w, paths):
    cells = lagoon(w, paths)
    galleon(w, 70, 318)
    skull_rock(w, 52, 372)
    fort(w, 28, 222, 78, 246) if False else fort(w, 32, 226, 80, 246)
    lighthouse(w, 132, 404)
    harbor(w, paths)
    rides.swinging_ship(w, 192, 236 + 0, angle_deg=30)
    # gangplank from the pier down onto the galleon deck
    for i, z in enumerate(range(303, 312)):
        y = G + 2 + (i * 3) // 9
        for x in (100, 101):
            w.set(x, y, z, 'spruce_slab[type=bottom]')
            w.clear(x, y + 1, z, x, y + 3, z)
    for x in (100, 101):
        w.set(x, G + 5, 312, 'air')
        w.set(x, G + 1, 302, 'air')
    paths.draw([(166, 236), (178, 236)], 5, land='pirate') if False else None
    decor.lawn_trees(w, 150, 222, 216, 418, ['palm', 'oak', 'spruce'], spacing=12, seed=80,
                     avoid=lambda x, z: paths.mask[z, x])
    for (x, z) in ((150, 280), (148, 360), (140, 250), (150, 395), (128, 260), (138, 380)):
        if w.name(x, G, z) == 'sand' and w.is_air(x, G + 1, z):
            decor.tree(w, x, G + 1, z, 'palm', 1.0)
    w.label(110, 280, 'BLACKWATER COVE', 'land')
