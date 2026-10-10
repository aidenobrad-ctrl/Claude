// GPS routing over the island's road graph: A* between two points on the
// network (each snapped to its nearest road), returned as a polyline.
import type { World, RoadHit } from './world';

export interface RoutePoint {
  x: number;
  y: number;
  z: number;
  /** Half width of the road here, m. */
  hw: number;
}

export interface Route {
  points: RoutePoint[];
  /** Total length, m. */
  length: number;
}

function hitAt(world: World, x: number, z: number): RoadHit | null {
  const hit: RoadHit = { edge: 0, i: 0, t: 0, lateral: 0, dist: 0, y: 0, halfWidth: 0, bridge: false };
  // Grow the search until a road is found (the grid holds roads within 60 m).
  for (const reach of [20, 60]) if (world.nearestRoad(x, z, reach, hit)) return hit;
  // Far from any road: brute-force the nearest sample.
  let best = Infinity;
  let found: RoadHit | null = null;
  for (const e of world.edges) {
    const r = e.road;
    for (let i = 0; i + 1 < r.n; i += 4) {
      const d = (r.x[i] - x) * (r.x[i] - x) + (r.z[i] - z) * (r.z[i] - z);
      if (d < best) {
        best = d;
        found = { edge: e.id, i, t: 0, lateral: 0, dist: Math.sqrt(d), y: r.y[i], halfWidth: e.info.halfWidth, bridge: false };
      }
    }
  }
  return found;
}

/** Shortest road route from (ax, az) to (bx, bz), or null if unreachable. */
export function findRoute(world: World, ax: number, az: number, bx: number, bz: number): Route | null {
  const a = hitAt(world, ax, az);
  const b = hitAt(world, bx, bz);
  if (!a || !b) return null;
  const ea = world.edges[a.edge];
  const eb = world.edges[b.edge];
  const sA = ea.road.s[a.i] + (ea.road.s[Math.min(ea.road.n - 1, a.i + 1)] - ea.road.s[a.i]) * a.t;
  const sB = eb.road.s[b.i] + (eb.road.s[Math.min(eb.road.n - 1, b.i + 1)] - eb.road.s[b.i]) * b.t;
  // Same edge: straight along it.
  if (a.edge === b.edge) return { points: slice(world, a.edge, sA, sB), length: Math.abs(sB - sA) };
  const n = world.nodes.length;
  const g = new Float64Array(n).fill(Infinity);
  const came = new Int32Array(n).fill(-1);
  const via = new Int32Array(n).fill(-1);
  const open: number[] = [];
  const h = (k: number): number => {
    const dx = world.nodes[k].x - bx;
    const dz = world.nodes[k].z - bz;
    return Math.sqrt(dx * dx + dz * dz);
  };
  // Seed with both ends of the start edge.
  g[ea.a] = sA;
  g[ea.b] = ea.road.length - sA;
  open.push(ea.a, ea.b);
  // Targets: both ends of the goal edge, plus the remaining distance along it.
  const toGoal = (k: number): number => (k === eb.a ? sB : k === eb.b ? eb.road.length - sB : Infinity);
  let best = Infinity;
  let bestEnd = -1;
  const closed = new Uint8Array(n);
  while (open.length) {
    let bi = 0;
    for (let k = 1; k < open.length; k++) if (g[open[k]] + h(open[k]) < g[open[bi]] + h(open[bi])) bi = k;
    const u = open[bi];
    open.splice(bi, 1);
    if (closed[u]) continue;
    closed[u] = 1;
    if (g[u] >= best) break;
    const tg = toGoal(u);
    if (g[u] + tg < best) {
      best = g[u] + tg;
      bestEnd = u;
    }
    for (const eid of world.nodes[u].edges) {
      const e = world.edges[eid];
      const v = e.a === u ? e.b : e.a;
      const ng = g[u] + e.road.length;
      if (ng < g[v]) {
        g[v] = ng;
        came[v] = u;
        via[v] = eid;
        open.push(v);
      }
    }
  }
  if (bestEnd < 0) return null;
  // Rebuild: start edge part, node chain, goal edge part.
  const chain: number[] = [];
  for (let k = bestEnd; k >= 0; k = came[k]) chain.push(k);
  chain.reverse();
  const pts: RoutePoint[] = [];
  const first = chain[0];
  pts.push(...slice(world, a.edge, sA, first === ea.a ? 0 : ea.road.length));
  for (let k = 1; k < chain.length; k++) {
    const eid = via[chain[k]];
    const e = world.edges[eid];
    const fromA = e.a === chain[k - 1];
    pts.push(...slice(world, eid, fromA ? 0 : e.road.length, fromA ? e.road.length : 0));
  }
  const last = chain[chain.length - 1];
  pts.push(...slice(world, b.edge, last === eb.a ? 0 : eb.road.length, sB));
  return { points: pts, length: best };
}

/** Road samples of an edge between arc lengths s0 and s1 (either direction). */
function slice(world: World, edge: number, s0: number, s1: number): RoutePoint[] {
  const r = world.edges[edge].road;
  const hw = world.edges[edge].info.halfWidth;
  const out: RoutePoint[] = [];
  const lo = Math.min(s0, s1);
  const hi = Math.max(s0, s1);
  for (let i = 0; i < r.n; i++) if (r.s[i] >= lo && r.s[i] <= hi) out.push({ x: r.x[i], y: r.y[i], z: r.z[i], hw });
  if (s1 < s0) out.reverse();
  return out;
}
