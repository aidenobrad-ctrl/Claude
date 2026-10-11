// Reference traces of the TypeScript vehicle physics, for the C# port's
// equivalence tests (dotnet/Halcyon.Core.Tests). Runs a set of scenarios
// and writes every recorded number with full double precision (JavaScript's
// shortest round-trip repr) to dotnet/ref/vehicle/.
//
//   node tools/run-ts.mjs tools/dev/vehicle-ref.ts [--out <dir>]
//
// The C# runner replays the same scenarios from the setup written here
// (cars, tuning, aids, ground, driver arguments), so both sides must keep
// the drivers below in sync with dotnet/Halcyon.Core.Tests/Scenarios.cs.
// Non-finite numbers and -0 are written as the strings "NaN", "Infinity",
// "-Infinity" and "-0".
import fs from 'node:fs';
import path from 'node:path';
import { CARS } from '../../src/vehicles/cars';
import type { CarSpec } from '../../src/vehicles/spec';
import { buildParams, defaultTune, noUpgrades, type Tune, type Upgrades, type VehicleParams } from '../../src/vehicles/params';
import { Vehicle, defaultAids, type Aids } from '../../src/vehicles/vehicle';
import { COMPOUNDS, makeCurve, mf, mfSlope, tireForce, type Compound, type TireResult } from '../../src/vehicles/tires';
import { PlayerControlFilter } from '../../src/vehicles/controls';
import { collideCars, collideWorld } from '../../src/vehicles/collision';
import { accelTest, brakeTest, skidpadTest } from '../../src/vehicles/telemetry';
import { neutralControls, shapeAxis, shapeTrigger, type PadSettings } from '../../src/engine/input';
import { SIM_DT } from '../../src/engine/loop';
import { dacos, dasin, datan, datan2, dcos, dexp, dlog, dpow, dsin, dtan, dtanh } from '../../src/engine/dmath';
import { FlatGround, SlopeGround, applyBumps, type Ground, type GroundHit } from '../../src/world/ground';
import { SURFACE, type SurfaceId } from '../../src/world/surfaces';
import { Colliders, type Circle } from '../../src/world/colliders';

const argOut = process.argv.indexOf('--out');
const OUT = path.resolve(argOut >= 0 ? process.argv[argOut + 1] : 'dotnet/ref/vehicle');
const RAD2DEG = 180 / Math.PI;

// --- JSON with exact numbers ---------------------------------------------

type Num = number | string;
function enc(x: number): Num {
  if (Number.isNaN(x)) return 'NaN';
  if (x === Infinity) return 'Infinity';
  if (x === -Infinity) return '-Infinity';
  if (Object.is(x, -0)) return '-0';
  return x;
}
function json(obj: unknown): string {
  return JSON.stringify(obj, (_k, v) => (typeof v === 'number' ? enc(v) : ArrayBuffer.isView(v) ? Array.from(v as Float64Array) : v));
}
/** A JSON object with one array row per line (diff-friendly, compact). */
function writeJson(name: string, head: Record<string, unknown>, rowsKey?: string, rows?: unknown[]): number {
  let s = json(head);
  if (rowsKey && rows) {
    s = s.slice(0, -1) + `${s.length > 2 ? ',' : ''}"${rowsKey}":[\n` + rows.map((r) => json(r)).join(',\n') + '\n]}';
  }
  s += '\n';
  fs.writeFileSync(path.join(OUT, name), s);
  return s.length;
}

// --- Cars ------------------------------------------------------------------

const clone = <T>(x: T): T => JSON.parse(JSON.stringify(x)) as T;
const BASE = CARS[0];

