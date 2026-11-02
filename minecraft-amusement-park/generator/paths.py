"""Walkway network: drawing paths with land-specific paving, and furnishing them afterwards."""
import math

import numpy as np

from .const import G, land_at
from .geom import point_seg_dist
from .world import hash2, pick
from . import decor

PAVING = {
    'hub': [('polished_andesite', 5), ('andesite', 2), ('polished_diorite', 1)],
    'mainstreet': [('smooth_stone', 6), ('polished_andesite', 2)],
    'medieval': [('stone_bricks', 5), ('cobblestone', 2), ('mossy_stone_bricks', 1), ('cracked_stone_bricks', 2),
                 ('andesite', 1)],
    'western': [('coarse_dirt', 4), ('dirt_path', 3), ('packed_mud', 3), ('rooted_dirt', 1)],
    'scifi_n': [('white_concrete', 5), ('light_gray_concrete', 3), ('smooth_quartz', 2)],
    'scifi_s': [('white_concrete', 5), ('light_gray_concrete', 3), ('smooth_quartz', 2)],
    'pirate': [('cobblestone', 4), ('mossy_cobblestone', 2), ('gravel', 2), ('andesite', 1), ('coarse_dirt', 1)],
    'tropical': [('smooth_sandstone', 4), ('sandstone', 2), ('packed_mud', 2), ('mud_bricks', 1)],
    'carnival': [('white_terracotta', 8), ('pink_terracotta', 1), ('light_blue_terracotta', 1),
                 ('yellow_terracotta', 1), ('lime_terracotta', 1)],
    'entrance': [('polished_andesite', 4), ('polished_diorite', 1), ('smooth_stone', 2)],
}
EDGE = {
    'hub': 'polished_diorite', 'mainstreet': 'polished_andesite', 'medieval': 'mossy_stone_bricks',
    'western': 'stripped_spruce_wood[axis=y]', 'scifi_n': 'cyan_concrete', 'scifi_s': 'cyan_concrete',
    'pirate': 'mossy_cobblestone', 'tropical': 'mud_bricks', 'carnival': 'red_terracotta',
    'entrance': 'polished_granite',
}
LAMP_STYLE = {
    'hub': 'hub', 'mainstreet': 'classic', 'medieval': 'medieval', 'western': 'western', 'scifi_n': 'scifi',
    'scifi_s': 'scifi', 'pirate': 'pirate', 'tropical': 'tropical', 'carnival': 'cartoon', 'entrance': 'classic',
}
BENCH_WOOD = {
    'hub': 'dark_oak', 'mainstreet': 'dark_oak', 'medieval': 'spruce', 'western': 'spruce', 'scifi_n': 'quartz',
    'scifi_s': 'quartz', 'pirate': 'dark_oak', 'tropical': 'bamboo', 'carnival': 'cherry', 'entrance': 'dark_oak',
}
TREES = {
    'hub': ['oak', 'cherry', 'birch'], 'mainstreet': ['oak', 'birch'], 'medieval': ['oak', 'dark_oak', 'spruce', 'birch'],
    'western': ['acacia', 'spruce'], 'scifi_n': ['birch', 'cone_topiary'], 'scifi_s': ['birch', 'cone_topiary'],
    'pirate': ['palm', 'oak', 'spruce'], 'tropical': ['palm', 'jungle_small', 'palm'], 'carnival': ['cherry', 'topiary', 'birch'],
    'entrance': ['oak', 'cherry'],
}


def paving(land, x, z):
    return pick(PAVING.get(land, PAVING['hub']), x, z, 3)


