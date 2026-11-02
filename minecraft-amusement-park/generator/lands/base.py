"""Ground plane, perimeter wall, service road and landscaped berm."""
from ..const import G, W, D
from ..world import hash2
from .. import decor

BERM_PROFILE = [1, 2, 3, 4, 4, 4, 3, 2, 1]


def ground(w):
    w.fill(0, G, 0, W - 1, G, D - 1, 'grass_block')


def _berm_column(w, x, z, h):
    for k in range(1, h):
        w.set(x, G + k, z, 'dirt')
    if h >= 1:
        w.set(x, G + h, z, 'grass_block')


def perimeter(w, gate_x0=262, gate_x1=378, service_gate=(396, 412)):
    """Wall + service road + berm on all four sides of the park (lands are x,z in 20..619)."""
    # --- north side (z grows inward) ---------------------------------------------------------
    def strip(coords_fn, length, wall_idx=3, road=(4, 10), berm=(11, 19), skip=lambda i: False, line_idx=7):
        for i in range(length):
            if skip(i):
                continue
            # wall
            x, z = coords_fn(i, wall_idx)
            pillar = i % 10 == 0
            for k in range(1, 5):
                w.set(x, G + k, z, 'stone_bricks' if not pillar else 'chiseled_stone_bricks')
            w.set(x, G + 5, z, 'stone_brick_slab[type=bottom]' if not pillar else 'chiseled_stone_bricks')
            if pillar:
                w.set(x, G + 6, z, 'lantern')
            # road
            for j in range(road[0], road[1] + 1):
                x, z = coords_fn(i, j)
                blk = 'gray_concrete'
                if j == line_idx and (i // 3) % 2 == 0:
                    blk = 'yellow_concrete'
                if j in (road[0], road[1]):
                    blk = 'light_gray_concrete'
                w.set(x, G, z, blk)
            # berm
            for idx, j in enumerate(range(berm[0], berm[1] + 1)):
                x, z = coords_fn(i, j)
                h = BERM_PROFILE[idx]
                _berm_column(w, x, z, h)

    # north: i = x, j = z
    strip(lambda i, j: (i, j), W)
    # south: berm next to the park (z 620..628), road 629..635, wall at 636
    strip(lambda i, j: (i, 639 - j), W, skip=lambda i: gate_x0 <= i <= gate_x1 or
          (service_gate[0] <= i <= service_gate[1]))
    # west: i = z (20..639), j = x
    strip(lambda i, j: (j, i), 640, skip=lambda i: i < 0)
    # east
    strip(lambda i, j: (639 - j, i), 640)
    # service gate through the south berm (road access to the backstage area)
    sx0, sx1 = service_gate
    for x in range(sx0, sx1 + 1):
        for z in range(620, 637):
            w.clear(x, G + 1, z, x, G + 6, z)
            w.set(x, G, z, 'gray_concrete' if 2 < x - sx0 < sx1 - sx0 - 2 else 'light_gray_concrete')
    for z in range(620, 629):
        for x in (sx0 - 1, sx1 + 1):
            for k in range(1, 4):
                w.set(x, G + k, z, 'stone_bricks')
    for x in range(sx0, sx1 + 1):
        w.set(x, G + 1, 636, 'spruce_fence_gate[facing=south,open=false,in_wall=false]' if x % 2 else 'spruce_fence')
    w.set(sx0 - 1, G + 4, 637, 'lantern')
    w.set(sx1 + 1, G + 4, 637, 'lantern')
    w.sign(sx0 - 1, G + 2, 637, ['SERVICE GATE', 'Authorized', 'Vehicles Only', ''], wood='spruce', facing='south')

    # trees on the berm tops
    for i in range(0, 640, 6):
        for (x, z) in ((i, 15), (i, 624), (15, i), (624, i)):
            if 20 <= x <= 619 or 20 <= z <= 619 or True:
                if w.name(x, G + 4, z) == 'grass_block' and w.is_air(x, G + 5, z):
                    r = hash2(x, z, 31)
                    kind = 'spruce' if r < 0.35 else 'oak' if r < 0.7 else 'dark_oak' if r < 0.8 else 'birch'
                    if kind == 'dark_oak':
                        kind = 'oak'
                    decor.tree(w, x + int(hash2(z, x, 2) * 3) - 1, G + 5, z, kind, 0.9 + r * 0.3)
    # bushes on lower berm slopes
    for i in range(0, 640, 3):
        for (x, z) in ((i, 12), (i, 627), (12, i), (627, i)):
            if w.name(x, G + 2, z) == 'grass_block' and w.is_air(x, G + 3, z) and hash2(x, z, 7) < 0.5:
                w.set(x, G + 3, z, decor.leaves('azalea' if hash2(x, z, 8) < 0.5 else 'flowering_azalea'))
