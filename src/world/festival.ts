// The Halcyon Festival site on the hub plateau: layout of the stage, the
// big wheel, pavilion tents, the entrance arch and flag lines; the cars on
// show, the car park, the food trucks, the stage's light towers and the
// crowds behind their barriers; plus their colliders. Deterministic world
// data shared by the physics and renderer (and exported to Unity).
import type { World } from './world';
import type { Colliders } from './colliders';
import { RNG } from '../engine/rng';
import { datan2, dcos, dsin } from '../engine/dmath';

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

/** A car standing still: on show on a plinth, or parked. */
export interface FestivalCar {
  x: number;
  z: number;
  y: number;
  /** Facing direction (unit). */
  fx: number;
  fz: number;
  specId: string;
  /** Its paint (0xRRGGBB), or -1 for the car's own colour. */
  paint: number;
}

/** An area where a crowd stands (inside barriers): centre, facing (towards what they watch), size and head count. */
export interface FestivalCrowd {
  x: number;
  z: number;
  y: number;
  fx: number;
  fz: number;
  /** Across the facing, and along it, m. */
  w: number;
  d: number;
  count: number;
}

export interface FestivalLayout {
  stage: FestivalPiece;
  wheel: FestivalPiece;
  arch: FestivalPiece;
  tents: FestivalPiece[];
  /** Flag poles (x, y, z, colour) along the festival road. */
  poles: FestivalPiece[];
  /** Cars on show on round plinths (DISPLAY_PLINTH: radius and height). */
  displays: FestivalCar[];
  /** The car park beyond the tents. */
  parked: FestivalCar[];
  /** Food trucks facing the food court. */
  trucks: FestivalPiece[];
  /** Light and speaker towers at the corners of the stage's pit. */
  towers: FestivalPiece[];
  /** Crowd barriers (BARRIER_HEIGHT tall) as polylines on the ground, closed when the first and last points meet. */
  barriers: { x: number; z: number; y: number }[][];
  crowds: FestivalCrowd[];
  /** The car park's gravel: centre, facing (along the bays), size across the rows and along the bays. */
  carPark: { x: number; z: number; y: number; fx: number; fz: number; w: number; d: number };
  /** Where a new session starts: on the festival road, facing the arch. */
  spawn: { x: number; z: number; yaw: number };
}

/** The crowd barriers' height, m. */
export const BARRIER_HEIGHT = 1.1;
/** The plinths under the cars on show: radius and height, m. */
export const DISPLAY_PLINTH = { r: 3.3, h: 0.35 };
/** The festival's cars, in show order (the twelve of the roster). */
const SHOW_CARS = ['orsini-saetta', 'halden-aster-gt', 'arcline-vanta', 'stallworth-ironside-454', 'hoshida-kiri-rs', 'nordvik-varg', 'aurelle-vif-r', 'ironbark-ridgeback', 'halden-280-sprint', 'nordvik-kyst-sport', 'stallworth-prairie', 'aurelle-pico'];
/** Everyday paints for the car park. */
const PARK_PAINTS = [0xf2f2f0, 0x18191b, 0xa7abb0, 0x5b6067, 0x7d1b1f, 0x1f3b6b, 0x2d4a3a, 0xd9d4c7, 0x9a2d22, 0x3b4c63, 0xe8e5dc, 0x2a2a2e];

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
  // Clear of every road (the layout is on the hub plateau, where the highway and its junctions run).
  const clear = (x: number, z: number, m: number): boolean => !world.nearestRoad(x, z, m, { ...hit });
  const facing = (fx: number, fz: number, turn: number): [number, number] => [fx * dcos(turn) - fz * dsin(turn), fx * dsin(turn) + fz * dcos(turn)];

  // The stage's pit: a crowd behind a U of barriers from the stage's front corners, a tower at each corner.
  const front = 150 - stage.d / 2;
  const pitHalf = stage.w / 2 + 7;
  const pitDepth = 41;
  const pt = (along: number, side: number): { x: number; z: number; y: number } => {
    const [x, z] = at(along, side);
    return { x, z, y: world.terrainHeight(x, z) };
  };
  const barriers: { x: number; z: number; y: number }[][] = [
    [pt(-stage.w / 2, front), pt(-pitHalf, front), pt(-pitHalf, front - pitDepth), pt(pitHalf, front - pitDepth), pt(pitHalf, front), pt(stage.w / 2, front)],
  ];
  const crowds: FestivalCrowd[] = [];
  {
    const [cx, cz] = at(0, front - pitDepth / 2);
    crowds.push({ x: cx, z: cz, y: world.terrainHeight(cx, cz), fx: nx, fz: nz, w: pitHalf * 2 - 3, d: pitDepth - 3, count: 260 });
  }
  const towers: FestivalPiece[] = [];
  for (const [al, sd] of [
    [-pitHalf - 4, front - pitDepth - 2],
    [pitHalf + 4, front - pitDepth - 2],
    [-pitHalf - 4, front + 4],
    [pitHalf + 4, front + 4],
  ]) {
    const [x, z] = at(al, sd);
    towers.push({ x, z, fx: -nx, fz: -nz, y: world.terrainHeight(x, z), w: 1.2, d: 1.2, h: 15, color: 0 });
  }

  // The cars on show: an arc of plinths east of the stage, each car turned a little towards the road.
  const displays: FestivalCar[] = [];
  for (let k = 0; k < 7; k++) {
    const [x, z] = at(64 + k * 22, 60 + 9 * dsin((k / 6) * Math.PI));
    if (!clear(x, z, DISPLAY_PLINTH.r + 10)) continue;
    const [fx, fz] = facing(-nx, -nz, (k % 2 ? 1 : -1) * 0.45);
    displays.push({ x, z, y: world.terrainHeight(x, z), fx, fz, specId: SHOW_CARS[k], paint: -1 });
  }

  // The food court west of the stage: six trucks facing a crowd, the whole court fenced.
  const trucks: FestivalPiece[] = [];
  for (let k = 0; k < 6; k++) {
    const [x, z] = at(-66 - k * 13, 72);
    if (!clear(x, z, 12)) continue;
    trucks.push({ x, z, fx: -nx, fz: -nz, y: world.terrainHeight(x, z), w: 6.4, d: 2.5, h: 3.3, color: FESTIVAL_COLORS[(k * 2 + 1) % FESTIVAL_COLORS.length] });
  }
  barriers.push([pt(-55, 38), pt(-142, 38), pt(-142, 80), pt(-55, 80), pt(-55, 38)]);
  {
    const [cx, cz] = at(-98.5, 55);
    crowds.push({ x: cx, z: cz, y: world.terrainHeight(cx, cz), fx: nx, fz: nz, w: 80, d: 24, count: 170 });
  }

  // The car park beyond the tents: two rows nose to nose.
  const parked: FestivalCar[] = [];
  for (let row = 0; row < 2; row++) {
    for (let k = -5; k <= 5; k++) {
      const [x, z] = at(k * 3.4, -162 - row * 5.6);
      if (!clear(x, z, 10) || rng.chance(0.1)) continue;
      const [fx, fz] = row ? [nx, nz] : [-nx, -nz];
      parked.push({ x, z, y: world.terrainHeight(x, z), fx, fz, specId: rng.pick(SHOW_CARS), paint: rng.pick(PARK_PAINTS) });
    }
  }

  const [px, pz] = at(0, -164.8);
  const carPark = { x: px, z: pz, y: world.terrainHeight(px, pz), fx: nx, fz: nz, w: 11 * 3.4 + 6, d: 16 };

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
  return { stage, wheel, arch, tents, poles, displays, parked, trucks, towers, barriers, crowds, carPark, spawn };
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

