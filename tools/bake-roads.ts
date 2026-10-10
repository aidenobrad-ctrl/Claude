// Offline road baker. Routes the island's road network with A* over the
// terrain, merges routes into one graph (junctions where routes meet or
// cross) and writes src/world/data/roads.ts. Runtime code only reads that
// file, so routing never costs startup time.
//
//   node tools/run-ts.mjs tools/bake-roads.ts
import fs from 'node:fs';
import { Island, WORLD_HALF, WORLD_SIZE, PLATEAUS, PROVING_ORIGIN, LAKE, newSample } from '../src/world/island';
import { ROAD_CLASSES, type RoadClassId } from '../src/world/road-classes';

const CELL = 12;
const N = Math.ceil(WORLD_SIZE / CELL);
const island = new Island(1);

// --- Cost grid -----------------------------------------------------------
const t0 = Date.now();
const H = new Float32Array(N * N);
const WATER = new Uint8Array(N * N); // 0 land, 1 river, 2 lake, 3 sea
const s = newSample();
for (let j = 0; j < N; j++) {
  for (let i = 0; i < N; i++) {
    const x = -WORLD_HALF + (i + 0.5) * CELL;
    const z = -WORLD_HALF + (j + 0.5) * CELL;
    island.sample(x, z, s);
    H[j * N + i] = s.h;
    if (s.coast < 25 || s.h < 0.5) WATER[j * N + i] = 3;
    else if (s.water > s.h) WATER[j * N + i] = Math.sqrt((x - LAKE.x) ** 2 + (z - LAKE.z) ** 2) < LAKE.r + 80 ? 2 : 1;
  }
}
// Smooth heights for routing so 12 m noise does not dominate grades.
const HS = new Float32Array(N * N);
for (let j = 0; j < N; j++) {
  for (let i = 0; i < N; i++) {
    let sum = 0;
    let n = 0;
    for (let dj = -2; dj <= 2; dj++) {
      for (let di = -2; di <= 2; di++) {
        const ii = i + di;
        const jj = j + dj;
        if (ii < 0 || jj < 0 || ii >= N || jj >= N) continue;
        sum += H[jj * N + ii];
        n++;
      }
    }
    HS[j * N + i] = sum / n;
  }
}
// The proving ground is fenced off: roads stay out except for its access
// road, which comes in from the west along a corridor to the paddock.
const KEEPOUT = new Uint8Array(N * N);
{
  const P = PROVING_ORIGIN;
  for (let j = 0; j < N; j++) {
    for (let i = 0; i < N; i++) {
      const lx = -WORLD_HALF + (i + 0.5) * CELL - P.x;
      const lz = -WORLD_HALF + (j + 0.5) * CELL - P.z;
      const inFacility = lx > -840 && lx < 760 && lz > -175 && lz < 580;
      const inCorridor = lx > -1100 && lx < -640 && lz > -92 && lz < -32;
      if (inFacility && !inCorridor) KEEPOUT[j * N + i] = 1;
    }
  }
}
console.log(`cost grid ${N}x${N} in ${Date.now() - t0} ms`);

const cellOf = (x: number, z: number): number => {
  const i = Math.max(0, Math.min(N - 1, Math.floor((x + WORLD_HALF) / CELL)));
  const j = Math.max(0, Math.min(N - 1, Math.floor((z + WORLD_HALF) / CELL)));
  return j * N + i;
};
const cx = (c: number): number => -WORLD_HALF + ((c % N) + 0.5) * CELL;
const cz = (c: number): number => -WORLD_HALF + (Math.floor(c / N) + 0.5) * CELL;

// 16-neighborhood: the 8 kings moves plus 8 knight moves for smoother angles.
const NB: [number, number][] = [
  [1, 0], [-1, 0], [0, 1], [0, -1], [1, 1], [1, -1], [-1, 1], [-1, -1],
  [2, 1], [2, -1], [-2, 1], [-2, -1], [1, 2], [1, -2], [-1, 2], [-1, -2],
];

