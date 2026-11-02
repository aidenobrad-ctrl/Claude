"""Voxel world: a dense block grid with a palette, plus a rotatable local builder."""
import json
import math

import numpy as np

DIRS = ['north', 'east', 'south', 'west']
DVEC = {'north': (0, -1), 'east': (1, 0), 'south': (0, 1), 'west': (-1, 0)}
OPP = {'north': 'south', 'south': 'north', 'east': 'west', 'west': 'east', 'up': 'down', 'down': 'up'}


def parse_state(s):
    """'minecraft:oak_stairs[facing=north]' -> ('minecraft:oak_stairs', {'facing': 'north'})"""
    if '[' in s:
        name, rest = s.split('[', 1)
        rest = rest.rstrip(']')
        props = {}
        for kv in rest.split(','):
            if kv:
                k, v = kv.split('=')
                props[k.strip()] = v.strip()
    else:
        name, props = s, {}
    name = name.strip()
    if ':' not in name:
        name = 'minecraft:' + name
    return name, props


def make_state(name, props):
    if ':' not in name:
        name = 'minecraft:' + name
    if not props:
        return name
    return name + '[' + ','.join(f'{k}={props[k]}' for k in sorted(props)) + ']'


_norm_cache = {}


def norm(s):
    r = _norm_cache.get(s)
    if r is None:
        n, p = parse_state(s)
        r = make_state(n, p)
        _norm_cache[s] = r
    return r


def base_name(s):
    n = s.split('[', 1)[0]
    return n[10:] if n.startswith('minecraft:') else n


_RAIL_ROT = {
    'north_south': 'east_west', 'east_west': 'north_south',
    'ascending_north': 'ascending_east', 'ascending_east': 'ascending_south',
    'ascending_south': 'ascending_west', 'ascending_west': 'ascending_north',
    'north_east': 'south_east', 'south_east': 'south_west', 'south_west': 'north_west', 'north_west': 'north_east',
}

_rot_cache = {}


def rotate_state(s, rot):
    """Rotate a block state clockwise (seen from above) by rot * 90 degrees."""
    rot %= 4
    if rot == 0 or '[' not in s:
        return s
    key = (s, rot)
    r = _rot_cache.get(key)
    if r is not None:
        return r
    name, props = parse_state(s)
    np_ = dict(props)
    for k, v in props.items():
        if k == 'facing' and v in DIRS:
            np_[k] = DIRS[(DIRS.index(v) + rot) % 4]
        elif k == 'axis' and rot % 2 == 1 and v in ('x', 'z'):
            np_[k] = 'z' if v == 'x' else 'x'
        elif k == 'rotation':
            np_[k] = str((int(v) + 4 * rot) % 16)
        elif k == 'shape' and 'rail' in name:
            vv = v
            for _ in range(rot):
                vv = _RAIL_ROT.get(vv, vv)
            np_[k] = vv
    # connection-style properties keyed by direction
    if any(d in props for d in DIRS) and 'rail' not in name:
        for d in DIRS:
            if d in props:
                np_[DIRS[(DIRS.index(d) + rot) % 4]] = props[d]
    r = make_state(name, np_)
    _rot_cache[key] = r
    return r


