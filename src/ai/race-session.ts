// A race: an event's route, a grid of cars (the player plus AI drivers), a
// countdown, then laps or a sprint to the finish, positions, lap times and
// results. Runs on the simulation's fixed step. A port of the Unity
// version's RaceSession.cs (the events, the grid, scoring and drafting).
import { RNG } from '../engine/rng';
import { dlog } from '../engine/dmath';
import { SIM_DT } from '../engine/loop';
import type { Sim, SimCar } from '../sim';
import { yawOf } from '../sim';
import { CARS } from '../vehicles/cars';
import { buildParams } from '../vehicles/params';
import { neutralControls, type Controls } from '../engine/input';
import type { Vehicle } from '../vehicles/vehicle';
import { newHit } from '../world/ground';
import { SURFACE, type SurfaceId } from '../world/surfaces';
import { AiDriver, personalityField, type Difficulty, type DriverField, type DriverView, Personality } from './ai-driver';
import { newPose, newProjection, type RaceRoute } from './race-route';
import { CarAbility, RacingLine, SpeedProfile } from './racing-line';
import type { AbilityMeasurer } from './ability-client';

/** Laps of a circuit, a point-to-point sprint, or a drag race (a quarter mile side by side, on its own lane each). */
export type RaceKind = 'circuit' | 'sprint' | 'drag';
export type RaceState = 'countdown' | 'racing' | 'finished';

export interface RaceEvent {
  id: string;
  name: string;
  description: string;
  kind: RaceKind;
  laps: number;
  route: RaceRoute;
  /** Start line (arc length on the route). */
  startS: number;
  /** Race distance from the start line, m. */
  distance: number;
  gridSize: number;
  /** The surface the AI plans for (dirt for a rally on the dirt tracks). */
  surface: SurfaceId;
  /** Head to head: the one rival, whoever the grid would otherwise be. */
  rival: Personality | null;
}

export const COUNTDOWN = 3.0;

/** Measured car abilities, shared between races (a car's measurement takes a fraction of a second). */
export class AbilityCache {
  private map = new Map<string, CarAbility>();
  /** Measures off the main thread when set (the game, ability-client.ts); otherwise `get` measures on the spot. */
  measurer: AbilityMeasurer | null = null;

  /**
   * Measure these cars ahead of a race (urgent: before any queued in advance); resolves when every one is
   * in the cache, so the race's own `get`s are instant.
   */
  prefetch(ids: readonly string[], surface: SurfaceId = SURFACE.asphalt, urgent = false): Promise<void> {
    const wetness = 0;
    const jobs: Promise<unknown>[] = [];
    for (const id of new Set(ids)) {
      const key = `${id}|${wetness}|${surface}`;
      if (this.map.has(key)) continue;
      if (!this.measurer) {
        this.get(id, wetness, surface);
        continue;
      }
      jobs.push(this.measurer.measure(key, id, wetness, surface, urgent).then((a) => this.map.set(key, a)));
    }
    return Promise.all(jobs).then(() => undefined);
  }

  get(id: string, wetness = 0, surface: SurfaceId = SURFACE.asphalt): CarAbility {
    if (surface !== SURFACE.asphalt) wetness = 0;
    const key = `${id}|${wetness}|${surface}`;
    let a = this.map.get(key);
    if (!a) {
      const spec = CARS.find((c) => c.id === id) ?? CARS[0];
      a = CarAbility.measure(spec, wetness, surface);
      this.map.set(key, a);
    }
    return a;
  }
}

export class Entrant {
  ai: AiDriver | null = null;
  name = '';
  player = false;
  grid = 0;
  /** Metres covered from the start line along the route (negative on the grid). */
  progress = 0;
  /** Current arc length on the route (wrapped), lateral offset and distance from the centreline. */
  s = 0;
  lateral = 0;
  dist = 0;
  lapsDone = 0;
  lapStart = 0;
  lastLap = NaN;
  bestLap = NaN;
  finished = false;
  finishTime = NaN;
  /** Speed over the finish line, m/s (a drag race's trap speed). */
  finishSpeed = NaN;
  position = 0;
  wrongWay = false;
  private backT = 0;
  private readonly proj = newProjection();
  private readonly pose = newPose();

  constructor(readonly car: SimCar) {}

