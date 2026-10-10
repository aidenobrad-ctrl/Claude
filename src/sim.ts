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
import type { Ground } from './world/ground';
import type { Colliders } from './world/colliders';

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
  readonly track: TrackLayout;
  readonly ground: Ground;
  readonly colliders: Colliders;
  cars: SimCar[] = [];
  player!: SimCar;
  readonly filter = new PlayerControlFilter();
  /** Presentation events since the last drain (impacts, laps, resets). */
  events: SimEvent[] = [];
  lap: LapState = { laps: 0, start: 0, last: NaN, best: NaN, sector: 0, s: 0 };
  private near = newNearest();
  private nextId = 1;

  constructor(seed = 1) {
    this.seed = seed;
    this.rng = new RNG(seed);
    this.track = buildTestTrack();
    this.ground = new TestTrackGround(this.track);
    this.colliders = this.track.colliders;
    this.reset();
  }

  reset(): void {
    this.tick = 0;
    this.time = 0;
    this.rng = new RNG(this.seed);
    this.cars = [];
    this.nextId = 1;
    this.events = [];
    this.filter.reset();
    this.player = this.addCar(CARS[0].id, true);
    const sp = this.track.spawn;
    this.player.vehicle.place(sp.x, 0, sp.z, sp.yaw);
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

  teleport(x: number, z: number, yaw = 0, y = 0): void {
    if (![x, z, yaw, y].every(Number.isFinite)) throw new Error(`teleport: non-finite argument (${x}, ${z}, ${yaw}, ${y})`);
    this.player.vehicle.place(x, y, z, yaw);
    this.filter.reset();
  }

  /** Put the player back on the nearest point of the circuit, facing along it. */
  resetToRoad(): void {
    const v = this.player.vehicle;
    const road = this.track.road;
    const near = this.near;
    let x = v.pos.x;
    let z = v.pos.z;
    let yaw = datan2(-v.fwd.x, -v.fwd.z);
    road.nearestAny(v.pos.x, v.pos.z, near);
    if (near.dist > 4) {
      const at = { x: 0, y: 0, z: 0, tx: 0, tz: 0, i: 0 };
      road.at(near.s, at);
      x = at.x;
      z = at.z;
      yaw = datan2(-at.tx, -at.tz);
    }
    v.place(x, 0, z, yaw);
    this.filter.reset();
    this.emit('reset', this.player.id, x, z, 0);
  }

  getState(): SimState {
    const v = this.player.vehicle;
    const onRoad = this.track.road.nearest(v.pos.x, v.pos.z, this.near) && this.near.dist <= 6;
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