// Test-only variants that cover the code paths the real cars do not (yet):
// AWD and FWD, NA, single turbo, supercharged and electric engines, open and
// spool differentials, a single-speed gearbox, zero downforce, and every
// tire compound family.
function variant(id: string, patch: Partial<CarSpec>): CarSpec {
  return { ...clone(BASE), ...patch, id, make: 'Test', model: id };
}
const VARIANTS: CarSpec[] = [
  variant('test-awd-turbo', {
    category: 'rally', body: 'hatch', yawInertiaScale: 0.95,
    mass: 1380, weightFront: 0.58, wheelbase: 2.55, trackFront: 1.52, trackRear: 1.52, cgHeight: 0.5, length: 4.2, width: 1.8, height: 1.45, groundClearance: 0.15,
    engine: { layout: 'I4', cylinders: 4, displacement: 2.0, aspiration: 'turbo', torque: [[1000, 200], [2500, 380], [4500, 400], [6000, 360], [7000, 300]], idle: 900, redline: 6800, limiter: 7000, inertia: 0.18 }, // no turboLag: the 0.4 s default
    drivetrain: 'AWD', awdFront: 0.42, diff: { type: 'spool', preload: 100, accel: 0.4, decel: 0.25 },
    gears: [3.2, 2.1, 1.55, 1.2, 0.98, 0.82], final: 4.1, reverse: 3.3, shiftTime: 0.15,
    tires: { compound: 'rally', widthF: 225, widthR: 225, radius: 0.32 },
    brakes: { torqueF: 2600, torqueR: 1500 },
    suspension: { freqF: 1.7, freqR: 1.8, dampF: 0.3, dampR: 0.3, arbF: 20000, arbR: 15000, travel: 0.2 },
    aero: { cdA: 0.7, clA: 0.05, balance: 0.5 }, steering: { maxAngle: 36, ratio: 12 },
    visual: { paint: 0x2050c0, style: 'hatch' },
  }),
  variant('test-fwd-na', {
    category: 'compact', body: 'hatch',
    mass: 1150, weightFront: 0.62, wheelbase: 2.5, trackFront: 1.48, trackRear: 1.46, cgHeight: 0.52, length: 4.0, width: 1.72, height: 1.45, groundClearance: 0.14,
    engine: { layout: 'I4', cylinders: 4, displacement: 1.6, aspiration: 'NA', torque: [[800, 110], [3000, 145], [4500, 155], [6000, 140], [6800, 120]], idle: 800, redline: 6500, limiter: 6700, inertia: 0.12 },
    drivetrain: 'FWD', diff: { type: 'open', preload: 0, accel: 0, decel: 0 },
    gears: [3.5, 1.95, 1.3, 1.0, 0.8], final: 4.0, reverse: 3.4, shiftTime: 0.3,
    tires: { compound: 'eco', widthF: 195, widthR: 195, radius: 0.3 },
    brakes: { torqueF: 2000, torqueR: 1000 },
    suspension: { freqF: 1.6, freqR: 1.7, dampF: 0.3, dampR: 0.28, arbF: 15000, arbR: 9000, travel: 0.18 },
    aero: { cdA: 0.62, clA: 0, balance: 0.5 }, steering: { maxAngle: 38, ratio: 15 },
    visual: { paint: 0x30a050, style: 'hatch' },
  }),
  variant('test-ev-awd', {
    category: 'gt', body: 'sedan',
    mass: 2050, weightFront: 0.48, wheelbase: 2.9, trackFront: 1.65, trackRear: 1.65, cgHeight: 0.45, length: 4.9, width: 1.95, height: 1.4, groundClearance: 0.13,
    engine: { layout: 'dual motor', cylinders: 0, displacement: 0, aspiration: 'electric', torque: [[0, 700], [6000, 700], [10000, 450], [16000, 220]], idle: 0, redline: 15500, limiter: 16000, inertia: 0.08 },
    drivetrain: 'AWD', awdFront: 0.45, diff: { type: 'open', preload: 0, accel: 0, decel: 0 },
    gears: [1.0], final: 9.0, reverse: 1.0, shiftTime: 0.1,
    tires: { compound: 'street', widthF: 245, widthR: 255, radius: 0.35 },
    brakes: { torqueF: 3500, torqueR: 2500 },
    suspension: { freqF: 1.5, freqR: 1.6, dampF: 0.35, dampR: 0.35, arbF: 22000, arbR: 16000, travel: 0.17 },
    aero: { cdA: 0.55, clA: 0.1, balance: 0.5 }, steering: { maxAngle: 32, ratio: 14 },
    visual: { paint: 0xe0e0e0, style: 'sedan' },
  }),
  variant('test-super-mid', {
    category: 'super', body: 'midengine', yawInertiaScale: 0.82,
    mass: 1300, weightFront: 0.42, wheelbase: 2.6, trackFront: 1.62, trackRear: 1.6, cgHeight: 0.4, length: 4.5, width: 1.95, height: 1.15, groundClearance: 0.1,
    engine: { layout: 'V8', cylinders: 8, displacement: 5.2, aspiration: 'supercharged', torque: [[1000, 350], [2500, 560], [5000, 650], [7000, 600], [8200, 520]], idle: 1000, redline: 8000, limiter: 8300, inertia: 0.22 },
    drivetrain: 'RWD', diff: { type: 'spool', preload: 0, accel: 0, decel: 0 },
    gears: [3.1, 2.2, 1.7, 1.35, 1.12, 0.95, 0.8], final: 3.6, reverse: 3.0, shiftTime: 0.08,
    tires: { compound: 'slick', widthF: 265, widthR: 325, radius: 0.34 },
    brakes: { torqueF: 4200, torqueR: 2600 },
    suspension: { freqF: 2.4, freqR: 2.6, dampF: 0.35, dampR: 0.35, arbF: 30000, arbR: 22000, travel: 0.12 },
    aero: { cdA: 0.8, clA: 1.2, balance: 0.42 }, steering: { maxAngle: 30, ratio: 12 },
    visual: { paint: 0xffb000, style: 'super' },
  }),
  variant('test-truck-offroad', {
    category: 'truck', body: 'pickup',
    mass: 2300, weightFront: 0.55, wheelbase: 3.1, trackFront: 1.72, trackRear: 1.72, cgHeight: 0.78, length: 5.3, width: 2.0, height: 1.9, groundClearance: 0.25,
    engine: { layout: 'V6', cylinders: 6, displacement: 3.5, aspiration: 'NA', torque: [[700, 300], [2000, 420], [4000, 450], [5500, 400], [6200, 350]], idle: 700, redline: 5800, limiter: 6000, inertia: 0.3 },
    drivetrain: 'AWD', diff: { type: 'lsd', preload: 60, accel: 0.3, decel: 0.15 },
    gears: [4.0, 2.4, 1.6, 1.2, 1.0, 0.8], final: 3.7, reverse: 3.8, shiftTime: 0.35,
    tires: { compound: 'offroad', widthF: 265, widthR: 265, radius: 0.42 },
    brakes: { torqueF: 3800, torqueR: 2400 },
    suspension: { freqF: 1.3, freqR: 1.4, dampF: 0.3, dampR: 0.3, arbF: 18000, arbR: 9000, travel: 0.28 },
    aero: { cdA: 1.15, clA: 0, balance: 0.5 }, steering: { maxAngle: 35, ratio: 17 },
    visual: { paint: 0x804020, style: 'pickup' },
  }),
];
const ALL_CARS: CarSpec[] = [...CARS, ...VARIANTS];
const carSpec = (id: string): CarSpec => {
  const c = ALL_CARS.find((x) => x.id === id);
  if (!c) throw new Error(`unknown car ${id}`);
  return c;
};

const TUNED: Tune = {
  ...defaultTune(BASE),
  pressureF: 0.3, pressureR: -0.2, finalDrive: 1.1, gearScale: [1.05, 1.02, 1, 0.98], // shorter than gears: the rest default to 1
  camberF: -2.5, camberR: -1.8, toeF: -0.2, toeR: 0.3, caster: 6.5, arbF: 1.4, arbR: 0.7, springF: 1.2, springR: 1.1,
  rideHeight: -0.02, bumpF: 1.3, bumpR: 0.8, reboundF: 1.1, reboundR: 0.9, aeroF: 1.5, aeroR: 0.8,
  brakeBalance: 0.6, brakePressure: 1.1, diffAccel: 0.6, diffDecel: 0.4, awdFront: 0.3,
};
const TUNED_UP: Upgrades = { powerScale: 1.15, massScale: 0.95, compound: 'semislick', tireWidthAdd: 10, brakeScale: 1.1, aeroKit: 1, clAAdd: 0.25 };
const compoundUp = (c: Compound): Upgrades => ({ ...noUpgrades(), compound: c });
const COMPOUND_ORDER: Compound[] = ['eco', 'street', 'sport', 'semislick', 'slick', 'rally', 'offroad', 'snow', 'drag'];

interface CarSetup {
  car: string;
  tune?: Tune;
  up?: Upgrades;
}
const paramsFor = (s: CarSetup): VehicleParams => buildParams(carSpec(s.car), s.tune ?? defaultTune(carSpec(s.car)), s.up ?? noUpgrades());

// --- Grounds and worlds ------------------------------------------------------

const STRIPES: SurfaceId[] = [
  SURFACE.asphalt, SURFACE.grass, SURFACE.dirt, SURFACE.sand, SURFACE.snow, SURFACE.gravel, SURFACE.mud,
  SURFACE.ice, SURFACE.concrete, SURFACE.curb, SURFACE.rock, SURFACE.asphaltWet, SURFACE.water,
];
/** 7 m stripes across the road, with the right-hand side three surfaces ahead of the left (split grip). */
function stripeSurface(x: number, z: number): SurfaceId {
  const n = STRIPES.length;
  const k = Math.floor(z / 7) + (x > 0.4 ? 3 : 0);
  return STRIPES[((k % n) + n) % n];
}
/** Flat asphalt with a wet patch whose standing water deepens from 0.03 to 0.13 m. */
class PuddleGround implements Ground {
  sample(x: number, z: number, _yRef: number, out: GroundHit): boolean {
    const inPuddle = x > -3 && x < 3 && z < -20 && z > -45;
    out.y = 0;
    out.nx = 0;
    out.ny = 1;
    out.nz = 0;
    out.surface = inPuddle ? SURFACE.asphaltWet : SURFACE.asphalt;
    out.water = inPuddle ? 0.03 + (0.1 * (-20 - z)) / 25 : 0;
    applyBumps(x, z, out);
    return true;
  }
}
/** Flat asphalt that ends at x = 20: beyond it sample() finds no ground at all (the world's edge). */
class EdgeGround implements Ground {
  sample(x: number, z: number, _yRef: number, out: GroundHit): boolean {
    if (x > 20) return false;
    out.y = 0;
    out.nx = 0;
    out.ny = 1;
    out.nz = 0;
    out.surface = SURFACE.concrete;
    out.water = 0;
    applyBumps(x, z, out);
    return true;
  }
}
type GroundSpec = { type: 'flat'; surface: number } | { type: 'stripes' } | { type: 'puddle' } | { type: 'edge' } | { type: 'slope'; grade: number; surface: number };
function makeGround(g: GroundSpec): Ground {
  switch (g.type) {
    case 'flat':
      return new FlatGround(() => g.surface as SurfaceId);
    case 'stripes':
      return new FlatGround(stripeSurface);
    case 'puddle':
      return new PuddleGround();
    case 'edge':
      return new EdgeGround();
    case 'slope':
      return new SlopeGround(g.grade, g.surface as SurfaceId);
  }
}
const FLAT: GroundSpec = { type: 'flat', surface: SURFACE.asphalt };

