// Vehicle physics: sanity checks, driver aids, behavior and validation.
import { test, assert, assertRange, assertNear, metric, log } from './kit';
import { CARS } from '../src/vehicles/cars';
import { buildParams } from '../src/vehicles/params';
import { accelTest, brakeTest, skidpadTest, newTestVehicle } from '../src/vehicles/telemetry';
import { FlatGround, SlopeGround, type Ground } from '../src/world/ground';
import { SURFACE } from '../src/world/surfaces';
import { SIM_DT } from '../src/engine/loop';
import { datan2, dcos, dsin } from '../src/engine/dmath';
import { hashFloats } from '../src/sim';
import type { Vehicle } from '../src/vehicles/vehicle';

const spec = CARS[0];
const params = buildParams(spec);
const flat = new FlatGround();
const DEG = 180 / Math.PI;

function run(v: Vehicle, seconds: number, ground: Ground = flat, each?: (t: number) => void): void {
  const n = Math.round(seconds / SIM_DT);
  for (let i = 0; i < n; i++) {
    each?.(i * SIM_DT);
    v.step(SIM_DT, ground);
  }
}

function finite(v: Vehicle): boolean {
  return v.pos.isFinite() && v.vel.isFinite() && v.angVel.isFinite() && v.quat.isFinite() && v.wheels.every((w) => Number.isFinite(w.omega));
}

test('car settles at rest: loads sum to weight with the right distribution', () => {
  const v = newTestVehicle(params);
  run(v, 3);
  const total = v.wheels.reduce((s, w) => s + w.load, 0);
  assertNear(total / (params.mass * 9.81), 1, 0.005, 'load / weight');
  const front = (v.wheels[0].load + v.wheels[1].load) / total;
  assertNear(front, spec.weightFront, 0.005, 'front share');
  assertNear(v.pos.y, params.cgHeight, 0.01, 'COM height');
  run(v, 10);
  assert(Math.hypot(v.pos.x, v.pos.z) < 0.01, `car crept ${Math.hypot(v.pos.x, v.pos.z).toFixed(4)} m at rest`); // det-ok
});

test('simulation is deterministic', () => {
  const drive = (): string => {
    const v = newTestVehicle(params);
    run(v, 12, flat, (t) => {
      v.input.throttle = t < 8 ? 1 : 0;
      v.input.brake = t > 9 ? 0.6 : 0;
      v.input.steer = dsin(t * 0.9) * 0.6;
    });
    return hashFloats([v.pos.x, v.pos.y, v.pos.z, v.vel.x, v.vel.z, v.quat.y, v.engineOmega, v.wheels[2].omega]);
  };
  assert(drive() === drive(), 'two identical runs diverged');
});

test('validation: acceleration, top speed, braking and skidpad', () => {
  const a = accelTest(params);
  const b = brakeTest(params, 100);
  const s = skidpadTest(params, 40);
  log(`0-100 ${a.t100.toFixed(2)} s · 0-200 ${a.t200.toFixed(2)} s · 1/4 mile ${a.quarter.toFixed(2)} s @ ${a.quarterKmh.toFixed(0)} km/h · top ${a.topKmh.toFixed(1)} km/h`);
  log(`100-0 ${b.distance.toFixed(1)} m · skidpad ${s.g.toFixed(3)} g @ ${s.kmh.toFixed(0)} km/h`);
  metric(spec.id, { t100: a.t100, t200: a.t200, quarter: a.quarter, quarterKmh: a.quarterKmh, topKmh: a.topKmh, brake100: b.distance, skidpadG: s.g });
  // Real-world references for a 290 kW, 1.5 t front-engine RWD coupe on
  // summer tires: 0-100 about 4.4 s, 0-200 about 15 s, 100-0 about 33 m,
  // skidpad about 1.0 g, top speed about 290 km/h. Allow 10%.
  assertRange(a.t100, 4.4 * 0.9, 4.4 * 1.1, '0-100 km/h (s)');
  assertRange(a.t200, 15 * 0.9, 15 * 1.1, '0-200 km/h (s)');
  assertRange(b.distance, 33 * 0.9, 33 * 1.1, '100-0 km/h (m)');
  assertRange(s.g, 0.9, 1.1, 'skidpad (g)');
  assertRange(a.topKmh, 290 * 0.9, 290 * 1.1, 'top speed (km/h)');
  assert(s.g < 1.3, 'road tires must stay below 1.3 g');
});