// Cells occupied by roads already in the network (edge id + 1), and a halo.
const ROADCELL = new Int32Array(N * N);
const HALO = new Uint8Array(N * N);

class Heap {
  private k: number[] = [];
  private v: number[] = [];
  get size(): number {
    return this.k.length;
  }
  push(key: number, val: number): void {
    const k = this.k;
    const v = this.v;
    k.push(key);
    v.push(val);
    let i = k.length - 1;
    while (i > 0) {
      const p = (i - 1) >> 1;
      if (k[p] <= k[i]) break;
      [k[p], k[i]] = [k[i], k[p]];
      [v[p], v[i]] = [v[i], v[p]];
      i = p;
    }
  }
  pop(): number {
    const k = this.k;
    const v = this.v;
    const top = v[0];
    const lk = k.pop() as number;
    const lv = v.pop() as number;
    if (k.length) {
      k[0] = lk;
      v[0] = lv;
      let i = 0;
      for (;;) {
        const l = i * 2 + 1;
        const r = l + 1;
        let m = i;
        if (l < k.length && k[l] < k[m]) m = l;
        if (r < k.length && k[r] < k[m]) m = r;
        if (m === i) break;
        [k[m], k[i]] = [k[i], k[m]];
        [v[m], v[i]] = [v[i], v[m]];
        i = m;
      }
    }
    return top;
  }
}

interface RouteOpts {
  cls: RoadClassId;
  /** Stop at the first cell of the existing network instead of a goal cell. */
  toNetwork?: boolean;
  /** Prefer existing roads (multiplier on their cell cost). */
  reuse?: number;
}

function stepCost(a: number, b: number, d: number, o: RouteOpts): number {
  const w = WATER[b];
  if (w === 3 || w === 2 || KEEPOUT[b]) return Infinity;
  const cls = ROAD_CLASSES[o.cls];
  const grade = Math.abs(HS[b] - HS[a]) / d;
  const g = grade / cls.maxGrade;
  let c = d * (1 + 2.2 * g * g);
  if (g > 1) c += d * 60 * (g - 1) * (g - 1) + d * 8;
  if (w === 1) c += d * 9; // river: bridge cost, keeps crossings short
  if (ROADCELL[b] && o.reuse) c *= o.reuse;
  else if (HALO[b]) c *= 1.6; // avoid running alongside an existing road
  return c;
}

function route(from: number, to: number, o: RouteOpts): number[] {
  const g = new Float64Array(N * N).fill(Infinity);
  const came = new Int32Array(N * N).fill(-1);
  const closed = new Uint8Array(N * N);
  const heap = new Heap();
  const gx = cx(to);
  const gz = cz(to);
  const h = (c: number): number => (o.toNetwork ? 0 : Math.sqrt((cx(c) - gx) ** 2 + (cz(c) - gz) ** 2) * 0.98);
  g[from] = 0;
  heap.push(h(from), from);
  const startX = cx(from);
  const startZ = cz(from);
  let found = -1;
  while (heap.size) {
    const c = heap.pop();
    if (closed[c]) continue;
    closed[c] = 1;
    if (o.toNetwork) {
      // Join the network once clear of the starting area.
      if (ROADCELL[c] && Math.sqrt((cx(c) - startX) ** 2 + (cz(c) - startZ) ** 2) > 80) {
        found = c;
        break;
      }
    } else if (c === to) {
      found = c;
      break;
    }
    const ci = c % N;
    const cj = Math.floor(c / N);
    for (const [di, dj] of NB) {
      const ni = ci + di;
      const nj = cj + dj;
      if (ni < 1 || nj < 1 || ni >= N - 1 || nj >= N - 1) continue;
      // Knight moves must not cut across water corners.
      const nc = nj * N + ni;
      if (closed[nc]) continue;
      const d = Math.sqrt(di * di + dj * dj) * CELL;
      const sc = stepCost(c, nc, d, o);
      if (!Number.isFinite(sc)) continue;
      const ng = g[c] + sc;
      if (ng < g[nc]) {
        g[nc] = ng;
        came[nc] = c;
        heap.push(ng + h(nc), nc);
      }
    }
  }
  if (found < 0) throw new Error(`no route (${o.cls}) from (${startX.toFixed(0)}, ${startZ.toFixed(0)})`);
  const path: number[] = [];
  for (let c = found; c >= 0; c = came[c]) path.push(c);
  path.reverse();
  return path;
}