function makeWorld(layout: string): Colliders {
  const w = new Colliders();
  if (layout === 'wall') {
    w.addSegment({ ax: -30, az: -40, bx: 30, bz: -40, top: 2, bottom: -1, bounce: 0.3, friction: 0.5, kind: 'wall' });
  } else if (layout === 'tree') {
    w.addCircle({ x: -0.3, z: -10, r: 0.2, top: 0.6, bottom: 0, kind: 'post', breakable: true });
    w.addSegment({ ax: -5, az: -14, bx: 5, bz: -14, top: 0.05, bottom: -1, bounce: 0.3, friction: 0.5, kind: 'barrier' });
    const tree: Circle = { x: 0.7, z: -24, r: 0.4, top: 10, bottom: 0, kind: 'tree' };
    w.extra = (_x, _z, _r, cb) => cb(tree);
  } else if (layout === 'post') {
    w.addCircle({ x: 1.0, z: -25, r: 0.25, top: 1.5, bottom: 0, kind: 'post' });
  } else if (layout === 'rail') {
    w.addSegment({ ax: -3, az: -200, bx: -3, bz: 10, top: 0.8, bottom: -1, bounce: 0.2, friction: 0.6, kind: 'rail' });
  } else if (layout === 'end') {
    w.addSegment({ ax: 0.5, az: -6, bx: 0.5, bz: -6, top: 1, bottom: -1, bounce: 0.2, friction: 0.5, kind: 'tires' }); // zero length
    w.addSegment({ ax: 2.5, az: -12, bx: 3, bz: -12, top: 1, bottom: -1, bounce: 0.2, friction: 0.5, kind: 'barrier' }); // beside the path
    w.addSegment({ ax: -0.05, az: -20, bx: 0.25, bz: -20, top: 1.2, bottom: -1, bounce: 0.25, friction: 0.4, kind: 'fence' });
  } else throw new Error(`unknown layout ${layout}`);
  return w;
}

// --- Recording ---------------------------------------------------------------

const VCOLS = [
  'pos.x', 'pos.y', 'pos.z', 'quat.x', 'quat.y', 'quat.z', 'quat.w', 'vel.x', 'vel.y', 'vel.z', 'angVel.x', 'angVel.y', 'angVel.z',
  'engineOmega', 'rpm', 'gear', 'clutchEngage', 'boost', 'throttleEff', 'tcsCut', 'escCut', 'steerAngle', 'shiftTimer',
  'speed', 'odometer', 'accelLong', 'accelLat', 'impact', 'input.steer', 'input.throttle', 'input.brake', 'input.handbrake',
];
const WCOLS = ['omega', 'length', 'load', 'slipRatio', 'slipAngle', 'fx', 'fy', 'steer', 'spin', 'absScale', 'brakeTorque', 'contact.x', 'contact.y', 'contact.z'];
function columns(nv: number): string[] {
  const cols = ['step'];
  for (let k = 0; k < nv; k++) {
    const p = nv > 1 ? `v${k}.` : '';
    for (const c of VCOLS) cols.push(p + c);
    for (let i = 0; i < 4; i++) for (const c of WCOLS) cols.push(`${p}w${i}.${c}`);
  }
  return cols;
}
function sampleVehicle(v: Vehicle, row: number[]): void {
  row.push(v.pos.x, v.pos.y, v.pos.z, v.quat.x, v.quat.y, v.quat.z, v.quat.w, v.vel.x, v.vel.y, v.vel.z, v.angVel.x, v.angVel.y, v.angVel.z);
  row.push(v.engineOmega, v.rpm, v.gear, v.clutchEngage, v.boost, v.throttleEff, v.tcsCut, v.escCut, v.steerAngle, v.shiftTimer);
  row.push(v.speed, v.odometer, v.accelLong, v.accelLat, v.impact, v.input.steer, v.input.throttle, v.input.brake, v.input.handbrake);
  for (const w of v.wheels) row.push(w.omega, w.length, w.load, w.slipRatio, w.slipAngle, w.fx, w.fy, w.steer, w.spin, w.absScale, w.brakeTorque, w.contact.x, w.contact.y, w.contact.z);
}

type Each = (t: number, i: number) => void;
class Ctx {
  n = 0;
  rows: number[][] = [];
  events: Num[][] = [];
  constructor(
    readonly v: Vehicle[],
    readonly ground: Ground,
    readonly every: number,
  ) {}
  rec(): void {
    const row = [this.n];
    for (const v of this.v) sampleVehicle(v, row);
    this.rows.push(row);
  }
  event(...xs: Num[]): void {
    this.events.push([this.n, ...xs]);
  }
  private after(): void {
    this.n++;
    if (this.n % this.every === 0) this.rec();
  }
  /** Step one vehicle for `seconds`, calling each(t, i) before and post() after every step. */
  run(v: Vehicle, seconds: number, each?: Each, post?: () => void): void {
    const n = Math.round(seconds / SIM_DT);
    for (let i = 0; i < n; i++) {
      each?.(i * SIM_DT, i);
      v.step(SIM_DT, this.ground);
      post?.();
      this.after();
    }
  }
  /** Step two vehicles and resolve their collision every step. */
  runPair(a: Vehicle, b: Vehicle, seconds: number, each?: Each): void {
    const n = Math.round(seconds / SIM_DT);
    for (let i = 0; i < n; i++) {
      each?.(i * SIM_DT, i);
      a.step(SIM_DT, this.ground);
      b.step(SIM_DT, this.ground);
      const j = collideCars(a, b);
      if (j !== 0) this.event('cars', j);
      this.after();
    }
  }
}

// --- Drivers (mirrored in dotnet/Halcyon.Core.Tests/Scenarios.cs) -------------

