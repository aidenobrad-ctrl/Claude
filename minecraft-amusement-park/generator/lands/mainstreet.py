"""Grand Avenue: Victorian main street, Town Square, gardens and the backstage staff area."""
import math
import random

from ..const import G
from ..geom import disc, circle, ring
from ..world import Builder, hash2, door as world_door
from .. import decor
from ..buildings import (style, building, restroom, info_booth, food_kiosk, cone_roof, hip_roof, gable_roof,
                         box_walls, flat_roof)

STREET_X0, STREET_X1 = 305, 334   # building front to building front
ROAD_X0, ROAD_X1 = 312, 327
Z_START, Z_END = 424, 587

WALLS = ['bricks', 'light_blue_terracotta', 'yellow_terracotta', 'white_terracotta', 'pink_terracotta',
         'cyan_terracotta', 'lime_terracotta', 'orange_terracotta', 'red_terracotta', 'smooth_sandstone',
         'mud_bricks', 'light_gray_terracotta']
AWNINGS = [['red', 'white'], ['green', 'white'], ['blue', 'white'], ['yellow', 'white'], ['purple', 'white'],
           ['orange', 'white'], ['cyan', 'white'], ['pink', 'white'], ['lime', 'white']]
ROOFS = [('deepslate_tile_stairs', 'deepslate_tile_slab', 'deepslate_tiles'),
         ('dark_prismarine_stairs', 'dark_prismarine_slab', 'dark_prismarine'),
         ('dark_oak_stairs', 'dark_oak_slab', 'dark_oak_planks'),
         ('spruce_stairs', 'spruce_slab', 'spruce_planks')]

WEST = [(18, 'THE PLAZA INN', 'restaurant', 2), (12, 'CRYSTAL ARTS', 'shop', 2), (9, 'SILHOUETTES', 'shop', 2),
        ('gap', 474), (14, 'CONFECTIONERY', 'shop', 3), (12, 'HAT SHOP', 'shop', 2),
        (16, 'MARKET HOUSE', 'restaurant', 2), (12, 'PHOTO SUPPLY', 'shop', 3), (20, 'EMPORIUM', 'shop', 3),
        (12, 'TOY STATION', 'shop', 2), (14, 'BOOKS & MAPS', 'shop', 2), (7, 'LOCKERS', 'office', 2)]
EAST = [(18, 'STARLIGHT CAFE', 'restaurant', 2), (12, 'PENNY ARCADE', 'shop', 2), (9, 'JEWELRY', 'shop', 2),
        ('gap', 474), (16, 'CINEMA', 'shop', 3), (12, 'CANDY PALACE', 'shop', 2), (14, 'GRAND BISTRO', 'restaurant', 2),
        (12, 'PIN TRADING', 'shop', 2), (16, 'FIRE STATION', 'office', 2), (12, 'FIRST AID', 'office', 2),
        (14, 'CITY HALL', 'office', 3), (11, 'STROLLERS', 'office', 2)]

GOODS = {
    'CONFECTIONERY': ['cake', 'pink_glazed_terracotta', 'honey_block'],
    'CANDY PALACE': ['cake', 'pink_concrete', 'magenta_glazed_terracotta'],
    'TOY STATION': ['red_wool', 'yellow_wool', 'blue_wool', 'barrel[facing=up]'],
    'BOOKS & MAPS': ['bookshelf', 'chiseled_bookshelf[facing=north]', 'lectern[facing=north]'],
    'HAT SHOP': ['white_wool', 'black_wool', 'barrel[facing=up]'],
    'EMPORIUM': ['barrel[facing=up]', 'bookshelf', 'chest[facing=north]', 'white_wool'],
    'JEWELRY': ['glass', 'amethyst_block', 'glass'],
    'CRYSTAL ARTS': ['glass', 'light_blue_stained_glass', 'purple_stained_glass'],
}