  track(r: RaceRoute, dt: number): void {
    const v = this.car.vehicle;
    r.project(v.pos.x, v.pos.z, this.s, 30, 70, this.proj);
    const d = r.delta(this.s, this.proj.s);
    if (Math.abs(d) < 60) this.progress += d;
    this.s = this.proj.s;
    this.lateral = this.proj.lateral;
    this.dist = this.proj.dist;
    const p = r.at(this.s, this.pose);
    const along = v.vel.x * p.tx + v.vel.z * p.tz;
    this.backT = along < -2 ? this.backT + dt : 0;
    this.wrongWay = this.backT > 1.0;
  }
}

export class RaceSession implements DriverField {
  readonly line: RacingLine;
  readonly entrants: Entrant[] = [];
  playerEntrant: Entrant | null = null;
  state: RaceState = 'countdown';
  /** Seconds since the start (negative during the countdown). */
  clock = -COUNTDOWN;
  /** Seconds since the player finished (results wait a little for the field). */
  sinceFinish = 0;
  /** Car-to-car contacts and wall hits since the start (a contact lasting several steps counts once). */
  contacts = 0;
  wallHits = 0;
  private lastContact = new Map<number, number>();
  private readonly holdControls: Controls;
  private readonly order: Entrant[] = [];
  private readonly view: DriverView = { id: 0, s: 0, lateral: 0, speed: 0, halfWidth: 0, length: 0 };
  private readonly pose = newPose();

  constructor(
    readonly sim: Sim,
    readonly event: RaceEvent,
    opponents: { carId: string; driver: Personality; paint?: number }[],
    abilities: AbilityCache,
    seed: number,
    line?: RacingLine,
    /** False: an AI-only race (tests); the player's car leaves the simulation until the race ends. */
    readonly includePlayer = true,
    /** The player's grid slot (-1 or out of range: the back). */
    playerGrid = -1,
  ) {
    this.line = line ?? RacingLine.build(event.route);
    this.holdControls = { ...neutralControls(), handbrake: 1 };
    // Clear any previous race's cars.
    for (let i = sim.cars.length - 1; i >= 0; i--) if (!sim.cars[i].player) sim.cars.splice(i, 1);
    const total = opponents.length + (includePlayer ? 1 : 0);
    if (!includePlayer) playerGrid = -1;
    else if (playerGrid < 0 || playerGrid >= total) playerGrid = total - 1;
    let k = 0;
    for (let slot = 0; slot < total; slot++) {
      let e: Entrant;
      if (slot === playerGrid) {
        e = new Entrant(sim.player);
        e.name = 'You';
        e.player = true;
        this.playerEntrant = e;
      } else {
        const r = opponents[k++];
        const car = sim.addCar(r.carId, false);
        car.name = r.driver.name;
        if (r.paint !== undefined) car.paint = r.paint;
        e = new Entrant(car);
        e.ai = new AiDriver(car, r.driver, abilities.get(car.specId, 0, event.surface), (Math.imul(seed >>> 0, 7919) + slot) >>> 0);
        e.ai.placer = (v, x, y, z, yaw) => this.place(v, x, y, z, yaw);
        e.name = r.driver.name;
      }
      e.grid = slot;
      this.entrants.push(e);
      this.placeOnGrid(e, slot);
    }
    if (!includePlayer) {
      const i = sim.cars.indexOf(sim.player);
      if (i >= 0) sim.cars.splice(i, 1);
    }
    for (const e of this.entrants) {
      // In a drag race each car keeps to its own lane.
      e.ai?.setRoute(event.route, event.kind === 'drag' ? RacingLine.lane(event.route, e.grid % 2 === 0 ? -2.3 : 2.3, e.car.vehicle.params.spec.width / 2) : this.line);
      if (e.ai) {
        e.ai.hold = true;
        // Keep to the grid column until 60 m past the start line.
        e.ai.holdGrid(event.route.delta(e.ai.s, event.startS) + 60);
      }
      e.track(event.route, 0);
      e.progress = event.route.delta(event.startS, e.s);
      e.lapStart = 0;
    }
    sim.filter.reset();
    this.rank();
  }

  private placeOnGrid(e: Entrant, slot: number): void {
    const r = this.event.route;
    const row = Math.floor(slot / 2);
    const col = slot % 2;
    // A drag race starts side by side; otherwise the grid is staggered.
    const s = this.event.startS - 7 - row * 8 - (this.event.kind === 'drag' ? 0 : col * 4);
    const p = r.at(s, this.pose);
    const wl = r.sample(r.wLeft, s);
    const wr = r.sample(r.wRight, s);
    const half = e.car.vehicle.params.spec.width / 2;
    const lat = col === 0 ? -Math.min(2.3, wl - half - 0.3) : Math.min(2.3, wr - half - 0.3);
    this.place(e.car.vehicle, p.x - p.tz * lat, p.y, p.z + p.tx * lat, yawOf(p.tx, p.tz));
    e.s = r.wrap(s);
  }