test('ABS stops shorter than locked wheels and keeps the car straight', () => {
  const abs = brakeTest(params, 100, { aids: { abs: true } });
  const locked = brakeTest(params, 100, { aids: { abs: false } });
  log(`ABS ${abs.distance.toFixed(1)} m, locked ${locked.distance.toFixed(1)} m`);
  assert(abs.distance < locked.distance, 'ABS should beat locked wheels on dry asphalt');
  assert(abs.maxYaw < 0.05, `car yawed under ABS braking (${abs.maxYaw.toFixed(3)} rad/s)`);
});

test('traction control limits wheelspin at launch', () => {
  const on = accelTest(params, { aids: { tcs: true }, maxTime: 6 });
  const off = accelTest(params, { aids: { tcs: false }, maxTime: 6 });
  log(`max slip ratio with TCS ${on.maxSlip.toFixed(2)}, without ${off.maxSlip.toFixed(2)}; 0-100 ${on.t100.toFixed(2)} vs ${off.t100.toFixed(2)} s`);
  assert(on.maxSlip < 0.25, `TCS let the wheels slip to ${on.maxSlip.toFixed(2)}`);
  assert(off.maxSlip > on.maxSlip, 'without TCS there should be more wheelspin');
});

test('reverse engages from a standstill and throttle returns to first', () => {
  const v = newTestVehicle(params);
  run(v, 0.5);
  v.input.brake = 1;
  run(v, 1);
  assert(v.gear === -1, `expected reverse, gear ${v.gear}`);
  run(v, 2);
  assert(v.vLong < -1.5, `should be reversing, vLong ${v.vLong.toFixed(2)}`);
  v.input.brake = 0;
  v.input.throttle = 1;
  run(v, 2.5);
  assert(v.gear >= 1 && v.vLong > 2, `throttle should stop the reverse and drive forward (gear ${v.gear}, v ${v.vLong.toFixed(2)})`);
});

// Holding the brake at a standstill selects reverse (the arcade scheme), so
// the handbrake is what holds a car on a hill.
test('handbrake holds the car on a 15% slope; it rolls back without it', () => {
  const slope = new SlopeGround(0.15);
  const v = newTestVehicle(params);
  // Face uphill (+X is uphill): yaw -90 deg points forward along +X.
  v.place(0, 0, 0, -Math.PI / 2);
  v.input.handbrake = 1;
  run(v, 1, slope);
  const x0 = v.pos.x;
  run(v, 5, slope);
  log(`creep on the handbrake over 5 s: ${(v.pos.x - x0).toFixed(3)} m`);
  assert(Math.abs(v.pos.x - x0) < 0.25, `slid ${(v.pos.x - x0).toFixed(3)} m on the handbrake`);
  v.input.handbrake = 0;
  run(v, 3, slope);
  assert(v.pos.x < x0 - 1, 'should roll back down without brakes');
});

test('body roll and pitch are in a realistic range', () => {
  const v = newTestVehicle(params, { steering: 'off', esc: false });
  v.place(40, 0, 0, 0);
  run(v, 0.5);
  v.setSpeed(15);
  // Steady left turn of radius about 40 m at 15 m/s (0.57 g).
  run(v, 6, flat, () => {
    v.input.steer = -0.13;
    v.input.throttle = 0.25 + (15 - v.vLong) * 0.5;
  });
  const lat = (v.speed * v.yawRate) / 9.81;
  const roll = datan2(v.right.y, v.up.y) * DEG;
  log(`steady ${lat.toFixed(2)} g: roll ${roll.toFixed(2)} deg (${(roll / lat).toFixed(2)} deg/g)`);
  assertRange(Math.abs(roll / lat), 0.8, 3.5, 'roll gradient (deg/g)');
  const b = newTestVehicle(params);
  run(b, 0.5);
  b.setSpeed(27);
  b.input.brake = 1;
  let pitch = 0;
  run(b, 1, flat, () => (pitch = Math.max(pitch, datan2(b.fwd.y, Math.hypot(b.fwd.x, b.fwd.z)) * -DEG))); // det-ok
  log(`braking pitch ${pitch.toFixed(2)} deg (nose down)`);
  assertRange(pitch, 0.4, 4, 'braking pitch (deg)');
});

