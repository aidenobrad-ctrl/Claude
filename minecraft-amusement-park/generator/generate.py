"""Build the whole Wonderhaven park and export it as Litematica schematics + overview maps.

Usage:  python3 -m generator.generate [--only hub,mainstreet] [--out output] [--no-export]
"""
import argparse
import os
import time

from .const import G, W, H, D, LANDS, LAND_NAMES
from .world import World
from .paths import Paths
from .post import postprocess, fix_support


def build_park(only=None, log=print):
    t0 = time.time()
    w = World(W, H, D)
    paths = Paths(w)
    from .lands import base
    base.ground(w)
    base.perimeter(w)
    order = ['hub', 'mainstreet', 'entrance', 'medieval', 'scifi', 'western', 'pirate', 'tropical', 'carnival']
    for name in order:
        if only and name not in only:
            continue
        mod = __import__(f'generator.lands.{name}', fromlist=['build'])
        t = time.time()
        mod.build(w, paths)
        log(f'  built {name:<11} {time.time() - t:6.1f}s  palette={len(w.pal)}')
    t = time.time()
    n = paths.furnish()
    log(f'  furnished paths: {n} lamps ({time.time() - t:.1f}s)')
    fix_support(w, log)
    postprocess(w, log)
    log(f'  total build time {time.time() - t0:.1f}s')
    return w


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--only', default='')
    ap.add_argument('--out', default='output')
    ap.add_argument('--no-export', action='store_true')
    ap.add_argument('--iso', action='store_true')
    args = ap.parse_args()
    only = set(args.only.split(',')) if args.only else None
    w = build_park(only)
    os.makedirs(args.out, exist_ok=True)
    from . import export
    export.export_all(w, args.out, do_schematics=not args.no_export, iso=args.iso)


if __name__ == '__main__':
    main()
