// An AI racing driver (a port of the Unity version's AiDriver.cs). It drives
// through the same Vehicle and physics as the player, only its inputs come
// from here:
//
// - Steering: pure pursuit on the driver's own line with a speed-dependent
//   look-ahead, plus a yaw-rate damper and countersteer in slides.
// - Pedals: feedforward from the driver's speed profile plus feedback,
//   turned into throttle and brake through the car's measured curves, with
//   braking capped by what the rear tyres can take while cornering. The
//   stability control is off (ABS and traction control stay on).
// - Racecraft: follow, pass (a small state machine on the car being passed,
//   preferring the inside of the next corner), defend, and never steer into
//   a car alongside.
// - Recovery: one reverse attempt when stuck, then a reset to the route;
//   also when flipped, far off the road or facing the wrong way.
// - Personality: skill (line accuracy, grip, braking and throttle use,
//   reaction), aggression (passing, defending, following distance),
//   consistency (mistakes) and risk (a little closer to the grip limit).
import { RNG } from '../engine/rng';
import { datan, datan2, dsin } from '../engine/dmath';
import type { SimCar } from '../sim';
import { yawOf } from '../sim';
import type { Vehicle } from '../vehicles/vehicle';
import { newPose, newProjection, type RaceRoute } from './race-route';
import { CarAbility, RacingLine, SpeedProfile } from './racing-line';

export class Personality {
  name = '';
  /** 0..1: line accuracy, braking points, reaction time. */
  skill = 0.7;
  /** 0..1: willingness to pass and defend, following distance. */
  aggression = 0.5;
  /** 0..1: 1 never makes a mistake. */
  consistency = 0.8;
  /** 0..1: how close to the grip limit the driver plans. */
  risk = 0.5;

  constructor(init: Partial<Personality> = {}) {
    Object.assign(this, init);
  }

  /** Share of the car's measured cornering grip the driver plans with. */
  get gripScale(): number {
    return 0.72 + 0.2 * this.skill + 0.03 * this.risk;
  }
  /** Share of the car's measured braking the driver plans with. */
  get brakeScale(): number {
    return 0.64 + 0.28 * this.skill + 0.03 * this.risk;
  }
  /** Share of the car's acceleration the driver commits to out of corners. */
  get accelScale(): number {
    return 0.8 + 0.2 * this.skill;
  }
  /** How much of the road the driver's line uses (1 = the full racing line, 0 = the centreline). */
  get lineAccuracy(): number {
    return 0.3 + 0.7 * this.skill;
  }
  /** Extra reaction time, s: braking starts this much early and the throttle comes this much late. */
  get reaction(): number {
    return 0.35 * (1 - this.skill);
  }
  /** Wander around the line, m. */
  get lineError(): number {
    return 0.9 * (1 - this.skill);
  }

  static readonly NAMES = ['M. Okafor', 'L. Varga', 'S. Lindqvist', 'K. Tanaka', 'R. Duarte', 'A. Moreau', 'J. Whitfield', 'E. Novak', 'D. Haldane', 'P. Ferreira', 'Y. Seo', 'T. Brennan', 'I. Castell', 'N. Oyelaran', 'F. Brandt', 'G. Ricci'];
}

export type Difficulty = 'novice' | 'average' | 'pro' | 'legend';
export const DIFFICULTIES: readonly Difficulty[] = ['novice', 'average', 'pro', 'legend'];

const clamp01 = (x: number): number => (x < 0 ? 0 : x > 1 ? 1 : x);
const clamp = (x: number, lo: number, hi: number): number => (x < lo ? lo : x > hi ? hi : x);
const sign = (x: number): number => (x > 0 ? 1 : x < 0 ? -1 : 0);

