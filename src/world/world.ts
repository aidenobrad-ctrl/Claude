// The island at runtime: the road network with vertical profiles, the final
// terrain (base terrain plus road cut and fill), a cached 4 m heightfield
// the physics samples, surfaces and water. Pure simulation code.
import { clamp, lerp, smoothstep } from '../engine/math';
import { dsin } from '../engine/dmath';
import { Island, LAKE, PLATEAUS, PROVING_ORIGIN, REGION, WORLD_HALF, newSample, type RegionId, type TerrainSample } from './island';
import { ROAD_CLASSES, type RoadClass, type RoadClassId } from './road-classes';
import { ROAD_CLASS_ORDER, ROAD_EDGES, ROAD_NODES } from './data/roads';
import { Road, sampleSpline, type RoadPoint } from './road';
import { SURFACE, type SurfaceId } from './surfaces';
import { applyBumps, type Ground, type GroundHit } from './ground';

export interface NetNode {
  id: string;
  x: number;
  z: number;
  y: number;
  edges: number[];
}

export interface NetEdge {
  id: number;
  cls: RoadClassId;
  info: RoadClass;
  a: number;
  b: number;
  road: Road;
  /** 1 where the road runs on a bridge deck. */
  bridge: Uint8Array;
  /** Base terrain height under each sample (before cut and fill). */
  base: Float32Array;
}

export interface RoadHit {
  edge: number;
  i: number;
  t: number;
  /** Signed lateral offset (+ right of the edge direction). */
  lateral: number;
  dist: number;
  y: number;
  halfWidth: number;
  bridge: boolean;
}

export const TILE = 64;
export const TILE_SPACING = 4;
const TILE_N = TILE / TILE_SPACING + 1; // 17 samples per side

/** Terrain layer weights used by the renderer and the physics surfaces. */
export interface Layers {
  grass: number;
  rock: number;
  dirt: number;
  sand: number;
  snow: number;
}

export const SNOWLINE = 590;

export class World {
  readonly island: Island;
  readonly nodes: NetNode[] = [];
  readonly edges: NetEdge[] = [];
  private grid = new Map<number, number[]>(); // packed (edge << 16 | i) per cell
  private readonly cell = 64;
  private readonly reach = 60;
  private tiles = new Map<number, Tile>();
  private tileOrder: number[] = [];
  private readonly maxTiles = 600;
  private s = newSample();
  readonly provingOrigin = PROVING_ORIGIN;

  constructor(seed = 1) {
    this.island = new Island(seed);
    this.buildNetwork();
  }

  // --- Road network ------------------------------------------------------
  private buildNetwork(): void {
    const isl = this.island;
    for (const [id, x, z] of ROAD_NODES) {
      // Node height: the average base height around it, kept above water.
      let h = 0;
      for (const [dx, dz] of [[0, 0], [12, 0], [-12, 0], [0, 12], [0, -12]]) h += isl.height(x + dx, z + dz);
      h /= 5;
      isl.sample(x, z, this.s);
      if (this.s.water > -Infinity) h = Math.max(h, this.s.water + 4.5);
      this.nodes.push({ id, x, z, y: h, edges: [] });
    }
    ROAD_EDGES.forEach(([ci, a, b, mid], id) => {
      const cls = ROAD_CLASS_ORDER[ci] as RoadClassId;
      const info = ROAD_CLASSES[cls];
      const ctrl: RoadPoint[] = [{ x: this.nodes[a].x, y: 0, z: this.nodes[a].z }];
      for (let k = 0; k < mid.length; k += 2) ctrl.push({ x: mid[k], y: 0, z: mid[k + 1] });
      ctrl.push({ x: this.nodes[b].x, y: 0, z: this.nodes[b].z });
      const pts = ctrl.length >= 2 ? sampleSpline(ctrl, false, 4) : ctrl;
      const road = new Road(pts, { closed: false, halfWidth: info.halfWidth });
      const n = road.n;
      const base = new Float32Array(n);
      const water = new Float32Array(n).fill(-Infinity);
      for (let i = 0; i < n; i++) {
        isl.sample(road.x[i], road.z[i], this.s);
        base[i] = this.s.h;
        if (this.s.water > this.s.h) water[i] = this.s.water;
      }
      // Profile: smooth the base heights, keep bridges above water, limit grade.
      const y = smoothProfile(base, road.s, info.smooth, this.nodes[a].y, this.nodes[b].y);
      for (let i = 0; i < n; i++) if (water[i] > -Infinity) y[i] = Math.max(y[i], water[i] + 4.5);
      // Approaches to a crossing rise at the class grade.
      gradeLimit(y, road.s, info.maxGrade, this.nodes[a].y, this.nodes[b].y);
      const bridge = new Uint8Array(n);
      for (let i = 0; i < n; i++) bridge[i] = y[i] - base[i] > 5.5 || water[i] > -Infinity ? 1 : 0;
      // Close tiny gaps in bridge runs.
      for (let i = 1; i + 1 < n; i++) if (!bridge[i] && bridge[i - 1] && bridge[i + 1]) bridge[i] = 1;
      for (let i = 0; i < n; i++) road.y[i] = y[i];
      const e: NetEdge = { id, cls, info, a, b, road, bridge, base };
      this.edges.push(e);
      this.nodes[a].edges.push(id);
      this.nodes[b].edges.push(id);
      for (let i = 0; i + 1 < n; i++) this.insertSeg(id, i);
    });
  }

