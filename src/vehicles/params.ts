// VehicleParams: the numbers the physics step uses, derived from a CarSpec
// plus tuning. Every tuning slider changes the physics through a formula in
// this file; docs/DESIGN.md lists them.
import { V3, clamp } from '../engine/math';
import { dtan } from '../engine/dmath';
import { COMPOUNDS, makeCurve, type TireSetup } from './tires';
import type { CarSpec, DiffSpec, Drivetrain } from './spec';
import type { Compound } from './tires';

export const GRAVITY = 9.81;
export const AIR_DENSITY = 1.225;

export interface WheelParams {
  /** Suspension hardpoint (top of travel) in the body frame. */
  hardpoint: V3;
  radius: number;
  /** Wheel, tire, hub and brake inertia, kg·m². */
  inertia: number;
  front: boolean;
  left: boolean;
  steerable: boolean;
  /** Share of drive torque this wheel receives through open differentials. */
  driveWeight: number;
  /** Spring rate at the wheel, N/m. */
  spring: number;
  /** Spring free length, m (from the hardpoint, along the strut). */
  freeLength: number;
  /** Static length at design load, m. */
  staticLength: number;
  /** Full droop length, m. */
  maxLength: number;
  /** Bump stop engages below this length, m. */
  bumpLength: number;
  damperBump: number;
  damperRebound: number;
  maxBrake: number;
  handbrake: boolean;
  /** Static toe angle, rad (+ = toe-in). */
  toe: number;
  /** Static camber, rad (negative = top leaning in). */
  camber: number;
  /** Height above the contact point where tire forces act, m. */
  rollCenter: number;
  tire: TireSetup;
}

export interface AxleParams {
  left: number;
  right: number;
  arb: number;
  diff: DiffSpec;
}

export interface EngineParams {
  rpm: Float64Array;
  torque: Float64Array;
  idle: number;
  redline: number;
  limiter: number;
  inertia: number;
  peakTorque: number;
  peakTorqueRpm: number;
  peakPower: number;
  peakPowerRpm: number;
  /** Closed-throttle engine braking at the redline, N·m. */
  brakeTorque: number;
  turbo: { lag: number; share: number; spoolRpm: number; fullRpm: number } | null;
  electric: boolean;
  cylinders: number;
}

export interface GearboxParams {
  /** ratios[0] is reverse (negative), ratios[1..n] are forward gears. */
  ratios: number[];
  final: number;
  shiftTime: number;
  upshiftRpm: number[];
  downshiftRpm: number[];
  clutchMaxTorque: number;
  /** Engine speed the auto clutch holds while launching, rpm. */
  launchRpm: number;
  efficiency: number;
}

export interface VehicleParams {
  spec: CarSpec;
  mass: number;
  invMass: number;
  /** Body-frame principal inertia (pitch about X, yaw about Y, roll about Z). */
  inertia: V3;
  invInertia: V3;
  wheelbase: number;
  cgHeight: number;
  /** Distance from the COM to the front and rear axles. */
  a: number;
  b: number;
  wheels: WheelParams[];
  axles: AxleParams[];
  drivetrain: Drivetrain;
  /** Center coupling locking torque (AWD), N·m per rad/s of axle speed difference. */
  centerLock: number;
  engine: EngineParams;
  gearbox: GearboxParams;
  aero: { cdA: number; clA: number; balance: number };
  maxSteer: number;
  steerRatio: number;
  ackermann: number;
  /** Body collision half extents (body frame) and center offset from the COM. */
  half: V3;
  boxCenter: V3;
  /** Points on the body that collide with the ground, body frame. */
  bodyPoints: V3[];
  /** Brake bias toward the front (0..1), already folded into maxBrake. */
  brakeBias: number;
}