// eslint-disable-next-line @typescript-eslint/no-explicit-any
type Args = Record<string, any>;
const DRIVERS: Record<string, (c: Ctx, a: Args) => void> = {
  launch(c, a) {
    const v = c.v[0];
    c.run(v, 0.5);
    c.run(v, a.seconds, () => {
      v.input.throttle = 1;
    });
  },
  brake(c, a) {
    const v = c.v[0];
    c.run(v, 0.5);
    v.setSpeed(a.kmh / 3.6);
    c.rec();
    c.run(v, a.seconds, () => {
      v.input.brake = 1;
    });
  },
  corner(c, a) {
    const v = c.v[0];
    v.place(a.radius, 0, 0, 0);
    c.run(v, 0.5);
    v.setSpeed(a.speed);
    c.rec();
    c.run(v, a.seconds, () => {
      v.input.steer = a.steer;
      v.input.throttle = 0.25 + (a.speed - v.vLong) * 0.5;
    });
  },
  weave(c, a) {
    const v = c.v[0];
    c.run(v, 0.3);
    v.setSpeed(a.speed);
    c.rec();
    c.run(v, a.seconds, (t) => {
      v.input.steer = a.steer + a.amp * dsin(t * a.freq);
      v.input.throttle = a.throttle;
    });
  },
  lanechange(c, a) {
    const v = c.v[0];
    c.run(v, 0.3);
    v.setSpeed(a.speed);
    c.rec();
    c.run(v, a.seconds, (t) => {
      v.input.steer = t < 0.6 ? -0.35 : t < 1.4 ? 0.35 : 0;
      v.input.throttle = t < 1.6 ? 0 : 0.3;
    });
  },
  handbrake(c, a) {
    const v = c.v[0];
    c.run(v, 0.3);
    v.setSpeed(a.speed);
    c.rec();
    c.run(v, 1.2, (t) => {
      v.input.steer = -0.5;
      v.input.handbrake = t < 0.5 ? 1 : 0;
      v.input.throttle = t > 0.5 ? 0.7 : 0;
    });
    // Drift controller: countersteer by about the sideslip, aiming for 25 degrees.
    const maxDeg = v.params.spec.steering.maxAngle;
    c.run(v, a.seconds, () => {
      const beta = datan2(v.vLat, Math.max(1, v.vLong)) * RAD2DEG;
      v.input.handbrake = 0;
      v.input.steer = Math.max(-1, Math.min(1, (beta + 0.6 * (beta - 25)) / maxDeg));
      v.input.throttle = Math.max(0, Math.min(1, 0.55 + 0.03 * (25 - beta)));
    });
  },
  surfaces(c, a) {
    const v = c.v[0];
    c.run(v, 0.5);
    v.setSpeed(a.speed);
    c.rec();
    c.run(v, a.seconds, (t) => {
      v.input.steer = 0.25 * dsin(t * 1.3);
      v.input.throttle = t < a.brakeAt ? 0.8 : 0;
      v.input.brake = t < a.brakeAt ? 0 : 1;
    });
  },
  puddle(c, a) {
    const v = c.v[0];
    c.run(v, 0.3);
    v.setSpeed(a.speed);
    c.rec();
    c.run(v, a.seconds, (t) => {
      v.input.throttle = 0.35;
      v.input.steer = 0.15 * dsin(t * 2.1);
    });
  },
  hillstart(c, a) {
    const v = c.v[0];
    v.place(0, 0, 0, -Math.PI / 2); // facing uphill (+X)
    c.rec();
    c.run(v, 1, () => {
      v.input.handbrake = 1;
    });
    c.run(v, 0.4, () => {
      v.input.handbrake = 1;
      v.input.throttle = 0.5;
    });
    c.run(v, a.seconds, () => {
      v.input.handbrake = 0;
      v.input.throttle = 0.7;
    });
  },
  rollback(c, a) {
    const v = c.v[0];
    v.place(0, 0, 0, -Math.PI / 2);
    c.rec();
    c.run(v, 1, () => {
      v.input.handbrake = 1;
    });
    c.run(v, a.seconds, () => {
      v.input.handbrake = 0;
    });
  },
  parked(c, a) {
    const v = c.v[0];
    v.place(0, 0, 0, 0); // side-on to the slope
    c.rec();
    c.run(v, a.seconds, () => {
      v.input.handbrake = 1;
    });
  },
  manual(c) {
    const v = c.v[0];
    c.run(v, 0.3);
    const ups = [2.0, 2.8, 3.6].map((s) => Math.round(s / SIM_DT));
    const downs = [5.2, 5.6, 6.0, 6.4].map((s) => Math.round(s / SIM_DT));
    c.run(v, 7, (t, i) => {
      v.input.shiftUp = ups.includes(i);
      v.input.shiftDown = downs.includes(i);
      v.input.throttle = t < 4.4 ? 1 : t < 5 ? 0.5 : 0;
      v.input.clutch = t >= 4.4 && t < 5 ? 1 : 0;
      v.input.brake = t >= 5 ? 0.6 : 0;
    });
  },
  reverse(c) {
    const v = c.v[0];
    c.run(v, 0.5);
    c.run(v, 3, () => {
      v.input.brake = 1;
    });
    c.run(v, 2.5, () => {
      v.input.brake = 0;
      v.input.throttle = 1;
    });
  },
  crash(c, a) {
    const v = c.v[0];
    const world = makeWorld(a.layout);
    const collide = (): void => {
      const hit = collideWorld(v, world, (circle) => c.event('break', circle.id ?? -1, circle.x, circle.z));
      if (hit) c.event('hit', hit.impulse, hit.x, hit.z, hit.kind);
    };
    v.place(a.x, 0, a.z, a.yaw);
    c.run(v, 0.3, undefined, collide);
    v.setSpeed(a.speed);
    c.rec();
    c.run(
      v,
      a.seconds,
      () => {
        v.input.throttle = a.throttle;
      },
      collide,
    );
  },
  twocars(c, a) {
    const [A, B] = c.v;
    A.place(a.ax, 0, a.az, a.ayaw);
    B.place(a.bx, 0, a.bz, a.byaw);
    c.run(A, 0.3);
    c.run(B, 0.3);
    A.setSpeed(a.aspeed);
    if (a.bspeed) B.setSpeed(a.bspeed);
    c.rec();
    c.runPair(A, B, a.seconds, () => {
      A.input.throttle = a.athr;
      B.input.throttle = a.bthr;
    });
  },
  filterKeys(c) {
    const v = c.v[0];
    const f = new PlayerControlFilter();
    f.settings.steerSensitivity = 1.2;
    const ctl = neutralControls();
    ctl.analogSteer = false;
    ctl.source = 'keyboard';
    c.run(v, 0.3);
    c.run(v, 6, (t, i) => {
      ctl.steer = t < 1 ? 0 : t < 2 ? 1 : t < 2.5 ? 0 : t < 3.5 ? -1 : t < 4 ? 1 : 0;
      ctl.throttle = t < 4 ? 1 : 0;
      ctl.brake = t < 4 ? 0 : 1;
      ctl.handbrake = t >= 4.5 && t < 4.8 ? 1 : 0;
      ctl.clutch = t >= 1.5 && t < 1.6 ? 1 : 0;
      ctl.pressed.shiftUp = i === 240 || i === 480;
      ctl.pressed.shiftDown = i === 1100;
      if (i === 700) f.reset();
      v.input = f.update(ctl, v, SIM_DT);
    });
  },
  filterPad(c) {
    const v = c.v[0];
    const f = new PlayerControlFilter();
    const pad: PadSettings = { deadzone: 0.1, outerDeadzone: 0.03, triggerDeadzone: 0.04, steerLinearity: 1.25 };
    const ctl = neutralControls();
    c.run(v, 0.3);
    c.run(v, 6, (t) => {
      f.settings.autoThrottle = t >= 2 && t < 3;
      ctl.steer = shapeAxis(0.9 * dsin(t * 1.7), pad);
      ctl.throttle = shapeTrigger(0.5 + 0.5 * dsin(t * 0.7), pad);
      ctl.brake = shapeTrigger(t > 4.5 ? 0.8 : 0.02, pad);
      v.input = f.update(ctl, v, SIM_DT);
    });
  },
  drop(c, a) {
    const v = c.v[0];
    v.place(0, a.height, 0, 0);
    c.rec();
    c.run(v, a.seconds);
  },
  rollover(c, a) {
    const v = c.v[0];
    v.place(0, 1.5, 0, 0);
    v.quat.set(0, 0, dsin((Math.PI / 4) * 0.98), dcos((Math.PI / 4) * 0.98));
    v.angVel.set(0.5, 1.5, 3);
    c.rec();
    c.run(v, a.seconds);
  },
  setparams(c, a) {
    const v = c.v[0];
    v.place(0, 1, 0, 0);
    c.rec();
    c.run(v, 0.1); // airborne: the struts hang at full droop
    v.setParams(paramsFor({ car: a.other }));
    c.rec();
    c.run(v, a.seconds, () => {
      v.input.throttle = 0.6;
    });
  },
  tuned(c) {
    const v = c.v[0];
    v.draftFactor = 0.7;
    c.run(v, 0.5);
    c.run(v, 3, () => {
      v.input.throttle = 1;
    });
    c.run(v, 3, (t) => {
      v.input.throttle = 0.6;
      v.input.steer = -0.3 * dsin(t * 1.5);
    });
  },
  overrev(c, a) {
    const v = c.v[0];
    c.run(v, 0.3);
    v.setSpeed(a.speed);
    v.gear = 1; // forced: the clutch drags the engine past the limiter (and the torque table's end)
    c.rec();
    c.run(v, 1, () => {
      v.input.throttle = 0;
    });
    v.gear = 0; // neutral
    c.rec();
    c.run(v, 1, () => {
      v.input.throttle = 0.5;
    });
  },
  cruise(c, a) {
    const v = c.v[0];
    c.run(v, 0.3);
    v.setSpeed(a.speed);
    c.rec();
    c.run(v, a.seconds, () => {
      v.input.throttle = a.throttle;
    });
  },
  edge(c, a) {
    const v = c.v[0];
    v.place(0, 0, 0, -Math.PI / 2); // heading +X, toward the edge at x = 20
    c.run(v, 0.3);
    v.setSpeed(a.speed);
    c.rec();
    c.run(v, a.seconds, () => {
      v.input.throttle = 0.3;
    });
  },
  limiter(c, a) {
    const v = c.v[0];
    c.run(v, 0.3);
    v.setSpeed(a.speed);
    c.rec();
    c.run(v, a.seconds, () => {
      v.input.throttle = 1;
    });
  },
};