def street(w, paths):
    for z in range(Z_START - 50, 612):
        for x in range(STREET_X0, STREET_X1 + 1):
            if z < Z_START and abs(x - 320) > 6:
                continue
            if ROAD_X0 <= x <= ROAD_X1 and z >= Z_START:
                blk = 'polished_granite' if hash2(x, z, 1) < 0.72 else 'granite'
                if x in (ROAD_X0, ROAD_X1):
                    blk = 'stone_bricks'
            elif z >= Z_START:
                blk = 'smooth_stone' if (x + z) % 7 else 'polished_andesite'
                if x in (ROAD_X0 - 1, ROAD_X1 + 1):
                    blk = 'polished_andesite'
            else:
                continue
            w.set(x, G, z, blk)
            paths.mask[z, x] = True
    # trolley line
    for z in range(Z_START + 2, 606):
        w.set(320, G + 1, z, 'rail[shape=north_south]')
    # trolley car
    trolley(w, 320, 515)
    # curb lamps, trees in grates, benches
    for z in range(Z_START + 4, 586, 10):
        for x in (ROAD_X0 - 2, ROAD_X1 + 2):
            decor.lamp(w, x, G + 1, z, 'classic', arm='z')
    for z in range(Z_START + 9, 584, 20):
        for x in (ROAD_X0 - 3, ROAD_X1 + 3):
            for dx in (-1, 0, 1):
                for dz in (-1, 0, 1):
                    w.set(x + dx, G, z + dz, 'grass_block' if (dx, dz) == (0, 0) else 'polished_blackstone_bricks')
            decor.tree(w, x, G + 1, z, 'birch', 0.8, seed=z)
    for z in range(Z_START + 14, 584, 20):
        decor.bench(w, ROAD_X0 - 3, G + 1, z - 1, 'east', 'dark_oak', 3)
        decor.bench(w, ROAD_X1 + 3, G + 1, z + 1, 'west', 'dark_oak', 3)
        decor.trash_can(w, ROAD_X0 - 3, G + 1, z + 4, 'classic')
        decor.trash_can(w, ROAD_X1 + 3, G + 1, z - 4, 'classic')
    w.label(320, 500, 'GRAND AVENUE', 'land')