  private insertSeg(edge: number, i: number): void {
    const r = this.edges[edge].road;
    const pad = this.edges[edge].info.halfWidth + this.reach;
    const c = this.cell;
    const x0 = Math.floor((Math.min(r.x[i], r.x[i + 1]) - pad) / c);
    const x1 = Math.floor((Math.max(r.x[i], r.x[i + 1]) + pad) / c);
    const z0 = Math.floor((Math.min(r.z[i], r.z[i + 1]) - pad) / c);
    const z1 = Math.floor((Math.max(r.z[i], r.z[i + 1]) + pad) / c);
    for (let gx = x0; gx <= x1; gx++) {
      for (let gz = z0; gz <= z1; gz++) {
        const key = gx * 100003 + gz;
        let l = this.grid.get(key);
        if (!l) this.grid.set(key, (l = []));
        l.push(edge * 65536 + i);
      }
    }
  }

  /**
   * Nearest road surface point within `maxDist` of the centerline edge
   * (lateral beyond the half width). Returns false if none.
   */
  nearestRoad(x: number, z: number, maxDist: number, out: RoadHit): boolean {
    const list = this.grid.get(Math.floor(x / this.cell) * 100003 + Math.floor(z / this.cell));
    if (!list) return false;
    let best = Infinity;
    for (let k = 0; k < list.length; k++) {
      const packed = list[k];
      const e = (packed / 65536) | 0;
      const i = packed - e * 65536;
      const r = this.edges[e].road;
      const ax = r.x[i];
      const az = r.z[i];
      const dx = r.x[i + 1] - ax;
      const dz = r.z[i + 1] - az;
      const l2 = dx * dx + dz * dz;
      let t = l2 > 0 ? ((x - ax) * dx + (z - az) * dz) / l2 : 0;
      t = t < 0 ? 0 : t > 1 ? 1 : t;
      const cx = ax + dx * t;
      const cz = az + dz * t;
      const d = Math.sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz));
      // Compare by distance beyond each road's own edge.
      const score = d - this.edges[e].info.halfWidth;
      if (score < best) {
        best = score;
        const len = Math.sqrt(l2) || 1;
        out.edge = e;
        out.i = i;
        out.t = t;
        out.dist = d;
        out.lateral = (x - cx) * (-dz / len) + (z - cz) * (dx / len);
        out.y = r.y[i] + (r.y[i + 1] - r.y[i]) * t;
        out.halfWidth = this.edges[e].info.halfWidth;
        out.bridge = this.edges[e].bridge[i] === 1 || this.edges[e].bridge[i + 1] === 1;
      }
    }
    return best <= maxDist;
  }

  // --- Terrain -----------------------------------------------------------
  /** Final terrain height: base terrain with cut and fill around roads. */
  terrainHeight(x: number, z: number, sample?: TerrainSample): number {
    const s = sample ?? this.s;
    this.island.sample(x, z, s);
    let h = s.h;
    const hit = hitScratch;
    if (this.nearestRoad(x, z, this.reach, hit)) {
      const e = this.edges[hit.edge];
      if (!hit.bridge) {
        const base = e.base[hit.i];
        const inner = hit.halfWidth + 3;
        const span = clamp(6 + Math.abs(hit.y - base) * 1.6, 6, 48);
        const w = hit.dist <= inner ? 1 : smoothstep(inner + span, inner, hit.dist);
        // Just under the road surface beneath the deck, at shoulder level beside it.
        const target = hit.y - (hit.dist < hit.halfWidth - 0.6 ? 0.12 : 0.04);
        h = lerp(h, target, w);
      }
    }
    return h;
  }

  /** Terrain layers at a point, from the base sample, height and slope. */
  layers(s: TerrainSample, h: number, slope: number, roadDist: number, out: Layers): Layers {
    const rock = smoothstep(0.42, 0.72, slope) * (s.region === REGION.beach ? 0.3 : 1);
    const snowLine = SNOWLINE + 60 * dsin(h * 0.01 + s.coast * 0.003);
    const snow = smoothstep(snowLine - 30, snowLine + 40, h) * (1 - rock * 0.75);
    let sand = 0;
    if (s.region === REGION.beach || (s.coast < 90 && h < 5)) sand = smoothstep(8, 2.5, h);
    if (s.water > -Infinity && h < s.water + 1.6) sand = Math.max(sand, 0.7);
    let dirt = s.desert * 0.85;
    // Road shoulders: worn earth and gravel.
    if (roadDist < 4.5) dirt = Math.max(dirt, smoothstep(4.5, 1.5, roadDist) * 0.8);
    const g = Math.max(0, 1 - rock - snow - sand - dirt);
    out.grass = g;
    out.rock = rock;
    out.dirt = dirt;
    out.sand = sand;
    out.snow = snow;
    return out;
  }

  /** Cached terrain tile at tile coordinates (tx, tz). */
  tile(tx: number, tz: number): Tile {
    const key = tx * 4096 + tz;
    let t = this.tiles.get(key);
    if (t) return t;
    t = this.buildTile(tx, tz);
    this.tiles.set(key, t);
    this.tileOrder.push(key);
    if (this.tileOrder.length > this.maxTiles) {
      const old = this.tileOrder.shift() as number;
      this.tiles.delete(old);
    }
    return t;
  }

  private buildTile(tx: number, tz: number): Tile {
    const h = new Float32Array(TILE_N * TILE_N);
    const surf = new Uint8Array(TILE_N * TILE_N);
    const water = new Float32Array(TILE_N * TILE_N);
    const x0 = tx * TILE;
    const z0 = tz * TILE;
    const s = newSample();
    const regions = new Uint8Array(TILE_N * TILE_N);
    const forest = new Float32Array(TILE_N * TILE_N);
    for (let j = 0; j < TILE_N; j++) {
      for (let i = 0; i < TILE_N; i++) {
        const k = j * TILE_N + i;
        h[k] = this.terrainHeight(x0 + i * TILE_SPACING, z0 + j * TILE_SPACING, s);
        water[k] = s.water > h[k] ? s.water : h[k] < 0 && s.coast < 30 ? 0 : -1e9;
        regions[k] = s.region;
        forest[k] = s.forest;
        surf[k] = s.region;
      }
    }
    // Surfaces from layers (needs slopes, so a second pass).
    const lay: Layers = { grass: 0, rock: 0, dirt: 0, sand: 0, snow: 0 };
    const hit = hitScratch;
    for (let j = 0; j < TILE_N; j++) {
      for (let i = 0; i < TILE_N; i++) {
        const k = j * TILE_N + i;
        const hx = h[j * TILE_N + Math.min(TILE_N - 1, i + 1)] - h[j * TILE_N + Math.max(0, i - 1)];
        const hz = h[Math.min(TILE_N - 1, j + 1) * TILE_N + i] - h[Math.max(0, j - 1) * TILE_N + i];
        const slope = Math.sqrt(hx * hx + hz * hz) / (2 * TILE_SPACING);
        const x = x0 + i * TILE_SPACING;
        const z = z0 + j * TILE_SPACING;
        this.island.sample(x, z, s);
        const rd = this.nearestRoad(x, z, 6, hit) ? Math.max(0, hit.dist - hit.halfWidth) : 99;
        this.layers(s, h[k], slope, rd, lay);
        surf[k] = surfaceFor(lay, s.region as RegionId);
      }
    }
    return { tx, tz, h, surf, water, regions, forest };
  }

  /** Terrain height and normal at (x, z), interpolated exactly like the render mesh. */
  groundAt(x: number, z: number, out: { y: number; nx: number; ny: number; nz: number; surface: SurfaceId; water: number }): void {
    const tx = Math.floor(x / TILE);
    const tz = Math.floor(z / TILE);
    const t = this.tile(tx, tz);
    const lx = (x - tx * TILE) / TILE_SPACING;
    const lz = (z - tz * TILE) / TILE_SPACING;
    let i = Math.floor(lx);
    let j = Math.floor(lz);
    if (i >= TILE_N - 1) i = TILE_N - 2;
    if (j >= TILE_N - 1) j = TILE_N - 2;
    const u = lx - i;
    const v = lz - j;
    const k = j * TILE_N + i;
    const h00 = t.h[k];
    const h10 = t.h[k + 1];
    const h01 = t.h[k + TILE_N];
    const h11 = t.h[k + TILE_N + 1];
    // Two triangles split along (0,0)-(1,1), matching the mesh index order.
    let y: number;
    let dhdx: number;
    let dhdz: number;
    if (u >= v) {
      y = h00 + (h10 - h00) * u + (h11 - h10) * v;
      dhdx = (h10 - h00) / TILE_SPACING;
      dhdz = (h11 - h10) / TILE_SPACING;
    } else {
      y = h00 + (h01 - h00) * v + (h11 - h01) * u;
      dhdx = (h11 - h01) / TILE_SPACING;
      dhdz = (h01 - h00) / TILE_SPACING;
    }
    const l = Math.sqrt(dhdx * dhdx + 1 + dhdz * dhdz);
    out.y = y;
    out.nx = -dhdx / l;
    out.ny = 1 / l;
    out.nz = -dhdz / l;
    const ni = u < 0.5 ? i : i + 1;
    const nj = v < 0.5 ? j : j + 1;
    out.surface = t.surf[nj * TILE_N + ni] as SurfaceId;
    const w = t.water[nj * TILE_N + ni];
    out.water = w > y ? w - y : 0;
  }
}