/** A field of drivers spread around a difficulty: deterministic for a seed. */
export function personalityField(d: Difficulty, count: number, rng: RNG): Personality[] {
  const centre = d === 'novice' ? 0.35 : d === 'average' ? 0.58 : d === 'pro' ? 0.78 : 0.93;
  const names = [...Personality.NAMES];
  for (let i = names.length - 1; i > 0; i--) {
    const j = rng.int(0, i + 1);
    const t = names[i];
    names[i] = names[j];
    names[j] = t;
  }
  const o: Personality[] = [];
  for (let i = 0; i < count; i++) {
    const f = count > 1 ? i / (count - 1) : 0.5;
    o.push(
      new Personality({
        name: names[i % names.length],
        skill: clamp01(centre - 0.14 + 0.22 * f + rng.range(-0.03, 0.03)),
        aggression: clamp01(rng.range(0.25, 0.85)),
        consistency: clamp01(0.6 + 0.35 * centre + rng.range(-0.1, 0.1)),
        risk: clamp01(rng.range(0.3, 0.8)),
      }),
    );
  }
  return o;
}

/** Another car, for racecraft. */
export interface DriverView {
  id: number;
  s: number;
  lateral: number;
  speed: number;
  halfWidth: number;
  length: number;
}

function newView(): DriverView {
  return { id: -1, s: 0, lateral: 0, speed: 0, halfWidth: 0, length: 0 };
}

/** A copy of a view (the C# view is a struct: the field may hand out one object it refills). */
function keep(to: DriverView, o: DriverView): DriverView {
  to.id = o.id;
  to.s = o.s;
  to.lateral = o.lateral;
  to.speed = o.speed;
  to.halfWidth = o.halfWidth;
  to.length = o.length;
  return to;
}

/** The other cars, for racecraft. */
export interface DriverField {
  readonly count: number;
  get(i: number): DriverView;
}

export class AiDriver {
  route!: RaceRoute;
  line!: RacingLine;
  profile!: SpeedProfile;

  /** Arc length on the route (wrapped), lateral offset (+ right) and distance from the centreline. */
  s = 0;
  lateral = 0;
  dist = 0;
  /** Lateral target relative to the racing line (racecraft), m. */
  lane = 0;
  targetSpeed = 0;
  /** Slow down and keep out of the way (after the finish). */
  cruise = false;
  /** True while held on the grid. */
  hold = false;
  /** Arc length to stop at (the end of a sprint), or NaN. */
  stopAt = NaN;

  // Statistics for the tests.
  stuckTime = 0;
  maxStuck = 0;
  resets = 0;
  reversals = 0;
  /** Largest distance beyond the drivable edge so far, m. */
  maxOff = 0;
  /** Diagnostics: resets and their causes. */
  onEvent: ((text: string) => void) | null = null;
  /** Puts the car down at (x, z, yaw) on the road at the route's height y there (set by the race). */
  placer: ((v: Vehicle, x: number, y: number, z: number, yaw: number) => void) | null = null;

  private readonly rng: RNG;
  private laneTarget = 0;
  private reverseT = 0;
  private stuckT = 0;
  private offT = 0;
  private flipT = 0;
  private wrongT = 0;
  private lastResetS = NaN;
  // Headway: the furthest point along the route lately, and how long since the car got 4 m past it.
  private headS = NaN;
  private headT = 0;
  /** Held up by a car close ahead that is barely moving (last step): waiting, not stuck. */
  private queued = false;
  private mistakeT = 0;
  private mistakeSpeed = 1;
  private mistakeLane = 0;
  private wobble: number;
  private wobbleRate: number;
  private passSide = 0;
  private passId = -1;
  private passT = 0;
  private passCooldown = 0;
  private started = false;
  // Off the grid: the column to keep to, from where, and for how far (0: released).
  private gridLat = 0;
  private gridS = 0;
  private gridHold = 0;
  private lead = 0;
  private lag = 0;
  /** Lateral room left by the cars alongside this step (absolute, + = right). */
  private sideMin = 0;
  private sideMax = 0;
  private readonly pose = newPose();
  private readonly proj = newProjection();
  // The cars racecraft keeps hold of, copied out of the field's views.
  private readonly aheadView = newView();
  private readonly targetView = newView();
  private readonly behindView = newView();