  /**
   * A car put down at (x, z) on the race's road, at the route's height y there: where two roads overlap (near a
   * junction), not on the top one.
   */
  private place(v: Vehicle, x: number, y: number, z: number, yaw: number): void {
    const hit = newHit();
    const h = this.sim.ground.sample(x, z, y - 0.5, hit) ? hit.y : this.sim.groundHeight(x, z);
    v.place(x, h, z, yaw);
    if (v === this.sim.player.vehicle) this.sim.filter.reset();
  }

  get count(): number {
    return this.entrants.length;
  }

  get(i: number): DriverView {
    const e = this.entrants[i];
    const v = e.car.vehicle;
    const o = this.view;
    o.id = e.car.id;
    o.s = e.s;
    o.lateral = e.lateral;
    o.speed = v.vLong;
    o.halfWidth = v.params.spec.width / 2;
    o.length = v.params.spec.length;
    return o;
  }

  /** One fixed step: the AI plans, everything moves, progress and positions update. */
  step(player: Controls): void {
    const dt = SIM_DT;
    const racing = this.clock >= 0;
    if (racing && this.state === 'countdown') this.state = 'racing';
    const ev = this.event;
    for (const e of this.entrants) {
      if (!e.ai) continue;
      e.ai.hold = !racing;
      e.ai.cruise = e.finished;
      e.ai.stopAt = ev.kind !== 'circuit' && e.finished ? ev.route.length - 8 : NaN;
      e.ai.update(dt, this);
    }
    this.drafting();
    const tick = this.sim.tick;
    this.sim.step(racing ? player : this.holdControls);
    if (racing) this.countContacts(tick);
    this.clock += dt;
    if (!racing) return;
    for (const e of this.entrants) {
      e.track(ev.route, dt);
      if (e.finished) continue;
      if (ev.kind === 'circuit') {
        const laps = Math.floor(e.progress / ev.route.length);
        if (laps > e.lapsDone && laps >= 1) {
          const t = this.clock - e.lapStart;
          e.lastLap = t;
          if (!(e.bestLap <= t)) e.bestLap = t;
          e.lapStart = this.clock;
          e.lapsDone = laps;
        }
      }
      if (e.progress >= ev.distance) {
        e.finished = true;
        e.finishTime = this.clock;
        e.finishSpeed = e.car.vehicle.speed;
      }
    }
    this.rank();
    if (this.state === 'racing' && this.playerEntrant?.finished) this.state = 'finished';
    if (this.state === 'finished') this.sinceFinish += dt;
  }

  /**
   * Sorts this step's impacts into car-to-car contacts (the event sits at the midpoint of
   * the two cars) and wall hits; a contact that continues within half a second is the same contact.
   */
  private countContacts(tick: number): void {
    const gap = Math.trunc(0.5 / SIM_DT);
    for (const ev of this.sim.events) {
      if (ev.tick !== tick || ev.kind !== 'impact') continue;
      const a = this.entrants.find((e) => e.car.id === ev.carId);
      if (!a) continue;
      let b: Entrant | null = null;
      for (const e of this.entrants) {
        if (e === a) continue;
        const pa = a.car.vehicle.pos;
        const pb = e.car.vehicle.pos;
        const mx = (pa.x + pb.x) / 2 - ev.x;
        const mz = (pa.z + pb.z) / 2 - ev.z;
        if (mx * mx + mz * mz < 1e-6) {
          b = e;
          break;
        }
      }
      const key = b === null ? -1 - a.car.id : Math.min(a.car.id, b.car.id) * 100000 + Math.max(a.car.id, b.car.id);
      const last = this.lastContact.get(key);
      const fresh = last === undefined || tick - last > gap;
      this.lastContact.set(key, tick);
      if (!fresh) continue;
      if (b) this.contacts++;
      else this.wallHits++;
    }
  }

  /** A car tucked in behind another loses some drag (applies to the player too). */
  private drafting(): void {
    const r = this.event.route;
    for (const a of this.entrants) {
      let best = 1;
      for (const b of this.entrants) {
        if (a === b) continue;
        const ds = r.delta(a.s, b.s);
        if (ds <= 3 || ds > 30) continue;
        if (Math.abs(b.lateral - a.lateral) > 1.4) continue;
        const f = 1 - 0.32 * (1 - (ds - 3) / 27);
        if (f < best) best = f;
      }
      a.car.vehicle.draftFactor = best;
    }
  }