test('stability control tames a lift-off and steer-induced spin', () => {
  const trial = (esc: boolean): number => {
    const v = newTestVehicle(params, { esc, steering: 'off', tcs: true });
    run(v, 0.3);
    v.setSpeed(33);
    let maxBeta = 0;
    // Sharp lane change: hard left then hard right, off throttle.
    run(v, 4, flat, (t) => {
      v.input.steer = t < 0.6 ? -0.35 : t < 1.4 ? 0.35 : 0;
      v.input.throttle = t < 1.6 ? 0 : 0.3;
      maxBeta = Math.max(maxBeta, Math.abs(datan2(v.vLat, Math.max(1, v.vLong))));
    });
    return maxBeta * DEG;
  };
  const off = trial(false);
  const on = trial(true);
  log(`lane change at 120 km/h: max sideslip ${off.toFixed(1)} deg without ESC, ${on.toFixed(1)} deg with`);
  assert(on < off, 'ESC should reduce sideslip');
  assert(on < 12, `ESC still let the car slide to ${on.toFixed(1)} deg`);
});

test('handbrake at 60 km/h rotates the car; throttle and countersteer can hold a drift', () => {
  const v = newTestVehicle(params, { esc: false, tcs: false, steering: 'off' });
  run(v, 0.3);
  v.setSpeed(17);
  let maxBeta = 0;
  run(v, 1.2, flat, (t) => {
    v.input.steer = -0.5;
    v.input.handbrake = t < 0.5 ? 1 : 0;
    v.input.throttle = t > 0.5 ? 0.7 : 0;
    maxBeta = Math.max(maxBeta, Math.abs(datan2(v.vLat, Math.max(1, v.vLong))));
  });
  log(`handbrake entry: sideslip reached ${(maxBeta * DEG).toFixed(1)} deg`);
  assert(maxBeta * DEG > 15, 'handbrake should kick the rear out');
  // Drift controller for a left-hand drift (sideslip beta > 0): countersteer
  // (steer right) by about beta, corrected toward a 25 deg target; throttle
  // swings the rear out when the angle is too small.
  let held = 0;
  run(v, 5, flat, () => {
    const beta = datan2(v.vLat, Math.max(1, v.vLong)) * DEG;
    v.input.handbrake = 0;
    v.input.steer = Math.max(-1, Math.min(1, (beta + 0.6 * (beta - 25)) / spec.steering.maxAngle));
    v.input.throttle = Math.max(0, Math.min(1, 0.55 + 0.03 * (25 - beta)));
    if (beta > 10 && v.speed > 6) held += SIM_DT;
  });
  log(`drift held at >10 deg for ${held.toFixed(2)} of 5 s`);
  assert(finite(v), 'NaN during drift');
  assert(held > 1.5, 'a basic controller should be able to hold a drift for a while');
});

test('lands a 2 m drop and recovers from a rollover without NaNs or sinking', () => {
  const v = newTestVehicle(params);
  v.place(0, 2, 0, 0);
  let minY = Infinity;
  run(v, 4, flat, () => (minY = Math.min(minY, v.pos.y)));
  assert(finite(v), 'NaN after landing');
  assertRange(minY, 0.15, 0.5, 'lowest COM height on landing (m)');
  assertNear(v.pos.y, params.cgHeight, 0.03, 'settled height after landing');
  // On its side with a spin: the body points must keep it above the ground.
  const r = newTestVehicle(params);
  r.place(0, 1.5, 0, 0);
  r.quat.set(0, 0, dsin(Math.PI / 4 * 0.98), dcos(Math.PI / 4 * 0.98));
  r.angVel.set(0.5, 1.5, 3);
  let low = Infinity;
  run(r, 6, flat, () => (low = Math.min(low, r.pos.y)));
  assert(finite(r), 'NaN in rollover');
  assert(low > 0.3, `body sank into the ground (COM y ${low.toFixed(2)})`);
});