type XZ = [number, number];

/** Douglas-Peucker simplification. */
function simplify(pts: XZ[], tol: number): XZ[] {
  if (pts.length < 3) return pts;
  const keep = new Uint8Array(pts.length);
  keep[0] = keep[pts.length - 1] = 1;
  const stack: [number, number][] = [[0, pts.length - 1]];
  while (stack.length) {
    const [a, b] = stack.pop() as [number, number];
    let best = -1;
    let bi = -1;
    const [ax, az] = pts[a];
    const [bx, bz] = pts[b];
    const dx = bx - ax;
    const dz = bz - az;
    const l = Math.sqrt(dx * dx + dz * dz) || 1;
    for (let i = a + 1; i < b; i++) {
      const d = Math.abs((pts[i][0] - ax) * dz - (pts[i][1] - az) * dx) / l;
      if (d > best) {
        best = d;
        bi = i;
      }
    }
    if (best > tol) {
      keep[bi] = 1;
      stack.push([a, bi], [bi, b]);
    }
  }
  return pts.filter((_, i) => keep[i]);
}

/** Chaikin corner cutting, keeping the endpoints. */
function chaikin(pts: XZ[], iterations: number): XZ[] {
  let p = pts;
  for (let k = 0; k < iterations; k++) {
    const out: XZ[] = [p[0]];
    for (let i = 0; i + 1 < p.length; i++) {
      const [ax, az] = p[i];
      const [bx, bz] = p[i + 1];
      out.push([ax * 0.75 + bx * 0.25, az * 0.75 + bz * 0.25], [ax * 0.25 + bx * 0.75, az * 0.25 + bz * 0.75]);
    }
    out.push(p[p.length - 1]);
    p = out;
  }
  return p;
}

// --- Graph ---------------------------------------------------------------
interface Node {
  id: string;
  x: number;
  z: number;
}
interface Edge {
  cls: RoadClassId;
  a: number;
  b: number;
  pts: XZ[];
}
const nodes: Node[] = [];
const edges: Edge[] = [];
const nodeIndex = new Map<string, number>();

function node(id: string, x: number, z: number): number {
  const i = nodes.push({ id, x, z }) - 1;
  nodeIndex.set(id, i);
  return i;
}
function nid(id: string): number {
  const i = nodeIndex.get(id);
  if (i === undefined) throw new Error(`unknown node ${id}`);
  return i;
}

function markEdge(e: Edge, idx: number): void {
  for (let k = 0; k + 1 < e.pts.length; k++) {
    const [ax, az] = e.pts[k];
    const [bx, bz] = e.pts[k + 1];
    const len = Math.sqrt((bx - ax) ** 2 + (bz - az) ** 2);
    const steps = Math.max(1, Math.ceil(len / (CELL * 0.5)));
    for (let t = 0; t <= steps; t++) {
      const x = ax + ((bx - ax) * t) / steps;
      const z = az + ((bz - az) * t) / steps;
      const c = cellOf(x, z);
      ROADCELL[c] = idx + 1;
      const ci = c % N;
      const cj = Math.floor(c / N);
      for (let dj = -4; dj <= 4; dj++) for (let di = -4; di <= 4; di++) {
        const ii = ci + di;
        const jj = cj + dj;
        if (ii >= 0 && jj >= 0 && ii < N && jj < N) HALO[jj * N + ii] = 1;
      }
    }
  }
}

function addEdge(cls: RoadClassId, a: number, b: number, pts: XZ[]): number {
  const e: Edge = { cls, a, b, pts };
  const idx = edges.push(e) - 1;
  markEdge(e, idx);
  return idx;
}

