// The festival's races on the island (a port of the Unity version's
// EventCatalog and RouteFinder): the proving ground's circuit, sprints over
// the road network between places (A* on the junction graph, the route
// sliced from each road with its drivable width), the hill climb, a drag race
// on the longest straight, a rally on the dirt tracks and a duel.
//
// The road data is read the way the Unity build reads its export (positions
// and widths as 32-bit floats), so the two versions plan the same routes.
import type { Sim } from '../sim';
import { mapPois } from '../ui/map';
import type { World } from '../world/world';
import { newHitRoad, type NetEdge } from '../world/world';
import { SURFACE } from '../world/surfaces';
import { datan2 } from '../engine/dmath';
import { Personality } from './ai-driver';
import { RaceRoute } from './race-route';
import { CURB_WIDTH, TRACK_HALF_WIDTH } from '../world/testtrack';
import type { RaceEvent } from './race-session';

const f32 = Math.fround;

/** Per-sample guardrail offsets of each road ([left, right] pairs; 0 = no rail), as the export carries them. */
function railOffsets(world: World): Map<number, Float32Array> {
  const out = new Map<number, Float32Array>();
  for (const run of world.rails) {
    let a = out.get(run.edge);
    if (!a) {
      a = new Float32Array(world.edges[run.edge].road.n * 2);
      out.set(run.edge, a);
    }
    const k = run.side > 0 ? 1 : 0;
    for (let s = run.i0; s <= run.i1; s++) a[s * 2 + k] = run.offset;
  }
  return out;
}

export class RouteFinder {
  private readonly rails: Map<number, Float32Array>;

  constructor(private readonly world: World) {
    this.rails = railOffsets(world);
  }

  /** The road route from (ax, az) to (bx, bz), each snapped to its nearest road; null if unreachable. */
  find(ax: number, az: number, bx: number, bz: number, name: string): RaceRoute | null {
    const w = this.world;
    const a = this.snap(ax, az);
    const b = this.snap(bx, bz);
    if (!a || !b) return null;
    const ea = w.edges[a.edge];
    const eb = w.edges[b.edge];
    const sOf = (e: NetEdge, i: number, t: number): number => f32(e.road.s[i]) + (f32(e.road.s[Math.min(e.road.n - 1, i + 1)]) - f32(e.road.s[i])) * t;
    const sA = sOf(ea, a.i, a.t);
    const sB = sOf(eb, b.i, b.t);
    const xs: number[] = [];
    const ys: number[] = [];
    const zs: number[] = [];
    const ws: number[] = [];
    if (a.edge === b.edge) {
      this.slice(a.edge, sA, sB, xs, ys, zs, ws);
      return xs.length >= 2 ? RaceRoute.fromPolyline(name, xs, ys, zs, ws, ws, false) : null;
    }
    const n = w.nodes.length;
    const g = new Float64Array(n).fill(Infinity);
    const came = new Int32Array(n).fill(-1);
    const via = new Int32Array(n).fill(-1);
    const closed = new Uint8Array(n);
    const H = (k: number): number => {
      const dx = f32(w.nodes[k].x) - bx;
      const dz = f32(w.nodes[k].z) - bz;
      return Math.sqrt(dx * dx + dz * dz);
    };
    const len = (e: NetEdge): number => f32(e.road.length);
    const open: number[] = [];
    g[ea.a] = sA;
    g[ea.b] = len(ea) - sA;
    open.push(ea.a, ea.b);
    const toGoal = (k: number): number => (k === eb.a ? sB : k === eb.b ? len(eb) - sB : Infinity);
    let best = Infinity;
    let bestEnd = -1;
    while (open.length > 0) {
      let bi = 0;
      for (let k = 1; k < open.length; k++) if (g[open[k]] + H(open[k]) < g[open[bi]] + H(open[bi])) bi = k;
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
      for (const eid of w.nodes[u].edges) {
        const e = w.edges[eid];
        const v = e.a === u ? e.b : e.a;
        const ng = g[u] + len(e);
        if (ng < g[v]) {
          g[v] = ng;
          came[v] = u;
          via[v] = eid;
          open.push(v);
        }
      }
    }
    if (bestEnd < 0) return null;
    const chain: number[] = [];
    for (let k = bestEnd; k >= 0; k = came[k]) chain.push(k);
    chain.reverse();
    const first = chain[0];
    this.slice(a.edge, sA, first === ea.a ? 0 : len(ea), xs, ys, zs, ws);
    for (let k = 1; k < chain.length; k++) {
      const eid = via[chain[k]];
      const e = w.edges[eid];
      const fromA = e.a === chain[k - 1];
      this.slice(eid, fromA ? 0 : len(e), fromA ? len(e) : 0, xs, ys, zs, ws);
    }
    const last = chain[chain.length - 1];
    this.slice(b.edge, last === eb.a ? 0 : len(eb), sB, xs, ys, zs, ws);
    // Drop repeated points where edges meet.
    for (let k = xs.length - 1; k > 0; k--) {
      const dx = xs[k] - xs[k - 1];
      const dz = zs[k] - zs[k - 1];
      if (dx * dx + dz * dz < 0.25) {
        xs.splice(k, 1);
        ys.splice(k, 1);
        zs.splice(k, 1);
        ws.splice(k, 1);
      }
    }
    return xs.length >= 2 ? RaceRoute.fromPolyline(name, xs, ys, zs, ws, ws, false) : null;
  }

