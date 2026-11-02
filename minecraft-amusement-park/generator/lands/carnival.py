"""Jollyville Carnival: cartoon land with the Jolly Wheel, Topsy Teacups, Big Top, bumper cars and midway."""
import math
import random

from ..const import G
from ..geom import disc, circle, ring, ellipsoid, ball
from ..world import Builder, hash2, hash3
from ..font import giant_text
from .. import decor
from .. import rides
from ..buildings import style, building, restroom, info_booth, food_kiosk, box_walls, hip_roof

TOON_ROOFS = [('cherry_stairs', 'cherry_slab', 'cherry_planks'), ('warped_stairs', 'warped_slab', 'warped_planks'),
              ('bamboo_stairs', 'bamboo_slab', 'bamboo_planks'), ('acacia_stairs', 'acacia_slab', 'acacia_planks'),
              ('mangrove_stairs', 'mangrove_slab', 'mangrove_planks'), ('purpur_stairs', 'purpur_slab', 'purpur_block'),
              ('prismarine_stairs', 'prismarine_slab', 'prismarine')]
TOON_WALLS = ['yellow_concrete', 'light_blue_concrete', 'lime_concrete', 'pink_concrete', 'orange_concrete',
              'magenta_concrete', 'cyan_concrete', 'white_concrete']


def toon_style(i):
    rs, rsl, rb = TOON_ROOFS[i % len(TOON_ROOFS)]
    wall = TOON_WALLS[i % len(TOON_WALLS)]
    return style(wall=wall, base=wall, pillar='white_concrete', trim=TOON_WALLS[(i + 3) % len(TOON_WALLS)],
                 floor='cherry_planks', ceiling='birch_planks', window='white_stained_glass_pane',
                 roof_stairs=rs, roof_slab=rsl, roof_block=rb, door='cherry', sign_wood='cherry',
                 awning=[['red', 'white'], ['blue', 'yellow'], ['pink', 'white'], ['lime', 'white']][i % 4],
                 carpet=['pink_carpet', 'yellow_carpet', 'light_blue_carpet', 'lime_carpet'][i % 4],
                 counter='white_concrete', counter_top='cherry_slab[type=bottom]',
                 shelf=('pink_wool', 'yellow_wool', 'light_blue_wool', 'lime_wool'), sign_color='magenta',
                 sign_glow=True, light='lantern[hanging=true]')


def cupcake(w, cx, cz):
    for (x, z) in disc(cx, cz, 3):
        for y in range(G + 1, G + 4):
            a = math.atan2(z - cz, x - cx)
            w.set(x, y, z, 'pink_terracotta' if int((a + math.pi) * 3) % 2 else 'white_terracotta')
    for (x, y, z) in ellipsoid(cx, G + 4, cz, 4.2, 3.2, 4.2):
        if y >= G + 4:
            r = hash3(x, y, z, 4)
            w.set(x, y, z, 'pink_concrete' if r > 0.12 else ['yellow_concrete', 'lime_concrete', 'light_blue_concrete'][int(r * 25) % 3])
    for (x, y, z) in ball(cx, G + 8, cz, 1.1):
        w.set(x, y, z, 'red_concrete')
    w.set(cx, G + 10, cz, 'green_concrete')


def ice_cream(w, cx, cz):
    for k in range(8):
        r = 0.6 + k * 0.35
        for (x, z) in disc(cx, cz, r):
            w.set(x, G + 1 + k, z, 'orange_terracotta' if (x + z + k) % 2 else 'yellow_terracotta')
    for i, (col, dy) in enumerate((('pink_concrete', 10), ('brown_concrete', 14), ('white_concrete', 17))):
        for (x, y, z) in ball(cx, G + dy, cz, 3.2 - i * 0.5):
            w.set(x, y, z, col)
    w.set(cx, G + 20, cz, 'red_concrete')


def balloons(w, x, z, h=7):
    for y in range(G + 1, G + h):
        w.set(x, y, z, 'chain[axis=y]' if y > G + 1 else 'white_concrete')
    cols = ['red', 'yellow', 'blue', 'lime', 'pink', 'orange', 'purple']
    for i, (dx, dy, dz) in enumerate(((0, 0, 0), (1, 1, 0), (-1, 1, 1), (0, 2, -1), (1, 2, 1))):
        w.set(x + dx, G + h + dy, z + dz, f'{cols[(i + x) % len(cols)]}_wool')


def game_booth(w, x0, z0, facing, title, color, i):
    b = Builder.for_rect(w, x0, z0, 7, 5, facing, G + 1)
    b.clear(0, 0, -1, 6, 6, 4)
    b.fill(0, -1, 0, 6, -1, 4, 'spruce_planks')
    box_walls(b, 0, 6, 0, 4, 0, 3, 'white_concrete', f'{color}_concrete')
    b.fill(1, 0, 1, 5, 3, 3, 'air')
    for x in range(1, 6):
        b.set(x, 0, 0, f'{color}_concrete')
        b.set(x, 1, 0, 'spruce_slab[type=bottom]')
        b.set(x, 2, 0, 'air')
    for x in range(-1, 8):
        for z in range(-1, 5):
            b.set(x, 4, z, f'{color}_wool' if x % 2 else 'white_wool')
    prizes = ['pink_wool', 'yellow_wool', 'light_blue_wool', 'lime_wool', 'orange_wool']
    for x in range(1, 6):
        b.set(x, 0, 3, ['target', 'target', 'hay_block[axis=y]', 'melon', 'note_block'][i % 5])
        b.set(x, 2, 3, prizes[(x + i) % len(prizes)])
        b.set(x, 1, 3, 'spruce_slab[type=top]')
    b.set(3, 3, 2, 'lantern[hanging=true]')
    b.sign(3, 4, -1, ['STEP RIGHT UP', title, 'Win a prize!', ''], wood='cherry', facing='north', color='yellow',
           glow=True)