/** Nearest point on any existing edge: returns edge, segment and point. */
function nearestOnNetwork(x: number, z: number): { e: number; k: number; px: number; pz: number; d: number } {
  let best = { e: -1, k: -1, px: 0, pz: 0, d: Infinity };
  edges.forEach((e, ei) => {
    for (let k = 0; k + 1 < e.pts.length; k++) {
      const [ax, az] = e.pts[k];
      const [bx, bz] = e.pts[k + 1];
      const dx = bx - ax;
      const dz = bz - az;
      const l2 = dx * dx + dz * dz;
      let t = l2 > 0 ? ((x - ax) * dx + (z - az) * dz) / l2 : 0;
      t = Math.max(0, Math.min(1, t));
      const px = ax + dx * t;
      const pz = az + dz * t;
      const d = Math.sqrt((x - px) ** 2 + (z - pz) ** 2);
      if (d < best.d) best = { e: ei, k, px, pz, d };
    }
  });
  return best;
}

/** Split an edge at a point on segment k, returning the new junction node. */
function splitEdge(ei: number, k: number, px: number, pz: number, id: string): number {
  const e = edges[ei];
  // Reuse an endpoint if the split lands close to it.
  const endA = nodes[e.a];
  const endB = nodes[e.b];
  if (Math.hypot(px - endA.x, pz - endA.z) < 25) return e.a;
  if (Math.hypot(px - endB.x, pz - endB.z) < 25) return e.b;
  const n = node(id, px, pz);
  const first = [...e.pts.slice(0, k + 1), [px, pz] as XZ];
  const second = [[px, pz] as XZ, ...e.pts.slice(k + 1)];
  edges[ei] = { cls: e.cls, a: e.a, b: n, pts: first };
  edges.push({ cls: e.cls, a: n, b: e.b, pts: second });
  return n;
}

let junctions = 0;
function polyFromCells(path: number[], cls: RoadClassId): XZ[] {
  const raw: XZ[] = path.map((c) => [cx(c), cz(c)]);
  const tol = cls === 'highway' ? 9 : cls === 'dirt' ? 5 : 6;
  return chaikin(simplify(raw, tol), 1).map(([x, z]) => [Math.round(x * 2) / 2, Math.round(z * 2) / 2]);
}

/** Route node to node through optional waypoints. */
function link(cls: RoadClassId, ids: string[]): void {
  let pts: XZ[] = [];
  for (let i = 0; i + 1 < ids.length; i++) {
    const a = nodes[nid(ids[i])];
    const b = nodes[nid(ids[i + 1])];
    const p = polyFromCells(route(cellOf(a.x, a.z), cellOf(b.x, b.z), { cls, reuse: 1 }), cls);
    p[0] = [a.x, a.z];
    p[p.length - 1] = [b.x, b.z];
    pts = pts.length ? [...pts, ...p.slice(1)] : p;
  }
  addEdge(cls, nid(ids[0]), nid(ids[ids.length - 1]), pts);
}

/** Route from a node to the nearest part of the existing network. */
function join(cls: RoadClassId, id: string): void {
  const a = nodes[nid(id)];
  const path = route(cellOf(a.x, a.z), -1, { cls, toNetwork: true, reuse: 1 });
  const pts = polyFromCells(path, cls);
  pts[0] = [a.x, a.z];
  const [ex, ez] = pts[pts.length - 1];
  const hit = nearestOnNetwork(ex, ez);
  const j = splitEdge(hit.e, hit.k, hit.px, hit.pz, `j${junctions++}`);
  pts[pts.length - 1] = [nodes[j].x, nodes[j].z];
  addEdge(cls, nid(id), j, pts);
}