class Paths:
    def __init__(self, w):
        self.w = w
        self.mask = np.zeros((w.D, w.W), dtype=bool)
        self.lines = []  # (pts, width, furnish)

    def draw(self, pts, width, mat=None, furnish=True, edge=True, land=None, y=G):
        """Draw a polyline path of given width. mat: block name, callable(x,z) or None (land paving)."""
        w = self.w
        r = width / 2.0
        self.lines.append((pts, width, furnish, land))
        cells = set()
        for (ax, az), (bx, bz) in zip(pts[:-1], pts[1:]):
            x0, x1 = int(math.floor(min(ax, bx) - r - 1)), int(math.ceil(max(ax, bx) + r + 1))
            z0, z1 = int(math.floor(min(az, bz) - r - 1)), int(math.ceil(max(az, bz) + r + 1))
            for x in range(x0, x1 + 1):
                for z in range(z0, z1 + 1):
                    d, _ = point_seg_dist(x + 0.0, z + 0.0, ax, az, bx, bz)
                    if d <= r - 0.01:
                        cells.add((x, z, d >= r - 1.0))
        for (x, z, is_edge) in cells:
            if not (0 <= x < w.W and 0 <= z < w.D):
                continue
            if self.mask[z, x] and is_edge:
                continue
            ld = land or land_at(x, z) or 'hub'
            if callable(mat):
                blk = mat(x, z)
            elif mat:
                blk = mat
            else:
                blk = paving(ld, x, z)
            if edge and is_edge and not callable(mat) and not mat:
                blk = EDGE.get(ld, blk)
            w.set(x, y, z, blk)
            self.mask[z, x] = True
        # remove any gaps marked as edge where interiors overlap
        return cells

    def furnish(self, spacing=14, trees=True):
        """Place lamps, benches, bins and trees along the sides of all furnished paths."""
        w = self.w
        placed_lamps = 0
        for pts, width, furnish, land in self.lines:
            if not furnish:
                continue
            for (ax, az), (bx, bz) in zip(pts[:-1], pts[1:]):
                L = math.hypot(bx - ax, bz - az)
                if L < 4:
                    continue
                ux, uz = (bx - ax) / L, (bz - az) / L
                nx, nz = -uz, ux
                off = width / 2.0 + 0.6
                n = int(L // spacing)
                for i in range(n + 1):
                    t = (i + 0.5) * spacing
                    if t > L:
                        break
                    for side in (-1, 1):
                        px = ax + ux * t + nx * off * side
                        pz = az + uz * t + nz * off * side
                        X, Z = int(round(px)), int(round(pz))
                        ld = land or land_at(X, Z) or 'hub'
                        if not self._verge(X, Z):
                            continue
                        arm = 'x' if abs(ux) > abs(uz) else 'z'
                        if decor.lamp(w, X, G + 1, Z, LAMP_STYLE.get(ld, 'classic'), arm=arm):
                            placed_lamps += 1
                        # bench halfway to the next lamp, facing the path
                        bt = t + spacing / 2.0
                        if bt < L - 2:
                            bx_, bz_ = ax + ux * bt + nx * off * side, az + uz * bt + nz * off * side
                            BX, BZ = int(round(bx_)), int(round(bz_))
                            look = _dir(-nx * side, -nz * side)
                            if self._verge(BX, BZ) and hash2(BX, BZ, 5) < 0.75:
                                ok = decor.bench(w, BX, G + 1, BZ, look, BENCH_WOOD.get(ld, 'dark_oak'), 3)
                                if ok and hash2(BX, BZ, 6) < 0.6:
                                    lx, lz = _left_of(look)
                                    decor.trash_can(w, BX + lx * 4, G + 1, BZ + lz * 4, LAMP_STYLE.get(ld, 'classic'))
                        # trees further out
                        if trees:
                            tt = t + spacing * 0.25
                            tx = ax + ux * tt + nx * (off + 3.5) * side
                            tz = az + uz * tt + nz * (off + 3.5) * side
                            TX, TZ = int(round(tx)), int(round(tz))
                            if self._tree_ok(TX, TZ) and hash2(TX, TZ, 8) < 0.85:
                                kinds = TREES.get(ld, ['oak'])
                                k = kinds[int(hash2(TX, TZ, 9) * len(kinds)) % len(kinds)]
                                decor.tree(w, TX, G + 1, TZ, k, 0.85 + hash2(TX, TZ, 10) * 0.35)
        return placed_lamps

    def _verge(self, x, z):
        w = self.w
        if not (2 <= x < w.W - 2 and 2 <= z < w.D - 2):
            return False
        if self.mask[z, x]:
            return False
        if w.name(x, G, z) not in ('grass_block',):
            return False
        for dy in range(1, 6):
            if not w.is_air(x, G + dy, z):
                return False
        return True

    def _tree_ok(self, x, z):
        w = self.w
        for dx in range(-2, 3):
            for dz in range(-2, 3):
                X, Z = x + dx, z + dz
                if not (0 <= X < w.W and 0 <= Z < w.D) or self.mask[Z, X]:
                    return False
                if w.name(X, G, Z) != 'grass_block':
                    return False
                for dy in range(1, 9):
                    if not w.is_air(X, G + dy, Z):
                        return False
        return True


def _dir(dx, dz):
    if abs(dx) >= abs(dz):
        return 'east' if dx > 0 else 'west'
    return 'south' if dz > 0 else 'north'


def _left_of(look):
    # bench extends to the viewer's right; return direction vector pointing past its right end? use right
    from .world import DVEC
    ldx, ldz = DVEC[look]
    return (-ldz, ldx)
