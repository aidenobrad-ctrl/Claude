// Settlements: villages strung along their roads, farmsteads in the
// farmland, a harbor and a city on its street grid. Pure, deterministic
// world code: the physics collides with every building and the tree
// placement keeps clear of them, in the worker and on the main thread alike.
import type { World } from './world';
import { REGION, PLATEAUS, newSample } from './island';
import { RNG } from '../engine/rng';
import { dexp } from '../engine/dmath';

export const BUILDING = {
  house: 0,
  shop: 1,
  tower: 2,
  warehouse: 3,
  barn: 4,
  silo: 5,
  midrise: 6,
} as const;

export const ROOF = { flat: 0, gable: 1, hip: 2 } as const;

export interface Building {
  /** Footprint center. */
  x: number;
  z: number;
  /** Unit vector the front faces (towards its road). */
  fx: number;
  fz: number;
  /** Width along the front, depth front to back, wall height above the floor, m. */
  w: number;
  d: number;
  h: number;
  /** Foundation bottom and floor level, m. */
  y0: number;
  floor: number;
  kind: number;
  roof: number;
  /** Stable random in [0, 1) for colours and details. */
  seed: number;
}

interface Village {
  node: string;
  radius: number;
  count: number;
  kinds: number[];
}

const VILLAGES: Village[] = [
  { node: 'northVillage', radius: 300, count: 30, kinds: [BUILDING.house, BUILDING.house, BUILDING.shop] },
  { node: 'farmVillage', radius: 320, count: 34, kinds: [BUILDING.house, BUILDING.house, BUILDING.shop, BUILDING.barn] },
  { node: 'desertTown', radius: 300, count: 28, kinds: [BUILDING.house, BUILDING.shop, BUILDING.warehouse] },
  { node: 'swBeach', radius: 280, count: 26, kinds: [BUILDING.house, BUILDING.shop] },
  { node: 'westCoast', radius: 260, count: 22, kinds: [BUILDING.house, BUILDING.house, BUILDING.warehouse] },
  { node: 'riverEast', radius: 260, count: 22, kinds: [BUILDING.house, BUILDING.shop] },
  { node: 'lakeEast', radius: 220, count: 14, kinds: [BUILDING.house] },
  { node: 'harbor', radius: 420, count: 34, kinds: [BUILDING.warehouse, BUILDING.warehouse, BUILDING.shop] },
];

/** Spatial index of building footprints (bounding circles). */
export class BuildingIndex {
  private grid = new Map<number, number[]>();
  constructor(
    readonly list: Building[],
    private cell = 64,
  ) {
    list.forEach((b, i) => this.insert(b, i));
  }

  private insert(b: Building, i: number): void {
    const r = Math.sqrt(b.w * b.w + b.d * b.d) / 2;
    const c = this.cell;
    for (let gx = Math.floor((b.x - r) / c); gx <= Math.floor((b.x + r) / c); gx++) {
      for (let gz = Math.floor((b.z - r) / c); gz <= Math.floor((b.z + r) / c); gz++) {
        const k = gx * 100003 + gz;
        let l = this.grid.get(k);
        if (!l) this.grid.set(k, (l = []));
        l.push(i);
      }
    }
  }

  add(b: Building): void {
    this.list.push(b);
    this.insert(b, this.list.length - 1);
  }

  /** True if (x, z) lies inside any footprint grown by `margin`. */
  occupied(x: number, z: number, margin: number): boolean {
    const l = this.grid.get(Math.floor(x / this.cell) * 100003 + Math.floor(z / this.cell));
    if (!l) return false;
    for (const i of l) {
      const b = this.list[i];
      const dx = x - b.x;
      const dz = z - b.z;
      const along = dx * b.fx + dz * b.fz;
      const across = dx * -b.fz + dz * b.fx;
      if (Math.abs(along) <= b.d / 2 + margin && Math.abs(across) <= b.w / 2 + margin) return true;
    }
    return false;
  }
}

/** Generate every building on the island. */
export function buildSettlements(world: World): BuildingIndex {
  const index = new BuildingIndex([]);
  const rng = new RNG(`${world.island.seed}:settlements`);
  for (const v of VILLAGES) placeVillage(world, index, rng.fork(v.node), v);
  placeCity(world, index, rng.fork('city'));
  placeFarms(world, index, rng.fork('farms'));
  return index;
}

