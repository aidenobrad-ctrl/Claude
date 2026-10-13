// Racing lines and speed profiles (a port of the Unity version's
// RacingLine.cs, same arithmetic).
//
// The line is the centreline moved sideways by an offset per sample, chosen to
// minimise the sum of squared curvature (second differences) within the track
// limits: Gauss-Seidel sweeps of the biharmonic update
//   p_i <- (-p_{i-2} + 4 p_{i-1} + 4 p_{i+1} - p_{i+2}) / 6
// projected onto each sample's normal and clamped to the drivable width, run
// coarse-to-fine so long corners converge quickly.
//
// A speed profile is one car's fastest speed along that line under its own
// measured limits (CarAbility): cornering grip with downforce, an
// acceleration curve, braking, all shared through a friction circle, with
// forward (acceleration) and backward (braking) passes.
import { RaceRoute, roundHalfEven } from './race-route';
import type { VehicleParams } from '../vehicles/params';
import { buildParams, AIR_DENSITY } from '../vehicles/params';
import type { CarSpec } from '../vehicles/spec';
import { SURFACE, SURFACES, type SurfaceId } from '../world/surfaces';
import { brakeTest, skidpadTest, newTestVehicle } from '../vehicles/telemetry';
import { FlatGround } from '../world/ground';
import { SIM_DT } from '../engine/loop';

export class RacingLine {
  /** Lateral offset from the centreline per route sample (+ = right). */
  readonly offset: Float64Array;
  /** Line points per route sample. */
  readonly x: Float64Array;
  readonly z: Float64Array;
  /** Signed curvature of the line, 1/m (+ = turning right). */
  readonly curv: Float64Array;
  /** Distance along the line from each sample to the next, m. */
  readonly ds: Float64Array;

  private constructor(readonly route: RaceRoute) {
    const n = route.n;
    this.offset = new Float64Array(n);
    this.x = new Float64Array(n);
    this.z = new Float64Array(n);
    this.curv = new Float64Array(n);
    this.ds = new Float64Array(n);
  }

  /** The minimum-curvature line keeping the car's centre at least halfCar + margin inside the edges. */
  static build(r: RaceRoute, halfCar = 1.0, margin = 0.7, sweeps = 400, minRadius = 7.5): RacingLine {
    const line = new RacingLine(r);
    const n = r.n;
    const lo = new Float64Array(n);
    const hi = new Float64Array(n);
    for (let i = 0; i < n; i++) {
      lo[i] = -Math.max(0, r.wLeft[i] - halfCar - margin);
      hi[i] = Math.max(0, r.wRight[i] - halfCar - margin);
    }
    if (!r.closed) {
      lo[0] = hi[0] = 0;
      lo[n - 1] = hi[n - 1] = 0;
    }
    // Keep the line's radius at least minRadius: in a hairpin tighter than
    // that the line must swing wide (the smoothing alone would bunch points
    // on the inside, where the samples' normals converge).
    const kc = centreCurvature(r);
    for (let i = 0; i < n; i++) {
      const k = kc[i];
      if (Math.abs(k) < 1e-4) continue;
      const insideMax = 1 / Math.abs(k) - minRadius;
      if (k > 0) {
        hi[i] = Math.min(hi[i], insideMax);
        if (hi[i] < lo[i]) hi[i] = lo[i];
      } else {
        lo[i] = Math.max(lo[i], -insideMax);
        if (lo[i] > hi[i]) lo[i] = hi[i];
      }
    }
    const a = line.offset;
    // Coarse to fine: optimise every 16th sample, then 8th, ... 1st.
    for (let step = 16; step >= 1; step = Math.floor(step / 2)) {
      const iters = step === 1 ? sweeps : Math.floor(sweeps / 2);
      for (let it = 0; it < iters; it++) {
        const forward = (it & 1) === 0;
        for (let q = 0; q < n; q += step) {
          const i = forward ? q : Math.floor((n - 1 - q) / step) * step;
          relax(r, a, lo, hi, i, step);
        }
      }
      if (step > 1) {
        // Interpolate the samples in between from the optimised ones.
        for (let i = 0; i < n; i++) {
          if (i % step === 0) continue;
          const i0 = Math.floor(i / step) * step;
          const i1 = i0 + step;
          const t = (i - i0) / step;
          let a1: number;
          if (i1 < n) a1 = a[i1];
          else a1 = r.closed ? a[0] : a[i0];
          a[i] = Math.max(lo[i], Math.min(hi[i], a[i0] + (a1 - a[i0]) * t));
        }
      }
    }
    line.finish();
    return line;
  }