  private rank(): void {
    const order = this.order;
    order.length = 0;
    order.push(...this.entrants);
    order.sort((x, y) => {
      if (x.finished && y.finished) return x.finishTime - y.finishTime;
      if (x.finished) return -1;
      if (y.finished) return 1;
      const c = y.progress - x.progress;
      return c !== 0 ? c : x.grid - y.grid;
    });
    for (let i = 0; i < order.length; i++) order[i].position = i + 1;
  }

  /** Entrants in race order. */
  get standings(): readonly Entrant[] {
    return this.order;
  }

  get allFinished(): boolean {
    return this.entrants.every((e) => e.finished);
  }

  lap(e: Entrant): number {
    return this.event.kind === 'circuit' ? Math.min(this.event.laps, Math.max(1, e.lapsDone + 1)) : 1;
  }

  /** Put the player back on the race route a few metres behind where they were, facing along it. */
  resetPlayer(): void {
    const e = this.playerEntrant;
    if (!e) return;
    const r = this.event.route;
    const s = r.wrap(e.s - 5);
    const p = r.at(s, this.pose);
    const off = this.line.offsetAt(s);
    this.place(e.car.vehicle, p.x - p.tz * off, p.y, p.z + p.tx * off, yawOf(p.tx, p.tz));
    e.progress += r.delta(e.s, s);
    e.s = s;
  }

  /** Remove the AI cars; the player's car stays where it is. */
  end(): void {
    for (const e of this.entrants) {
      if (e.ai) {
        const i = this.sim.cars.indexOf(e.car);
        if (i >= 0) this.sim.cars.splice(i, 1);
      }
      e.car.vehicle.draftFactor = 1;
    }
    if (!this.includePlayer && !this.sim.cars.includes(this.sim.player)) this.sim.cars.unshift(this.sim.player);
  }
}

/**
 * The rivals in qualifying order, fastest first: each one's time round the route at its own pace (its car's
 * measured limits through its driver's margins). A field lined up as drawn puts quick starters behind slow ones,
 * and the opening seconds turn into a queue of near misses.
 */
export function qualifyingOrder<T extends { carId: string; driver: Personality }>(ev: RaceEvent, line: RacingLine, field: readonly T[], abilities: AbilityCache): T[] {
  const r = ev.route;
  const step = r.length / r.n;
  const time = field.map((f) => {
    const p = f.driver;
    const prof = SpeedProfile.build(line.blend(p.lineAccuracy), abilities.get(f.carId, 0, ev.surface), p.gripScale, p.brakeScale, p.accelScale);
    let t = 0;
    for (let i = 0; i < r.n; i++) t += step / Math.max(1, prof.v[i]);
    return t;
  });
  const order = field.map((_, i) => i);
  order.sort((a, b) => (time[a] !== time[b] ? time[a] - time[b] : a - b));
  return order.map((i) => field[i]);
}

/** The cars raceField picks rivals from: a power-to-weight within a factor of 1.57 of the player's car, and at least three. */
export function fieldPool(playerCar: string): string[] {
  const mine = buildParams(CARS.find((c) => c.id === playerCar) ?? CARS[0]);
  const pw = mine.engine.peakPower / mine.mass;
  const close: { score: number; id: string }[] = [];
  for (const c of CARS) {
    const p = buildParams(c);
    close.push({ score: Math.abs(dlog(p.engine.peakPower / p.mass / pw)), id: c.id });
  }
  close.sort((a, b) => a.score - b.score);
  const pool: string[] = [];
  for (const { score, id } of close) if (score < 0.45 || pool.length < 3) pool.push(id);
  return pool;
}

/** Opponents for a race: cars with a power-to-weight close to the player's, with drivers around a difficulty. */
export function raceField(playerCar: string, count: number, d: Difficulty, seed: number): { carId: string; driver: Personality }[] {
  const rng = new RNG(seed >>> 0);
  const pool = fieldPool(playerCar);
  const drivers = personalityField(d, count, rng);
  const o: { carId: string; driver: Personality }[] = [];
  for (let i = 0; i < count; i++) o.push({ carId: pool[(i + rng.int(0, pool.length)) % pool.length], driver: drivers[i] });
  return o;
}