/**
 * The festival's newer solid parts: plinths, parked cars, food trucks, towers and the
 * barriers (added after the rest, so the older colliders keep their places).
 */
export function addFestivalExtraColliders(f: FestivalLayout, out: Colliders): void {
  for (const d of f.displays) out.addCircle({ x: d.x, z: d.z, r: DISPLAY_PLINTH.r, top: d.y + DISPLAY_PLINTH.h + 1.3, bottom: d.y - 1, kind: 'pier' });
  const footprint = (x: number, z: number, fx: number, fz: number, w: number, d: number, top: number, bottom: number): void => {
    const rx = -fz;
    const rz = fx;
    const pts = [
      [-1, -1],
      [1, -1],
      [1, 1],
      [-1, 1],
    ].map(([a, b]) => ({ x: x + rx * a * (w / 2) + fx * b * (d / 2), z: z + rz * a * (w / 2) + fz * b * (d / 2) }));
    out.addPolyline(pts, { top, bottom, bounce: 0.15, friction: 0.5, kind: 'wall' }, true);
  };
  for (const c of f.parked) footprint(c.x, c.z, c.fx, c.fz, 1.9, 4.5, c.y + 1.35, c.y - 1);
  for (const t of f.trucks) footprint(t.x, t.z, t.fx, t.fz, t.w, t.d, t.y + t.h, t.y - 1);
  for (const t of f.towers) out.addCircle({ x: t.x, z: t.z, r: 0.85, top: t.y + t.h, bottom: t.y - 1, kind: 'pier' });
  for (const b of f.barriers) {
    for (let i = 0; i + 1 < b.length; i++) {
      const p = b[i];
      const q = b[i + 1];
      out.addSegment({ ax: p.x, az: p.z, bx: q.x, bz: q.z, top: Math.max(p.y, q.y) + BARRIER_HEIGHT, bottom: Math.min(p.y, q.y) - 1, bounce: 0.25, friction: 0.4, kind: 'barrier' });
    }
  }
}

/** Ground the festival keeps bare of grass blades: the car park's gravel and under the plinths. */
export function festivalBare(f: FestivalLayout, x: number, z: number): boolean {
  const p = f.carPark;
  const dx = x - p.x;
  const dz = z - p.z;
  if (Math.abs(dx * p.fx + dz * p.fz) < p.d / 2 && Math.abs(-dx * p.fz + dz * p.fx) < p.w / 2) return true;
  const r = DISPLAY_PLINTH.r + 0.4;
  for (const d of f.displays) if ((x - d.x) * (x - d.x) + (z - d.z) * (z - d.z) < r * r) return true;
  return false;
}
