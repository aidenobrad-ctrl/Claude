// The island at runtime: the road network with vertical profiles, the final
// terrain (base terrain plus road cut and fill), a cached 4 m heightfield
// the physics samples, surfaces and water. Pure simulation code.
import { clamp, lerp, smoothstep } from '../engine/math';
import { dsin } from '../engine/dmath';
import { Island, LAKE, PLATEAUS, PROVING_ORIGIN, REGION, WORLD_HALF, newSample, type RegionId, type TerrainSample } from './island';
import { ROAD_CLASSES, ROAD_SHOULDER, PARAPET_W, CURB_H, type RoadClass, type RoadClassId } from './road-classes';
import type { Colliders } from './colliders';
import { buildSettlements, BUILDING, type BuildingIndex } from './settlements';
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

  /** Every building on the island (villages, farms, harbor, city). */
  readonly buildings: BuildingIndex;

  constructor(seed = 1) {
    this.island = new Island(seed);
    this.buildNetwork();
    this.buildings = buildSettlements(this);
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
  /** Distance beyond the nearest road's paved edge from the last terrainHeight call (99 if none). */
  lastRoadDist = 99;

  /** Final terrain height: base terrain with cut and fill around roads. */
  terrainHeight(x: number, z: number, sample?: TerrainSample): number {
    const s = sample ?? this.s;
    this.island.sample(x, z, s);
    let h = s.h;
    const hit = hitScratch;
    this.lastRoadDist = 99;
    if (this.nearestRoad(x, z, this.reach, hit)) {
      this.lastRoadDist = Math.max(0, hit.dist - hit.halfWidth);
      const e = this.edges[hit.edge];
      if (!hit.bridge) {
        const base = e.base[hit.i];
        const inner = hit.halfWidth + Math.max(3, e.info.shoulder + 1);
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
    // Forest floor: leaf litter and bare earth between the trees.
    dirt = Math.max(dirt, smoothstep(0.35, 0.9, s.forest) * 0.5);
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
    const NN = TILE_N * TILE_N;
    const h = new Float32Array(NN);
    const surf = new Uint8Array(NN);
    const water = new Float32Array(NN);
    const x0 = tx * TILE;
    const z0 = tz * TILE;
    const s = newSample();
    const regions = new Uint8Array(NN);
    const forest = new Float32Array(NN);
    const coast = new Float32Array(NN);
    const desert = new Float32Array(NN);
    const baseWater = new Float32Array(NN);
    const roadDist = new Float32Array(NN);
    for (let j = 0; j < TILE_N; j++) {
      for (let i = 0; i < TILE_N; i++) {
        const k = j * TILE_N + i;
        h[k] = this.terrainHeight(x0 + i * TILE_SPACING, z0 + j * TILE_SPACING, s);
        roadDist[k] = this.lastRoadDist;
        water[k] = s.water > h[k] ? s.water : h[k] < 0 && s.coast < 30 ? 0 : -1e9;
        regions[k] = s.region;
        forest[k] = s.forest;
        coast[k] = s.coast;
        desert[k] = s.desert;
        baseWater[k] = s.water;
      }
    }
    // Surfaces from layers (needs slopes, so a second pass over the stored samples).
    const lay: Layers = { grass: 0, rock: 0, dirt: 0, sand: 0, snow: 0 };
    const splat = new Float32Array(NN * 4);
    for (let j = 0; j < TILE_N; j++) {
      for (let i = 0; i < TILE_N; i++) {
        const k = j * TILE_N + i;
        const hx = h[j * TILE_N + Math.min(TILE_N - 1, i + 1)] - h[j * TILE_N + Math.max(0, i - 1)];
        const hz = h[Math.min(TILE_N - 1, j + 1) * TILE_N + i] - h[Math.max(0, j - 1) * TILE_N + i];
        const slope = Math.sqrt(hx * hx + hz * hz) / (2 * TILE_SPACING);
        s.h = h[k];
        s.region = regions[k] as RegionId;
        s.coast = coast[k];
        s.water = baseWater[k];
        s.forest = forest[k];
        s.desert = desert[k];
        this.layers(s, h[k], slope, roadDist[k], lay);
        surf[k] = surfaceFor(lay, s.region);
        splat[k * 4] = lay.rock;
        splat[k * 4 + 1] = lay.dirt;
        splat[k * 4 + 2] = lay.sand;
        splat[k * 4 + 3] = lay.snow;
      }
    }
    const tile: Tile = { tx, tz, h, surf, water, regions, forest, coast, desert, roadDist, splat, trees: EMPTY_TREES };
    tile.trees = this.placeTrees(tile);
    return tile;
  }

  // --- Vegetation ----------------------------------------------------------
  /**
   * Trees for one 64 m tile: a jittered 8 m candidate grid thinned by a
   * density from forest cover, region, height, slope, water and roads.
   * Deterministic in (seed, tile), so physics and rendering agree.
   */
  private placeTrees(t: Tile): Float32Array {
    const out: number[] = [];
    const x0 = t.tx * TILE;
    const z0 = t.tz * TILE;
    const seed = this.island.seed * 7919;
    for (let cj = 0; cj < 8; cj++) {
      for (let ci = 0; ci < 8; ci++) {
        let hsh = hash3(t.tx * 8 + ci, t.tz * 8 + cj, seed);
        const r1 = (hsh >>> 0) / 4294967296;
        hsh = hashNext(hsh);
        const r2 = (hsh >>> 0) / 4294967296;
        hsh = hashNext(hsh);
        const r3 = (hsh >>> 0) / 4294967296;
        hsh = hashNext(hsh);
        const r4 = (hsh >>> 0) / 4294967296;
        hsh = hashNext(hsh);
        const r5 = (hsh >>> 0) / 4294967296;
        const lx = ci * 8 + 0.6 + r1 * 6.8;
        const lz = cj * 8 + 0.6 + r2 * 6.8;
        const i = Math.min(TILE_N - 2, Math.floor(lx / TILE_SPACING));
        const j = Math.min(TILE_N - 2, Math.floor(lz / TILE_SPACING));
        const k = j * TILE_N + i;
        const hk = t.h[k];
        if (t.water[k] > -1e8 || t.water[k + 1] > -1e8 || t.water[k + TILE_N + 1] > -1e8) continue;
        if (t.roadDist[k] < 4 || t.roadDist[k + TILE_N + 1] < 4) continue;
        const region = t.regions[k];
        if (region === REGION.hub || region === REGION.proving || region === REGION.city || region === REGION.harbor) continue;
        const hx = t.h[k + 1] - hk;
        const hz = t.h[k + TILE_N] - hk;
        const slope = Math.sqrt(hx * hx + hz * hz) / TILE_SPACING;
        if (slope > 0.95 || hk > 780) continue;
        const surf = t.surf[k];
        if (surf === SURFACE.snow || surf === SURFACE.rock) continue;
        const x = x0 + lx;
        const z = z0 + lz;
        // Gardens, not trees through roofs.
        if (this.buildings.occupied(x, z, 3.5)) continue;
        const f = t.forest[k];
        const coast = t.coast[k];
        // Groves: clumps of trees across open country.
        const grove = valueNoise(x / 140, z / 140, seed + 11);
        let dens: number;
        let type: number;
        if (region === REGION.desert || surf === SURFACE.sand) {
          dens = region === REGION.beach || coast < 260 ? (coast > 25 && coast < 260 ? 0.06 + 0.1 * grove : 0) : 0.012;
          type = region === REGION.desert ? TREE.dead : TREE.palm;
        } else if (region === REGION.beach || coast < 160) {
          dens = coast > 25 ? 0.05 + 0.12 * grove : 0;
          type = TREE.palm;
        } else {
          dens = 0.012 + 0.32 * Math.max(0, grove - 0.55) * 2 + f * 0.8;
          const conifer = clamp((hk - 160) / 300, 0, 1) * 0.85 + (region === REGION.mountains ? 0.3 : 0);
          type = r4 < conifer ? TREE.conifer : TREE.broadleaf;
          if (region === REGION.farmland && t.roadDist[k] < 16 && r5 < 0.45) {
            type = TREE.cypress;
            dens = Math.max(dens, 0.22);
          }
          if (hk > 560) dens *= clamp((780 - hk) / 220, 0, 1);
        }
        dens *= 1 - smoothstep(0.55, 0.95, slope);
        if (r3 >= dens) continue;
        // Undergrowth: a share of the candidates become bushes.
        if (type !== TREE.palm && type !== TREE.dead && r5 > 0.78) type = TREE.bush;
        // Height on the render/physics mesh (same triangle split as groundAt).
        const u = lx / TILE_SPACING - i;
        const v = lz / TILE_SPACING - j;
        const h00 = hk;
        const h10 = t.h[k + 1];
        const h01 = t.h[k + TILE_N];
        const h11 = t.h[k + TILE_N + 1];
        const y = u >= v ? h00 + (h10 - h00) * u + (h11 - h10) * v : h00 + (h01 - h00) * v + (h11 - h01) * u;
        const scale = 0.72 + 0.56 * ((r1 + r2 * 0.7 + r4 * 0.3) % 1);
        out.push(x, y, z, scale, r2 * 6.2831853, type, r3 / Math.max(dens, 1e-6));
      }
    }
    return new Float32Array(out);
  }

  /** Bushes cars have driven through (by tree id), part of the simulation state. */
  readonly brokenTrees = new Set<number>();

  /** Visit tree trunks within r of (x, z) as collision circles. */
  queryTrees(x: number, z: number, r: number, cb: (c: TreeCircle) => void): void {
    const tx0 = Math.floor((x - r) / TILE);
    const tx1 = Math.floor((x + r) / TILE);
    const tz0 = Math.floor((z - r) / TILE);
    const tz1 = Math.floor((z + r) / TILE);
    const c = treeCircle;
    for (let tx = tx0; tx <= tx1; tx++) {
      for (let tz = tz0; tz <= tz1; tz++) {
        const tr = this.tile(tx, tz).trees;
        for (let k = 0; k < tr.length; k += TREE_STRIDE) {
          const type = tr[k + 5];
          const dx = tr[k] - x;
          const dz = tr[k + 2] - z;
          const rr = r + 1;
          if (dx * dx + dz * dz > rr * rr) continue;
          c.x = tr[k];
          c.z = tr[k + 2];
          c.r = TREE_TRUNK[type] * tr[k + 3];
          c.bottom = tr[k + 1] - 1;
          c.top = tr[k + 1] + 6;
          c.breakable = type === TREE.bush;
          // Flattened bushes stay flattened until the session resets.
          const id = (tx * 4096 + tz) * 64 + k / TREE_STRIDE;
          if (c.breakable && this.brokenTrees.has(id)) continue;
          c.broken = false;
          cb(c);
          if (c.broken) this.brokenTrees.add(id);
        }
      }
    }
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
  coast: Float32Array;
  desert: Float32Array;
  /** Render splat weights per sample: rock, dirt, sand, snow. */
  splat: Float32Array;
  /** Distance beyond the nearest road's paved edge, m (99 if far). */
  roadDist: Float32Array;
  /** Trees: TREE_STRIDE floats each (x, y, z, scale, yaw, type, rank). */
  trees: Float32Array;
}

/** Tree kinds placed by the world. */
export const TREE = { broadleaf: 0, conifer: 1, palm: 2, cypress: 3, bush: 4, dead: 5 } as const;
export const TREE_KINDS = 6;
export const TREE_STRIDE = 7;
/** Trunk collision radius per kind at scale 1, m (bushes break). */
const TREE_TRUNK = [0.38, 0.32, 0.24, 0.26, 0.9, 0.22];
const EMPTY_TREES = new Float32Array(0);

export interface TreeCircle {
  x: number;
  z: number;
  r: number;
  top: number;
  bottom: number;
  kind: 'tree';
  breakable: boolean;
  broken: boolean;
}
const treeCircle: TreeCircle = { x: 0, z: 0, r: 0, top: 0, bottom: 0, kind: 'tree', breakable: false, broken: false };

/** 32-bit integer hash of three ints (deterministic everywhere). */
function hash3(a: number, b: number, c: number): number {
  let h = Math.imul(a | 0, 0x27d4eb2d) ^ Math.imul(b | 0, 0x165667b1) ^ Math.imul(c | 0, 0x9e3779b1);
  h = Math.imul(h ^ (h >>> 15), 0x85ebca6b);
  h = Math.imul(h ^ (h >>> 13), 0xc2b2ae35);
  return (h ^ (h >>> 16)) >>> 0;
}
function hashNext(h: number): number {
  h = Math.imul(h ^ (h >>> 16), 0x7feb352d);
  h = Math.imul(h ^ (h >>> 15), 0x846ca68b);
  return (h ^ (h >>> 16)) >>> 0;
}
/** Smooth value noise in [0, 1] on an integer lattice. */
function valueNoise(x: number, z: number, seed: number): number {
  const ix = Math.floor(x);
  const iz = Math.floor(z);
  const fx = x - ix;
  const fz = z - iz;
  const sx = fx * fx * (3 - 2 * fx);
  const sz = fz * fz * (3 - 2 * fz);
  const v = (a: number, b: number): number => hash3(a, b, seed) / 4294967296;
  const a = v(ix, iz) + (v(ix + 1, iz) - v(ix, iz)) * sx;
  const b = v(ix, iz + 1) + (v(ix + 1, iz + 1) - v(ix, iz + 1)) * sx;
  return a + (b - a) * sz;
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
    // Bridge decks run out to the parapets and city streets have raised
    // sidewalks; elsewhere the shoulder is terrain.
    if (this.world.nearestRoad(x, z, ROAD_SHOULDER, hit) && hit.y <= yRef + 1.6) {
      const info = this.world.edges[hit.edge].info;
      const onDeck = hit.dist <= hit.halfWidth || ((hit.bridge || info.sidewalk) && hit.dist <= hit.halfWidth + info.shoulder);
      // On the paved surface (or a bridge deck above the terrain).
      if (onDeck && (hit.bridge || hit.y >= t.y - 0.3)) {
        const kerb = info.sidewalk && hit.dist > hit.halfWidth;
        out.y = hit.y + (kerb ? CURB_H : 0);
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
        out.surface = kerb ? SURFACE.concrete : info.surface;
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

/** Parapet walls along every bridge run, so cars cannot drive off the deck. */
export function addBridgeRails(world: World, out: Colliders): number {
  let count = 0;
  for (const e of world.edges) {
    const r = e.road;
    const W = e.info.halfWidth + e.info.shoulder - PARAPET_W;
    for (let i = 0; i + 1 < r.n; i++) {
      if (!e.bridge[i] || !e.bridge[i + 1]) continue;
      const y0 = Math.min(r.y[i], r.y[i + 1]);
      const y1 = Math.max(r.y[i], r.y[i + 1]);
      for (const side of [-1, 1]) {
        const lat = side * W;
        out.addSegment({
          ax: r.x[i] - r.tz[i] * lat,
          az: r.z[i] + r.tx[i] * lat,
          bx: r.x[i + 1] - r.tz[i + 1] * lat,
          bz: r.z[i + 1] + r.tx[i + 1] * lat,
          top: y1 + 0.85,
          bottom: y0 - 1.5,
          bounce: 0.2,
          friction: 0.4,
          kind: 'barrier',
        });
        count++;
      }
    }
  }
  return count;
}

/** Walls around every building (silos as circles). */
export function addBuildingColliders(world: World, out: Colliders): number {
  let n = 0;
  for (const b of world.buildings.list) {
    if (b.kind === BUILDING.silo) {
      out.addCircle({ x: b.x, z: b.z, r: b.w / 2, top: b.floor + b.h, bottom: b.y0 - 1, kind: 'building' });
      n++;
      continue;
    }
    const rx = -b.fz;
    const rz = b.fx;
    const pts: { x: number; z: number }[] = [];
    for (const [sa, sb] of [
      [-1, -1],
      [1, -1],
      [1, 1],
      [-1, 1],
    ]) {
      pts.push({ x: b.x + rx * sa * (b.w / 2) + b.fx * sb * (b.d / 2), z: b.z + rz * sa * (b.w / 2) + b.fz * sb * (b.d / 2) });
    }
    out.addPolyline(pts, { top: b.floor + b.h + 4, bottom: b.y0 - 1, bounce: 0.15, friction: 0.5, kind: 'wall' }, true);
    n += 4;
  }
  return n;
}

/** Every plateau the island flattens, for the renderer and scenery. */
export { PLATEAUS, LAKE };
