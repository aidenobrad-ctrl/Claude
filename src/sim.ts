// The simulation: everything that advances on the fixed 240 Hz clock.
// It must not touch the DOM or three.js so it can run headless in Node.
import { SIM_DT } from './engine/loop';
import { RNG } from './engine/rng';
import { dcos, dsin, datan2 } from './engine/dmath';
import type { Controls } from './engine/input';
import { Vehicle, defaultAids } from './vehicles/vehicle';
import { buildParams } from './vehicles/params';
import { CARS } from './vehicles/cars';
import { PlayerControlFilter } from './vehicles/controls';
import { collideCars, collideWorld } from './vehicles/collision';
import { buildTestTrack, TestTrackGround, type TrackLayout } from './world/testtrack';
import { newNearest } from './world/road';
import { applyBumps, newHit, type Ground, type GroundHit } from './world/ground';
import type { Colliders } from './world/colliders';
import { World, WorldGround, addBridgeRails, addBuildingColliders, type RoadHit, type TreeCircle } from './world/world';
import { PROVING_ORIGIN, PLATEAUS, REGION } from './world/island';

export interface SimCar {
  id: number;
  name: string;
  specId: string;
  vehicle: Vehicle;
  player: boolean;
}

export type SimEventKind = 'impact' | 'break' | 'lap' | 'reset';

export interface SimEvent {
  kind: SimEventKind;
  carId: number;
  x: number;
  z: number;
  /** Impulse (N·s) for impacts, lap time (s) for laps. */
  value: number;
  tick: number;
}

export interface LapState {
  laps: number;
  start: number;
  last: number;
  best: number;
  sector: number;
  /** Arc length along the circuit from the start line, m. */
  s: number;
}

export interface SimState {
  tick: number;
  time: number;
  pos: [number, number, number];
  vel: [number, number, number];
  yaw: number;
  speed: number;
  kmh: number;
  gear: number;
  rpm: number;
  grounded: number;
  lap: LapState;
  onRoad: boolean;
  lateral: number;
}

export class Sim {
  tick = 0;
  time = 0;
  readonly seed: number;
  rng: RNG;
  readonly world: World;
  readonly track: TrackLayout;
  readonly trackGround: TestTrackGround;
  readonly ground: Ground;
  readonly colliders: Colliders;
  /** Where new sessions start: on the island's roads, or at the proving ground. */
  spawnAt: 'island' | 'proving' = 'island';
  cars: SimCar[] = [];
  player!: SimCar;
  readonly filter = new PlayerControlFilter();
  /** Presentation events since the last drain (impacts, laps, resets). */
  events: SimEvent[] = [];
  lap: LapState = { laps: 0, start: 0, last: NaN, best: NaN, sector: 0, s: 0 };
  private near = newNearest();
  private nextId = 1;

  constructor(seed = 1, spawnAt: 'island' | 'proving' = 'island') {
    this.seed = seed;
    this.rng = new RNG(seed);
    this.spawnAt = spawnAt;
    this.world = new World(seed);
    const plateau = PLATEAUS.find((p) => p.region === REGION.proving);
    const h = plateau ? plateau.height : 12;
    this.track = buildTestTrack(PROVING_ORIGIN.x, PROVING_ORIGIN.z, h);
    this.trackGround = new TestTrackGround(this.track);
    const wg = new WorldGround(this.world);
    const tg = this.trackGround;
    const o = this.track.origin;
    // The proving ground's paved areas sit on its flat plateau.
    wg.overlay = (x: number, z: number, out: GroundHit): boolean => {
      if (x < o.x - 800 || x > o.x + 720 || z < o.z - 130 || z > o.z + 540) return false;
      const surf = tg.pavedAt(x, z);
      if (surf < 0) return false;
      out.y = h;
      out.nx = 0;
      out.ny = 1;
      out.nz = 0;
      out.surface = surf as GroundHit['surface'];
      out.water = 0;
      applyBumps(x, z, out);
      return true;
    };
    this.ground = wg;
    this.colliders = this.track.colliders;
    addBridgeRails(this.world, this.colliders);
    addBuildingColliders(this.world, this.colliders);
    // Trees are generated per terrain tile; the world hands them to collisions on demand.
    this.colliders.extra = (x, z, r, cb) => this.world.queryTrees(x, z, r, cb as (c: TreeCircle) => void);
    this.reset();
  }

  /** Height of the drivable surface at (x, z) (top surface, for placing cars). */
  groundHeight(x: number, z: number): number {
    return this.ground.sample(x, z, 1e5, this.hitScratch) ? this.hitScratch.y : 0;
  }

  private hitScratch = newHit();
  private roadScratch: RoadHit = { edge: 0, i: 0, t: 0, lateral: 0, dist: 0, y: 0, halfWidth: 0, bridge: false };