class World:
    def __init__(self, W, H, D):
        self.W, self.H, self.D = W, H, D
        self.a = np.zeros((H, D, W), dtype=np.uint16)
        self.pal = ['minecraft:air']
        self.pi = {'minecraft:air': 0}
        self._ids = {'air': 0, 'minecraft:air': 0}
        self.tiles = {}
        self.labels = []  # (x, z, text, kind) for the overview map

    # ---- palette -------------------------------------------------------
    def id(self, s):
        i = self._ids.get(s)
        if i is None:
            n = norm(s)
            i = self.pi.get(n)
            if i is None:
                i = len(self.pal)
                self.pal.append(n)
                self.pi[n] = i
            self._ids[s] = i
        return i

    # ---- access --------------------------------------------------------
    def inb(self, x, y, z):
        return 0 <= x < self.W and 0 <= y < self.H and 0 <= z < self.D

    def set(self, x, y, z, s):
        if 0 <= x < self.W and 0 <= y < self.H and 0 <= z < self.D:
            self.a[y, z, x] = self.id(s)
            if self.tiles and (x, y, z) in self.tiles:
                del self.tiles[(x, y, z)]

    def set_if_air(self, x, y, z, s):
        if 0 <= x < self.W and 0 <= y < self.H and 0 <= z < self.D and self.a[y, z, x] == 0:
            self.a[y, z, x] = self.id(s)
            return True
        return False

    def get(self, x, y, z):
        if 0 <= x < self.W and 0 <= y < self.H and 0 <= z < self.D:
            return self.pal[self.a[y, z, x]]
        return 'minecraft:air'

    def name(self, x, y, z):
        return base_name(self.get(x, y, z))

    def is_air(self, x, y, z):
        if 0 <= x < self.W and 0 <= y < self.H and 0 <= z < self.D:
            return self.a[y, z, x] == 0
        return True

    def fill(self, x0, y0, z0, x1, y1, z1, s):
        if x0 > x1:
            x0, x1 = x1, x0
        if y0 > y1:
            y0, y1 = y1, y0
        if z0 > z1:
            z0, z1 = z1, z0
        x0, y0, z0 = max(0, x0), max(0, y0), max(0, z0)
        x1, y1, z1 = min(self.W - 1, x1), min(self.H - 1, y1), min(self.D - 1, z1)
        if x0 > x1 or y0 > y1 or z0 > z1:
            return
        self.a[y0:y1 + 1, z0:z1 + 1, x0:x1 + 1] = self.id(s)
        if self.tiles:
            for k in [k for k in self.tiles if x0 <= k[0] <= x1 and y0 <= k[1] <= y1 and z0 <= k[2] <= z1]:
                del self.tiles[k]

    def clear(self, x0, y0, z0, x1, y1, z1):
        self.fill(x0, y0, z0, x1, y1, z1, 'air')

    def replace(self, x0, y0, z0, x1, y1, z1, old, new):
        if x0 > x1:
            x0, x1 = x1, x0
        if y0 > y1:
            y0, y1 = y1, y0
        if z0 > z1:
            z0, z1 = z1, z0
        sub = self.a[y0:y1 + 1, z0:z1 + 1, x0:x1 + 1]
        sub[sub == self.id(old)] = self.id(new)

    def box_air(self, x0, y0, z0, x1, y1, z1):
        if x0 > x1:
            x0, x1 = x1, x0
        if y0 > y1:
            y0, y1 = y1, y0
        if z0 > z1:
            z0, z1 = z1, z0
        if x0 < 0 or z0 < 0 or x1 >= self.W or z1 >= self.D:
            return False
        return not self.a[y0:y1 + 1, z0:z1 + 1, x0:x1 + 1].any()

    def label(self, x, z, text, kind='attraction'):
        self.labels.append((x, z, text, kind))

    def door(self, x, y, z, facing, wood='oak', hinge='left'):
        door(self, x, y, z, facing, wood, hinge)

    # ---- tile entities ---------------------------------------------------
    def sign(self, x, y, z, lines, wood='oak', facing=None, rotation=None, color='black', glow=False,
             hanging=False, wall=True):
        """Place a sign with text. facing => wall sign facing that way; rotation => standing sign."""
        if hanging:
            if facing:
                st = f'{wood}_wall_hanging_sign[facing={facing}]'
            else:
                st = f'{wood}_hanging_sign[rotation={rotation or 0},attached=false]'
            tid = 'minecraft:hanging_sign'
        elif facing is not None:
            st = f'{wood}_wall_sign[facing={facing}]'
            tid = 'minecraft:sign'
        else:
            st = f'{wood}_sign[rotation={rotation or 0}]'
            tid = 'minecraft:sign'
        self.set(x, y, z, st)
        self.tiles[(x, y, z)] = sign_nbt(tid, lines, color, glow)


def sign_nbt(tid, lines, color='black', glow=False):
    from .nbt import Byte, List, TAG_STRING
    lines = list(lines)[:4] + [''] * (4 - len(lines[:4]))
    msgs = [json.dumps({'text': l}) for l in lines]
    empty = [json.dumps({'text': ''})] * 4
    return {
        'id': tid,
        'is_waxed': Byte(1),
        'front_text': {'color': color, 'has_glowing_text': Byte(1 if glow else 0),
                       'messages': List(TAG_STRING, msgs)},
        'back_text': {'color': color, 'has_glowing_text': Byte(1 if glow else 0),
                      'messages': List(TAG_STRING, msgs if lines else empty)},
    }


ROT_OF = {'north': 0, 'east': 1, 'south': 2, 'west': 3}


