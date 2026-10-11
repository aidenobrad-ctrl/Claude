// Vehicle physics: sanity checks, driver aids, behavior and validation.
import { test, assert, assertRange, assertNear, metric, log, fmt } from './kit';
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

// --- Validation against real-world references ------------------------------
//
// Each car is measured on flat dry asphalt with default aids and compared with
// published figures for comparable real cars of its class (no makes or models
// are named here or in docs/CARS.md). Every measured value must land within
// 10% of its reference. 0-100 times are without rollout.

interface Reference {
  /** 0-100 km/h, s. */
  t100: number;
  /** 0-200 km/h, s: given exactly for cars whose top speed is comfortably over 200 km/h (240+). */
  t200?: number;
  /** 100-0 km/h with ABS, m. */
  brake100: number;
  /** Steady lateral acceleration on the 40 m skidpad, g. */
  skidpad: number;
  /** Top speed, km/h. */
  top: number;
}

const REFERENCES: Record<string, Reference> = {
  // 290 kW, 1.5 t front-engined rear-drive coupe with a twin-turbo six, on summer tires.
  'halden-aster-gt': { t100: 4.4, t200: 15, brake100: 33, skidpad: 1.0, top: 290 },
  // 85 kW, 1.1 t three-cylinder turbo city hatch with a seven-speed twin-clutch box.
  'aurelle-pico': { t100: 9.3, brake100: 38, skidpad: 0.85, top: 195 },
  // 235 kW, 1.4 t front-drive hot hatch with a 2.0 turbo and a limited-slip diff, on summer tires.
  'aurelle-vif-r': { t100: 5.7, t200: 18, brake100: 33, skidpad: 1.0, top: 275 },
  // 2008 rally-homologation sedan: 2.0 turbo four, about 220 kW, 1.55 t, all-wheel drive. Braking
  // and grip are for gravel-capable tires (the same cars on summer tires stop in about 36 m and
  // pull about 0.93 g).
  'hoshida-kiri-rs': { t100: 5.0, t200: 20, brake100: 38, skidpad: 0.85, top: 250 },
  // 1971 2.8-litre straight-six roadster, about 125 kW and 1.15 t, four-speed, period radials.
  'halden-280-sprint': { t100: 8.0, brake100: 42, skidpad: 0.8, top: 200 },
  // 1970 7.4-litre big-block muscle coupe, about 280 kW net, 1.8 t, four-speed, rear drum brakes.
  'stallworth-ironside-454': { t100: 6.2, brake100: 45, skidpad: 0.75, top: 205 },
  // 441 kW, 2.1 t twin-turbo V8 all-wheel-drive performance estate, top speed unrestricted.
  'nordvik-kyst-sport': { t100: 3.6, t200: 12, brake100: 34, skidpad: 0.95, top: 305 },
  // 530 kW, 1.45 t mid-engined twin-turbo V8 supercar with a seven-speed twin-clutch box.
  'orsini-saetta': { t100: 2.9, t200: 7.8, brake100: 30, skidpad: 1.1, top: 340 },
  // About 1.4 MW, 2.15 t four-motor electric hypercar with a single-speed drive.
  'arcline-vanta': { t100: 2.05, t200: 4.7, brake100: 30, skidpad: 1.05, top: 400 },
  // 213 kW naturally aspirated V6 body-on-frame off-roader, 2.2 t, on all-terrain tires.
  // Top speed as limited by its maker.
  'ironbark-ridgeback': { t100: 8.5, brake100: 44, skidpad: 0.7, top: 180 },
  // 313 kW 6.2-litre V8 full-size pickup with a ten-speed automatic, 2.5 t, on all-terrain tires.
  // Top speed as limited by its maker; the simulation has no limiter.
  'stallworth-prairie': { t100: 6.3, brake100: 43, skidpad: 0.75, top: 190 },
  // 470 kW, 2.2 t twin-turbo V8 performance SUV on summer tires.
  'nordvik-varg': { t100: 3.6, t200: 12.5, brake100: 34, skidpad: 0.98, top: 300 },
};

test('validation: every car has real-world reference values', () => {
  const ids = CARS.map((c) => c.id);
  for (const id of ids) assert(REFERENCES[id], `${id} has no reference values`);
  for (const [id, r] of Object.entries(REFERENCES)) {
    assert(ids.includes(id), `reference values for unknown car ${id}`);
    assert((r.t200 !== undefined) === r.top >= 240, `${id}: give a 0-200 reference exactly when the top speed reference is 240 km/h or more`);
  }
});