// --- Scenarios ---------------------------------------------------------------

interface Scenario {
  name: string;
  driver: string;
  cars: CarSetup[];
  aids?: Partial<Aids>;
  ground?: GroundSpec;
  every?: number;
  args?: Args;
}
const H = BASE.id;
const OFF: Partial<Aids> = { abs: false, tcs: false, esc: false, steering: 'off', gearbox: 'manual' };
const SCENARIOS: Scenario[] = [
  ...ALL_CARS.map((s): Scenario => ({ name: `launch.${s.id}`, driver: 'launch', cars: [{ car: s.id }], args: { seconds: 5.5 } })),
  { name: `launch-notcs.${H}`, driver: 'launch', cars: [{ car: H }], aids: { tcs: false }, every: 12, args: { seconds: 3 } },
  { name: `brake-abs.${H}`, driver: 'brake', cars: [{ car: H }], args: { kmh: 100, seconds: 3.5 } },
  { name: `brake-noabs.${H}`, driver: 'brake', cars: [{ car: H }], aids: { abs: false }, args: { kmh: 100, seconds: 3.5 } },
  { name: 'brake-abs.test-fwd-na', driver: 'brake', cars: [{ car: 'test-fwd-na' }], args: { kmh: 100, seconds: 3.5 } },
  { name: 'brake-abs.test-ev-awd', driver: 'brake', cars: [{ car: 'test-ev-awd' }], args: { kmh: 100, seconds: 3.5 } },
  { name: 'brake-noabs.test-truck-offroad', driver: 'brake', cars: [{ car: 'test-truck-offroad' }], aids: { abs: false }, args: { kmh: 100, seconds: 3.5 } },
  { name: `corner.${H}`, driver: 'corner', cars: [{ car: H }], aids: { steering: 'off', esc: false }, args: { radius: 40, speed: 15, steer: -0.13, seconds: 6 } },
  { name: `weave-assisted.${H}`, driver: 'weave', cars: [{ car: H }], aids: { steering: 'assisted' }, args: { speed: 20, steer: -0.5, amp: 0.3, freq: 2, throttle: 0.3, seconds: 4 } },
  { name: 'weave-standard.test-awd-turbo', driver: 'weave', cars: [{ car: 'test-awd-turbo' }], args: { speed: 18, steer: 0.2, amp: 0.6, freq: 2.5, throttle: 0.5, seconds: 4 } },
  { name: 'weave-off.test-super-mid', driver: 'weave', cars: [{ car: 'test-super-mid' }], aids: { steering: 'off' }, args: { speed: 30, steer: 0, amp: 0.25, freq: 3, throttle: 0.7, seconds: 4 } },
  { name: `lanechange-esc.${H}`, driver: 'lanechange', cars: [{ car: H }], aids: { esc: true, steering: 'off', tcs: true }, args: { speed: 33, seconds: 4 } },
  { name: `lanechange-noesc.${H}`, driver: 'lanechange', cars: [{ car: H }], aids: { esc: false, steering: 'off', tcs: true }, args: { speed: 33, seconds: 4 } },
  { name: 'lanechange-esc.test-fwd-na', driver: 'lanechange', cars: [{ car: 'test-fwd-na' }], aids: { steering: 'off' }, args: { speed: 30, seconds: 4 } },
  { name: `handbrake.${H}`, driver: 'handbrake', cars: [{ car: H }], aids: { esc: false, tcs: false, steering: 'off' }, args: { speed: 17, seconds: 3 } },
  { name: 'handbrake.test-awd-turbo', driver: 'handbrake', cars: [{ car: 'test-awd-turbo' }], aids: { esc: false, tcs: false, steering: 'off' }, args: { speed: 17, seconds: 3 } },
  { name: `surfaces.${H}`, driver: 'surfaces', cars: [{ car: H }], ground: { type: 'stripes' }, args: { speed: 12, seconds: 6, brakeAt: 4.5 } },
  { name: 'surfaces-snowtires.test-awd-turbo', driver: 'surfaces', cars: [{ car: 'test-awd-turbo', up: compoundUp('snow') }], aids: { tcs: false }, ground: { type: 'stripes' }, args: { speed: 10, seconds: 6, brakeAt: 4 } },
  { name: `surfaces-dragtires.${H}`, driver: 'surfaces', cars: [{ car: H, up: compoundUp('drag') }], aids: { tcs: false, esc: false }, ground: { type: 'stripes' }, args: { speed: 14, seconds: 5, brakeAt: 3.5 } },
  { name: `puddle.${H}`, driver: 'puddle', cars: [{ car: H }], ground: { type: 'puddle' }, args: { speed: 25, seconds: 3 } },
  { name: `hillstart.${H}`, driver: 'hillstart', cars: [{ car: H }], ground: { type: 'slope', grade: 0.1, surface: SURFACE.asphalt }, args: { seconds: 3.6 } },
  { name: 'hillstart.test-fwd-na', driver: 'hillstart', cars: [{ car: 'test-fwd-na' }], ground: { type: 'slope', grade: 0.1, surface: SURFACE.asphalt }, args: { seconds: 3.6 } },
  { name: 'rollback.test-ev-awd', driver: 'rollback', cars: [{ car: 'test-ev-awd' }], ground: { type: 'slope', grade: 0.1, surface: SURFACE.asphalt }, args: { seconds: 3 } },
  { name: `parked-ice.${H}`, driver: 'parked', cars: [{ car: H }], ground: { type: 'slope', grade: 0.15, surface: SURFACE.ice }, args: { seconds: 3 } },
  { name: `manual.${H}`, driver: 'manual', cars: [{ car: H }], aids: OFF },
  { name: 'manual.test-ev-awd', driver: 'manual', cars: [{ car: 'test-ev-awd' }], aids: OFF },
  { name: `reverse.${H}`, driver: 'reverse', cars: [{ car: H }] },
  { name: 'reverse.test-fwd-na', driver: 'reverse', cars: [{ car: 'test-fwd-na' }] },
  { name: `crash-wall.${H}`, driver: 'crash', cars: [{ car: H }], every: 12, args: { layout: 'wall', x: 0, z: 0, yaw: 0.25, speed: 18, throttle: 0.4, seconds: 3 } },
  { name: `crash-tree.${H}`, driver: 'crash', cars: [{ car: H }], every: 12, args: { layout: 'tree', x: 0, z: 0, yaw: 0, speed: 16, throttle: 0.3, seconds: 3 } },
  { name: 'crash-post.test-truck-offroad', driver: 'crash', cars: [{ car: 'test-truck-offroad' }], every: 12, args: { layout: 'post', x: 0, z: 0, yaw: -0.02, speed: 14, throttle: 0.3, seconds: 3 } },
  { name: 'crash-rail.test-super-mid', driver: 'crash', cars: [{ car: 'test-super-mid' }], every: 12, args: { layout: 'rail', x: 0, z: 0, yaw: 0.12, speed: 25, throttle: 0.5, seconds: 3 } },
  { name: `crash-end.${H}`, driver: 'crash', cars: [{ car: H }], every: 12, args: { layout: 'end', x: 0, z: 0, yaw: 0, speed: 10, throttle: 0.2, seconds: 3 } },
  { name: 'crash-end2.test-fwd-na', driver: 'crash', cars: [{ car: 'test-fwd-na' }], every: 12, args: { layout: 'end', x: 0.2, z: 0, yaw: 0, speed: 9, throttle: 0.2, seconds: 3 } },
  { name: `twocars-rear.${H}`, driver: 'twocars', cars: [{ car: H }, { car: H }], every: 12, args: { ax: 0, az: 0, ayaw: 0, bx: 0, bz: -12, byaw: 0, aspeed: 15, bspeed: 0, athr: 0, bthr: 0, seconds: 2 } },
  { name: 'twocars-tbone.test-fwd-na.test-truck-offroad', driver: 'twocars', cars: [{ car: 'test-fwd-na' }, { car: 'test-truck-offroad' }], every: 12, args: { ax: 0, az: 0, ayaw: 0, bx: -10, bz: -14, byaw: -Math.PI / 2, aspeed: 14, bspeed: 10, athr: 0.2, bthr: 0.2, seconds: 3 } },
  { name: `filter-keys.${H}`, driver: 'filterKeys', cars: [{ car: H }], aids: { gearbox: 'manual' } },
  { name: `filter-pad.${H}`, driver: 'filterPad', cars: [{ car: H }] },
  { name: `drop.${H}`, driver: 'drop', cars: [{ car: H }], every: 12, args: { height: 2, seconds: 3 } },
  { name: `rollover.${H}`, driver: 'rollover', cars: [{ car: H }], args: { seconds: 4 } },
  { name: `setparams.${H}`, driver: 'setparams', cars: [{ car: H }], args: { other: 'test-super-mid', seconds: 2.5 } },
  { name: `tuned.${H}`, driver: 'tuned', cars: [{ car: H, tune: TUNED, up: TUNED_UP }] },
  { name: `overrev.${H}`, driver: 'overrev', cars: [{ car: H }], aids: OFF, args: { speed: 28 } },
  { name: `cruise.${H}`, driver: 'cruise', cars: [{ car: H }], every: 1200, args: { speed: 75, throttle: 1, seconds: 60 } },
  { name: `edge.${H}`, driver: 'edge', cars: [{ car: H }], ground: { type: 'edge' }, every: 12, args: { speed: 15, seconds: 3 } },
  { name: 'limiter.test-ev-awd', driver: 'limiter', cars: [{ car: 'test-ev-awd' }], args: { speed: 60, seconds: 3 } },
];