  constructor(
    readonly car: SimCar,
    readonly p: Personality,
    readonly ability: CarAbility,
    seed: number,
  ) {
    this.rng = new RNG(seed >>> 0);
    this.wobble = this.rng.range(0, Math.PI * 2);
    this.wobbleRate = this.rng.range(0.6, 1.2);
    // The AI does its own stability work (yaw damping, countersteer), like a
    // skilled driver with the stability control off; ABS and traction control stay on.
    car.vehicle.aids.steering = 'off';
    car.vehicle.aids.gearbox = 'auto';
    car.vehicle.aids.esc = false;
  }

  setRoute(route: RaceRoute, line: RacingLine): void {
    this.route = route;
    this.line = line.blend(this.p.lineAccuracy);
    this.profile = SpeedProfile.build(this.line, this.ability, this.p.gripScale, this.p.brakeScale, this.p.accelScale);
    const v = this.car.vehicle;
    route.projectAny(v.pos.x, v.pos.z, this.proj);
    this.s = this.proj.s;
    this.lateral = this.proj.lateral;
    this.dist = this.proj.dist;
    this.started = true;
  }

  /**
   * Keep to the grid column off the start, merging onto the racing line over the 60 m after `hold`
   * metres: the line is one car's line, and a grid two abreast all aiming for it at once sees the
   * car in the other column as in the way, and swerves across to pass it.
   */
  holdGrid(hold: number): void {
    this.gridLat = this.lateral;
    this.gridS = this.s;
    this.gridHold = hold;
    this.lane = this.lateral - this.line.offsetAt(this.s);
  }

