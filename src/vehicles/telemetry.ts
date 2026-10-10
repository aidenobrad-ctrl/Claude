// Standard performance tests, run headless on flat ground. Used by the
// physics validation suite, the PI calculation and the in-game test track.
import { SIM_DT } from '../engine/loop';
import { datan2 } from '../engine/dmath';
import { clamp } from '../engine/math';
import { FlatGround, type Ground } from '../world/ground';
import { SURFACE, type SurfaceId } from '../world/surfaces';
import { Vehicle, defaultAids, type Aids } from './vehicle';
import type { VehicleParams } from './params';

export interface AccelResult {
  /** Seconds to 100 km/h, 200 km/h (NaN if not reached). */
  t100: number;
  t200: number;
  /** Quarter-mile (402 m) time and trap speed (km/h). */
  quarter: number;
  quarterKmh: number;
  /** Top speed reached, km/h, and when the car stopped gaining. */
  topKmh: number;
  topTime: number;
  shifts: number;
  maxSlip: number;
}

function flat(surface: SurfaceId = SURFACE.asphalt): Ground {
  return new FlatGround(() => surface);
}

function settle(v: Vehicle, ground: Ground, seconds = 1): void {
  for (let i = 0; i < seconds / SIM_DT; i++) v.step(SIM_DT, ground);
}

export function newTestVehicle(params: VehicleParams, aids: Partial<Aids> = {}): Vehicle {
  const v = new Vehicle(params);
  v.aids = { ...defaultAids(), ...aids };
  return v;
}

/** Standing start at full throttle until the car stops gaining speed. */
export function accelTest(params: VehicleParams, opts: { maxTime?: number; aids?: Partial<Aids>; surface?: SurfaceId } = {}): AccelResult {
  const ground = flat(opts.surface);
  const v = newTestVehicle(params, opts.aids);
  settle(v, ground);
  const start = v.pos.clone();
  const r: AccelResult = { t100: NaN, t200: NaN, quarter: NaN, quarterKmh: NaN, topKmh: 0, topTime: 0, shifts: 0, maxSlip: 0 };
  const maxTime = opts.maxTime ?? 120;
  let t = 0;
  let lastGear = v.gear;
  let best = 0;
  let bestT = 0;
  v.input.throttle = 1;
  while (t < maxTime) {
    v.step(SIM_DT, ground);
    t += SIM_DT;
    const kmh = v.vLong * 3.6;
    if (isNaN(r.t100) && kmh >= 100) r.t100 = t;
    if (isNaN(r.t200) && kmh >= 200) r.t200 = t;
    const dist = Math.hypot(v.pos.x - start.x, v.pos.z - start.z); // det-ok: measurement only
    if (isNaN(r.quarter) && dist >= 402.336) {
      r.quarter = t;
      r.quarterKmh = kmh;
    }
    if (v.gear !== lastGear) {
      r.shifts++;
      lastGear = v.gear;
    }
    for (const w of v.wheels) r.maxSlip = Math.max(r.maxSlip, w.slipRatio);
    if (kmh > best + 0.05) {
      best = kmh;
      bestT = t;
    }
    // Stop once the car has gained less than 0.05 km/h in 4 s.
    if (t - bestT > 4 && t > 15) break;
  }
  r.topKmh = best;
  r.topTime = bestT;
  return r;
}

export interface BrakeResult {
  distance: number;
  time: number;
  /** Largest yaw rate during the stop, rad/s (stability check). */
  maxYaw: number;
}

/** Full braking from a speed to a standstill, in a straight line. */
export function brakeTest(params: VehicleParams, fromKmh = 100, opts: { aids?: Partial<Aids>; surface?: SurfaceId } = {}): BrakeResult {
  const ground = flat(opts.surface);
  const v = newTestVehicle(params, opts.aids);
  settle(v, ground);
  v.setSpeed(fromKmh / 3.6);
  // Let the suspension settle at speed with a whiff of throttle.
  v.input.throttle = 0.25;
  for (let i = 0; i < 60; i++) v.step(SIM_DT, ground);
  v.setSpeed(fromKmh / 3.6);
  v.input.throttle = 0;
  v.input.brake = 1;
  const start = v.pos.clone();
  let t = 0;
  let maxYaw = 0;
  while (v.vLong > 0.05 && t < 20) {
    v.step(SIM_DT, ground);
    t += SIM_DT;
    maxYaw = Math.max(maxYaw, Math.abs(v.yawRate));
  }
  return { distance: Math.hypot(v.pos.x - start.x, v.pos.z - start.z), time: t, maxYaw }; // det-ok: measurement only
}