/** Tuning layer. Every field has a documented effect in buildParams(). */
export interface Tune {
  /** Tire pressure offset from nominal, bar (−0.6 .. +0.6). */
  pressureF: number;
  pressureR: number;
  /** Final drive multiplier (0.8 .. 1.25). */
  finalDrive: number;
  /** Per-gear ratio multipliers (index 0 = first gear). */
  gearScale: number[];
  /** Camber, degrees (−4 .. +1). */
  camberF: number;
  camberR: number;
  /** Toe, degrees (+ = toe-in, −1 .. +1). */
  toeF: number;
  toeR: number;
  /** Caster, degrees (3 .. 8). */
  caster: number;
  /** Anti-roll bar multipliers (0.2 .. 2.5). */
  arbF: number;
  arbR: number;
  /** Spring natural frequency multipliers (0.6 .. 1.8). */
  springF: number;
  springR: number;
  /** Ride height offset, m (−0.05 .. +0.08). */
  rideHeight: number;
  /** Damping ratio multipliers (0.5 .. 2), bump and rebound. */
  bumpF: number;
  bumpR: number;
  reboundF: number;
  reboundR: number;
  /** Aero downforce multipliers (0 .. 2), front and rear. */
  aeroF: number;
  aeroR: number;
  /** Brake balance, front share (0.45 .. 0.75) and pressure multiplier (0.7 .. 1.3). */
  brakeBalance: number;
  brakePressure: number;
  /** Differential locking: accel and decel (0..1), and AWD front split (0.1 .. 0.9). */
  diffAccel: number;
  diffDecel: number;
  awdFront: number;
}

export function defaultTune(spec: CarSpec): Tune {
  const brakeBias = spec.brakes.torqueF / (spec.brakes.torqueF + spec.brakes.torqueR);
  return {
    pressureF: 0,
    pressureR: 0,
    finalDrive: 1,
    gearScale: spec.gears.map(() => 1),
    camberF: -1.5,
    camberR: -1.0,
    toeF: 0,
    toeR: 0.1,
    caster: 5.5,
    arbF: 1,
    arbR: 1,
    springF: 1,
    springR: 1,
    rideHeight: 0,
    bumpF: 1,
    bumpR: 1,
    reboundF: 1,
    reboundR: 1,
    aeroF: 1,
    aeroR: 1,
    brakeBalance: brakeBias,
    brakePressure: 1,
    diffAccel: spec.diff.accel,
    diffDecel: spec.diff.decel,
    awdFront: spec.awdFront ?? 0.4,
  };
}

/** Upgrade layer (M5 fills this in). Multipliers default to 1. */
export interface Upgrades {
  powerScale: number;
  massScale: number;
  compound: Compound | null;
  tireWidthAdd: number;
  brakeScale: number;
  aeroKit: number;
  /** Extra downforce area added by an aero kit, m². */
  clAAdd: number;
}

export function noUpgrades(): Upgrades {
  return { powerScale: 1, massScale: 1, compound: null, tireWidthAdd: 0, brakeScale: 1, aeroKit: 0, clAAdd: 0 };
}

function interpTorque(curve: [number, number][], rpm: number): number {
  if (rpm <= curve[0][0]) return curve[0][1];
  for (let i = 1; i < curve.length; i++) {
    if (rpm <= curve[i][0]) {
      const [r0, t0] = curve[i - 1];
      const [r1, t1] = curve[i];
      return t0 + ((t1 - t0) * (rpm - r0)) / (r1 - r0);
    }
  }
  return curve[curve.length - 1][1];
}

/** Camber grip formula (documented in DESIGN.md): best lateral grip near −2°. */
export function camberLateralGrip(camberDeg: number): number {
  const d = camberDeg + 2.0;
  return 1.03 - 0.012 * d * d;
}
export function camberLongGrip(camberDeg: number): number {
  return 1 - 0.008 * Math.abs(camberDeg);
}
/** Pressure grip: peak at nominal, ±1 bar costs about 7%. Higher pressure sharpens response. */
export function pressureGrip(bar: number): number {
  return 1 - 0.07 * bar * bar;
}