def trolley(w, x, z0, length=9):
    y = G + 2
    for z in range(z0, z0 + length):
        for dx in (-1, 0, 1):
            w.set(x + dx, y - 1, z, 'spruce_slab[type=top]') if False else None
            w.set(x + dx, y, z, 'spruce_slab[type=bottom]')
    for z in range(z0, z0 + length):
        for dx in (-1, 1):
            if z in (z0, z0 + length - 1) or (z - z0) % 2 == 0:
                for k in range(1, 3):
                    w.set(x + dx, y + k, z, 'dark_oak_fence')
            else:
                w.set(x + dx, y + 1, z, f'spruce_stairs[facing={"west" if dx < 0 else "east"},half=bottom]')
        w.set(x, y + 3, z, 'red_concrete')
        w.set(x - 1, y + 3, z, 'yellow_concrete')
        w.set(x + 1, y + 3, z, 'yellow_concrete')
    for z in (z0, z0 + length - 1):
        w.set(x, y + 1, z, 'red_terracotta')
        w.set(x, y + 2, z, 'glass_pane')
        w.set(x, y + 4, z, 'lantern')
    w.set(x, y + 4, z0 + length // 2, 'bell[attachment=floor,facing=north]')


def shops(w):
    rnd = random.Random(42)
    for side, lst in (('west', WEST), ('east', EAST)):
        z = Z_START + 1
        for i, entry in enumerate(lst):
            if entry[0] == 'gap':
                z = entry[1]
                continue
            width, name, kind, floors = entry
            if z + width - 1 > Z_END:
                break
            wall = WALLS[(i * 5 + (0 if side == 'west' else 3)) % len(WALLS)]
            rs, rsl, rb = ROOFS[(i + (1 if side == 'east' else 0)) % len(ROOFS)]
            trim = 'smooth_quartz' if i % 2 == 0 else 'white_concrete'
            st = style(wall=wall, base=wall, pillar='quartz_pillar[axis=y]' if i % 3 else 'stone_bricks', trim=trim,
                       floor='dark_oak_planks' if i % 2 else 'spruce_planks', ceiling='spruce_planks',
                       window='glass_pane' if i % 2 else 'white_stained_glass_pane', roof_stairs=rs,
                       roof_slab=rsl, roof_block=rb, awning=AWNINGS[(i * 3 + len(side)) % len(AWNINGS)],
                       door='dark_oak' if i % 2 else 'spruce', sign_wood='dark_oak',
                       carpet=['red_carpet', 'green_carpet', 'blue_carpet', 'purple_carpet'][i % 4],
                       parapet=f'{trim}' if False else 'smooth_quartz_slab[type=bottom]', cornice='quartz_stairs',
                       sign_color='yellow', sign_glow=True)
            depth = 22 if kind != 'restaurant' else 24
            roof = ['flat', 'gable_z', 'flat', 'hip', 'flat', 'gable_z'][(i + len(side)) % 6]
            if name in ('CITY HALL', 'THE PLAZA INN', 'STARLIGHT CAFE'):
                roof = 'steep_hip'
            x0 = 280 + (25 - depth) if side == 'west' else 335
            facing = 'east' if side == 'west' else 'west'
            b = building(w, x0, z, width, depth, facing, st, floors=floors, fh=5, roof=roof,
                         name=name, kind=kind, door_double=width >= 14, seed=i * 31 + len(side),
                         goods=GOODS.get(name))
            # second-floor flower boxes & balcony
            for lx in range(1, width - 1):
                if (lx - 1) % 3 == 1 and floors >= 2:
                    for f in range(1, floors):
                        if b.is_air(lx, f * 5, -1):
                            b.set(lx, f * 5, -1, 'potted_' + rnd.choice(['red_tulip', 'azure_bluet', 'poppy',
                                                                          'cornflower', 'allium', 'fern']))
            if name == 'FIRE STATION':
                b.clear(3, 0, 0, width - 4, 3, 0)
                for lx in range(3, width - 3):
                    b.set(lx, 3, 0, 'red_terracotta')
                b.sign(width // 2, 4, -1, ['', 'FIRE STATION', 'No. 1', ''], wood='dark_oak', facing='north')
                # vintage fire engine parked inside
                for lz in range(2, 8):
                    for lx in range(width // 2 - 1, width // 2 + 2):
                        b.set(lx, 0, lz, 'red_concrete')
                    b.set(width // 2, 1, lz, 'red_concrete' if lz > 3 else 'glass')
                for lz in (2, 7):
                    b.set(width // 2 - 1, 0, lz, 'black_concrete')
                    b.set(width // 2 + 1, 0, lz, 'black_concrete')
                b.set(width // 2, 2, 4, 'bell[attachment=floor,facing=north]')
                b.set(width // 2, 2, 6, 'ladder[facing=north]') if False else None
            if name == 'CINEMA':
                b.sign(2, 2, -1, ['NOW SHOWING', 'Steamboat', 'Cartoons', 'Every Hour'], wood='dark_oak',
                       facing='north', color='white', glow=True)
                b.sign(width - 3, 2, -1, ['COMING SOON', 'Space Pals', 'in 3-D', ''], wood='dark_oak',
                       facing='north', color='white', glow=True)
                for lx in range(0, width):
                    b.set(lx, 4, -1, 'redstone_lamp[lit=true]' if lx % 2 == 0 else 'gold_block')
                    b.set(lx, 4, 0, 'redstone_block')
            z += width + 1
    # entrance blocks of the cross alleys
    for x0, x1 in ((280, 304), (335, 359)):
        pass


def town_square(w, paths):
    cx, cz = 320, 598
    for x in range(272, 369):
        for z in range(588, 612):
            r = math.hypot(x - cx, z - cz)
            blk = 'bricks' if (x + z) % 2 == 0 and r < 22 else 'polished_andesite'
            if int(r) % 6 == 0:
                blk = 'polished_diorite'
            w.set(x, G, z, blk)
            paths.mask[z, x] = True
    # flagpole garden
    for (x, z) in disc(cx, cz, 6):
        w.set(x, G, z, 'grass_block')
        if (x - cx) ** 2 + (z - cz) ** 2 > 4:
            decor.flower_at(w, x, G + 1, z, 3, ['red_tulip', 'white_tulip', 'cornflower'])
    for (x, z) in circle(cx, cz, 6.5):
        w.set(x, G, z, 'stone_bricks')
        w.set(x, G + 1, z, 'stone_brick_wall')
    for (x, z) in disc(cx, cz, 1.5):
        w.set(x, G + 1, z, 'polished_andesite')
    decor.flagpole(w, cx, G + 2, cz, 18, ('blue_wool', 'yellow_wool', 'blue_wool'), 'east')
    # bandstand gazebo
    gx, gz = 290, 599
    for (x, z) in disc(gx, gz, 5):
        w.set(x, G + 1, z, 'spruce_planks')
    for (x, z) in circle(gx, gz, 5):
        w.set(x, G + 2, z, 'white_concrete' if False else 'birch_fence')
    for i in range(8):
        a = i * math.tau / 8
        X, Z = int(round(gx + 4.6 * math.cos(a))), int(round(gz + 4.6 * math.sin(a)))
        for k in range(2, 6):
            w.set(X, G + k, Z, 'birch_fence')
    w.clear(gx + 5, G + 2, gz, gx + 5, G + 2, gz)
    cone_roof(w, gx + 0.0, gz + 0.0, G + 6, 6.2, 5, 'red_nether_brick_stairs', 'red_nether_bricks',
              finial=['gold_block', 'lightning_rod[facing=up]'])
    decor.hang_lantern(w, gx, G + 5, gz)
    for (x, z) in ((gx - 2, gz), (gx + 1, gz - 2), (gx, gz + 2)):
        w.set(x, G + 2, z, 'note_block')
    w.set(gx - 1, G + 2, gz - 1, 'jukebox')
    # benches & lamps around the square
    for (x, z, look) in ((300, 590, 'south'), (336, 590, 'south'), (300, 608, 'north'), (336, 608, 'north'),
                         (350, 595, 'west'), (350, 602, 'west')):
        decor.bench(w, x, G + 1, z, look, 'dark_oak', 3)
    for (x, z) in ((276, 590), (364, 590), (276, 610), (364, 610), (306, 596), (334, 596), (306, 602), (334, 602)):
        decor.lamp(w, x, G + 1, z, 'classic', arm='x')
    # horse-less carriage / vintage car
    for dz in range(4):
        for dx in range(2):
            w.set(345 + dx, G + 1, 600 + dz, 'green_concrete' if dz in (1, 2) else 'black_concrete')
    w.set(345, G + 2, 601, 'spruce_stairs[facing=south,half=bottom]')
    w.set(346, G + 2, 601, 'spruce_stairs[facing=south,half=bottom]')
    w.set(345, G + 2, 602, 'green_concrete')
    w.set(346, G + 2, 602, 'green_concrete')
    w.label(320, 598, 'Town Square', 'small')


def gardens_west(w, paths):
    """Grand Avenue Gardens between the west shops and Coral Cay."""
    x0, x1, z0, z1 = 224, 276, 478, 610
    paths.draw([(306, 469), (216, 469)], 7, land='mainstreet')
    paths.draw([(250, 472), (250, 606)], 5, land='mainstreet', mat=lambda x, z: 'polished_andesite' if (x + z) % 3 else 'smooth_stone')
    paths.draw([(250, 540), (279, 540)], 4, land='mainstreet', mat='smooth_stone')
    # pond with central fountain
    pond_cells = [(x, z) for (x, z) in disc(238, 520, 10)]
    decor.pond(w, pond_cells, depth=2, edge='stone_bricks', seed=21)
    decor.fountain(w, 238, 520, 4, rim='stone_bricks', rim_top='stone_brick_slab[type=bottom]', tiers=1)
    # gazebo
    gx, gz = 262, 575
    for (x, z) in disc(gx, gz, 4):
        w.set(x, G + 1, z, 'white_concrete')
    for i in range(6):
        a = i * math.tau / 6
        X, Z = int(round(gx + 3.6 * math.cos(a))), int(round(gz + 3.6 * math.sin(a)))
        for k in range(2, 6):
            w.set(X, G + k, Z, 'birch_fence')
    cone_roof(w, gx + 0.0, gz + 0.0, G + 6, 5.0, 4, 'birch_stairs', 'birch_planks', finial=['lantern'])
    for (x0b, z0b) in ((228, 560), (228, 586)):
        decor.flower_bed(w, x0b, z0b, x0b + 12, z0b + 8, border='spruce_fence', density=0.85, seed=x0b + z0b)
    restroom(w, 254, 486, 'east', style(wall='bricks', trim='smooth_quartz', pillar='stone_bricks',
                                         roof_stairs='dark_prismarine_stairs', roof_slab='dark_prismarine_slab',
                                         roof_block='dark_prismarine', door='spruce', sign_wood='spruce',
                                         ceiling='spruce_planks'))
    decor.lawn_trees(w, 224, 478, 276, 610, ['oak', 'birch', 'cherry'], spacing=10, seed=12,
                     avoid=lambda x, z: paths.mask[z, x])
    w.label(248, 540, 'Avenue Gardens', 'small')


def backstage(w, paths):
    """Staff & maintenance compound behind the east shops (hidden by a fence line and hedges)."""
    x0, x1, z0, z1 = 362, 418, 478, 611
    # guest cross path to Jollyville at the north edge of the compound
    paths.draw([(333, 469), (424, 469)], 7, land='mainstreet')
    # compound paving
    for x in range(x0, x1 + 1):
        for z in range(z0, z1 + 1):
            w.set(x, G, z, 'gray_concrete' if (x // 3 + z // 3) % 5 else 'light_gray_concrete')
    # perimeter: wooden privacy fence (spruce) + hedge on the guest side
    for z in range(z0 - 1, z1 + 1):
        for k in (1, 2):
            w.set(x0 - 1, G + k, z, 'spruce_fence')
        w.set(x0 - 1, G + 3, z, decor.leaves('oak'))
    for x in range(x0 - 1, x1 + 2):
        for k in (1, 2):
            w.set(x, G + k, z0 - 1, 'spruce_fence')
        w.set(x, G + 3, z0 - 1, decor.leaves('oak'))
    for z in range(z0 - 1, z1 + 1):
        for k in (1, 2):
            w.set(x1 + 1, G + k, z, 'spruce_fence')
    w.set(x0 - 1, G + 1, 600, 'spruce_fence_gate[facing=east,open=false,in_wall=false]')
    w.set(x0 - 1, G + 2, 600, 'air')
    w.set(x0 - 1, G + 3, 600, 'air')
    w.sign(x0 - 2, G + 2, 601, ['CAST MEMBERS', 'ONLY', 'Backstage', 'Area'], wood='spruce', facing='west')
    # service road down the middle to the south gate
    for z in range(z0, z1 + 1):
        for x in range(398, 411):
            w.set(x, G, z, 'gray_concrete' if x not in (398, 410) else 'light_gray_concrete')
            if x == 404 and (z // 3) % 2 == 0:
                w.set(x, G, z, 'yellow_concrete')
    # Cast Member Center (offices, break room, lockers)
    st_office = style(wall='light_gray_concrete', base='stone_bricks', pillar='gray_concrete', trim='white_concrete',
                      floor='polished_andesite', ceiling='smooth_stone', window='glass_pane',
                      roof_block='smooth_stone', door='iron' if False else 'spruce', sign_wood='spruce',
                      carpet='blue_carpet', parapet='smooth_stone_slab[type=bottom]')
    building(w, 366, 482, 26, 16, 'east', st_office, floors=3, fh=5, roof='flat', name='CAST CENTER',
             kind='office', awning=False, sign_lines=['', 'CAST MEMBER', 'CENTER', ''])
    # Maintenance shop: tall shed with big roller doors & workshop
    st_shop = style(wall='light_gray_concrete', base='light_gray_concrete', pillar='gray_concrete',
                    trim='gray_concrete', floor='smooth_stone', ceiling='smooth_stone',
                    roof_stairs='stone_brick_stairs' if False else 'andesite_stairs', roof_slab='andesite_slab',
                    roof_block='andesite', door='spruce', sign_wood='spruce')
    b = building(w, 366, 512, 30, 28, 'east', st_shop, floors=1, fh=9, roof='gable_z', kind='plain', awning=False,
                 sign_lines=['', 'CENTRAL', 'MAINTENANCE', 'SHOP'])
    b.clear(4, 0, 0, 11, 5, 0)
    b.clear(18, 0, 0, 25, 5, 0)
    for lx in list(range(4, 12)) + list(range(18, 26)):
        b.set(lx, 6, 0, 'iron_trapdoor[facing=north,half=top,open=false]') if False else b.set(lx, 6, 0, 'gray_concrete')
    for lz in range(3, 26, 3):
        b.set(2, 0, lz, 'anvil[facing=east]')
        b.set(2, 1, lz, 'chest[facing=east,type=single]' if False else 'barrel[facing=up]')
        b.set(27, 0, lz, 'smithing_table')
        b.set(27, 1, lz, 'grindstone[face=floor,facing=north]')
    for lz in range(4, 24, 5):
        for lx in range(12, 18):
            b.set(lx, 0, lz, 'crafting_table' if lx % 2 else 'stonecutter[facing=north]')
    for lx in range(6, 24, 6):
        for lz in range(6, 24, 6):
            b.set(lx, 8, lz, 'sea_lantern')
    # ride parts on racks
    for lx in range(5, 25, 2):
        b.set(lx, 0, 25, 'iron_block' if lx % 4 else 'cyan_concrete')
        b.set(lx, 1, 25, 'chain[axis=y]')
    # Wardrobe & costuming
    st_ward = style(wall='white_terracotta', pillar='light_gray_terracotta', trim='light_gray_concrete',
                    floor='birch_planks', ceiling='birch_planks', roof_stairs='stone_brick_stairs',
                    roof_slab='stone_brick_slab', roof_block='stone_bricks', door='birch', sign_wood='birch',
                    carpet='magenta_carpet', shelf=('white_wool', 'pink_wool', 'light_blue_wool', 'yellow_wool'))
    building(w, 366, 546, 18, 14, 'east', st_ward, floors=2, roof='hip', name='WARDROBE', kind='shop', awning=False,
             sign_lines=['', 'WARDROBE &', 'COSTUMING', ''])
    # parade float barn
    st_barn = style(wall='red_terracotta', pillar='stripped_spruce_log[axis=y]', trim='white_concrete',
                    floor='smooth_stone', ceiling='spruce_planks', roof_stairs='spruce_stairs',
                    roof_slab='spruce_slab', roof_block='spruce_planks', door='spruce', sign_wood='spruce')
    b = building(w, 366, 570, 28, 22, 'east', st_barn, floors=1, fh=10, roof='gable_z', kind='plain', awning=False,
                 sign_lines=['', 'PARADE', 'FLOAT BARN', ''])
    b.clear(6, 0, 0, 21, 7, 0)
    # a parade float inside
    for lz in range(4, 16):
        for lx in range(9, 19):
            b.set(lx, 0, lz, 'blue_concrete')
    for (x, y, z) in [(14, 1, 10), (14, 2, 10), (14, 3, 10)]:
        b.set(x, y, z, 'gold_block')
    for lz in range(5, 15):
        for lx in range(10, 18):
            if (lx + lz) % 3 == 0:
                b.set(lx, 1, lz, ['pink_wool', 'yellow_wool', 'light_blue_wool'][(lx + lz) % 3])
    b.set(14, 4, 10, 'sea_lantern')
    # maintenance vehicles (utility carts & a truck) and staff parking
    for i, z in enumerate(range(486, 600, 7)):
        if i % 3 == 2:
            continue
        _cart(w, 413, z, ['white', 'lime', 'orange', 'yellow'][i % 4])
    _truck(w, 401, 590)
    for z in range(z0 + 2, z1, 12):
        decor.lamp(w, 412, G + 1, z, 'classic')
    # dumpsters & storage
    for z in range(602, 610, 3):
        w.set(388, G + 1, z, 'green_concrete')
        w.set(389, G + 1, z, 'green_concrete')
        w.set(388, G + 2, z, 'green_carpet')
        w.set(389, G + 2, z, 'green_carpet')
    w.label(390, 545, 'Backstage', 'small')


def _cart(w, x, z, color):
    y = G + 1
    w.set(x, y, z, 'black_concrete')
    w.set(x + 1, y, z, 'black_concrete')
    w.set(x, y, z + 2, 'black_concrete')
    w.set(x + 1, y, z + 2, 'black_concrete')
    w.set(x, y, z + 1, f'{color}_concrete')
    w.set(x + 1, y, z + 1, f'{color}_concrete')
    w.set(x, y + 1, z + 1, 'quartz_stairs[facing=south,half=bottom]')
    w.set(x + 1, y + 1, z + 1, 'quartz_stairs[facing=south,half=bottom]')
    w.set(x, y + 1, z, 'iron_bars')
    w.set(x + 1, y + 1, z, 'iron_bars')
    w.set(x, y + 2, z, f'{color}_carpet' if False else 'smooth_stone_slab[type=bottom]')
    w.set(x + 1, y + 2, z, 'smooth_stone_slab[type=bottom]')


def _truck(w, x, z):
    y = G + 1
    for dz in range(9):
        for dx in range(3):
            w.set(x + dx, y, z + dz, 'black_concrete' if dz in (1, 2, 6, 7) and dx != 1 else 'gray_concrete')
    for dz in range(0, 3):
        for dx in range(3):
            w.set(x + dx, y + 1, z + dz, 'white_concrete')
            w.set(x + dx, y + 2, z + dz, 'glass' if dz == 0 else 'white_concrete')
    for dz in range(3, 9):
        for dx in range(3):
            for k in range(1, 4):
                w.set(x + dx, y + k, z + dz, 'white_concrete')
    w.sign(x + 1, y + 2, z + 9, ['WONDERHAVEN', 'Maintenance', '', ''], wood='birch', facing='south')


def build(w, paths):
    street(w, paths)
    shops(w)
    town_square(w, paths)
    gardens_west(w, paths)
    backstage(w, paths)