  /** The centreline itself as a line (offset 0). */
  static centre(r: RaceRoute): RacingLine {
    const line = new RacingLine(r);
    line.finish();
    return line;
  }

  /** A lane: a constant offset from the centreline (+ = right), kept inside the road for a car this wide. */
  static lane(r: RaceRoute, offset: number, halfCar: number): RacingLine {
    const line = new RacingLine(r);
    for (let i = 0; i < line.offset.length; i++) line.offset[i] = Math.max(-(r.wLeft[i] - halfCar - 0.3), Math.min(r.wRight[i] - halfCar - 0.3, offset));
    line.finish();
    return line;
  }

  /** A road driver's line: the right-hand lane (the inner lane of a dual carriageway). */
  static keepRight(r: RaceRoute): RacingLine {
    const line = new RacingLine(r);
    for (let i = 0; i < line.offset.length; i++) {
      const wr = r.wRight[i];
      line.offset[i] = wr > 6 ? wr / 4 : wr / 2;
    }
    line.finish();
    return line;
  }

  /**
   * A driver's own version of the line: the offsets scaled by accuracy
   * (1 = this line, 0 = the centreline). A less accurate driver uses less
   * of the road, so corners are tighter and their speed profile slower.
   */
  blend(accuracy: number): RacingLine {
    if (accuracy >= 0.999) return this;
    const line = new RacingLine(this.route);
    for (let i = 0; i < this.offset.length; i++) line.offset[i] = this.offset[i] * accuracy;
    line.finish();
    return line;
  }

  private finish(): void {
    const r = this.route;
    const n = r.n;
    for (let i = 0; i < n; i++) {
      this.x[i] = r.x[i] - r.tz[i] * this.offset[i];
      this.z[i] = r.z[i] + r.tx[i] * this.offset[i];
    }
    for (let i = 0; i < n; i++) {
      const j = r.closed ? (i + 1) % n : Math.min(n - 1, i + 1);
      const dx = this.x[j] - this.x[i];
      const dz = this.z[j] - this.z[i];
      this.ds[i] = Math.sqrt(dx * dx + dz * dz);
    }
    const raw = new Float64Array(n);
    for (let i = 0; i < n; i++) {
      if (!r.closed && (i === 0 || i === n - 1)) {
        raw[i] = 0;
        continue;
      }
      const ia = idx(r, i - 1);
      const ib = idx(r, i + 1);
      const ax = this.x[i] - this.x[ia];
      const az = this.z[i] - this.z[ia];
      const bx = this.x[ib] - this.x[i];
      const bz = this.z[ib] - this.z[i];
      const cx = this.x[ib] - this.x[ia];
      const cz = this.z[ib] - this.z[ia];
      const la = Math.sqrt(ax * ax + az * az);
      const lb = Math.sqrt(bx * bx + bz * bz);
      const lc = Math.sqrt(cx * cx + cz * cz);
      const cross = ax * bz - az * bx;
      const den = la * lb * lc;
      raw[i] = den > 1e-9 ? (2 * cross) / den : 0;
    }
    // Light smoothing against sampling noise.
    for (let i = 0; i < n; i++) this.curv[i] = (raw[idx(r, i - 1)] + 2 * raw[i] + raw[idx(r, i + 1)]) / 4;
  }

  /** Offset of the line at arc length s (interpolated). */
  offsetAt(s: number): number {
    return this.route.sample(this.offset, s);
  }

  curvAt(s: number): number {
    return this.route.sample(this.curv, s);
  }
}

/** Signed curvature of the route's centreline (+ = right), the largest over a few samples either side. */
function centreCurvature(r: RaceRoute): Float64Array {
  const n = r.n;
  const k = new Float64Array(n);
  for (let i = 0; i < n; i++) {
    if (!r.closed && (i === 0 || i === n - 1)) continue;
    const ia = idx(r, i - 1);
    const ib = idx(r, i + 1);
    const ax = r.x[i] - r.x[ia];
    const az = r.z[i] - r.z[ia];
    const bx = r.x[ib] - r.x[i];
    const bz = r.z[ib] - r.z[i];
    const cx = r.x[ib] - r.x[ia];
    const cz = r.z[ib] - r.z[ia];
    const den = Math.sqrt(ax * ax + az * az) * Math.sqrt(bx * bx + bz * bz) * Math.sqrt(cx * cx + cz * cz);
    k[i] = den > 1e-9 ? (2 * (ax * bz - az * bx)) / den : 0;
  }
  const o = new Float64Array(n);
  for (let i = 0; i < n; i++) {
    let best = 0;
    for (let d = -3; d <= 3; d++) {
      const v = k[idx(r, i + d)];
      if (Math.abs(v) > Math.abs(best)) best = v;
    }
    o[i] = best;
  }
  return o;
}