export interface Tile {
  tx: number;
  tz: number;
  h: Float32Array;
  surf: Uint8Array;
  water: Float32Array;
  regions: Uint8Array;
  forest: Float32Array;
}

export const TILE_SAMPLES = TILE_N;

const hitScratch: RoadHit = { edge: 0, i: 0, t: 0, lateral: 0, dist: 0, y: 0, halfWidth: 0, bridge: false };

function surfaceFor(l: Layers, region: RegionId): SurfaceId {
  let best: keyof Layers = 'grass';
  let w = l.grass;
  for (const k of ['rock', 'dirt', 'sand', 'snow'] as const) {
    if (l[k] > w) {
      w = l[k];
      best = k;
    }
  }
  switch (best) {
    case 'rock':
      return SURFACE.rock;
    case 'dirt':
      return region === REGION.desert ? SURFACE.sand : SURFACE.dirt;
    case 'sand':
      return SURFACE.sand;
    case 'snow':
      return SURFACE.snow;
    default:
      return SURFACE.grass;
  }
}

/** Moving-average smoothing over a window in meters, endpoints pinned. */
function smoothProfile(base: Float32Array, s: Float64Array, window: number, y0: number, y1: number): Float64Array {
  const n = base.length;
  const out = new Float64Array(n);
  // Prefix sums for an O(n) box filter on uneven spacing (samples are ~uniform).
  const pre = new Float64Array(n + 1);
  for (let i = 0; i < n; i++) pre[i + 1] = pre[i] + base[i];
  const step = n > 1 ? (s[n - 1] - s[0]) / (n - 1) : 1;
  const half = Math.max(1, Math.round(window / 2 / Math.max(0.5, step)));
  for (let i = 0; i < n; i++) {
    const a = Math.max(0, i - half);
    const b = Math.min(n - 1, i + half);
    out[i] = (pre[b + 1] - pre[a]) / (b - a + 1);
  }
  // Blend toward the pinned node heights near the ends.
  const L = s[n - 1] || 1;
  for (let i = 0; i < n; i++) {
    const ta = smoothstep(window * 0.8, 0, s[i]);
    const tb = smoothstep(L - window * 0.8, L, s[i]);
    out[i] = lerp(out[i], y0, ta);
    out[i] = lerp(out[i], y1, tb);
  }
  out[0] = y0;
  out[n - 1] = y1;
  return out;
}

