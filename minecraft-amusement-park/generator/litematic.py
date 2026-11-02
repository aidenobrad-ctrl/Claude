"""Litematica (.litematic) exporter.

Format notes (Litematica schematic version 6):
  root{ MinecraftDataVersion:int, Version:int, SubVersion:int, Metadata{...}, Regions{ name{...} } }
  region{ Position{x,y,z}, Size{x,y,z}, BlockStatePalette:[{Name, Properties{}}], BlockStates:long[],
          TileEntities:[], Entities:[], PendingBlockTicks:[], PendingFluidTicks:[] }
  Block index = (y * sizeZ + z) * sizeX + x, packed with bits = max(2, bitlen(paletteSize-1)) per entry,
  little-endian bit order, entries may span two longs (LitematicaBitArray).
"""
import time

import numpy as np

from .nbt import IntArray, List, LongArray, TAG_COMPOUND, write_gzip
from .world import parse_state

DATA_VERSION = 3465  # Minecraft 1.20.1 -- newer Litematica versions upgrade it automatically
SCHEMATIC_VERSION = 6
SCHEMATIC_SUBVERSION = 1


def pack_bits(values, bits):
    """Pack an array of small unsigned ints the way LitematicaBitArray stores them."""
    values = np.ascontiguousarray(values.reshape(-1))
    n = values.size
    nlongs = (n * bits + 63) // 64
    out = np.zeros(nlongs, dtype=np.uint64)
    chunk = 64 * 32768
    shifts = np.arange(bits, dtype=np.uint32)
    for s in range(0, n, chunk):
        v = values[s:s + chunk].astype(np.uint32)
        bm = ((v[:, None] >> shifts) & 1).astype(np.uint8).reshape(-1)
        pad = (-bm.size) % 64
        if pad:
            bm = np.concatenate([bm, np.zeros(pad, np.uint8)])
        longs = np.packbits(bm, bitorder='little').view('<u8')
        start = (s * bits) // 64
        out[start:start + longs.size] = longs
    return out.view(np.int64)


def unpack_bits(longs, bits, n):
    """Inverse of pack_bits (used for self-verification)."""
    raw = np.asarray(longs, dtype=np.int64).view(np.uint64).astype('<u8').view(np.uint8)
    bm = np.unpackbits(raw, bitorder='little')[: n * bits].reshape(n, bits).astype(np.uint32)
    return (bm << np.arange(bits, dtype=np.uint32)).sum(axis=1)


def _pos(x, y, z):
    return {'x': int(x), 'y': int(y), 'z': int(z)}


def bbox_nonair(sub):
    nz = np.nonzero(sub)
    if nz[0].size == 0:
        return None
    return (nz[0].min(), nz[0].max(), nz[1].min(), nz[1].max(), nz[2].min(), nz[2].max())


def build_region(world, x0, y0, z0, x1, y1, z1, trim=False):
    """Return (region_min(x,y,z), size(x,y,z), region nbt dict, non-air count) for an inclusive world box."""
    sub = world.a[y0:y1 + 1, z0:z1 + 1, x0:x1 + 1]
    if trim:
        bb = bbox_nonair(sub)
        if bb is None:
            return None
        ya, yb, za, zb, xa, xb = bb
        sub = sub[ya:yb + 1, za:zb + 1, xa:xb + 1]
        x0, y0, z0 = int(x0 + xa), int(y0 + ya), int(z0 + za)
    sy, sz, sx = (int(v) for v in sub.shape)
    used = np.unique(sub)
    if used[0] != 0:
        used = np.concatenate([[0], used])
    remap = np.zeros(len(world.pal), dtype=np.uint32)
    remap[used] = np.arange(len(used), dtype=np.uint32)
    local = remap[sub]
    bits = max(2, int(len(used) - 1).bit_length())
    packed = pack_bits(local, bits)

    palette = []
    for gid in used:
        name, props = parse_state(world.pal[gid])
        e = {'Name': name}
        if props:
            e['Properties'] = dict(props)
        palette.append(e)

    tiles = []
    for (tx, ty, tz), tag in world.tiles.items():
        if x0 <= tx < x0 + sx and y0 <= ty < y0 + sy and z0 <= tz < z0 + sz:
            if world.a[ty, tz, tx] == 0:
                continue
            t = dict(tag)
            t['x'], t['y'], t['z'] = tx - x0, ty - y0, tz - z0
            tiles.append(t)

    region = {
        'Position': None,  # filled by caller
        'Size': _pos(sx, sy, sz),
        'BlockStatePalette': List(TAG_COMPOUND, palette),
        'BlockStates': LongArray(packed),
        'TileEntities': List(TAG_COMPOUND, tiles),
        'Entities': List(TAG_COMPOUND, []),
        'PendingBlockTicks': List(TAG_COMPOUND, []),
        'PendingFluidTicks': List(TAG_COMPOUND, []),
    }
    count = int(np.count_nonzero(sub))
    return (x0, y0, z0), (sx, sy, sz), region, count


def write_litematic(world, path, boxes, name, author, description, preview=None, trim=False):
    """boxes: list of (region_name, x0, y0, z0, x1, y1, z1) inclusive world-space boxes."""
    built = []
    for rname, x0, y0, z0, x1, y1, z1 in boxes:
        r = build_region(world, x0, y0, z0, x1, y1, z1, trim=trim)
        if r is not None:
            built.append((rname,) + r)
    if not built:
        raise ValueError('nothing to export')
    gx = min(b[1][0] for b in built)
    gy = min(b[1][1] for b in built)
    gz = min(b[1][2] for b in built)
    ex = max(b[1][0] + b[2][0] for b in built) - gx
    ey = max(b[1][1] + b[2][1] for b in built) - gy
    ez = max(b[1][2] + b[2][2] for b in built) - gz
    regions = {}
    total_blocks = 0
    total_volume = 0
    for rname, (rx, ry, rz), (sx, sy, sz), region, count in built:
        region['Position'] = _pos(rx - gx, ry - gy, rz - gz)
        regions[rname] = region
        total_blocks += count
        total_volume += sx * sy * sz
    now = int(time.time() * 1000)
    meta = {
        'Name': name,
        'Author': author,
        'Description': description,
        'RegionCount': len(regions),
        'TotalVolume': int(total_volume),
        'TotalBlocks': int(total_blocks),
        'TimeCreated': _long(now),
        'TimeModified': _long(now),
        'EnclosingSize': _pos(ex, ey, ez),
    }
    if preview is not None:
        meta['PreviewImageData'] = IntArray(preview)
    root = {
        'MinecraftDataVersion': DATA_VERSION,
        'Version': SCHEMATIC_VERSION,
        'SubVersion': SCHEMATIC_SUBVERSION,
        'Metadata': meta,
        'Regions': regions,
    }
    write_gzip(path, root)
    return {'origin': (gx, gy, gz), 'size': (ex, ey, ez), 'blocks': total_blocks, 'volume': total_volume,
            'regions': [(b[0], b[1], b[2]) for b in built]}


def _long(v):
    from .nbt import Long
    return Long(v)