export interface SkidpadResult {
  /** Maximum steady lateral acceleration, g. */
  g: number;
  kmh: number;
  /** Mean radius error while steady, m. */
  err: number;
}

/**
 * Constant-radius skidpad. The driver steers with a kinematic feedforward
 * (L/R), a yaw-rate term and PI feedback on the radius error, so it holds
 * the line until the tires truly give up, while the target speed creeps up.
 * The result is the highest lateral acceleration held for a full second
 * with the car within 0.6 m of the line.
 */
export function skidpadTest(params: VehicleParams, radius = 40, opts: { aids?: Partial<Aids>; surface?: SurfaceId; maxTime?: number } = {}): SkidpadResult {
  const ground = flat(opts.surface);
  const v = newTestVehicle(params, { steering: 'off', esc: false, ...opts.aids });
  // Counter-clockwise (left turn) around the origin, starting at (R, 0).
  // Heading at (R,0) tangent toward -Z (north) is yaw 0.
  v.place(radius, 0, 0, 0);
  settle(v, ground, 0.5);
  const L = params.wheelbase;
  let vTarget = 8;
  let errInt = 0;
  v.setSpeed(vTarget);
  let best = 0;
  let bestKmh = 0;
  let steadyTime = 0;
  let accG = 0;
  let accN = 0;
  let errSum = 0;
  let errN = 0;
  const maxTime = opts.maxTime ?? 160;
  for (let t = 0; t < maxTime; t += SIM_DT) {
    const px = v.pos.x;
    const pz = v.pos.z;
    const rNow = Math.sqrt(px * px + pz * pz);
    const err = rNow - radius;
    // Unit tangent for travel around the circle (decreasing angle, a left turn).
    const hx = pz / Math.max(1e-6, rNow);
    const hz = -px / Math.max(1e-6, rNow);
    // Heading error, yaw sense: + when the car points left of the tangent.
    const headErr = datan2(v.fwd.x * hz - v.fwd.z * hx, v.fwd.x * hx + v.fwd.z * hz);
    errInt = clamp(errInt + err * SIM_DT, -4, 4);
    const yawTarget = v.vLong / radius;
    // Steer angle (+ = left): kinematic L/R, PI on running wide, heading and yaw-rate terms.
    const delta = L / radius + 0.04 * err + 0.02 * errInt - 0.6 * headErr + 0.15 * (yawTarget - v.yawRate);
    v.input.steer = clamp(-delta / params.maxSteer, -1, 1);
    // Speed: creep the target up while the car holds the line.
    if (Math.abs(err) < 0.6) vTarget += 0.12 * SIM_DT;
    else if (err > 1.5) vTarget -= 0.4 * SIM_DT;
    const dv = vTarget - v.vLong;
    v.input.throttle = clamp(0.25 + dv * 0.6, 0, 1);
    v.input.brake = clamp(-dv * 0.3 - 0.1, 0, 1);
    v.step(SIM_DT, ground);
    const g = (v.speed * v.speed) / Math.max(1, rNow) / 9.81;
    if (Math.abs(err) < 0.6 && Math.abs(v.input.steer) < 0.98) {
      steadyTime += SIM_DT;
      accG += g;
      accN++;
      errSum += Math.abs(err);
      errN++;
      if (steadyTime >= 1) {
        const mean = accG / accN;
        if (mean > best) {
          best = mean;
          bestKmh = v.speed * 3.6;
        }
        steadyTime = 0;
        accG = 0;
        accN = 0;
      }
    } else {
      steadyTime = 0;
      accG = 0;
      accN = 0;
    }
    if (Math.abs(err) > 8) break;
  }
  return { g: best, kmh: bestKmh, err: errN ? errSum / errN : NaN };
}