  /** Plan this step's inputs. */
  update(dt: number, field: DriverField | null): void {
    const v = this.car.vehicle;
    const inp = v.input;
    if (!this.started) return;
    const route = this.route;
    route.project(v.pos.x, v.pos.z, this.s, 30, 70, this.proj);
    this.s = this.proj.s;
    this.lateral = this.proj.lateral;
    this.dist = this.proj.dist;
    if (this.hold) {
      inp.steer = 0;
      inp.throttle = 0;
      inp.brake = 0;
      inp.handbrake = 1;
      return;
    }
    inp.handbrake = 0;
    inp.clutch = 0;
    const speed = v.vLong;
    const S = this.s;
    const wl = route.sample(route.wLeft, S);
    const wr = route.sample(route.wRight, S);
    const edge = this.lateral > 0 ? wr : wl;
    if (Math.abs(this.lateral) - edge > this.maxOff) this.maxOff = Math.abs(this.lateral) - edge;

    // --- Parked at the end of a sprint after finishing ------------------------------------
    if (this.cruise && !Number.isNaN(this.stopAt) && S > this.stopAt - 30 && Math.abs(speed) < 3) {
      inp.steer = 0;
      inp.throttle = 0;
      inp.brake = 1;
      inp.handbrake = 1;
      this.stuckTime = 0;
      return;
    }

    // --- Recovery -------------------------------------------------------------------
    if (this.recover(dt, speed, edge)) return;

    // --- Mistakes ---------------------------------------------------------------------
    const P = this.p;
    const rng = this.rng;
    this.mistakeT -= dt;
    if (this.mistakeT <= 0) {
      this.mistakeSpeed = 1;
      this.mistakeLane = 0;
      if (rng.chance((1 - P.consistency) * 0.08 * dt)) {
        this.mistakeT = rng.range(1.0, 2.2);
        const kind = rng.next();
        if (kind < 0.4) this.mistakeSpeed = Math.min(1.05 + 0.04 * rng.next(), Math.sqrt(0.98 / P.gripScale)); // brakes late, within the car's grip
        else if (kind < 0.75) this.mistakeSpeed = 0.88; // lifts
        else this.mistakeLane = rng.range(-1.6, 1.6); // runs wide
        this.onEvent?.(`mistake at s ${S.toFixed(0)}: speed x${this.mistakeSpeed.toFixed(2)}, lane ${this.mistakeLane.toFixed(1)} for ${this.mistakeT.toFixed(1)} s`);
      }
    }

    // --- Speed target -------------------------------------------------------------------
    // Braking reads the profile a reaction time ahead; a slower driver also
    // gets back on the throttle later (reads the rising profile further back).
    const vv0 = Math.max(0, speed);
    this.lead = 1.5 + vv0 * (0.12 + P.reaction);
    this.lag = 2 * vv0 * P.reaction;
    let vT = this.want(S);

    // --- Racecraft ----------------------------------------------------------------------
    const half = this.ability.halfWidth;
    this.laneTarget = this.mistakeLane;
    if (this.gridHold > 0) {
      const d = route.delta(this.gridS, S);
      const k = d < this.gridHold ? 1 : 1 - (d - this.gridHold) / 60;
      if (k <= 0) this.gridHold = 0;
      else this.laneTarget += k * (this.gridLat - this.line.offsetAt(S));
    }
    this.sideMin = -Infinity;
    this.sideMax = Infinity;
    if (field) vT = this.racecraft(dt, field, speed, half, wl, wr, vT);
    // Lane changes ease in, and never faster than 2.5 m/s sideways, so a
    // pass at 250 km/h is as gentle as one at 100.
    this.lane += clamp((this.laneTarget - this.lane) * Math.min(1, dt * 1.6), -2.5 * dt, 2.5 * dt);
    // Keep the lane inside the room the cars alongside leave.
    const line = this.line;
    const lineHere = line.offsetAt(S);
    if (this.sideMin <= this.sideMax) this.lane = clamp(this.lane, this.sideMin - lineHere, this.sideMax - lineHere);
    // Off the line towards the inside of a corner the radius is tighter than the profile planned for.
    const kAhead = line.curvAt(S + this.lead);
    const inward = this.lane * sign(kAhead);
    if (Math.abs(kAhead) > 1e-3 && inward > 0.2) {
      const radius = 1 / Math.abs(kAhead);
      vT *= Math.sqrt(Math.max(0.55, (radius - inward) / radius));
    }

    // --- Steering: pure pursuit on the line plus the lane offset ------------------------
    let ld = clamp(5.5 + 0.38 * Math.max(0, speed), 7.5, 42);
    // Far from where it should be (off the road, after a spin), aim further
    // ahead: rejoin at a shallow angle instead of turning back at full lock.
    const latErr = Math.abs(this.lateral - (line.offsetAt(S) + this.lane));
    if (latErr > 3) ld = Math.max(ld, 2.5 * latErr);
    const sT = S + ld;
    const pose = route.at(sT, this.pose);
    const cx = pose.x;
    const cz = pose.z;
    const tx = pose.tx;
    const tz = pose.tz;
    this.wobble += dt * this.wobbleRate;
    const err = P.lineError * dsin(this.wobble * 0.9) * 0.6;
    let off = line.offsetAt(sT) + this.lane + err;
    const wlT = route.sample(route.wLeft, sT);
    const wrT = route.sample(route.wRight, sT);
    if (this.sideMin <= this.sideMax) off = clamp(off, this.sideMin, this.sideMax);
    else off = (this.sideMin + this.sideMax) / 2;
    // Keep the target off the edges, further at speed: a car tracking a
    // target that hugs the edge runs past it.
    const margin = 0.25 + 0.025 * Math.max(0, speed - 20);
    const offLo = -wlT + half + margin;
    const offHi = wrT - half - margin;
    off = offLo <= offHi ? clamp(off, offLo, offHi) : (offLo + offHi) / 2;
    const px = cx - tz * off;
    const pz = cz + tx * off;
    const p = v.params;
    const rx = v.pos.x - v.fwd.x * p.b;
    const rz = v.pos.z - v.fwd.z * p.b;
    const dx = px - rx;
    const dz = pz - rz;
    const right = dx * v.right.x + dz * v.right.z;
    const d2 = Math.max(1, dx * dx + dz * dz);
    const kappa = (-2 * right) / d2; // + = left
    let delta = datan(kappa * p.wheelbase); // road-wheel angle, + = left
    // Yaw damping: steer against yaw beyond what the arc to the target asks for (+ yaw = left).
    const rDes = kappa * Math.max(0, speed);
    delta += 0.2 * (rDes - v.yawRate);
    let steer = -delta / p.maxSteer;
    // Countersteer in a slide (sideslip beta > 0: steer right).
    const beta = datan2(v.vLat, Math.max(2, Math.abs(v.vLong)));
    if (Math.abs(beta) > 0.035) steer += (beta - 0.035 * sign(beta)) * 2.6;
    inp.steer = clamp(steer, -1, 1);
    // The rear stepping out shows as yaw running ahead of the arc's: a
    // driver feels it at once and eases the pedals before it is a slide.
    const yawOver = Math.abs(v.yawRate - rDes) - 0.035;

    // --- Pedals: the acceleration wanted, from the car's own measured curves -----------
    this.targetSpeed = vT;
    const ab = this.ability;
    const vv = Math.max(0, speed);
    // Feedforward from the profile's own slope keeps the car on its braking curve.
    const vA = this.want(S);
    const vB = this.want(S + 6);
    const aFF = clamp((vA * (vB - vA)) / 6, -16, 6);
    const aReq = clamp(aFF + 2.5 * (vT - vv), -40, 40);
    // Grip left for driving after cornering (friction circle).
    const kHere = Math.abs(line.curvAt(S));
    const useLat = Math.min(1, (vv * vv * kHere) / Math.max(0.1, ab.latMax(vv)));
    const full = ab.accelAt(vv);
    const avail = full * Math.sqrt(Math.max(0, 1 - useLat * useLat));
    const coast = ab.dragK * vv * vv + 0.35; // drag, rolling and engine braking off throttle
    let throttle: number;
    let brake: number;
    if (aReq >= -coast) {
      const a = Math.min(aReq, avail);
      throttle = clamp((a + coast) / Math.max(0.5, full + coast), 0, 1);
      brake = 0;
    } else {
      throttle = 0;
      const grip = ab.brakeG * CarAbility.G * (1 + ab.aeroK * vv * vv);
      brake = clamp((-aReq - coast) / Math.max(1, grip), 0.03, 1);
    }
    // In a slide: off the brakes, ease the throttle.
    if (Math.abs(beta) > 0.1 && vv > 8) {
      brake *= 0.3;
      throttle *= 0.5;
    } else if (yawOver > 0 && vv > 8) {
      brake *= clamp(1 - yawOver * 7, 0.35, 1);
      throttle *= clamp(1 - yawOver * 4, 0.4, 1);
    }
    // Braking shares the tyres with cornering, and the rear must keep enough grip to stay behind.
    brake *= Math.sqrt(Math.max(0.15, 1 - useLat * useLat));
    brake = Math.min(brake, Math.max(0.12, ab.stableBrake((vv * vv * kHere) / CarAbility.G) / ab.brakeG));
    // Off the road: slow to what grass and gravel allow, then rejoin.
    if (Math.abs(this.lateral) > edge + 1.5) {
      throttle = Math.min(throttle, speed < 14 ? 0.7 : 0.25);
      if (speed > 22) {
        throttle = 0;
        brake = Math.max(brake, Math.abs(beta) > 0.1 ? 0.25 : 0.6);
      }
    }
    // Stopped behind a car (on the grid, in a jam): held on the handbrake. The brake pedal held
    // at a standstill selects reverse, and the car would back into the one behind it.
    const held = brake > 0 && throttle === 0 && Math.abs(v.vLong) < 0.8;
    inp.throttle = throttle;
    inp.brake = held ? 0 : brake;
    if (held) inp.handbrake = 1;
    inp.shiftUp = false;
    inp.shiftDown = false;
  }