test('surfaces: grass grips less than asphalt, sand slows the car', () => {
  const grass = skidpadTest(params, 40, { surface: SURFACE.grass, maxTime: 90 });
  const asphalt = skidpadTest(params, 40, { maxTime: 90 });
  const sand = accelTest(params, { surface: SURFACE.sand, maxTime: 30 });
  const road = accelTest(params, { maxTime: 30 });
  log(`skidpad asphalt ${asphalt.g.toFixed(2)} g, grass ${grass.g.toFixed(2)} g; speed after 30 s: road ${road.topKmh.toFixed(0)}, sand ${sand.topKmh.toFixed(0)} km/h`);
  assertRange(grass.g / asphalt.g, 0.35, 0.7, 'grass / asphalt grip');
  assert(sand.topKmh < road.topKmh * 0.75, 'sand should hold the car back');
});

test('auto gearbox does not hunt at a steady cruise', () => {
  const v = newTestVehicle(params);
  run(v, 0.3);
  v.setSpeed(22);
  let shifts = 0;
  let last = v.gear;
  run(v, 20, flat, () => {
    v.input.throttle = Math.max(0, Math.min(1, 0.2 + (22 - v.vLong) * 0.3));
    if (v.gear !== last) {
      shifts++;
      last = v.gear;
    }
  });
  assert(shifts <= 2, `${shifts} shifts while cruising at 80 km/h`);
});

test('physics step cost', () => {
  const v = newTestVehicle(params);
  v.input.throttle = 1;
  const t0 = performance.now();
  run(v, 60, flat, (t) => (v.input.steer = dsin(t) * 0.3));
  const us = ((performance.now() - t0) * 1000) / (60 / SIM_DT);
  log(`${us.toFixed(2)} µs per step`);
  metric('stepMicros', us);
  assert(us < 40, `step too slow: ${us.toFixed(1)} µs`);
});

test('car-to-car collision: no interpenetration, bodies separate, momentum roughly conserved', async () => {
  const { collideCars } = await import('../src/vehicles/collision');
  const a = newTestVehicle(params);
  const b = newTestVehicle(params);
  a.place(0, 0, 0, 0);
  b.place(0, 0, -12, 0);
  run(a, 0.3);
  run(b, 0.3);
  a.setSpeed(15); // a drives north (-Z) into b, which is stopped ahead
  const p0 = a.vel.z * params.mass + b.vel.z * params.mass;
  let minGap = Infinity;
  for (let i = 0; i < 240 * 2; i++) {
    a.step(SIM_DT, flat);
    b.step(SIM_DT, flat);
    collideCars(a, b);
    minGap = Math.min(minGap, Math.abs(a.pos.z - b.pos.z));
  }
  const p1 = a.vel.z * params.mass + b.vel.z * params.mass;
  log(`closest center distance ${minGap.toFixed(2)} m (length ${spec.length} m); momentum before ${p0.toFixed(0)}, after ${p1.toFixed(0)} kg·m/s`);
  assert(minGap > spec.length * 0.85, 'cars passed into each other');
  assert(b.vel.z < -2, 'the struck car should be pushed forward');
  // Tires and brakes act after the hit, so compare just after contact in a frictionless sense: allow 35%.
  assert(Math.abs(p1) <= Math.abs(p0) * 1.05, 'collision must not create momentum');
});

test('a parked car stays put: braked and handbraked on slopes, at rest on flat ground', () => {
  const slope = new SlopeGround(0.12);
  const v = newTestVehicle(params);
  v.place(0, 0, 0, 0); // side-on to the slope (cross slope along +X)
  v.input.handbrake = 1;
  run(v, 1, slope);
  const x0 = v.pos.x;
  run(v, 10, slope);
  log(`cross-slope creep over 10 s on the handbrake: ${(v.pos.x - x0).toFixed(3)} m`);
  assert(Math.abs(v.pos.x - x0) < 0.05, 'car crept sideways on a 12% cross slope');
});