  private snap(x: number, z: number): { edge: number; i: number; t: number } | null {
    const w = this.world;
    const hit = newHitRoad();
    for (const reach of [20, 60]) if (w.nearestRoad(x, z, reach, hit)) return { edge: hit.edge, i: hit.i, t: hit.t };
    let best = Infinity;
    let found: { edge: number; i: number; t: number } | null = null;
    // Every sample, the last of each road too (a destination off the end of a road is nearest its end).
    for (const e of w.edges) {
      const r = e.road;
      for (let i = 0; i < r.n; i++) {
        const d = (f32(r.x[i]) - x) * (f32(r.x[i]) - x) + (f32(r.z[i]) - z) * (f32(r.z[i]) - z);
        if (d < best) {
          best = d;
          found = { edge: e.id, i, t: 0 };
        }
      }
    }
    return found;
  }

  /** Samples of an edge between arc lengths s0 and s1 (either direction), with the drivable half width. */
  private slice(edge: number, s0: number, s1: number, xs: number[], ys: number[], zs: number[], ws: number[]): void {
    const e = this.world.edges[edge];
    const r = e.road;
    // The paved road only; narrower where a guardrail or parapet stands close.
    const hw = f32(e.info.halfWidth);
    const lo = Math.min(s0, s1);
    const hi = Math.max(s0, s1);
    const start = xs.length;
    const rails = this.rails.get(edge);
    for (let i = 0; i < r.n; i++) {
      const s = f32(r.s[i]);
      if (s < lo || s > hi) continue;
      xs.push(f32(r.x[i]));
      ys.push(f32(r.y[i]));
      zs.push(f32(r.z[i]));
      let h = hw;
      if (rails) {
        for (let k = 0; k < 2; k++) {
          const off = rails[i * 2 + k];
          if (off > 0 && !Number.isNaN(off)) h = Math.min(h, off - 0.5);
        }
      }
      if (e.bridge[i] === 1) h = Math.min(h, hw - 0.3);
      ws.push(Math.max(2.4, h));
    }
    if (s1 < s0) {
      for (const arr of [xs, ys, zs, ws]) {
        const tail = arr.splice(start).reverse();
        arr.push(...tail);
      }
    }
  }
}

/** The festival's rival: the driver to beat head to head. */
export function theRival(): Personality {
  return new Personality({ name: 'Mika Varga', skill: 0.95, aggression: 0.75, consistency: 0.95, risk: 0.65 });
}

function base(id: string, name: string, route: RaceRoute): RaceEvent {
  return { id, name, description: '', kind: 'sprint', laps: 1, route, startS: 0, distance: route.length, gridSize: 8, surface: SURFACE.asphalt, rival: null };
}

