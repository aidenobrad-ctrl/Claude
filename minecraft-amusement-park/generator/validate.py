"""Validate every block state in the world against the Minecraft 1.20.1 block registry,
and round-trip the exported .litematic files through an independent reader when available."""
import json
import os

import numpy as np

from .world import parse_state

_REG = None


def registry():
    global _REG
    if _REG is None:
        with open(os.path.join(os.path.dirname(__file__), 'data', 'blocks_1.20.1.json')) as f:
            _REG = json.load(f)
    return _REG


def check_palette(world):
    reg = registry()
    used = set(np.unique(world.a).tolist())
    problems = []
    for i, s in enumerate(world.pal):
        if i not in used:
            continue
        name, props = parse_state(s)
        b = name.split(':', 1)[1]
        if b not in reg:
            problems.append(f'unknown block: {s}')
            continue
        for k, v in props.items():
            if k not in reg[b]:
                problems.append(f'unknown property {k} on {s}')
            elif v not in reg[b][k]:
                problems.append(f'bad value {k}={v} on {s}')
    return problems


def check_tiles(world):
    bad = []
    for (x, y, z), tag in world.tiles.items():
        b = world.name(x, y, z)
        if tag.get('id') in ('minecraft:sign',) and not b.endswith('_sign'):
            bad.append((x, y, z, b))
        if tag.get('id') == 'minecraft:hanging_sign' and 'hanging_sign' not in b:
            bad.append((x, y, z, b))
    return bad


def roundtrip(world, path, origin, samples=40000, seed=1):
    """Load a written file with litemapy (if installed) and compare random blocks with the world."""
    try:
        from litemapy import Schematic
    except ImportError:
        return 'litemapy not installed - skipped'
    s = Schematic.load(path)
    rng = np.random.default_rng(seed)
    checked = bad = 0
    gx, gy, gz = origin
    for rname, reg in s.regions.items():
        sx, sy, sz = reg.width, reg.height, reg.length
        n = samples // max(1, len(s.regions))
        # bias half the samples to non-air cells
        for k in range(n):
            x, y, z = int(rng.integers(0, sx)), int(rng.integers(0, sy)), int(rng.integers(0, sz))
            got = reg[x, y, z].to_block_state_identifier()
            exp = world.get(gx + reg.x + x, gy + reg.y + y, gz + reg.z + z)
            checked += 1
            if parse_state(got) != parse_state(exp):
                bad += 1
                if bad < 5:
                    print('   mismatch', rname, (x, y, z), got, exp)
    if bad:
        raise AssertionError(f'{bad} mismatching blocks in {path}')
    return f'{len(s.regions)} regions, {checked} random blocks read back identical'
