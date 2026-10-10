// The Halcyon Festival site on the hub plateau: layout of the stage, the
// big wheel, pavilion tents, the entrance arch and flag lines, plus their
// colliders. Deterministic world data shared by the physics and renderer.
import type { World } from './world';
import type { Colliders } from './colliders';
import { RNG } from '../engine/rng';
import { datan2 } from '../engine/dmath';

export interface FestivalPiece {
  x: number;
  z: number;
  /** Facing direction (unit). */
  fx: number;
  fz: number;
  y: number;
  /** Size: width (across facing), depth, height. */
  w: number;
  d: number;
  h: number;
  color: number;
}

export interface FestivalLayout {
  stage: FestivalPiece;
  wheel: FestivalPiece;
  arch: FestivalPiece;
  tents: FestivalPiece[];
  /** Flag poles (x, y, z, colour) along the festival road. */
  poles: FestivalPiece[];
  /** Where a new session starts: on the festival road, facing the arch. */
  spawn: { x: number; z: number; yaw: number };
}

export const FESTIVAL_COLORS = [0xff6b2c, 0x14b8a6, 0xe0337a, 0xf5f0e6, 0xffc23d, 0x3b82f6];

export function buildFestival(world: World): FestivalLayout {
  const rng = new RNG(`${world.island.seed}:festival`);
  const hub = world.nodes.find((n) => n.id === 'hub') ?? world.nodes[0];
  const y = world.terrainHeight(hub.x, hub.z);
  // The highway through the hub runs east-west; its tangent at the hub.
  const hit = { edge: 0, i: 0, t: 0, lateral: 0, dist: 0, y: 0, halfWidth: 0, bridge: false };
  world.nearestRoad(hub.x + 1, hub.z + 1, 50, hit);
  const r = world.edges[hit.edge].road;
  let tx = r.tx[hit.i];
  let tz = r.tz[hit.i];
  if (tx < 0) {
    tx = -tx;
    tz = -tz;
  }
  // North of the road (-z side) is the stage field, south the tents.
  const nx = tz;
  const nz = -tx;
  const at = (along: number, side: number): [number, number] => [hub.x + tx * along + nx * side, hub.z + tz * along + nz * side];
  const [sx, sz] = at(0, 150);
  const stage: FestivalPiece = { x: sx, z: sz, fx: -nx, fz: -nz, y, w: 40, d: 18, h: 16, color: 0 };
  const [wx, wz] = at(-230, 110);
  const wheel: FestivalPiece = { x: wx, z: wz, fx: -nx, fz: -nz, y, w: 46, d: 8, h: 50, color: 0 };
  // The arch spans the highway east of the hub, facing travellers from the west.
  let ai = hit.i;
  let along = 0;
  while (ai + 1 < r.n && along < 260) {
    along += r.s[ai + 1] - r.s[ai];
    ai++;
  }
  const ax = r.x[ai];
  const az = r.z[ai];
  const atx = r.tx[ai] * (r.tx[ai] < 0 ? -1 : 1);
  const atz = r.tz[ai] * (r.tx[ai] < 0 ? -1 : 1);
  const arch: FestivalPiece = { x: ax, z: az, fx: -atx, fz: -atz, y: world.terrainHeight(ax, az), w: world.edges[hit.edge].info.halfWidth * 2 + 10, d: 3, h: 12, color: 0 };
  const tents: FestivalPiece[] = [];
  for (let row = 0; row < 4; row++) {
    for (let col = -6; col <= 6; col++) {
      const [px, pz] = at(col * 22 + (row % 2) * 11, -(60 + row * 24));
      const probe = { ...hit };
      if (world.nearestRoad(px, pz, 14, probe)) continue;
      if (rng.chance(0.12)) continue;
      const size = rng.range(8, 12);
      tents.push({ x: px, z: pz, fx: nx, fz: nz, y: world.terrainHeight(px, pz), w: size, d: size, h: rng.range(5.5, 7.5), color: FESTIVAL_COLORS[rng.int(0, FESTIVAL_COLORS.length)] });
    }
  }
  const poles: FestivalPiece[] = [];
  const hw = world.edges[hit.edge].info.halfWidth;
  for (let k = -12; k <= 10; k++) {
    for (const side of [-1, 1]) {
      const [px, pz] = at(k * 24, side * (hw + 5));
      poles.push({ x: px, z: pz, fx: tx, fz: tz, y: world.terrainHeight(px, pz), w: 0.2, d: 0.2, h: 9, color: FESTIVAL_COLORS[(k + 12 + (side > 0 ? 3 : 0)) % FESTIVAL_COLORS.length] });
    }
  }
  // Spawn on the road itself, ~130 m before the arch, in the right-hand
  // lane (right of travel is (-tz, tx)), facing the arch.
  let si = ai;
  let back = 0;
  while (si > 0 && back < 130) {
    back += r.s[si] - r.s[si - 1];
    si--;
  }
  const sdx = r.tx[si] * (r.tx[ai] < 0 ? -1 : 1);
  const sdz = r.tz[si] * (r.tx[ai] < 0 ? -1 : 1);
  const lane = hw * 0.45;
  const spawn = { x: r.x[si] - sdz * lane, z: r.z[si] + sdx * lane, yaw: datan2(-sdx, -sdz) };
  return { stage, wheel, arch, tents, poles, spawn };
}

/** Solid parts of the festival for the physics. */
export function addFestivalColliders(f: FestivalLayout, out: Colliders): void {
  const box = (p: FestivalPiece, w: number, d: number, top: number): void => {
    const rx = -p.fz;
    const rz = p.fx;
    const pts = [
      [-1, -1],
      [1, -1],
      [1, 1],
      [-1, 1],
    ].map(([a, b]) => ({ x: p.x + rx * a * (w / 2) + p.fx * b * (d / 2), z: p.z + rz * a * (w / 2) + p.fz * b * (d / 2) }));
    out.addPolyline(pts, { top: p.y + top, bottom: p.y - 1, bounce: 0.15, friction: 0.5, kind: 'wall' }, true);
  };
  box(f.stage, f.stage.w, f.stage.d, f.stage.h);
  box(f.wheel, 14, 10, 6);
  for (const t of f.tents) box(t, t.w * 0.9, t.d * 0.9, 3);
  // Arch legs either side of the road.
  const rx = -f.arch.fz;
  const rz = f.arch.fx;
  for (const side of [-1, 1]) {
    out.addCircle({ x: f.arch.x + rx * side * (f.arch.w / 2), z: f.arch.z + rz * side * (f.arch.w / 2), r: 1.2, top: f.arch.y + f.arch.h, bottom: f.arch.y - 1, kind: 'pier' });
  }
  for (const p of f.poles) out.addCircle({ x: p.x, z: p.z, r: 0.15, top: p.y + p.h, bottom: p.y - 1, kind: 'post', breakable: true });
}