// --- Static checks -----------------------------------------------------------

/** Deterministic pseudo-random inputs (the values are written out, so the C# side needs no generator). */
let lcg = 0x12345678;
function rnd(): number {
  lcg = (Math.imul(lcg, 1664525) + 1013904223) >>> 0;
  return lcg / 4294967296;
}
const pick = <T>(xs: readonly T[]): T => xs[Math.floor(rnd() * xs.length)];

function paramsDump(p: VehicleParams): unknown {
  const { spec: _spec, ...rest } = p;
  return rest;
}

function staticChecks(): Record<string, unknown> {
  // Curves: every compound's, plus clamped and edge cases.
  const curveCases: number[][] = COMPOUND_ORDER.map((k) => [COMPOUNDS[k].slide, COMPOUNDS[k].E]);
  curveCases.push([0.2, 0.3], [1.0, 0.1], [0.5, 0], [0.95, 0.6]);
  const xs = [0, 1e-7, 0.25, 0.5, 0.999, 1, 1.5, 2.5, 5, 12, 100];
  const curves = curveCases.map(([slide, E]) => {
    const c = makeCurve(slide, E);
    return { slide, E, B: c.B, C: c.C, curveE: c.E, mf: xs.map((x) => mf(c, x)), mfSlope: xs.map((x) => mfSlope(c, x)) };
  });
  const curveXs = xs;

  // tireForce on each compound's front tire setup (the Aster GT with the compound fitted).
  const tire: unknown[] = [];
  for (const k of COMPOUND_ORDER) {
    const setup = buildParams(BASE, defaultTune(BASE), compoundUp(k)).wheels[0].tire;
    const inputs: number[][] = [
      [0, 10, 1, 10, 1],
      [-50, 10, 1, 10, 1],
      [4000, 10, 0, 10, 1], // zero slip
      [4000, 0, 0, 0, 1],
      [4000, -0, 0, 0, 1],
      [3500, 0.5, 0.02, 0.6, 0.8],
      [9000, 30, -3, 36, 1],
      [2000, -12, 1.5, -15, 0.45],
    ];
    for (let i = 0; i < 40; i++) {
      const vLong = pick([-25, -3, -0.4, 0, 0.2, 2.5, 8, 19, 42]) * (0.8 + 0.4 * rnd());
      const vLat = pick([0, 0, 0.05, -0.3, 1.2, -4, 7]) * (0.5 + rnd());
      const ws = vLong * pick([1, 0.5, 0.95, 1.08, 1.4, 0, -0.2, 3]) + pick([0, 0, 0.3, -0.7]);
      inputs.push([pick([300, 2500, 3700, 4200, 6100, 11000]) * (0.9 + 0.2 * rnd()), vLong, vLat, ws, pick([1, 1, 0.74, 0.42, 0.11, 1.05])]);
    }
    const res: TireResult = { fx: 0, fy: 0, dFxdV: 0, slip: 0 };
    const outputs = inputs.map(([Fz, vL, vS, ws, grip]) => {
      tireForce(setup, Fz, vL, vS, ws, grip, res);
      return [res.fx, res.fy, res.dFxdV, res.slip];
    });
    tire.push({ compound: k, inputs, outputs });
  }

  // Pad shaping.
  const pads: PadSettings[] = [
    { deadzone: 0.1, outerDeadzone: 0.03, triggerDeadzone: 0.04, steerLinearity: 1.25 },
    { deadzone: 0, outerDeadzone: 0, triggerDeadzone: 0, steerLinearity: 1 },
    { deadzone: 0.2, outerDeadzone: 0.1, triggerDeadzone: 0.1, steerLinearity: 2 },
    { deadzone: 0.05, outerDeadzone: 0.95, triggerDeadzone: 0.5, steerLinearity: 3 },
  ];
  const vs = [-1.2, -1, -0.73, -0.5, -0.11, -0.1, -0.05, -0, 0, 0.03, 0.1, 0.1000001, 0.35, 0.5, 0.87, 0.97, 1, 1.4];
  const shaping = pads.map((pad) => ({ pad, v: vs, axis: vs.map((v) => shapeAxis(v, pad)), trigger: vs.map((v) => shapeTrigger(v, pad)) }));

  // dmath (the user's DMath port) and the JavaScript semantics JsMath and M rely on.
  const dx: number[] = [0, -0, 1e-12, -3e-9, 0.1, -0.5, 0.7853981633974483, -0.7853981633974483, 1, -1, 1.5707963267948966, 2, -2.5, 3.141592653589793, 4, -7.3, 10, 100, 1e4, -12345.678, 0.999, -0.999, 0.5, 1e20, -3e19, 1e300, -1e300, 9.3e18];
  for (let i = 0; i < 60; i++) dx.push((rnd() - 0.5) * pick([2, 10, 80, 2000]));
  const unary = (f: string, fn: (x: number) => number, xs1: number[]): unknown => ({ f, a: xs1, out: xs1.map(fn) });
  const absx = dx.map((x) => Math.abs(x));
  const expx = dx.map((x) => Math.max(-700, Math.min(700, x)));
  const atanB = dx.map((_x, i) => dx[(i * 7 + 3) % dx.length]);
  const powY = dx.map((_x, i) => [0, 1, 2, 0.5, 3, -2, 1.25, 2.5, -0.5, 1 / 3][i % 10]);
  const dmath = [
    unary('sin', dsin, dx), unary('cos', dcos, dx), unary('tan', dtan, dx), unary('atan', datan, dx), unary('asin', dasin, dx), unary('acos', dacos, dx),
    unary('exp', dexp, expx), unary('log', dlog, absx), unary('tanh', dtanh, dx),
    { f: 'atan2', a: dx, b: atanB, out: dx.map((y, i) => datan2(y, atanB[i])) },
    { f: 'pow', a: absx, b: powY, out: absx.map((x, i) => dpow(x, powY[i])) },
  ];
  const rx = [0.5, 1.5, 2.5, -0.5, -1.5, -2.5, -0.4, -0.6, 0.49999999999999994, -0.49999999999999994, -0, 0, 3.7, -3.7, 1e16 + 2, -4503599627370495.5];
  const mm = [[0, -0], [-0, 0], [-0, -0], [1, NaN], [NaN, 1], [2, 3], [-3, -2], [5, 5]];
  const jsmath = {
    round: { x: rx, r: rx.map((x) => Math.round(x)) },
    minmax: { ab: mm, max: mm.map(([a, b]) => Math.max(a, b)), min: mm.map(([a, b]) => Math.min(a, b)) },
    hypot: Array.from({ length: 200 }, (_, i) => {
      const x = (rnd() - 0.5) * 10 ** ((i % 9) - 4);
      const y = i % 13 === 0 ? 0 : (rnd() - 0.5) * 10 ** (((i * 7) % 9) - 4);
      return [x, y, Math.hypot(x, y)];
    }),
  };

  // Params: every car with defaults, plus the tuned and compound configurations the scenarios use.
  const configs: CarSetup[] = [
    ...ALL_CARS.map((s) => ({ car: s.id })),
    { car: H, tune: TUNED, up: TUNED_UP },
    ...COMPOUND_ORDER.map((k) => ({ car: H, up: compoundUp(k) })),
    { car: 'test-awd-turbo', up: compoundUp('snow') },
    { car: 'test-ev-awd', tune: { ...defaultTune(carSpec('test-ev-awd')), awdFront: 0.7, gearScale: [] }, up: { ...noUpgrades(), clAAdd: -0.1, aeroKit: 2 } },
  ];
  const params = configs.map((s) => ({ ...s, params: paramsDump(paramsFor(s)) }));

  return { curveXs, curves, tire, shaping, dmath, jsmath, params, collisionWorld: fuzzWorld(), collisionCars: fuzzCars() };
}