/** The proving ground's circuit, from its start line, as a race route. */
export function provingRoute(sim: Sim): RaceRoute {
  const tr = sim.track;
  const road = tr.road;
  const n = road.n;
  const startS = f32(tr.startS);
  let i0 = 0;
  while (i0 + 1 < n && f32(road.s[i0 + 1]) <= startS) i0++;
  const xs: number[] = [];
  const ys: number[] = [];
  const zs: number[] = [];
  const wl: number[] = [];
  // The start point: interpolated on the circuit at the start line.
  const i1 = (i0 + 1) % n;
  const s0 = f32(road.s[i0]);
  const s1 = i1 === 0 ? f32(road.length) : f32(road.s[i1]);
  const t = s1 > s0 ? (startS - s0) / (s1 - s0) : 0;
  const px = (i: number): number => f32(road.x[i]);
  const pz = (i: number): number => f32(road.z[i]);
  const h = f32(tr.height);
  const sx = px(i0) + (px(i1) - px(i0)) * t;
  const sz = pz(i0) + (pz(i1) - pz(i0)) * t;
  xs.push(sx);
  ys.push(h);
  zs.push(sz);
  const w = TRACK_HALF_WIDTH + CURB_WIDTH * 0.6;
  wl.push(w);
  for (let k = 1; k <= n; k++) {
    const i = (i0 + k) % n;
    xs.push(px(i));
    ys.push(h);
    zs.push(pz(i));
    wl.push(w);
  }
  const last = xs.length - 1;
  const ddx = xs[last] - sx;
  const ddz = zs[last] - sz;
  if (ddx * ddx + ddz * ddz < 0.25) {
    xs.pop();
    ys.pop();
    zs.pop();
    wl.pop();
  }
  return RaceRoute.fromPolyline('Proving Ground', xs, ys, zs, wl, wl, true);
}

export function provingGP(sim: Sim, laps = 3): RaceEvent {
  const r = provingRoute(sim);
  return { ...base('proving-gp', 'Proving Ground Grand Prix', r), kind: 'circuit', laps, distance: laps * r.length, description: `${laps} laps of the ${(r.length / 1000).toFixed(1)} km proving circuit.` };
}

/** A point-to-point race over the island's roads between two places. */
export function sprint(finder: RouteFinder, id: string, name: string, ax: number, az: number, bx: number, bz: number): RaceEvent | null {
  const r = finder.find(ax, az, bx, bz, name);
  if (!r || r.length < 800) return null;
  const start = 50;
  const runOff = 160;
  return { ...base(id, name, r), startS: start, distance: r.length - start - runOff, description: `${((r.length - start - runOff) / 1000).toFixed(1)} km on the open road.` };
}

/** Road curvature (1/m) at each sample, over three samples either side. */
function curvature(e: NetEdge): Float64Array {
  const r = e.road;
  const k = new Float64Array(r.n);
  for (let i = 0; i < r.n; i++) {
    const a = Math.max(0, i - 3);
    const b = Math.min(r.n - 1, i + 3);
    let da = datan2(f32(r.tz[b]), f32(r.tx[b])) - datan2(f32(r.tz[a]), f32(r.tx[a]));
    while (da > Math.PI) da -= 2 * Math.PI;
    while (da < -Math.PI) da += 2 * Math.PI;
    const ds = f32(r.s[b]) - f32(r.s[a]);
    k[i] = ds > 0 ? Math.abs(da) / ds : 0;
  }
  return k;
}