  /** The speed the driver wants at s, with their reaction: the profile lead metres ahead, or lag metres behind that while it rises. */
  private want(s: number): number {
    const a = this.targetAt(s + this.lead);
    return this.lag > 0 ? Math.min(a, this.targetAt(s + this.lead - this.lag)) : a;
  }

  /** The driver's target speed at arc length s: the profile, mistakes, cool-down and stopping after a sprint. */
  private targetAt(s: number): number {
    let v = this.profile.at(s) * this.mistakeSpeed;
    if (this.cruise) v = Math.min(v, 0.72 * this.profile.at(s));
    if (!Number.isNaN(this.stopAt)) {
      const left = this.stopAt - s;
      v = Math.min(v, left <= 0 ? 0 : Math.sqrt(2 * 3.5 * left));
    }
    return v;
  }

  /**
   * Following, passing and defending. A pass is a small state machine: it
   * starts when the driver is faster and close behind a car on their line,
   * picks a side (the inside of the next corner if there is room), then
   * keeps beside that car until it is clearly behind, or gives up after a
   * while if it never got alongside. Returns the speed target, lowered if
   * the car must follow.
   */
  private racecraft(dt: number, field: DriverField, speed: number, half: number, wl: number, wr: number, vT: number): number {
    const route = this.route;
    const line = this.line;
    const S = this.s;
    const P = this.p;
    const ab = this.ability;
    const myLine = line.offsetAt(S);
    const minLat = -wl + half + 0.3;
    const maxLat = wr - half - 0.3;
    let best = Infinity;
    let ahead: DriverView | null = null;
    let target: DriverView | null = null;
    let behind: DriverView | null = null;
    let nearBehind = Infinity;
    const myId = this.car.id;
    for (let i = 0; i < field.count; i++) {
      const o = field.get(i);
      if (o.id === myId) continue;
      const ds = route.delta(S, o.s);
      const lat = o.lateral - this.lateral;
      // Alongside (or about to be): its side of the road is not mine. The
      // steering target keeps a car's width and a margin away from it.
      if (Math.abs(ds) < (ab.length + o.length) * 0.5 + 1.5 && Math.abs(lat) < half + o.halfWidth + 1.6) {
        const sep = half + o.halfWidth + 0.6;
        if (lat >= 0) this.sideMax = Math.min(this.sideMax, o.lateral - sep);
        else this.sideMin = Math.max(this.sideMin, o.lateral + sep);
      }
      if (o.id === this.passId) target = keep(this.targetView, o);
      if (ds > 0 && ds < best && ds < 80) {
        // In my path if it overlaps where I am heading.
        const myLat = line.offsetAt(S + ds) + this.lane;
        if (Math.abs(o.lateral - myLat) < half + o.halfWidth + 0.5) {
          best = ds;
          ahead = keep(this.aheadView, o);
        }
      }
      if (ds < 0 && -ds < nearBehind && -ds < 25) {
        nearBehind = -ds;
        behind = keep(this.behindView, o);
      }
    }

    // --- A pass in progress ---------------------------------------------------------------
    if (this.passId >= 0) {
      const ds = target ? route.delta(S, target.s) : 0;
      const clear = target ? (ab.length + target.length) * 0.5 + 1.5 : 0;
      if (!target || ds < -clear || ds > 45 || (this.passT <= 0 && ds > clear)) {
        // Done (it is behind), lost, or never got alongside: back to the line, and a pause before the next try.
        if (target && ds > -clear) this.passCooldown = 1.5;
        this.passId = -1;
        this.passSide = 0;
      } else {
        const need = half + target.halfWidth + 0.8;
        let want = target.lateral + this.passSide * need;
        const lo = minLat + 0.3;
        const hi = maxLat - 0.3;
        if (want < lo || want > hi) {
          // The gap closed: try the other side, else back out rather than be squeezed off the road.
          const other = target.lateral - this.passSide * need;
          if (other >= lo && other <= hi && ds > 0) {
            this.passSide = -this.passSide;
            want = other;
          } else {
            this.passId = -1;
            this.passSide = 0;
            this.passCooldown = 1.0;
          }
        }
        if (this.passId >= 0) this.laneTarget += want - line.offsetAt(S + Math.max(5, ds));
      }
    }

    // --- Following, and deciding to pass ---------------------------------------------------
    this.queued = false;
    if (ahead) {
      const gap = best - (ab.length + ahead.length) * 0.5;
      // A car spinning or rolling backwards counts as stopped: follow it, never chase its speed below zero.
      const aheadV = Math.max(0, ahead.speed);
      const closing = speed - aheadV;
      const follow = 2.5 + Math.max(0, speed) * (0.17 - 0.08 * P.aggression);
      this.queued = gap < follow + 4 && aheadV < 2;
      const profileHere = this.profile.at(S + best);
      const faster = profileHere > aheadV + 0.5 || closing > 1.0;
      // Stay on the line until close (about half a second), then go for the gap; on a
      // straight the slipstream pulls the car in, so the move can start further back.
      let bend = 0;
      for (let d = 0; d <= 150; d += 15) bend += Math.abs(line.curvAt(S + d));
      const straight = bend < 0.012;
      const close = gap < Math.max(9, Math.max(0, speed) * (0.45 + 0.25 * P.aggression)) || (straight && gap < 26 && closing > -0.5);
      if (this.passId < 0 && faster && close && this.passCooldown <= 0 && P.aggression > 0.15) {
        const need = half + ahead.halfWidth + 0.8;
        const roomLeft = ahead.lateral - need >= minLat + 0.3;
        const roomRight = ahead.lateral + need <= maxLat - 0.3;
        let k = 0;
        for (let d = 20; d <= 120; d += 20) k += line.curvAt(S + d);
        const inside = k > 0 ? 1 : -1;
        let side = 0;
        if (inside > 0 && roomRight) side = 1;
        else if (inside < 0 && roomLeft) side = -1;
        else if (roomRight && (!roomLeft || ahead.lateral < this.lateral)) side = 1;
        else if (roomLeft) side = -1;
        if (side !== 0) {
          this.passId = ahead.id;
          this.passSide = side;
          this.passT = 4.0;
          this.laneTarget += ahead.lateral + side * need - line.offsetAt(S + Math.max(5, best));
        }
      }
      if (this.passId !== ahead.id) {
        // Follow: match speed at the following distance.
        const vf = aheadV + 0.6 * (gap - follow);
        if (gap < follow * 2.5) vT = Math.min(vT, Math.max(0, vf));
      }
      // Never drive into it: brake if the overlap remains and we are closing fast.
      const myLatThere = line.offsetAt(S + best) + this.lane;
      if (Math.abs(ahead.lateral - myLatThere) < half + ahead.halfWidth + 0.2 && closing > 0) {
        const tts = gap / Math.max(0.1, closing);
        if (tts < 1.2) vT = Math.min(vT, aheadV - 1);
        // A much slower car in the way (traffic, a car that spun): no faster than lets the car
        // brake to its speed by the following distance, at three quarters of what it can brake.
        const room = Math.max(0, gap - follow);
        vT = Math.min(vT, Math.sqrt(aheadV * aheadV + 2 * 0.75 * ab.brakeMax(Math.max(0, speed)) * room));
      }
    }
    if (this.passT > 0) this.passT -= dt;
    if (this.passCooldown > 0) this.passCooldown -= dt;
    // Defend: a faster car close behind, an aggressive driver moves to the inside.
    if (behind && nearBehind < 14 && behind.speed > speed + 0.5 && P.aggression > 0.55) {
      let k = 0;
      for (let d = 30; d <= 150; d += 30) k += line.curvAt(S + d);
      const inside = k > 0 ? 1 : -1;
      this.laneTarget += inside * 1.1 * P.aggression;
    }
    this.laneTarget = clamp(this.laneTarget, minLat - myLine, maxLat - myLine);
    return vT;
  }