def build(w, paths):
    # walkways
    paths.draw([(422, 422), (446, 446), (446, 505)], 9, land='carnival')
    paths.draw([(422, 469), (446, 469)], 7, land='carnival')
    paths.draw([(446, 505), (612, 505)], 11, land='carnival')
    paths.draw([(600, 418), (600, 505)], 7, land='carnival')
    paths.draw([(520, 505), (520, 455), (536, 455)], 7, land='carnival')
    paths.draw([(520, 505), (520, 600), (566, 600)], 7, land='carnival')
    paths.draw([(446, 505), (446, 600), (520, 600)], 7, land='carnival')
    # big attractions
    rides.teacups(w, 482, 462)
    rides.big_top(w, 562, 456, 21, door='west')
    cy = rides.ferris_wheel(w, 565, 568, R=30, n_gondolas=18)
    rides.bumper_cars(w, 456, 518, 30, 22)
    # midway games
    names = ['RING TOSS', 'BALLOON DARTS', 'HOOP SHOT', 'DUCK POND', 'WATER RACE', 'HIGH STRIKER',
             'SKEE BALL', 'COIN TOSS']
    cols = ['red', 'blue', 'yellow', 'lime', 'purple', 'orange', 'pink', 'cyan']
    for i, x0 in enumerate(range(452, 518, 9)):
        game_booth(w, x0, 492, 'south', names[i % len(names)], cols[i % len(cols)], i)
    for i, x0 in enumerate(range(527, 598, 9)):
        if 562 <= x0 <= 570:
            continue
        game_booth(w, x0, 513, 'north', names[(i + 3) % len(names)], cols[(i + 2) % len(cols)], i + 3)
    # food
    food_kiosk(w, 604, 470, 'west', 'COTTON CANDY', ('pink', 'white'), 'cherry', subtitle='CLOUDS')
    food_kiosk(w, 604, 440, 'west', 'CORN DOGS', ('yellow', 'red'), 'cherry', subtitle='& LEMONADE')
    food_kiosk(w, 527, 475, 'east', 'FUNNEL CAKE', ('orange', 'white'), 'cherry', subtitle='POWDERED')
    food_kiosk(w, 432, 528, 'east', 'CANDY APPLES', ('red', 'white'), 'cherry', subtitle='CARAMEL')
    # toon village houses
    houses = [(452, 560, 14, 12, 'south', 'GIGGLE HOUSE'), (468, 576, 12, 12, 'south', 'PUPPET SHOP'),
              (484, 556, 13, 12, 'south', 'TOY BOX'), (430, 570, 12, 13, 'east', 'JOKE SHOP'),
              (497, 574, 12, 12, 'south', 'HAT HOUSE')]
    for i, (x0, z0, wd, dp, f, name) in enumerate(houses):
        st = toon_style(i)
        b = building(w, x0, z0, wd, dp, f, st, floors=2, fh=5, roof='hip', name=name, kind='shop', seed=i + 70)
        # cartoon round windows + chimney with a curly top
        b.set(wd - 3, 11, dp - 3, 'red_concrete')
        b.set(wd - 3, 12, dp - 3, 'red_concrete')
        b.set(wd - 3, 13, dp - 3, 'white_concrete')
    restroom(w, 586, 528, 'west', toon_style(3))
    info_booth(w, 452, 484, toon_style(1), title='GUEST INFO')
    # sculptures and balloons
    cupcake(w, 506, 475)
    ice_cream(w, 433, 600)
    cupcake(w, 600, 590)
    for (x, z) in ((450, 512), (512, 512), (540, 498), (596, 512), (520, 470), (440, 455), (530, 590)):
        if w.is_air(x, G + 1, z):
            balloons(w, x, z)
    # string lights over the midway
    for x in range(452, 600, 12):
        decor.string_lights(w, (x, G + 6, 499), (x + 6, G + 6, 511), sag=1, every=2)
    # entrance arch for the land
    for (px, pz) in ((441, 440), (451, 450)):
        for y in range(G + 1, G + 10):
            w.set(px, y, pz, 'red_concrete' if y % 2 else 'white_concrete')
        w.set(px, G + 10, pz, 'ochre_froglight[axis=y]')
    decor.lawn_trees(w, 424, 424, 616, 616, ['cherry', 'topiary', 'birch'], spacing=12, seed=70,
                     avoid=lambda x, z: paths.mask[z, x] or math.hypot(x - 565, z - 568) < 36)
    w.label(520, 540, 'JOLLYVILLE CARNIVAL', 'land')