/** A drag race: a quarter mile from a standing start on the island's longest straight (no bend under 600 m, no bridge). */
export function drag(world: World): RaceEvent | null {
  let best: NetEdge | null = null;
  let b0 = 0;
  let b1 = 0;
  let bestLen = 0;
  for (const e of world.edges) {
    if (e.cls === 'street' || e.cls === 'dirt') continue;
    const k = curvature(e);
    const r = e.road;
    let start = -1;
    for (let i = 0; i <= r.n; i++) {
      const straight = i < r.n && k[i] < 1 / 600 && e.bridge[i] === 0;
      if (straight && start < 0) start = i;
      if (straight || start < 0) continue;
      const len = f32(r.s[i - 1]) - f32(r.s[start]);
      if (len > bestLen) {
        bestLen = len;
        best = e;
        b0 = start;
        b1 = i - 1;
      }
      start = -1;
    }
  }
  if (!best || bestLen < 650) return null;
  const xs: number[] = [];
  const ys: number[] = [];
  const zs: number[] = [];
  const ws: number[] = [];
  for (let i = b0; i <= b1; i++) {
    xs.push(f32(best.road.x[i]));
    ys.push(f32(best.road.y[i]));
    zs.push(f32(best.road.z[i]));
    ws.push(f32(best.info.halfWidth));
  }
  const r = RaceRoute.fromPolyline('Highway Quarter Mile', xs, ys, zs, ws, ws, false);
  const start0 = 25;
  const quarter = 402.3;
  return { ...base('drag-highway', 'Highway Quarter Mile', r), kind: 'drag', startS: start0, distance: quarter, gridSize: 2, description: `A quarter mile from a standing start, side by side, then ${(r.length - start0 - quarter).toFixed(0)} m to stop.` };
}

/** Every race the web build offers, in the festival menu's order. */
export function festivalEvents(sim: Sim): RaceEvent[] {
  const world = sim.world;
  const finder = new RouteFinder(world);
  const list: RaceEvent[] = [provingGP(sim)];
  const sp = world.festival.spawn;
  const pois = mapPois(world);
  const poi = (name: string): { x: number; z: number } | undefined => pois.find((p) => p.name === name);
  const node = (id: string): { x: number; z: number } | undefined => world.nodes.find((n) => n.id === id);
  const add = (e: RaceEvent | null): void => {
    if (e) list.push(e);
  };
  for (const [id, name, to] of [
    ['sprint-port-calder', 'Festival to Port Calder', 'Port Calder'],
    ['sprint-harbor', 'Harbour Dash', 'Harbor'],
    ['sprint-ashby', 'Ashby Sprint', 'Ashby'],
    ['sprint-saltmarsh', 'Saltmarsh Bay Run', 'Saltmarsh Bay'],
  ] as const) {
    const p = poi(to);
    if (p) add(sprint(finder, id, name, sp.x, sp.z, p.x, p.z));
  }
  // The Highcrest hill climb: from the lake up the pass to the village.
  const lakeNorth = node('lakeNorth');
  const high = poi('Highcrest');
  if (lakeNorth && high) {
    const climb = sprint(finder, 'sprint-highcrest', 'Highcrest Hill Climb', lakeNorth.x, lakeNorth.z, high.x, high.z);
    if (climb) {
      climb.description = `${(climb.distance / 1000).toFixed(1)} km up the mountain pass: hairpins all the way to the village.`;
      list.push(climb);
    }
  }
  add(drag(world));
  // A rally on the dirt tracks, from the canyon over the hills and down to the lake (the AI plans for dirt).
  const lakeWest = node('lakeWest');
  const canyon = node('canyonNorth');
  if (lakeWest && canyon) {
    const rally = sprint(finder, 'rally-redmesa', 'Redmesa Rally', canyon.x, canyon.z, lakeWest.x, lakeWest.z);
    if (rally) {
      rally.surface = SURFACE.dirt;
      rally.description = `${(rally.distance / 1000).toFixed(1)} km of dirt track from the canyon over the hills and down to the lake.`;
      list.push(rally);
    }
  }
  // Head to head with the festival's rival, on the Harbour Dash.
  const harbor = poi('Harbor');
  if (harbor) {
    const duel = sprint(finder, 'duel-harbor', 'Showdown: Mika Varga', sp.x, sp.z, harbor.x, harbor.z);
    if (duel) {
      duel.gridSize = 2;
      duel.rival = theRival();
      duel.description = `${(duel.distance / 1000).toFixed(1)} km to the harbour, one on one with the festival's fastest driver.`;
      list.push(duel);
    }
  }
  return list;
}