// Key places.
const P = PROVING_ORIGIN;
node('hub', 900, 3330);
node('hubEast', 1300, 3230);
node('proving', P.x - 652, P.z - 62); // the paddock's west entrance
node('southeast', 3350, 2600);
node('eastCoast', 4050, 1500);
node('citySouth', 3100, 560);
node('cityWest', 2560, -160);
node('cityNorth', 3300, -900);
node('harbor', 3950, -1350);
node('lakeSouth', 120, 260);
node('lakeWest', -480, -560);
node('lakeNorth', 250, -1250);
node('lakeEast', 980, -560);
node('passSouth', 400, -1950);
node('passTop', 380, -3120);
node('northVillage', 450, -3950);
node('farmVillage', -900, 1950);
node('riverEast', -250, 3420);
node('swBeach', -2250, 2800);
node('westCoast', -3450, 1950);
node('desertTown', -2950, 1250);
node('canyonNorth', -3650, -250);
node('rally', -2450, -1150);
node('forestEast', 1900, -1500);

// Every key place must be on dry land.
{
  const bad: string[] = [];
  for (const n of nodes) {
    const c = cellOf(n.x, n.z);
    if (WATER[c]) bad.push(`${n.id} (${n.x}, ${n.z}) water=${WATER[c]} h=${H[c].toFixed(1)}`);
  }
  if (bad.length) throw new Error(`nodes in water:\n  ${bad.join('\n  ')}`);
  for (const n of nodes) console.log(`  ${n.id.padEnd(13)} (${n.x}, ${n.z}) h=${H[cellOf(n.x, n.z)].toFixed(0)} m`);
}

// Highways first, then main roads, the pass and dirt tracks.
link('highway', ['hub', 'hubEast', 'southeast', 'eastCoast', 'citySouth']);
link('highway', ['hub', 'riverEast', 'swBeach', 'westCoast']);
link('highway', ['cityWest', 'lakeEast']);
link('main', ['cityNorth', 'harbor']);
link('main', ['hubEast', 'proving']);
join('main', 'farmVillage');
link('main', ['farmVillage', 'lakeSouth']);
link('main', ['lakeSouth', 'lakeEast']);
link('main', ['lakeEast', 'lakeNorth']);
link('main', ['lakeNorth', 'lakeWest']);
link('main', ['lakeWest', 'lakeSouth']);
link('pass', ['lakeNorth', 'passSouth', 'passTop', 'northVillage']);
link('main', ['desertTown', 'farmVillage']);
link('main', ['desertTown', 'westCoast']);
link('main', ['canyonNorth', 'desertTown']);
link('dirt', ['lakeWest', 'rally', 'canyonNorth']);
link('main', ['lakeEast', 'forestEast', 'cityNorth']);
join('dirt', 'swBeach');

// City grid: avenues and streets on the city platform, joined to the network.
const city = PLATEAUS.find((p) => p.region === 5);
if (city) {
  const xs = [2700, 2900, 3100, 3300, 3500, 3700];
  const zs = [-760, -560, -360, -160, 40, 240, 440];
  const gid = (i: number, j: number): string => `c${i}_${j}`;
  xs.forEach((x, i) => zs.forEach((z, j) => node(gid(i, j), x, z)));
  for (let i = 0; i < xs.length; i++) {
    for (let j = 0; j < zs.length; j++) {
      if (i + 1 < xs.length) addEdge('street', nid(gid(i, j)), nid(gid(i + 1, j)), [[xs[i], zs[j]], [xs[i + 1], zs[j]]]);
      if (j + 1 < zs.length) addEdge('street', nid(gid(i, j)), nid(gid(i, j + 1)), [[xs[i], zs[j]], [xs[i], zs[j + 1]]]);
    }
  }
  // Connect the grid to the highway ends.
  addEdge('street', nid('cityWest'), nid(gid(0, 3)), [[2560, -160], [2700, -160]]);
  addEdge('street', nid('citySouth'), nid(gid(2, 6)), [[3100, 560], [3100, 440]]);
  addEdge('street', nid('cityNorth'), nid(gid(3, 0)), [[3300, -900], [3300, -760]]);
}