  private recover(dt: number, speed: number, edge: number): boolean {
    const v = this.car.vehicle;
    const inp = v.input;
    const route = this.route;
    // Upside down or on its side: reset.
    if (v.up.y < 0.3) this.flipT += dt;
    else this.flipT = 0;
    // Far off the road.
    if (this.dist > edge + 22) this.offT += dt;
    else this.offT = Math.max(0, this.offT - dt);
    // Facing the wrong way.
    const pose = route.at(this.s, this.pose);
    const along = v.fwd.x * pose.tx + v.fwd.z * pose.tz;
    if (along < -0.2 && speed < 6) this.wrongT += dt;
    else this.wrongT = 0;
    // Not moving while it should (waiting behind a car that is barely moving is not stuck: no reversing).
    if (Math.abs(speed) < 0.8 && this.reverseT <= 0 && !this.queued) this.stuckT += dt;
    else if (this.reverseT <= 0) this.stuckT = Math.max(0, this.stuckT - dt * 2);
    this.stuckTime = Math.abs(speed) < 0.8 ? this.stuckTime + dt : 0;
    if (this.stuckTime > this.maxStuck) this.maxStuck = this.stuckTime;
    // Wedged against a car or a wall, rocking back and forth without getting anywhere: no
    // headway along the route for 8 s (the stuck test above wants a car standing still).
    if (Number.isNaN(this.headS) || route.delta(this.headS, this.s) > 4) {
      this.headS = this.s;
      this.headT = 0;
    } else this.headT += dt;
    if (this.flipT > 1.5 || this.offT > 4 || this.stuckT > 3.8 || this.wrongT > 3 || this.headT > 8) {
      const why = this.flipT > 1.5 ? 'flipped' : this.offT > 4 ? 'off the road' : this.stuckT > 3.8 ? 'stuck' : this.wrongT > 3 ? 'wrong way' : 'no headway';
      this.onEvent?.(`reset (${why}) at s ${this.s.toFixed(0)}, lateral ${this.lateral.toFixed(1)}, speed ${speed.toFixed(1)}`);
      this.resetToRoute();
      return true;
    }
    // One attempt at reversing out; if the car is still stuck afterwards, the reset above follows.
    if (this.reverseT <= 0 && ((this.stuckT > 1.4 && this.stuckT < 2.0) || (this.wrongT > 1.2 && this.wrongT < 1.5))) {
      this.reverseT = 1.6;
      this.reversals++;
    }
    if (this.reverseT > 0) {
      this.reverseT -= dt;
      // Holding the brake at a standstill selects reverse; steer to swing the nose towards the road.
      const ahead = route.at(this.s + 6, this.pose);
      const right = (ahead.x - v.pos.x) * v.right.x + (ahead.z - v.pos.z) * v.right.z;
      inp.steer = right > 0 ? -1 : 1;
      inp.throttle = 0;
      inp.brake = 0.8;
      inp.handbrake = 0;
      if (this.reverseT <= 0) this.stuckT = Math.max(this.stuckT, 2.2);
      return true;
    }
    return false;
  }

  /** Back on the route, a few metres behind where it was, facing along the road. */
  resetToRoute(): void {
    const v = this.car.vehicle;
    this.gridHold = 0;
    const route = this.route;
    // A second reset near the last one: start further back, on the centreline.
    const again = this.resets > 0 && Math.abs(route.delta(this.lastResetS, this.s)) < 40;
    this.lastResetS = this.s;
    const s = route.wrap(this.s - (again ? 30 : 8));
    const pose = route.at(s, this.pose);
    const off = again ? 0 : this.line.offsetAt(s);
    const x = pose.x - pose.tz * off;
    const z = pose.z + pose.tx * off;
    this.placer?.(v, x, pose.y, z, yawOf(pose.tx, pose.tz));
    this.resets++;
    this.flipT = this.offT = this.stuckT = this.wrongT = this.reverseT = this.headT = 0;
    this.headS = s;
    this.stuckTime = 0;
    this.lane = 0;
    this.s = s;
  }
}