  reset(): void {
    this.tick = 0;
    this.time = 0;
    this.rng = new RNG(this.seed);
    this.cars = [];
    this.nextId = 1;
    this.events = [];
    this.filter.reset();
    this.world.brokenTrees.clear();
    this.player = this.addCar(CARS[0].id, true);
    if (this.spawnAt === 'proving') {
      const sp = this.track.spawn;
      this.player.vehicle.place(sp.x, this.groundHeight(sp.x, sp.z), sp.z, sp.yaw);
    } else {
      const sp = this.islandSpawn();
      this.player.vehicle.place(sp.x, this.groundHeight(sp.x, sp.z), sp.z, sp.yaw);
    }
    this.lap = { laps: 0, start: 0, last: NaN, best: NaN, sector: 0, s: 0 };
  }

  addCar(specId: string, player = false): SimCar {
    const spec = CARS.find((c) => c.id === specId) ?? CARS[0];
    const v = new Vehicle(buildParams(spec));
    v.aids = defaultAids();
    const car: SimCar = { id: this.nextId++, name: player ? 'You' : spec.model, specId: spec.id, vehicle: v, player };
    this.cars.push(car);
    return car;
  }

  step(c: Controls): void {
    const dt = SIM_DT;
    const pv = this.player.vehicle;
    pv.input = this.filter.update(c, pv, dt);
    for (const car of this.cars) car.vehicle.step(dt, this.ground);
    // Collisions: world first, then every pair of cars.
    for (const car of this.cars) {
      const hit = collideWorld(car.vehicle, this.colliders, (prop) => this.emit('break', car.id, prop.x, prop.z, 0));
      if (hit && hit.impulse > 300) this.emit('impact', car.id, hit.x, hit.z, hit.impulse);
    }
    for (let i = 0; i < this.cars.length; i++) {
      for (let j = i + 1; j < this.cars.length; j++) {
        const a = this.cars[i].vehicle;
        const b = this.cars[j].vehicle;
        const imp = collideCars(a, b);
        if (imp > 300) this.emit('impact', this.cars[i].id, (a.pos.x + b.pos.x) / 2, (a.pos.z + b.pos.z) / 2, imp);
      }
    }
    this.updateLap();
    this.tick++;
    this.time += dt;
  }

  private emit(kind: SimEventKind, carId: number, x: number, z: number, value: number): void {
    if (this.events.length > 256) this.events.shift();
    this.events.push({ kind, carId, x, z, value, tick: this.tick });
  }

  /** Lap timing with three sectors so cutting back over the line does not count. */
  private updateLap(): void {
    const v = this.player.vehicle;
    const road = this.track.road;
    if (!road.nearest(v.pos.x, v.pos.z, this.near)) return;
    const L = road.length;
    let s = this.near.s - this.track.startS;
    if (s < 0) s += L;
    const prev = this.lap.s;
    this.lap.s = s;
    const sector = s < L / 3 ? 0 : s < (2 * L) / 3 ? 1 : 2;
    if (sector === (this.lap.sector + 1) % 3 && Math.abs(s - prev) < 50) this.lap.sector = sector;
    // Crossing the line forward: s wraps from near L to near 0.
    if (prev > L - 60 && s < 60 && this.near.dist < 30) {
      if (this.lap.sector === 2) {
        const t = this.time - this.lap.start;
        if (this.lap.laps > 0) {
          this.lap.last = t;
          if (!(this.lap.best <= t)) this.lap.best = t;
          this.emit('lap', this.player.id, v.pos.x, v.pos.z, t);
        }
        this.lap.laps++;
      } else if (this.lap.laps === 0) {
        // First crossing starts the clock.
        this.lap.laps = 1;
      }
      this.lap.start = this.time;
      this.lap.sector = 0;
    }
  }

  /** First stretch of road heading north from the festival: mountains ahead. */
  islandSpawn(): { x: number; z: number; yaw: number } {
    const hub = this.world.nodes.find((n) => n.id === 'hub') ?? this.world.nodes[0];
    let best = this.world.edges[0];
    let bestScore = Infinity;
    for (const id of hub.edges) {
      const e = this.world.edges[id];
      // Prefer the road leaving the hub most nearly northward.
      const r = e.road;
      const fromA = e.a === this.world.nodes.indexOf(hub);
      const k = fromA ? Math.min(r.n - 1, 40) : Math.max(0, r.n - 41);
      const dz = r.z[k] - hub.z;
      if (dz < bestScore) {
        bestScore = dz;
        best = e;
      }
    }
    const r = best.road;
    const fromA = best.a === this.world.nodes.indexOf(hub);
    const i = fromA ? Math.min(r.n - 2, 60) : Math.max(1, r.n - 61);
    const dir = fromA ? 1 : -1;
    const tx = r.tx[i] * dir;
    const tz = r.tz[i] * dir;
    // Keep to the right-hand lane.
    const lane = best.info.halfWidth * 0.45;
    return { x: r.x[i] - tz * lane, z: r.z[i] + tx * lane, yaw: datan2(-tx, -tz) };
  }