/** Place one building facing (fx, fz) if the ground allows; returns it or null. */
function tryPlace(world: World, index: BuildingIndex, x: number, z: number, fx: number, fz: number, w: number, d: number, h: number, kind: number, roof: number, seed: number, maxStep: number): Building | null {
  const rx = -fz;
  const rz = fx;
  let lo = Infinity;
  let hi = -Infinity;
  const hit = roadHit;
  for (const [sa, sb] of [
    [-1, -1],
    [1, -1],
    [1, 1],
    [-1, 1],
    [0, 0],
  ]) {
    const px = x + rx * sa * (w / 2) + fx * sb * (d / 2);
    const pz = z + rz * sa * (w / 2) + fz * sb * (d / 2);
    const s = world.island.sample(px, pz, sampleScratch);
    if (s.water > -Infinity || s.coast < 20) return null;
    const hgt = world.terrainHeight(px, pz);
    if (hgt < 1) return null;
    lo = Math.min(lo, hgt);
    hi = Math.max(hi, hgt);
    // Keep the footprint off every road (with a sidewalk's margin).
    if (world.nearestRoad(px, pz, 3, hit)) return null;
  }
  if (hi - lo > maxStep) return null;
  const r = Math.sqrt(w * w + d * d) / 2;
  if (index.occupied(x, z, r * 0.6) || index.occupied(x + fx * d * 0.4, z + fz * d * 0.4, 1) || index.occupied(x - fx * d * 0.4, z - fz * d * 0.4, 1)) return null;
  const b: Building = { x, z, fx, fz, w, d, h, y0: lo - 0.6, floor: hi + 0.15, kind, roof, seed };
  index.add(b);
  return b;
}

const roadHit = { edge: 0, i: 0, t: 0, lateral: 0, dist: 0, y: 0, halfWidth: 0, bridge: false };
const sampleScratch = newSample();

function dims(kind: number, rng: RNG): { w: number; d: number; h: number; roof: number } {
  switch (kind) {
    case BUILDING.shop:
      return { w: rng.range(9, 15), d: rng.range(9, 13), h: rng.range(6.2, 9.6), roof: rng.chance(0.6) ? ROOF.flat : ROOF.gable };
    case BUILDING.warehouse:
      return { w: rng.range(18, 34), d: rng.range(14, 24), h: rng.range(6, 9), roof: ROOF.gable };
    case BUILDING.barn:
      return { w: rng.range(10, 14), d: rng.range(16, 22), h: rng.range(5, 6.5), roof: ROOF.gable };
    default:
      return { w: rng.range(7.5, 11), d: rng.range(7, 10), h: rng.range(2.9, 6.2), roof: rng.chance(0.75) ? ROOF.gable : ROOF.hip };
  }
}

function placeVillage(world: World, index: BuildingIndex, rng: RNG, v: Village): void {
  const node = world.nodes.find((n) => n.id === v.node);
  if (!node) return;
  let placed = 0;
  // Walk every road near the village, lining both sides with lots.
  for (const e of world.edges) {
    const r = e.road;
    let next = rng.range(4, 14);
    for (let i = 0; i < r.n && placed < v.count; i++) {
      const dx = r.x[i] - node.x;
      const dz = r.z[i] - node.z;
      if (dx * dx + dz * dz > v.radius * v.radius) continue;
      if (r.s[i] < next) continue;
      next = r.s[i] + rng.range(13, 22);
      if (e.bridge[i]) continue;
      for (const side of [-1, 1]) {
        const kind = v.kinds[rng.int(0, v.kinds.length)];
        const dm = dims(kind, rng);
        // Right of the travel direction is (-tz, tx); the front faces the road.
        const nx = -r.tz[i] * side;
        const nz = r.tx[i] * side;
        const setback = e.info.halfWidth + 1.3 + rng.range(4, 9) + dm.d / 2;
        const x = r.x[i] + nx * setback;
        const z = r.z[i] + nz * setback;
        if (tryPlace(world, index, x, z, -nx, -nz, dm.w, dm.d, dm.h, kind, dm.roof, rng.next(), 2.6)) placed++;
      }
    }
  }
}