/** Enforce a maximum grade with forward and backward passes, endpoints pinned. */
function gradeLimit(y: Float64Array, s: Float64Array, g: number, y0: number, y1: number): void {
  const n = y.length;
  y[0] = y0;
  y[n - 1] = y1;
  for (let pass = 0; pass < 3; pass++) {
    for (let i = 1; i < n; i++) {
      const ds = s[i] - s[i - 1];
      y[i] = clamp(y[i], y[i - 1] - g * ds, y[i - 1] + g * ds);
    }
    y[n - 1] = y1;
    for (let i = n - 2; i >= 0; i--) {
      const ds = s[i + 1] - s[i];
      y[i] = clamp(y[i], y[i + 1] - g * ds, y[i + 1] + g * ds);
    }
    y[0] = y0;
  }
}

/** Ground for the island: road decks, terrain, water; the proving ground stays flat. */
export class WorldGround implements Ground {
  private hit: RoadHit = { edge: 0, i: 0, t: 0, lateral: 0, dist: 0, y: 0, halfWidth: 0, bridge: false };
  private terr = { y: 0, nx: 0, ny: 1, nz: 0, surface: SURFACE.grass as SurfaceId, water: 0 };
  /** Optional local override (the proving ground's own surfaces). */
  overlay: ((x: number, z: number, out: GroundHit) => boolean) | null = null;