// --- Collision fuzz: random poses, velocities and obstacles ---------------------

const SEG_KINDS = ['wall', 'barrier', 'tires', 'rail', 'fence'] as const;
const CIRCLE_KINDS = ['post', 'tree', 'rock', 'pier', 'building'] as const;
const r3 = (s: number): number => (rnd() - 0.5) * s;

/** A car at (x, z, yaw), raised by dy, with velocity and angular velocity (inputs for one fuzz case). */
function fuzzVehicle(car: string, pose: number[], vel: number[], ang: number[]): Vehicle {
  const v = new Vehicle(paramsFor({ car }));
  v.place(pose[0], 0, pose[1], pose[2]);
  v.pos.y += pose[3];
  v.vel.set(vel[0], vel[1], vel[2]);
  v.angVel.set(ang[0], ang[1], ang[2]);
  return v;
}
const vstate = (v: Vehicle): number[] => [v.pos.x, v.pos.y, v.pos.z, v.vel.x, v.vel.y, v.vel.z, v.angVel.x, v.angVel.y, v.angVel.z, v.impact];

function fuzzWorld(): unknown[] {
  const out: unknown[] = [];
  for (let i = 0; i < 250; i++) {
    const car = ALL_CARS[i % ALL_CARS.length].id;
    const pose = [r3(6), r3(6), r3(2 * Math.PI), (rnd() - 0.3) * 1.2];
    const vel = [r3(30), r3(2), r3(30)];
    const ang = [r3(2), r3(4), r3(2)];
    const v = fuzzVehicle(car, pose, vel, ang);
    // A few cases with the heading forced vertical (carBox's `|| 1`).
    const vertical = i % 97 === 5;
    if (vertical) v.fwd.set(0, 1, 0);
    const w = new Colliders();
    const obstacles: Num[][] = [];
    const n = 1 + Math.floor(rnd() * 3);
    for (let k = 0; k < n; k++) {
      if (rnd() < 0.55) {
        const cx = pose[0] + r3(7);
        const cz = pose[1] + r3(7);
        const dir = rnd() * 2 * Math.PI;
        const len = rnd() < 0.08 ? 0 : rnd() * 6;
        const seg = {
          ax: cx - (Math.cos(dir) * len) / 2, az: cz - (Math.sin(dir) * len) / 2, bx: cx + (Math.cos(dir) * len) / 2, bz: cz + (Math.sin(dir) * len) / 2,
          top: rnd() * 2.5 - 0.3, bottom: -1 + rnd() * 0.8, bounce: rnd() * 0.5, friction: rnd(), kind: pick(SEG_KINDS),
        };
        w.addSegment(seg);
        obstacles.push(['s', seg.ax, seg.az, seg.bx, seg.bz, seg.top, seg.bottom, seg.bounce, seg.friction, seg.kind]);
      } else {
        const circle: Circle = { x: pose[0] + r3(6), z: pose[1] + r3(6), r: rnd() * 1.2, top: rnd() * 3 - 0.2, bottom: -0.5 + rnd() * 0.6, kind: pick(CIRCLE_KINDS), breakable: rnd() < 0.2 };
        w.addCircle(circle);
        obstacles.push(['c', circle.x, circle.z, circle.r, circle.top, circle.bottom, circle.kind, circle.breakable ? 1 : 0]);
      }
    }
    const breaks: number[] = [];
    const hit = collideWorld(v, w, (c) => breaks.push(c.id ?? -1));
    out.push({ car, pose, vel, ang, vertical, obstacles, hit: hit ? [hit.impulse, hit.x, hit.z, hit.kind] : null, breaks, state: vstate(v) });
  }
  return out;
}