  teleport(x: number, z: number, yaw = 0, y?: number): void {
    if (![x, z, yaw, y ?? 0].every(Number.isFinite)) throw new Error(`teleport: non-finite argument (${x}, ${z}, ${yaw}, ${y})`);
    this.player.vehicle.place(x, y ?? this.groundHeight(x, z), z, yaw);
    this.filter.reset();
  }

  /** Put the player back on the nearest road (circuit or island network), facing along it. */
  resetToRoad(): void {
    const v = this.player.vehicle;
    const px = v.pos.x;
    const pz = v.pos.z;
    let x = px;
    let z = pz;
    let yaw = datan2(-v.fwd.x, -v.fwd.z);
    // Nearest point on the circuit.
    const near = this.near;
    this.track.road.nearestAny(px, pz, near);
    let bestD = near.dist;
    let bx = 0;
    let bz = 0;
    let btx = 0;
    let btz = 0;
    {
      const at = { x: 0, y: 0, z: 0, tx: 0, tz: 0, i: 0 };
      this.track.road.at(near.s, at);
      bx = at.x;
      bz = at.z;
      btx = at.tx;
      btz = at.tz;
    }
    // Nearest point on the island's roads (keep the car's travel direction).
    for (const e of this.world.edges) {
      const r = e.road;
      for (let i = 0; i < r.n; i += 2) {
        const d = Math.sqrt((r.x[i] - px) * (r.x[i] - px) + (r.z[i] - pz) * (r.z[i] - pz));
        if (d < bestD) {
          bestD = d;
          bx = r.x[i];
          bz = r.z[i];
          const same = r.tx[i] * v.fwd.x + r.tz[i] * v.fwd.z >= 0 ? 1 : -1;
          btx = r.tx[i] * same;
          btz = r.tz[i] * same;
        }
      }
    }
    if (bestD > 3) {
      x = bx;
      z = bz;
      yaw = datan2(-btx, -btz);
    }
    v.place(x, this.groundHeight(x, z), z, yaw);
    this.filter.reset();
    this.emit('reset', this.player.id, x, z, 0);
  }

  /** True when the player is on a paved or dirt road surface. */
  onRoad(): boolean {
    const v = this.player.vehicle;
    if (this.track.road.nearest(v.pos.x, v.pos.z, this.near) && this.near.dist <= 6.5) return true;
    return this.world.nearestRoad(v.pos.x, v.pos.z, 0.5, this.roadScratch) && this.roadScratch.dist <= this.roadScratch.halfWidth + 0.5;
  }

  getState(): SimState {
    const v = this.player.vehicle;
    const onRoad = this.onRoad();
    this.track.road.nearest(v.pos.x, v.pos.z, this.near);
    return {
      tick: this.tick,
      time: this.time,
      pos: [v.pos.x, v.pos.y, v.pos.z],
      vel: [v.vel.x, v.vel.y, v.vel.z],
      yaw: datan2(-v.fwd.x, -v.fwd.z),
      speed: v.speed,
      kmh: v.vLong * 3.6,
      gear: v.gear,
      rpm: v.rpm,
      grounded: v.groundedCount,
      lap: { ...this.lap },
      onRoad,
      lateral: this.near.lateral,
    };
  }

  /** Bit-exact hash of the simulation state, for determinism tests. */
  hash(): string {
    const vals: number[] = [this.tick];
    for (const c of this.cars) {
      const v = c.vehicle;
      vals.push(v.pos.x, v.pos.y, v.pos.z, v.vel.x, v.vel.y, v.vel.z, v.quat.x, v.quat.y, v.quat.z, v.quat.w, v.engineOmega);
      for (const w of v.wheels) vals.push(w.omega);
    }
    return hashFloats(vals);
  }
}

/** Yaw for a direction vector (x, z) in this game's convention. */
export function yawOf(x: number, z: number): number {
  return datan2(-x, -z);
}

/** Forward unit vector for a yaw. */
export function forwardOf(yaw: number): [number, number] {
  return [-dsin(yaw), -dcos(yaw)];
}

export function hashFloats(values: ArrayLike<number>): string {
  const buf = new DataView(new ArrayBuffer(8));
  let h1 = 0x811c9dc5;
  let h2 = 0x01000193;
  for (let i = 0; i < values.length; i++) {
    buf.setFloat64(0, values[i]);
    for (let b = 0; b < 8; b++) {
      const byte = buf.getUint8(b);
      h1 = Math.imul(h1 ^ byte, 0x01000193);
      h2 = Math.imul(h2 ^ byte, 0x5bd1e995);
    }
  }
  return (h1 >>> 0).toString(16).padStart(8, '0') + (h2 >>> 0).toString(16).padStart(8, '0');
}