  constructor(readonly world: World) {}

  sample(x: number, z: number, yRef: number, out: GroundHit): boolean {
    if (Math.abs(x) > WORLD_HALF || Math.abs(z) > WORLD_HALF) return false;
    if (this.overlay && this.overlay(x, z, out)) return true;
    const t = this.terr;
    this.world.groundAt(x, z, t);
    const hit = this.hit;
    if (this.world.nearestRoad(x, z, 0, hit) && hit.dist <= hit.halfWidth && hit.y <= yRef + 1.6) {
      // On the paved surface (or a bridge deck above the terrain).
      if (hit.bridge || hit.y >= t.y - 0.3) {
        out.y = hit.y;
        out.nx = 0;
        out.ny = 1;
        out.nz = 0;
        // Road normal from the profile slope along the road.
        const r = this.world.edges[hit.edge].road;
        const i = hit.i;
        const ds = r.s[i + 1] - r.s[i] || 1;
        const gy = (r.y[i + 1] - r.y[i]) / ds;
        const l = Math.sqrt(1 + gy * gy);
        out.nx = (-r.tx[i] * gy) / l;
        out.ny = 1 / l;
        out.nz = (-r.tz[i] * gy) / l;
        out.surface = this.world.edges[hit.edge].info.surface;
        out.water = 0;
        applyBumps(x, z, out);
        return true;
      }
    }
    out.y = t.y;
    out.nx = t.nx;
    out.ny = t.ny;
    out.nz = t.nz;
    out.surface = t.surface;
    out.water = t.water;
    applyBumps(x, z, out);
    return true;
  }
}

/** Every plateau the island flattens, for the renderer and scenery. */
export { PLATEAUS, LAKE };