function placeCity(world: World, index: BuildingIndex, rng: RNG): void {
  const city = PLATEAUS.find((p) => p.region === REGION.city);
  if (!city) return;
  const xs = [2700, 2900, 3100, 3300, 3500, 3700];
  const zs = [-760, -560, -360, -160, 40, 240, 440];
  const cx = 3200;
  const cz = -160;
  const street = 5.5 + 1.3 + 3.5; // half width, shoulder, sidewalk
  for (let i = 0; i + 1 < xs.length; i++) {
    for (let j = 0; j + 1 < zs.length; j++) {
      const x0 = xs[i] + street;
      const x1 = xs[i + 1] - street;
      const z0 = zs[j] + street;
      const z1 = zs[j + 1] - street;
      const bx = (x0 + x1) / 2;
      const bz = (z0 + z1) / 2;
      const dc = Math.sqrt((bx - cx) * (bx - cx) + (bz - cz) * (bz - cz));
      const downtown = dexp(-(dc * dc) / (520 * 520));
      // A park block or two.
      if ((i === 1 && j === 4) || (i === 3 && j === 1)) continue;
      const lot = (len: number): number[] => {
        const out: number[] = [];
        let left = len;
        while (left > 14) {
          const w = Math.min(left, rng.range(18, 34 + 16 * downtown));
          out.push(w);
          left -= w;
        }
        if (left > 0 && out.length) out[out.length - 1] += left;
        return out;
      };
      const height = (): number => {
        const base = 9 + 62 * downtown * rng.range(0.45, 1.35);
        return Math.max(8, base + (rng.chance(0.08 * downtown) ? rng.range(30, 70) : 0));
      };
      const kindFor = (h: number): number => (h > 34 ? BUILDING.tower : BUILDING.midrise);
      const depthN = rng.range(22, 38);
      const depthS = rng.range(22, 38);
      // North and south frontages span the block; east and west fill between.
      for (const [zEdge, fz, depth] of [
        [z0, -1, depthN],
        [z1, 1, depthS],
      ] as [number, number, number][]) {
        let x = x0;
        for (const w of lot(x1 - x0)) {
          const h = height();
          // Some lots stay open: plazas, car parks, gaps between towers.
          if (!rng.chance(0.14)) tryPlace(world, index, x + w / 2, zEdge - (fz * depth) / 2, 0, fz, w - 1.2, depth, h, kindFor(h), ROOF.flat, rng.next(), 6);
          x += w;
        }
      }
      for (const [xEdge, fx] of [
        [x0, -1],
        [x1, 1],
      ] as [number, number][]) {
        const za = z0 + depthN + 1;
        const zb = z1 - depthS - 1;
        let z = za;
        const depth = rng.range(20, 34);
        for (const w of lot(zb - za)) {
          const h = height();
          if (!rng.chance(0.14)) tryPlace(world, index, xEdge - (fx * depth) / 2, z + w / 2, fx, 0, w - 1.2, depth, h, kindFor(h), ROOF.flat, rng.next(), 6);
          z += w;
        }
      }
    }
  }
}

function placeFarms(world: World, index: BuildingIndex, rng: RNG): void {
  // A farmstead every few hundred metres along roads through farmland.
  const s = newSample();
  for (const e of world.edges) {
    if (e.cls === 'street' || e.cls === 'highway') continue;
    const r = e.road;
    let next = rng.range(150, 400);
    for (let i = 0; i < r.n; i++) {
      if (r.s[i] < next) continue;
      next = r.s[i] + rng.range(380, 900);
      world.island.sample(r.x[i], r.z[i], s);
      if (s.region !== REGION.farmland && s.region !== REGION.plains) continue;
      if (s.forest > 0.4) continue;
      const side = rng.chance(0.5) ? 1 : -1;
      const nx = -r.tz[i] * side;
      const nz = r.tx[i] * side;
      const tx = r.tx[i];
      const tz = r.tz[i];
      const base = e.info.halfWidth + 1.3 + rng.range(18, 30);
      // Farmhouse facing the road, barn behind and to one side, a silo by the barn.
      const hd = dims(BUILDING.house, rng);
      const hx = r.x[i] + nx * (base + hd.d / 2);
      const hz = r.z[i] + nz * (base + hd.d / 2);
      if (!tryPlace(world, index, hx, hz, -nx, -nz, hd.w, hd.d, hd.h, BUILDING.house, hd.roof, rng.next(), 2.5)) continue;
      const bd = dims(BUILDING.barn, rng);
      const along = rng.range(14, 22) * (rng.chance(0.5) ? 1 : -1);
      const bx = hx + nx * (hd.d / 2 + 8 + bd.d / 2) + tx * along;
      const bz = hz + nz * (hd.d / 2 + 8 + bd.d / 2) + tz * along;
      const barn = tryPlace(world, index, bx, bz, tx, tz, bd.w, bd.d, bd.h, BUILDING.barn, ROOF.gable, rng.next(), 3);
      if (barn) {
        const sr = rng.range(2.6, 3.4);
        tryPlace(world, index, bx + tx * (bd.w / 2 + sr + 1.5), bz + tz * (bd.w / 2 + sr + 1.5), -nx, -nz, sr * 2, sr * 2, rng.range(11, 16), BUILDING.silo, ROOF.flat, rng.next(), 3);
      }
    }
  }
}