function idx(r: RaceRoute, i: number): number {
  const n = r.n;
  if (r.closed) return ((i % n) + n) % n;
  return i < 0 ? 0 : i >= n ? n - 1 : i;
}

function relax(r: RaceRoute, a: Float64Array, lo: Float64Array, hi: Float64Array, i: number, step: number): void {
  if (!r.closed && (i === 0 || i >= r.n - 1)) return;
  const im2 = idx(r, i - 2 * step);
  const im1 = idx(r, i - step);
  const ip1 = idx(r, i + step);
  const ip2 = idx(r, i + 2 * step);
  const px = (k: number): number => r.x[k] - r.tz[k] * a[k];
  const pz = (k: number): number => r.z[k] + r.tx[k] * a[k];
  const qx = (-px(im2) + 4 * px(im1) + 4 * px(ip1) - px(ip2)) / 6;
  const qz = (-pz(im2) + 4 * pz(im1) + 4 * pz(ip1) - pz(ip2)) / 6;
  // Offset along the normal (right = (-tz, tx)) that brings p_i closest to q.
  const off = (qx - r.x[i]) * -r.tz[i] + (qz - r.z[i]) * r.tx[i];
  a[i] = Math.max(lo[i], Math.min(hi[i], off));
}

/**
 * What a car can do, measured by driving it on the test track: cornering
 * grip (40 m skidpad), braking (from 100 km/h), an acceleration curve, and
 * its aero. The AI plans with these, so every AI drives its own car to the
 * same physical limits as the player.
 */
export class CarAbility {
  static readonly G = 9.81;
  static readonly BIN = 2.0;

  id = '';
  /** How wet the paved test track was (0 dry to 1 soaked): the AI plans for the road it races on. */
  wetness = 0;
  /** Lateral grip in g on the skidpad. */
  mu = 0;
  /** Braking deceleration in g from 100 km/h with ABS. */
  brakeG = 0;
  /** Downforce as a fraction of weight per (m/s)^2. */
  aeroK = 0;
  /** Drag deceleration per (m/s)^2, m/s^2. */
  dragK = 0;
  topSpeed = 0;
  /** Full-throttle acceleration (m/s^2) at v = k * BIN m/s. */
  accel: Float64Array = new Float64Array(1);
  wheelbase = 0;
  halfWidth = 0;
  length = 0;
  maxSteer = 0;
  /** Rear axle: share of the brake force, share of the weight, CG height over wheelbase, tyre grip (longitudinal, lateral). */
  rearBrake = 0;
  rearWeight = 0;
  hOverL = 0;
  rearMuX = 0;
  rearMuY = 0;
  /** Lateral acceleration (g) the car can use before it risks tipping over (rollLimit). */
  rollG = Infinity;

  accelAt(v: number): number {
    const a = this.accel;
    if (v <= 0) return a[0];
    const f = v / CarAbility.BIN;
    const i = Math.trunc(f);
    if (i >= a.length - 1) return a[a.length - 1];
    const t = f - i;
    return a[i] + (a[i + 1] - a[i]) * t;
  }

  /**
   * A tall car on sticky tyres tips before it slides. Half the track over
   * the CG height (the static stability factor) is where the inside wheels
   * lift in a steady turn; body roll, kerbs and turn-in overshoot take some
   * of that, so 85% of it is the usable limit, in g.
   */
  static rollLimit(p: VehicleParams): number {
    return (0.85 * (p.spec.trackFront + p.spec.trackRear)) / 4 / p.cgHeight;
  }

  /** Lateral acceleration limit at speed v: grip with downforce, but never past the rollover limit. */
  latMax(v: number): number {
    return Math.min(this.mu * CarAbility.G * (1 + this.aeroK * v * v), this.rollG * CarAbility.G);
  }

  /** Braking deceleration limit at speed v (grip with downforce, plus drag). */
  brakeMax(v: number): number {
    return this.brakeG * CarAbility.G * (1 + this.aeroK * v * v) + this.dragK * v * v;
  }