class Builder:
    """Local frame: x = right, y = up, z = backward. The front faces local north (-z).

    rot: 0 front faces north, 1 east, 2 south, 3 west.
    """

    def __init__(self, w, ox, oy, oz, rot=0):
        self.w, self.ox, self.oy, self.oz, self.rot = w, ox, oy, oz, rot % 4

    @classmethod
    def for_rect(cls, w, x0, z0, width, depth, facing, y):
        """Builder whose local (0..width-1, 0..depth-1) footprint maps into a world rect starting at x0,z0."""
        r = ROT_OF[facing] if isinstance(facing, str) else facing % 4
        if r == 0:
            ox, oz = x0, z0
        elif r == 1:
            ox, oz = x0 + depth - 1, z0
        elif r == 2:
            ox, oz = x0 + width - 1, z0 + depth - 1
        else:
            ox, oz = x0, z0 + width - 1
        return cls(w, ox, y, oz, r)

    @staticmethod
    def world_dims(width, depth, facing):
        r = ROT_OF[facing] if isinstance(facing, str) else facing % 4
        return (width, depth) if r % 2 == 0 else (depth, width)

    def xz(self, lx, lz):
        r = self.rot
        if r == 0:
            return self.ox + lx, self.oz + lz
        if r == 1:
            return self.ox - lz, self.oz + lx
        if r == 2:
            return self.ox - lx, self.oz - lz
        return self.ox + lz, self.oz - lx

    def pos(self, lx, ly, lz):
        x, z = self.xz(lx, lz)
        return x, self.oy + ly, z

    def d(self, local_dir):
        """Convert a local direction name to world direction name."""
        if local_dir in DIRS:
            return DIRS[(DIRS.index(local_dir) + self.rot) % 4]
        return local_dir

    def rs(self, s):
        return rotate_state(s, self.rot)

    def set(self, lx, ly, lz, s):
        x, z = self.xz(lx, lz)
        self.w.set(x, self.oy + ly, z, rotate_state(s, self.rot))

    def set_if_air(self, lx, ly, lz, s):
        x, z = self.xz(lx, lz)
        return self.w.set_if_air(x, self.oy + ly, z, rotate_state(s, self.rot))

    def get(self, lx, ly, lz):
        x, z = self.xz(lx, lz)
        return self.w.get(x, self.oy + ly, z)

    def is_air(self, lx, ly, lz):
        x, z = self.xz(lx, lz)
        return self.w.is_air(x, self.oy + ly, z)

    def fill(self, lx0, ly0, lz0, lx1, ly1, lz1, s):
        x0, z0 = self.xz(lx0, lz0)
        x1, z1 = self.xz(lx1, lz1)
        self.w.fill(x0, self.oy + ly0, z0, x1, self.oy + ly1, z1, rotate_state(s, self.rot))

    def clear(self, lx0, ly0, lz0, lx1, ly1, lz1):
        self.fill(lx0, ly0, lz0, lx1, ly1, lz1, 'air')

    def sign(self, lx, ly, lz, lines, wood='oak', facing=None, rotation=None, **kw):
        x, z = self.xz(lx, lz)
        f = self.d(facing) if facing else None
        rot = None if rotation is None else (rotation + 4 * self.rot) % 16
        self.w.sign(x, self.oy + ly, z, lines, wood=wood, facing=f, rotation=rot, **kw)

    def door(self, lx, ly, lz, facing, wood='oak', hinge='left', open_=False):
        """Two-block door; facing in local directions."""
        f = self.d(facing)
        x, z = self.xz(lx, lz)
        y = self.oy + ly
        o = 'true' if open_ else 'false'
        self.w.set(x, y, z, f'{wood}_door[facing={f},half=lower,hinge={hinge},open={o},powered=false]')
        self.w.set(x, y + 1, z, f'{wood}_door[facing={f},half=upper,hinge={hinge},open={o},powered=false]')

    def double_door(self, lx, ly, lz, facing, wood='oak', open_=False):
        """Two doors side by side at lx and lx+1 (local), opening as a pair."""
        self.door(lx, ly, lz, facing, wood, 'left', open_)
        self.door(lx + 1, ly, lz, facing, wood, 'right', open_)


def door(w, x, y, z, facing, wood='oak', hinge='left', open_=False):
    o = 'true' if open_ else 'false'
    w.set(x, y, z, f'{wood}_door[facing={facing},half=lower,hinge={hinge},open={o},powered=false]')
    w.set(x, y + 1, z, f'{wood}_door[facing={facing},half=upper,hinge={hinge},open={o},powered=false]')


def bed(w, x, y, z, facing, color='red'):
    """Bed whose head is toward `facing` from the foot at (x,y,z)."""
    dx, dz = DVEC[facing]
    w.set(x, y, z, f'{color}_bed[facing={facing},part=foot,occupied=false]')
    w.set(x + dx, y, z + dz, f'{color}_bed[facing={facing},part=head,occupied=false]')


def hash2(x, z, seed=0):
    """Deterministic pseudo-random float in [0,1) for a column."""
    h = (x * 374761393 + z * 668265263 + seed * 2147483647) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    h ^= h >> 16
    return (h & 0xFFFFFF) / float(0x1000000)


def hash3(x, y, z, seed=0):
    return hash2(x * 31 + y * 1013, z * 17 - y * 7919, seed)


def pick(options, x, z, seed=0):
    """Weighted pick from [(item, weight), ...] or plain list, deterministic per column."""
    r = hash2(x, z, seed)
    if isinstance(options[0], tuple):
        tot = sum(wt for _, wt in options)
        acc = 0.0
        for it, wt in options:
            acc += wt / tot
            if r < acc:
                return it
        return options[-1][0]
    return options[int(r * len(options)) % len(options)]


def dist(a, b):
    return math.sqrt(sum((p - q) ** 2 for p, q in zip(a, b)))