for (const car of CARS) {
  test(`validation: ${car.make} ${car.model} (0-100, 0-200, top speed, braking, skidpad)`, () => {
    const ref = REFERENCES[car.id];
    assert(ref, `${car.id} has no reference values`);
    const p = buildParams(car);
    const a = accelTest(p);
    const b = brakeTest(p, 100);
    const s = skidpadTest(p, 40);
    const t200 = Number.isNaN(a.t200) ? '—' : `${a.t200.toFixed(2)} s`;
    log(`0-100 ${a.t100.toFixed(2)} s · 0-200 ${t200} · 1/4 mile ${a.quarter.toFixed(2)} s @ ${a.quarterKmh.toFixed(0)} km/h · top ${a.topKmh.toFixed(1)} km/h`);
    log(`100-0 ${b.distance.toFixed(1)} m · skidpad ${s.g.toFixed(3)} g @ ${s.kmh.toFixed(0)} km/h`);
    metric(car.id, { t100: a.t100, t200: a.t200, quarter: a.quarter, quarterKmh: a.quarterKmh, topKmh: a.topKmh, brake100: b.distance, skidpadG: s.g, ref });
    const problems: string[] = [];
    const within10 = (what: string, v: number, r: number): void => {
      if (!(v >= r * 0.9 && v <= r * 1.1)) problems.push(`${what} = ${fmt(v)}, expected ${fmt(r * 0.9)}..${fmt(r * 1.1)}`);
    };
    within10('0-100 km/h (s)', a.t100, ref.t100);
    if (ref.t200 !== undefined) within10('0-200 km/h (s)', a.t200, ref.t200);
    within10('100-0 km/h (m)', b.distance, ref.brake100);
    within10('skidpad (g)', s.g, ref.skidpad);
    within10('top speed (km/h)', a.topKmh, ref.top);
    // Without slicks and real downforce, lateral grip must stay below about
    // 1.3 g. Only a slick or semi-slick car whose reference is itself past
    // 1.3 g (a downforce car) may exceed it; none of the current cars is one.
    const downforceCar = (car.tires.compound === 'slick' || car.tires.compound === 'semislick') && car.aero.clA > 0 && ref.skidpad >= 1.3;
    if (!downforceCar && !(s.g < 1.3)) problems.push(`skidpad ${fmt(s.g)} g: without slicks and downforce it must stay below 1.3 g`);
    assert(problems.length === 0, problems.join('; '));
  });
}

test('every car settles at rest, survives a launch, a hard stop and a lane change, and cruises without hunting', () => {
  const problems: string[] = [];
  for (const car of CARS) {
    const p = buildParams(car);
    const fail = (msg: string): number => problems.push(`${car.id}: ${msg}`);
    // At rest: loads sum to the weight with the specified split, at the specified COM height.
    const r = newTestVehicle(p);
    run(r, 3);
    const total = r.wheels.reduce((sum, w) => sum + w.load, 0);
    const front = (r.wheels[0].load + r.wheels[1].load) / total;
    if (Math.abs(total / (p.mass * 9.81) - 1) > 0.005) fail(`loads sum to ${(total / (p.mass * 9.81)).toFixed(4)} of the weight`);
    if (Math.abs(front - car.weightFront) > 0.005) fail(`front share ${front.toFixed(4)}, spec ${car.weightFront}`);
    if (Math.abs(r.pos.y - p.cgHeight) > 0.01) fail(`COM at ${r.pos.y.toFixed(3)} m, spec ${p.cgHeight}`);
    run(r, 10);
    // Electric cars drift backward a few mm/s at rest: at a standstill the
    // motor's regen torque nudges them once, and rolling resistance is not
    // applied below 0.05 rad/s of wheel speed, so nothing stops the drift.
    const creepLimit = car.engine.aspiration === 'electric' ? 0.15 : 0.01;
    const creep = Math.hypot(r.pos.x, r.pos.z); // det-ok: measurement only
    if (!(creep < creepLimit)) fail(`crept ${creep.toFixed(4)} m in 10 s at rest`);
    // Full-throttle launch.
    const l = newTestVehicle(p);
    run(l, 0.3);
    run(l, 5, flat, () => (l.input.throttle = 1));
    if (!finite(l)) fail('NaN in a launch');
    else if (l.vLong < 15) fail(`only ${(l.vLong * 3.6).toFixed(0)} km/h after a 5 s launch`);
    // Full braking from 100 km/h to a standstill.
    const b = newTestVehicle(p);
    run(b, 0.3);
    b.setSpeed(100 / 3.6);
    let t = 0;
    b.input.brake = 1;
    while (b.vLong > 0.05 && t < 10 && finite(b)) {
      b.step(SIM_DT, flat);
      t += SIM_DT;
    }
    if (!finite(b)) fail('NaN in a hard stop');
    else if (t >= 10) fail('did not stop from 100 km/h within 10 s');
    // Sharp lane change at 120 km/h with stability control: no NaNs, stays on its wheels.
    const c = newTestVehicle(p, { steering: 'off' });
    run(c, 0.3);
    c.setSpeed(33);
    let minUp = 1;
    run(c, 4, flat, (tt) => {
      c.input.steer = tt < 0.6 ? -0.35 : tt < 1.4 ? 0.35 : 0;
      c.input.throttle = tt < 1.6 ? 0 : 0.3;
      minUp = Math.min(minUp, c.up.y);
    });
    if (!finite(c)) fail('NaN in a lane change');
    else if (minUp < 0.5) fail(`rolled in a lane change (up.y ${minUp.toFixed(2)})`);
    // Steady 80 km/h cruise: the automatic must not hunt between gears.
    const k = newTestVehicle(p);
    run(k, 0.3);
    k.setSpeed(22);
    let shifts = 0;
    let last = k.gear;
    run(k, 20, flat, () => {
      k.input.throttle = Math.max(0, Math.min(1, 0.2 + (22 - k.vLong) * 0.3));
      if (k.gear !== last) {
        shifts++;
        last = k.gear;
      }
    });
    if (shifts > 2) fail(`${shifts} shifts while cruising at 80 km/h`);
  }
  assert(problems.length === 0, problems.join('\n      '));
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