  /**
   * Braking (g) that still leaves the rear tyres grip for a lateral
   * acceleration aLat (g). Braking moves weight off the rear axle while it
   * keeps its share of the brake force, so in a corner the rear saturates
   * first and the car spins. In a straight line ABS holds the rear and the
   * full braking is stable, so the limit fades in with lateral load.
   */
  stableBrake(aLat: number): number {
    aLat = Math.abs(aLat);
    const excess = (b: number): number => {
      const load = this.rearWeight - b * this.hOverL;
      if (load <= 0.02) return 1e9;
      const x = (this.rearBrake * b) / (this.rearMuX * load);
      const y = (this.rearWeight * aLat) / (this.rearMuY * load);
      return x * x + y * y - 0.81; // keep a tenth of the rear's grip in reserve
    };
    let stable: number;
    if (excess(this.brakeG) <= 0) stable = this.brakeG;
    else if (excess(0) > 0) stable = 0;
    else {
      let lo = 0;
      let hi = this.brakeG;
      for (let i = 0; i < 40; i++) {
        const mid = (lo + hi) / 2;
        if (excess(mid) <= 0) lo = mid;
        else hi = mid;
      }
      stable = lo;
    }
    const t = Math.min(1, aLat / 0.12);
    return this.brakeG + (stable - this.brakeG) * t;
  }

  /**
   * A car's abilities on a road this wet, or on another surface (a dirt event: the skidpad, the stop and the
   * acceleration run all on dirt, where the road's wetness does not count).
   */
  static measure(spec: CarSpec, wetness = 0, surface: SurfaceId = SURFACE.asphalt, params?: VehicleParams): CarAbility {
    const p = params ?? buildParams(spec);
    const rear = p.wheels[2].tire.info;
    if (surface !== SURFACE.asphalt) wetness = 0;
    const wet = surface === SURFACE.asphalt ? 1 + wetness * (rear.surface.wet / rear.surface.asphalt - 1) : rear.surface[SURFACES[surface].grip] / rear.surface.asphalt;
    const c = new CarAbility();
    c.id = spec.id;
    c.wetness = wetness;
    c.wheelbase = spec.wheelbase;
    c.halfWidth = spec.width / 2;
    c.length = spec.length;
    c.maxSteer = p.maxSteer;
    c.rearBrake = 1 - p.brakeBias;
    c.rearWeight = p.a / p.wheelbase;
    c.hOverL = p.cgHeight / p.wheelbase;
    c.rearMuX = rear.mu * rear.muLong * wet;
    c.rearMuY = rear.mu * wet;
    c.mu = skidpadTest(p, 40, { surface, wetness }).g;
    c.rollG = CarAbility.rollLimit(p);
    const br = brakeTest(p, 100, { surface, wetness });
    const v0 = 100 / 3.6;
    c.brakeG = (v0 * v0) / (2 * Math.max(1, br.distance)) / CarAbility.G;
    c.aeroK = (0.5 * AIR_DENSITY * p.aero.clA) / (p.mass * CarAbility.G);
    c.dragK = (0.5 * AIR_DENSITY * p.aero.cdA) / p.mass;
    // Acceleration curve from a full-throttle run, binned by speed.
    const ground = new FlatGround(() => surface);
    const v = newTestVehicle(p);
    v.wetness = wetness;
    for (let i = 0; i < 240; i++) v.step(SIM_DT, ground);
    const bins = 120;
    const sum = new Float64Array(bins);
    const cnt = new Int32Array(bins);
    let prev = 0;
    let top = 0;
    let stall = 0;
    v.input.throttle = 1;
    for (let step = 0; step < 240 * 90; step++) {
      v.step(SIM_DT, ground);
      const sp = v.vLong;
      const a = (sp - prev) / SIM_DT;
      prev = sp;
      if (step % 24 === 0 && step > 0) {
        const b = Math.trunc(sp / CarAbility.BIN);
        if (b >= 0 && b < bins) {
          sum[b] += a;
          cnt[b]++;
        }
      }
      if (sp > top + 0.01) {
        top = sp;
        stall = 0;
      } else stall += SIM_DT;
      if (stall > 3) break;
    }
    c.topSpeed = top;
    const topBin = Math.min(bins - 1, Math.trunc(top / CarAbility.BIN) + 1);
    c.accel = new Float64Array(topBin + 1);
    let last = 0;
    for (let b = 0; b <= topBin; b++) {
      const val = cnt[b] > 0 ? sum[b] / cnt[b] : last;
      c.accel[b] = Math.max(0, val);
      last = c.accel[b];
    }
    // The first bins are launch-limited and noisy: use the best of the first few.
    let launch = 0;
    for (let b = 0; b < Math.min(4, c.accel.length); b++) launch = Math.max(launch, c.accel[b]);
    for (let b = 0; b < Math.min(2, c.accel.length); b++) c.accel[b] = launch;
    c.accel[topBin] = 0;
    return c;
  }
}