export function buildParams(spec: CarSpec, tune: Tune = defaultTune(spec), up: Upgrades = noUpgrades()): VehicleParams {
  const mass = spec.mass * up.massScale;
  const wf = spec.weightFront;
  const L = spec.wheelbase;
  const a = L * (1 - wf);
  const b = L * wf;
  const cgHeight = spec.cgHeight + tune.rideHeight * 0.9;
  const r = spec.tires.radius;
  const W = spec.width;
  const H = spec.height;
  const len = spec.length;
  const yawScale = spec.yawInertiaScale ?? 1;
  // Effective dimensions: mass sits lower and more central than a uniform box.
  const hEff = H * 0.75;
  const inertia = new V3((mass / 12) * (hEff * hEff + len * len) * 0.88, (mass / 12) * (W * W + len * len) * 0.92 * yawScale, (mass / 12) * (W * W + hEff * hEff) * 0.9);

  const compound: Compound = up.compound ?? spec.tires.compound;
  const info = COMPOUNDS[compound];
  const curve = makeCurve(info.slide, info.E);
  const staticF = (mass * GRAVITY * wf) / 2;
  const staticR = (mass * GRAVITY * (1 - wf)) / 2;
  const mkTire = (width: number, load: number, pressure: number, camberDeg: number): TireSetup => {
    // Wider tires relative to the load they carry grip a little more.
    const loadPerMm = load / (width + up.tireWidthAdd);
    const widthGrip = Math.max(0.9, Math.min(1.12, 1 + 0.18 * (1 - loadPerMm / 15)));
    const alphaPeak = info.alphaPeak * (1 - 0.06 * pressure);
    return {
      compound,
      info,
      curve,
      widthGrip,
      tuneGrip: pressureGrip(pressure) * camberLongGrip(camberDeg),
      tuneLateral: camberLateralGrip(camberDeg) / camberLongGrip(camberDeg),
      alphaPeak,
      tanAlphaPeak: dtan(alphaPeak),
      kappaPeak: info.kappaPeak,
      nominalLoad: load,
    };
  };
  const tireF = mkTire(spec.tires.widthF, staticF, tune.pressureF, tune.camberF);
  const tireR = mkTire(spec.tires.widthR, staticR, tune.pressureR, tune.camberR);

  const travel = spec.suspension.travel;
  const deg = Math.PI / 180;
  const corner = (front: boolean, left: boolean): WheelParams => {
    const load = front ? staticF : staticR;
    const cornerMass = load / GRAVITY;
    const freq = (front ? spec.suspension.freqF * tune.springF : spec.suspension.freqR * tune.springR);
    const omegaN = 2 * Math.PI * freq;
    const k = cornerMass * omegaN * omegaN;
    const crit = 2 * Math.sqrt(k * cornerMass);
    const zeta = front ? spec.suspension.dampF : spec.suspension.dampR;
    const staticBase = travel * 0.45;
    const staticLength = Math.max(travel * 0.12, staticBase + tune.rideHeight);
    const track = front ? spec.trackFront : spec.trackRear;
    const hpY = r - spec.cgHeight + staticBase; // fixed in the body: ride height moves the COM instead
    const driven = spec.drivetrain === 'AWD' || (spec.drivetrain === 'FWD' ? front : !front);
    const awdF = tune.awdFront;
    const driveWeight = !driven ? 0 : spec.drivetrain === 'AWD' ? (front ? awdF : 1 - awdF) / 2 : 0.5;
    const brakeTotal = (spec.brakes.torqueF + spec.brakes.torqueR) * up.brakeScale * tune.brakePressure;
    const maxBrake = front ? brakeTotal * tune.brakeBalance : brakeTotal * (1 - tune.brakeBalance);
    return {
      hardpoint: new V3((left ? -1 : 1) * track / 2, hpY - (spec.cgHeight - cgHeight), front ? -a : b),
      radius: r,
      inertia: ((0.55 * (front ? spec.tires.widthF : spec.tires.widthR)) / 255) * (r / 0.33) * (r / 0.33) + 0.6,
      front,
      left,
      steerable: front,
      driveWeight,
      spring: k,
      freeLength: staticLength + load / k,
      staticLength,
      maxLength: travel,
      bumpLength: travel * 0.04,
      damperBump: crit * zeta * (front ? tune.bumpF : tune.bumpR),
      damperRebound: crit * zeta * 1.6 * (front ? tune.reboundF : tune.reboundR),
      maxBrake,
      handbrake: !front,
      toe: (front ? tune.toeF : tune.toeR) * deg,
      camber: (front ? tune.camberF : tune.camberR) * deg,
      // Forces act at the contact patch so total load transfer is m*a*h/track (or /wheelbase).
      rollCenter: 0,
      tire: front ? tireF : tireR,
    };
  };
  const wheels = [corner(true, true), corner(true, false), corner(false, true), corner(false, false)];
  const diff = (front: boolean): DiffSpec => {
    const driven = spec.drivetrain === 'AWD' || (spec.drivetrain === 'FWD' ? front : !front);
    if (!driven) return { type: 'open', preload: 0, accel: 0, decel: 0 };
    // AWD cars carry the spec diff at the rear and a milder one at the front.
    if (spec.drivetrain === 'AWD' && front) return { type: spec.diff.type === 'spool' ? 'lsd' : spec.diff.type, preload: spec.diff.preload * 0.5, accel: tune.diffAccel * 0.5, decel: tune.diffDecel * 0.5 };
    return { ...spec.diff, accel: tune.diffAccel, decel: tune.diffDecel };
  };
  const axles: AxleParams[] = [
    { left: 0, right: 1, arb: spec.suspension.arbF * tune.arbF, diff: diff(true) },
    { left: 2, right: 3, arb: spec.suspension.arbR * tune.arbR, diff: diff(false) },
  ];

  // Engine: sample the authored curve every 100 rpm for fast lookup.
  const e = spec.engine;
  const n = Math.ceil(e.limiter / 100) + 2;
  const rpmArr = new Float64Array(n);
  const torqueArr = new Float64Array(n);
  let peakTorque = 0;
  let peakTorqueRpm = 0;
  let peakPower = 0;
  let peakPowerRpm = 0;
  for (let i = 0; i < n; i++) {
    const rpm = i * 100;
    const t = interpTorque(e.torque, Math.max(rpm, e.idle * 0.5)) * up.powerScale;
    rpmArr[i] = rpm;
    torqueArr[i] = t;
    if (rpm <= e.limiter) {
      if (t > peakTorque) {
        peakTorque = t;
        peakTorqueRpm = rpm;
      }
      const p = (t * rpm * Math.PI) / 30;
      if (p > peakPower) {
        peakPower = p;
        peakPowerRpm = rpm;
      }
    }
  }
  const electric = e.aspiration === 'electric';
  const turbo = e.aspiration === 'turbo' || e.aspiration === 'twinturbo'
    ? { lag: e.turboLag ?? 0.4, share: e.aspiration === 'twinturbo' ? 0.4 : 0.45, spoolRpm: e.idle * 1.4, fullRpm: Math.max(e.idle * 2.6, peakTorqueRpm) }
    : null;
  const engine: EngineParams = {
    rpm: rpmArr,
    torque: torqueArr,
    idle: e.idle,
    redline: e.redline,
    limiter: e.limiter,
    inertia: e.inertia,
    peakTorque,
    peakTorqueRpm,
    peakPower,
    peakPowerRpm,
    brakeTorque: electric ? peakTorque * 0.12 : peakTorque * 0.2,
    turbo,
    electric,
    cylinders: e.cylinders,
  };

  const ratios = [-Math.abs(spec.reverse), ...spec.gears.map((g, i) => g * (tune.gearScale[i] ?? 1))];
  const final = spec.final * tune.finalDrive;
  const gearbox: GearboxParams = {
    ratios,
    final,
    shiftTime: spec.shiftTime,
    upshiftRpm: [],
    downshiftRpm: [],
    clutchMaxTorque: peakTorque * 1.6,
    // Like launch control or a torque converter's stall speed: the clutch
    // slips with the engine held in its torque band until the wheels catch up.
    launchRpm: electric ? 0 : clamp(e.redline * 0.45, e.idle * 2.5, e.redline * 0.6),
    efficiency: spec.drivetrain === 'AWD' ? 0.86 : 0.9,
  };
  computeShiftPoints(engine, gearbox);

  const half = new V3(W / 2, (H - spec.groundClearance) / 2, len / 2);
  const boxCenter = new V3(0, spec.groundClearance + (H - spec.groundClearance) / 2 - cgHeight, (b - a) / 2 * 0.15);
  const yBottom = spec.groundClearance - cgHeight + 0.02;
  const yTop = H - cgHeight - 0.08;
  const bx = W / 2 - 0.12;
  const bzF = -len / 2 + 0.2;
  const bzR = len / 2 - 0.2;
  const bodyPoints = [
    new V3(-bx, yBottom, bzF), new V3(bx, yBottom, bzF), new V3(-bx, yBottom, bzR), new V3(bx, yBottom, bzR),
    new V3(-bx, yBottom, 0), new V3(bx, yBottom, 0),
    new V3(-bx * 0.8, yTop, -len * 0.12), new V3(bx * 0.8, yTop, -len * 0.12), new V3(-bx * 0.8, yTop, len * 0.2), new V3(bx * 0.8, yTop, len * 0.2),
    new V3(-bx, yTop * 0.4, bzF), new V3(bx, yTop * 0.4, bzF), new V3(-bx, yTop * 0.4, bzR), new V3(bx, yTop * 0.4, bzR),
  ];

  return {
    spec,
    mass,
    invMass: 1 / mass,
    inertia,
    invInertia: new V3(1 / inertia.x, 1 / inertia.y, 1 / inertia.z),
    wheelbase: L,
    cgHeight,
    a,
    b,
    wheels,
    axles,
    drivetrain: spec.drivetrain,
    centerLock: spec.drivetrain === 'AWD' ? 18 : 0,
    engine,
    gearbox,
    aero: {
      cdA: spec.aero.cdA * (1 + 0.04 * up.aeroKit),
      clA: spec.aero.clA + up.clAAdd,
      balance: (() => {
        const cl = spec.aero.clA + up.clAAdd;
        const fShare = spec.aero.balance * tune.aeroF;
        const rShare = (1 - spec.aero.balance) * tune.aeroR;
        return cl === 0 ? spec.aero.balance : fShare / Math.max(1e-6, fShare + rShare);
      })(),
    },
    maxSteer: spec.steering.maxAngle * deg,
    steerRatio: spec.steering.ratio,
    ackermann: 0.6,
    half,
    boxCenter,
    bodyPoints,
    brakeBias: tune.brakeBalance,
  };
}

