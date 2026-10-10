// Guardrails: steel barriers along every stretch where the ground falls
// away beside a road (embankments, switchbacks, cliff roads). Found from
// the terrain, deterministic, solid for the physics and drawn as W-beams on
// a retaining wall that reaches down to the ground.
import { newHitRoad, type World, type RoadHit } from './world';
import type { Colliders } from './colliders';

export interface RailRun {
  edge: number;
  /** +1 right of the edge direction, -1 left. */
  side: number;
  /** First and last road sample covered. */
  i0: number;
  i1: number;
  /** Lateral distance of the rail from the centreline, m. */
  offset: number;
}

/** Usual lateral position of a rail: just beyond the shoulder, m. */
export function railOffset(world: World, edge: number): number {
  const info = world.edges[edge].info;
  return info.halfWidth + Math.min(info.shoulder, 1.3) + 0.25;
}

/** Tight position at the edge of the asphalt, where the shoulder would stand on another road. */
function tightOffset(world: World, edge: number): number {
  return world.edges[edge].info.halfWidth + 0.35;
}

function onOtherRoad(world: World, edge: number, i: number, x: number, z: number, hit: RoadHit): boolean {
  if (!world.nearestRoad(x, z, 2, hit)) return false;
  if (hit.edge === edge && Math.abs(hit.i - i) <= 4) return false;
  return hit.dist <= hit.halfWidth + 0.25;
}

export function findRailRuns(world: World): RailRun[] {
  const runs: RailRun[] = [];
  const hit = newHitRoad();
  for (const e of world.edges) {
    if (e.info.sidewalk || e.cls === 'dirt') continue;
    const r = e.road;
    const full = railOffset(world, e.id);
    const tight = tightOffset(world, e.id);
    for (const side of [-1, 1]) {
      // Where a rail at either offset would stand on another road.
      const blockedFull = new Uint8Array(r.n);
      const blockedTight = new Uint8Array(r.n);
      for (let i = 0; i < r.n; i++) {
        const nx = -r.tz[i] * side;
        const nz = r.tx[i] * side;
        blockedFull[i] = onOtherRoad(world, e.id, i, r.x[i] + nx * full, r.z[i] + nz * full, hit) ? 1 : 0;
        blockedTight[i] = blockedFull[i] && onOtherRoad(world, e.id, i, r.x[i] + nx * tight, r.z[i] + nz * tight, hit) ? 1 : 0;
      }
      // 0 = none, 1 = rail beyond the shoulder, 2 = tight rail at the asphalt edge.
      const need = new Uint8Array(r.n);
      // Every other sample (8 m) is plenty; odd samples inherit from neighbours.
      for (let i = 0; i < r.n; i += 2) {
        if (e.bridge[i] || blockedTight[i]) continue;
        const nx = -r.tz[i] * side;
        const nz = r.tx[i] * side;
        // Pulled in to the asphalt edge where the shoulder is another road.
        const kind = blockedFull[i] ? 2 : 1;
        const off = kind === 2 ? tight : full;
        // Ground just beyond the rail and a few metres further out.
        const near = world.terrainHeight(r.x[i] + nx * (off + 0.8), r.z[i] + nz * (off + 0.8));
        const far = world.terrainHeight(r.x[i] + nx * (off + 5), r.z[i] + nz * (off + 5));
        const drop = Math.max(r.y[i] - near - 0.6, (r.y[i] - far) / 2.2);
        if (drop > 0.9) {
          for (let k = Math.max(0, i - 1); k <= Math.min(r.n - 1, i + 1); k++) need[k] = Math.max(need[k], kind);
        }
      }
      // Runs, padded at both ends so rails start before the drop; a run with
      // any tight sample is tight throughout. Then split wherever the rail
      // would still stand on another road.
      let i = 0;
      while (i < r.n) {
        if (!need[i]) {
          i++;
          continue;
        }
        let j = i;
        while (j + 1 < r.n && (need[j + 1] || (j + 3 < r.n && need[j + 3]))) j++;
        let tightRun = false;
        for (let k = i; k <= j; k++) if (need[k] === 2) tightRun = true;
        const blocked = tightRun ? blockedTight : blockedFull;
        const lo = Math.max(0, i - 3);
        const hi = Math.min(r.n - 1, j + 3);
        let k = lo;
        while (k <= hi) {
          if (blocked[k] || e.bridge[k]) {
            k++;
            continue;
          }
          let m = k;
          while (m + 1 <= hi && !blocked[m + 1] && !e.bridge[m + 1]) m++;
          if (m - k >= 3) runs.push({ edge: e.id, side, i0: k, i1: m, offset: tightRun ? tight : full });
          k = m + 1;
        }
        i = j + 1;
      }
    }
  }
  return runs;
}

/** Ground height just outside a rail at sample i (the foot of its retaining wall). */
export function railFoot(world: World, run: RailRun, i: number): number {
  const r = world.edges[run.edge].road;
  const o = (run.offset + 0.2) * run.side;
  return world.terrainHeight(r.x[i] - r.tz[i] * o, r.z[i] + r.tx[i] * o) - 0.6;
}

export function addRailColliders(world: World, runs: RailRun[], out: Colliders): void {
  for (const run of runs) {
    const r = world.edges[run.edge].road;
    const off = run.offset * run.side;
    let foot0 = railFoot(world, run, run.i0);
    for (let i = run.i0; i < run.i1; i++) {
      const foot1 = railFoot(world, run, i + 1);
      const y0 = Math.min(r.y[i], r.y[i + 1]);
      const y1 = Math.max(r.y[i], r.y[i + 1]);
      out.addSegment({
        ax: r.x[i] - r.tz[i] * off,
        az: r.z[i] + r.tx[i] * off,
        bx: r.x[i + 1] - r.tz[i + 1] * off,
        bz: r.z[i + 1] + r.tx[i + 1] * off,
        top: y1 + 0.85,
        // The retaining wall below reaches the ground: solid from below too.
        bottom: Math.min(y0 - 2, foot0, foot1),
        bounce: 0.25,
        friction: 0.3,
        kind: 'rail',
      });
      foot0 = foot1;
    }
  }
}