/** Target speed along a racing line for one car. */
export class SpeedProfile {
  /** Target speed per route sample, m/s. */
  readonly v: Float64Array;

  private constructor(readonly line: RacingLine) {
    this.v = new Float64Array(line.route.n);
  }

  /** grip, brake and accel scale the car's measured limits (a driver's margin): 1 drives at the measured limit. */
  static build(line: RacingLine, car: CarAbility, grip = 0.96, brake = 0.94, accel = 1.0): SpeedProfile {
    const sp = new SpeedProfile(line);
    const r = line.route;
    const n = r.n;
    const g = CarAbility.G;
    const vmax = car.topSpeed * 0.995;
    const v = sp.v;
    const lat = (vv: number): number => car.latMax(vv) * grip;
    // Crests: the road's vertical curvature over 12 m either side (negative over a rise).
    const step = r.length / n;
    const kk = Math.max(1, roundHalfEven(12 / Math.max(0.5, step)));
    const span = kk * step;
    const crest = new Float64Array(n);
    for (let i = 0; i < n; i++) {
      let a = i - kk;
      let b = i + kk;
      if (!r.closed && (a < 0 || b >= n)) continue;
      a = (a + n) % n;
      b %= n;
      crest[i] = Math.max(0, -(r.y[b] - 2 * r.y[i] + r.y[a]) / (span * span));
    }
    // Cornering limit. Over a crest the car is lighter by v^2 times the crest's curvature,
    // and so has less grip: v^2 k <= mu (g - v^2 c) gives v^2 <= mu g / (k + mu c).
    for (let i = 0; i < n; i++) {
      const k = Math.abs(line.curv[i]);
      const mu = car.mu * grip;
      const den = k + mu * crest[i] - mu * car.aeroK * g;
      v[i] = den <= 1e-6 ? vmax : Math.min(vmax, Math.sqrt((mu * g) / den));
      if (k > 1e-9) v[i] = Math.min(v[i], Math.sqrt((car.rollG * grip * g) / k));
      // Above sqrt(g / c) the car leaves the ground (and lands on its nose on a mountain road): plan for 95% of that.
      if (crest[i] > 1e-4) v[i] = Math.min(v[i], 0.95 * Math.sqrt(g / crest[i]));
    }
    const laps = r.closed ? 2 : 1;
    // Forward: acceleration-limited, sharing grip with cornering.
    for (let pass = 0; pass < laps; pass++) {
      for (let q = 0; q < n; q++) {
        const i = q;
        const j = r.closed ? (i + 1) % n : i + 1;
        if (j >= n) break;
        const vi = v[i];
        const la = vi * vi * Math.abs(line.curv[i]);
        const use = Math.min(1, la / Math.max(1e-6, lat(vi)));
        const grade = (g * (r.y[j] - r.y[i])) / Math.max(0.5, line.ds[i]);
        const a = car.accelAt(vi) * accel * Math.sqrt(Math.max(0, 1 - use * use)) - grade;
        let vj = Math.sqrt(Math.max(0, vi * vi + 2 * Math.max(0, a) * line.ds[i]));
        if (a <= 0) vj = Math.max(vj, Math.min(v[j], vi));
        if (vj < v[j]) v[j] = vj;
      }
    }
    // Backward: braking-limited.
    for (let pass = 0; pass < laps; pass++) {
      for (let q = n - 1; q >= 0; q--) {
        const i = q;
        const j = r.closed ? (i + 1) % n : i + 1;
        if (j >= n) continue;
        const vj = v[j];
        const la = vj * vj * Math.abs(line.curv[j]);
        const use = Math.min(1, la / Math.max(1e-6, lat(vj)));
        const grade = (g * (r.y[j] - r.y[i])) / Math.max(0.5, line.ds[i]);
        let b = car.brakeMax(vj) * brake * Math.sqrt(Math.max(0, 1 - use * use));
        // ...and no harder than the rear can take while cornering.
        const bs = car.stableBrake(la / g) * g * (1 + car.aeroK * vj * vj) * brake + car.dragK * vj * vj;
        if (bs < b) b = bs;
        b += grade;
        const vi = Math.sqrt(Math.max(0, vj * vj + 2 * Math.max(0.5, b) * line.ds[i]));
        if (vi < v[i]) v[i] = vi;
      }
    }
    return sp;
  }

  at(s: number): number {
    return this.line.route.sample(this.v, s);
  }
}