function fuzzCars(): unknown[] {
  const out: unknown[] = [];
  for (let i = 0; i < 150; i++) {
    const carA = ALL_CARS[i % ALL_CARS.length].id;
    const carB = ALL_CARS[(i * 7 + 3) % ALL_CARS.length].id;
    const poseA = [r3(4), r3(4), r3(2 * Math.PI), (rnd() - 0.3) * 0.8];
    // Every fifth pair has B up to 3 m higher (one car landing on another: the height filter).
    const poseB = [poseA[0] + r3(9), poseA[1] + r3(9), r3(2 * Math.PI), (rnd() - 0.3) * (i % 5 === 0 ? 4.5 : 0.8)];
    const velA = [r3(30), r3(1), r3(30)];
    const velB = [r3(30), r3(1), r3(30)];
    const angA = [r3(1), r3(3), r3(1)];
    const angB = [r3(1), r3(3), r3(1)];
    const a = fuzzVehicle(carA, poseA, velA, angA);
    const b = fuzzVehicle(carB, poseB, velB, angB);
    const j = collideCars(a, b);
    out.push({ carA, carB, poseA, poseB, velA, velB, angA, angB, j, a: vstate(a), b: vstate(b) });
  }
  return out;
}

// --- Telemetry ---------------------------------------------------------------

function telemetry(): unknown[] {
  const out: unknown[] = [];
  for (const s of ALL_CARS) {
    const p = buildParams(s);
    out.push({ car: s.id, test: 'accel', r: accelTest(p) });
    out.push({ car: s.id, test: 'accel-notcs-6s', r: accelTest(p, { aids: { tcs: false }, maxTime: 6 }) });
    out.push({ car: s.id, test: 'brake-abs-100', r: brakeTest(p, 100, { aids: { abs: true } }) });
    out.push({ car: s.id, test: 'brake-noabs-100', r: brakeTest(p, 100, { aids: { abs: false } }) });
    out.push({ car: s.id, test: 'skidpad-40', r: skidpadTest(p, 40) });
  }
  const p = buildParams(BASE);
  out.push({ car: H, test: 'skidpad-40-grass-90s', r: skidpadTest(p, 40, { surface: SURFACE.grass, maxTime: 90 }) });
  out.push({ car: H, test: 'accel-sand-30s', r: accelTest(p, { surface: SURFACE.sand, maxTime: 30 }) });
  out.push({ car: H, test: 'brake-snow-60', r: brakeTest(p, 60, { surface: SURFACE.snow }) });
  out.push({ car: H, test: 'skidpad-3-too-tight', r: skidpadTest(p, 3, { maxTime: 10 }) }); // never steady: err NaN
  out.push({ car: H, test: 'skidpad-40-ice-20s', r: skidpadTest(p, 40, { surface: SURFACE.ice, maxTime: 20 }) });
  out.push({ car: H, test: 'skidpad-25-assisted-esc', r: skidpadTest(p, 25, { aids: { steering: 'assisted', esc: true }, maxTime: 40 }) });
  return out;
}

// --- Main ----------------------------------------------------------------------

fs.mkdirSync(OUT, { recursive: true });
for (const f of fs.readdirSync(OUT)) if (f.endsWith('.json')) fs.rmSync(path.join(OUT, f));
const t0 = performance.now();
let bytes = 0;

bytes += writeJson('cars.json', {
  generator: 'tools/dev/vehicle-ref.ts',
  simDt: SIM_DT,
  cars: CARS,
  variants: VARIANTS,
  compounds: COMPOUND_ORDER.map((k) => ({ key: k, info: COMPOUNDS[k] })),
  aidsDefault: defaultAids(),
});
bytes += writeJson('static.json', staticChecks());
bytes += writeJson('telemetry.json', { results: telemetry() });

const index: unknown[] = [];
for (const sc of SCENARIOS) {
  const ps = sc.cars.map(paramsFor);
  const vs = ps.map((p) => {
    const v = new Vehicle(p);
    v.aids = { ...defaultAids(), ...sc.aids };
    return v;
  });
  const ground = sc.ground ?? FLAT;
  const c = new Ctx(vs, makeGround(ground), sc.every ?? 24);
  DRIVERS[sc.driver](c, sc.args ?? {});
  const file = `trace.${sc.name}.json`;
  const head = {
    name: sc.name,
    driver: sc.driver,
    cars: sc.cars,
    aids: vs.map((v) => v.aids),
    ground,
    every: c.every,
    args: sc.args ?? {},
    steps: c.n,
    columns: columns(vs.length),
    events: c.events,
  };
  bytes += writeJson(file, head, 'rows', c.rows);
  index.push({ name: sc.name, file, steps: c.n, rows: c.rows.length, events: c.events.length });
}
bytes += writeJson('index.json', { scenarios: index });
console.log(`wrote ${SCENARIOS.length} traces + static checks to ${path.relative(process.cwd(), OUT)}: ${(bytes / 1e6).toFixed(2)} MB in ${((performance.now() - t0) / 1000).toFixed(1)} s`);