/** Engine torque at full throttle (before boost scaling) at the given rpm. */
export function curveTorque(e: EngineParams, rpm: number): number {
  const x = rpm / 100;
  if (x <= 0) return e.torque[0];
  const i = Math.floor(x);
  if (i >= e.rpm.length - 1) return e.torque[e.rpm.length - 1];
  const f = x - i;
  return e.torque[i] + (e.torque[i + 1] - e.torque[i]) * f;
}

/**
 * Upshift where the next gear gives more wheel torque at the same road
 * speed (or just before the limiter). Downshift low enough that the lower
 * gear lands safely below its own upshift point.
 */
export function computeShiftPoints(e: EngineParams, g: GearboxParams): void {
  const n = g.ratios.length - 1;
  g.upshiftRpm = new Array(n + 1).fill(e.limiter - 150);
  g.downshiftRpm = new Array(n + 1).fill(0);
  for (let gear = 1; gear < n; gear++) {
    const r0 = g.ratios[gear];
    const r1 = g.ratios[gear + 1];
    let up = e.limiter - 150;
    for (let rpm = e.peakTorqueRpm; rpm < e.limiter - 150; rpm += 50) {
      const tNow = curveTorque(e, rpm) * r0;
      const tNext = curveTorque(e, (rpm * r1) / r0) * r1;
      if (tNext >= tNow) {
        up = rpm;
        break;
      }
    }
    g.upshiftRpm[gear] = Math.max(up, Math.min(e.redline * 0.85, e.limiter - 150));
  }
  for (let gear = 2; gear <= n; gear++) {
    g.downshiftRpm[gear] = (g.upshiftRpm[gear - 1] * 0.72 * g.ratios[gear]) / g.ratios[gear - 1];
  }
}
