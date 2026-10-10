// Where terrain tiles and tree cells come from: Web Workers each running
// their own copy of the world (normal play), or the main thread (tests, and
// browsers that refuse workers). Both paths run the same deterministic code.
// Requests are routed by area, so a worker's tile cache serves both the
// terrain and the trees of the same neighbourhood.
import type { World } from '../world';
import { WORLD_HALF } from '../island';
import { buildTerrainTile, buildTreeCell, terrainRaster, T_LEAF, VEG_CELL, type TerrainTileData } from '../terrain-data';

type TerrainCb = (d: TerrainTileData) => void;
type TreesCb = (t: Float32Array) => void;

export class TileSource {
  private workers: Worker[] = [];
  private nextId = 1;
  private terrainCbs = new Map<number, { cb: TerrainCb; level: number; ix: number; iz: number }>();
  private treeCbs = new Map<number, { cb: TreesCb; cx: number; cz: number; keep: number }>();
  /** Requests sent and not answered yet, by kind. */
  terrainInFlight = 0;
  treesInFlight = 0;
  /** Milliseconds of main-thread work spent building tiles synchronously. */
  syncMs = 0;

  constructor(
    private world: World,
    useWorker: boolean,
    count = 2,
  ) {
    if (!useWorker || typeof Worker === 'undefined' || !__TILE_WORKER__) return;
    try {
      const url = URL.createObjectURL(new Blob([__TILE_WORKER__], { type: 'text/javascript' }));
      for (let i = 0; i < Math.max(1, count); i++) {
        const w = new Worker(url);
        w.onmessage = (e: MessageEvent) => this.onMessage(e.data);
        w.onerror = () => this.fail();
        w.postMessage({ type: 'init', id: 0, seed: world.island.seed });
        this.workers.push(w);
      }
    } catch {
      this.fail();
    }
  }

  /** A worker died or could not start: build on the main thread from now on. */
  private fail(): void {
    for (const w of this.workers) w.terminate();
    this.workers = [];
    this.terrainInFlight = 0;
    this.treesInFlight = 0;
    // Answer outstanding requests synchronously so nothing waits forever.
    const terrain = [...this.terrainCbs.values()];
    const trees = [...this.treeCbs.values()];
    this.terrainCbs.clear();
    this.treeCbs.clear();
    for (const r of terrain) this.terrain(r.level, r.ix, r.iz, r.cb, true);
    for (const r of trees) this.trees(r.cx, r.cz, r.keep, r.cb, true);
  }

  get async(): boolean {
    return this.workers.length > 0;
  }

  get workerCount(): number {
    return this.workers.length;
  }

  /** Worker for the 512 m block containing world point (x, z). */
  private route(x: number, z: number): Worker {
    const bx = Math.floor(x / 512);
    const bz = Math.floor(z / 512);
    const h = ((bx * 73856093) ^ (bz * 19349663)) >>> 0;
    return this.workers[h % this.workers.length];
  }

  terrain(level: number, ix: number, iz: number, cb: TerrainCb, sync = false): void {
    if (!this.async || sync) {
      const t0 = performance.now();
      cb(buildTerrainTile(this.world, level, ix, iz));
      this.syncMs += performance.now() - t0;
      return;
    }
    const id = this.nextId++;
    this.terrainCbs.set(id, { cb, level, ix, iz });
    this.terrainInFlight++;
    const size = T_LEAF * (1 << level);
    this.route(-WORLD_HALF + ix * size + 1, -WORLD_HALF + iz * size + 1).postMessage({ type: 'terrain', id, level, ix, iz });
  }

  trees(cx: number, cz: number, keep: number, cb: TreesCb, sync = false): void {
    if (!this.async || sync) {
      const t0 = performance.now();
      cb(buildTreeCell(this.world, cx, cz, keep));
      this.syncMs += performance.now() - t0;
      return;
    }
    const id = this.nextId++;
    this.treeCbs.set(id, { cb, cx, cz, keep });
    this.treesInFlight++;
    this.route(cx * VEG_CELL + 1, cz * VEG_CELL + 1).postMessage({ type: 'trees', id, cx, cz, keep });
  }

  private mapCbs = new Map<number, (px: Uint8ClampedArray, n: number) => void>();

  /** Island map raster (n x n RGBA), from a worker when there is one. */
  mapRaster(n: number, cb: (px: Uint8ClampedArray, n: number) => void, sync = false): void {
    if (!this.async || sync) {
      cb(terrainRaster(this.world, n), n);
      return;
    }
    const id = this.nextId++;
    this.mapCbs.set(id, cb);
    this.workers[0].postMessage({ type: 'map', id, level: n });
  }

  private onMessage(m: { type: string; id: number; data?: TerrainTileData; trees?: Float32Array; px?: Uint8ClampedArray; n?: number }): void {
    if (m.type === 'map') {
      const cb = this.mapCbs.get(m.id);
      this.mapCbs.delete(m.id);
      if (cb && m.px) cb(m.px, m.n ?? 0);
      return;
    }
    if (m.type === 'terrain') {
      this.terrainInFlight--;
      const r = this.terrainCbs.get(m.id);
      this.terrainCbs.delete(m.id);
      if (r && m.data) r.cb(m.data);
    } else if (m.type === 'trees') {
      this.treesInFlight--;
      const r = this.treeCbs.get(m.id);
      this.treeCbs.delete(m.id);
      if (r && m.trees) r.cb(m.trees);
    }
  }

  dispose(): void {
    for (const w of this.workers) w.terminate();
    this.workers = [];
  }
}