// Insert junctions where edges cross.
function segIntersect(a: XZ, b: XZ, c: XZ, d: XZ): [number, number] | null {
  const r = [b[0] - a[0], b[1] - a[1]];
  const s2 = [d[0] - c[0], d[1] - c[1]];
  const den = r[0] * s2[1] - r[1] * s2[0];
  if (Math.abs(den) < 1e-9) return null;
  const t = ((c[0] - a[0]) * s2[1] - (c[1] - a[1]) * s2[0]) / den;
  const u = ((c[0] - a[0]) * r[1] - (c[1] - a[1]) * r[0]) / den;
  if (t <= 0.001 || t >= 0.999 || u <= 0.001 || u >= 0.999) return null;
  return [t, u];
}
let crossings = 0;
for (let pass = 0; pass < 200; pass++) {
  let changed = false;
  outer: for (let e1 = 0; e1 < edges.length; e1++) {
    for (let e2 = e1 + 1; e2 < edges.length; e2++) {
      const A = edges[e1];
      const B = edges[e2];
      if (A.a === B.a || A.a === B.b || A.b === B.a || A.b === B.b) {
        // Shared endpoints are fine; still check interior crossings below.
      }
      for (let i = 0; i + 1 < A.pts.length; i++) {
        for (let k = 0; k + 1 < B.pts.length; k++) {
          const hit = segIntersect(A.pts[i], A.pts[i + 1], B.pts[k], B.pts[k + 1]);
          if (!hit) continue;
          const px = A.pts[i][0] + (A.pts[i + 1][0] - A.pts[i][0]) * hit[0];
          const pz = A.pts[i][1] + (A.pts[i + 1][1] - A.pts[i][1]) * hit[0];
          // Ignore hits next to an existing shared node.
          const near = nodes.some((n) => Math.hypot(n.x - px, n.z - pz) < 15);
          if (near) continue;
          const n = splitEdge(e1, i, px, pz, `x${crossings++}`);
          // Find B's segment again (it is unchanged) and split it at the same node.
          const nb = nodes[n];
          const sB = splitEdge(e2, k, nb.x, nb.z, `x${crossings}`);
          if (sB !== n) {
            // Merge the two nodes: point B's new edges at n.
            for (const e of edges) {
              if (e.a === sB) e.a = n;
              if (e.b === sB) e.b = n;
            }
          }
          changed = true;
          break outer;
        }
      }
    }
  }
  if (!changed) break;
}

// Drop orphan nodes and write the data file.
const used = new Set<number>();
for (const e of edges) {
  used.add(e.a);
  used.add(e.b);
}
const remap = new Map<number, number>();
const outNodes: [string, number, number][] = [];
nodes.forEach((n, i) => {
  if (!used.has(i)) return;
  remap.set(i, outNodes.length);
  outNodes.push([n.id, Math.round(n.x * 2) / 2, Math.round(n.z * 2) / 2]);
});
const classIds = Object.keys(ROAD_CLASSES) as RoadClassId[];
const outEdges = edges.map((e) => [classIds.indexOf(e.cls), remap.get(e.a), remap.get(e.b), e.pts.slice(1, -1).flat()]);
let total = 0;
for (const e of edges) for (let k = 0; k + 1 < e.pts.length; k++) total += Math.hypot(e.pts[k + 1][0] - e.pts[k][0], e.pts[k + 1][1] - e.pts[k][1]);
const file = `// Generated by tools/bake-roads.ts. Do not edit by hand.
// ${outNodes.length} nodes, ${outEdges.length} edges, ${(total / 1000).toFixed(1)} km.
export const ROAD_CLASS_ORDER = ${JSON.stringify(classIds)} as const;
/** [id, x, z] */
export const ROAD_NODES: [string, number, number][] = ${JSON.stringify(outNodes)};
/** [class index, node a, node b, interior control points x0,z0,x1,z1...] */
export const ROAD_EDGES: [number, number, number, number[]][] = ${JSON.stringify(outEdges)};
`;
fs.mkdirSync('src/world/data', { recursive: true });
fs.writeFileSync('src/world/data/roads.ts', file);
console.log(`baked ${outNodes.length} nodes, ${outEdges.length} edges, ${(total / 1000).toFixed(1)} km, ${crossings} crossings, in ${((Date.now() - t0) / 1000).toFixed(1)} s`);
