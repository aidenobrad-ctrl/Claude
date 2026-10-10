// Car specification: the authored data for one car. Everything the physics
// uses is derived from this (see params.ts); performance numbers and PI are
// measured from it, never typed in.
import type { Compound } from './tires';

export type Category =
  | 'compact'
  | 'hothatch'
  | 'classic'
  | 'muscle'
  | 'sports'
  | 'gt'
  | 'super'
  | 'hyper'
  | 'rally'
  | 'offroad'
  | 'truck'
  | 'suv';

export type BodyType = 'hatch' | 'coupe' | 'sedan' | 'wagon' | 'pickup' | 'suv' | 'midengine' | 'roadster';
export type Drivetrain = 'FWD' | 'RWD' | 'AWD';
export type Aspiration = 'NA' | 'turbo' | 'twinturbo' | 'supercharged' | 'electric';
export type DiffType = 'open' | 'lsd' | 'spool';

export interface EngineSpec {
  /** Display layout, e.g. "I4", "V8", "flat-6", "dual motor". */
  layout: string;
  /** Cylinder count; 0 for electric motors. Drives the engine sound. */
  cylinders: number;
  /** Displacement in liters (0 for electric). */
  displacement: number;
  aspiration: Aspiration;
  /** Full-throttle crank torque curve as [rpm, N·m] pairs, ascending rpm. */
  torque: [number, number][];
  idle: number;
  redline: number;
  /** Rev limiter cut-in. */
  limiter: number;
  /** Engine plus flywheel inertia, kg·m². */
  inertia: number;
  /** Turbo spool time constant, s (turbo cars only). */
  turboLag?: number;
}

export interface DiffSpec {
  type: DiffType;
  /** Locking torque with no load, N·m. */
  preload: number;
  /** Extra locking torque per N·m of drive torque (0..1). */
  accel: number;
  /** Extra locking torque per N·m of engine braking (0..1). */
  decel: number;
}

export interface CarVisual {
  /** Default paint, hex RGB. */
  paint: number;
  /** Body style for the generator. */
  style: string;
  /** Free-form generator parameters (see vehicles/render/generator.ts). */
  params?: Record<string, number>;
}

export interface CarSpec {
  id: string;
  make: string;
  model: string;
  year: number;
  category: Category;
  body: BodyType;
  description: string;

  /** Curb mass plus driver, kg. */
  mass: number;
  /** Fraction of static weight on the front axle. */
  weightFront: number;
  wheelbase: number;
  trackFront: number;
  trackRear: number;
  /** Center of gravity height above the ground, m. */
  cgHeight: number;
  length: number;
  width: number;
  height: number;
  groundClearance: number;
  /** Yaw inertia relative to a uniform box (mid-engine cars are lower). */
  yawInertiaScale?: number;

  engine: EngineSpec;
  drivetrain: Drivetrain;
  /** AWD front torque share (0..1). */
  awdFront?: number;
  diff: DiffSpec;
  /** Forward gear ratios, first to top. */
  gears: number[];
  final: number;
  reverse: number;
  /** Time to complete a shift, s. */
  shiftTime: number;

  tires: {
    compound: Compound;
    /** Section widths, mm. */
    widthF: number;
    widthR: number;
    /** Rolling radius, m. */
    radius: number;
  };
  /** Maximum brake torque per wheel, N·m. */
  brakes: { torqueF: number; torqueR: number };
  suspension: {
    /** Natural frequency, Hz. */
    freqF: number;
    freqR: number;
    /** Damping ratio (fraction of critical) in bump; rebound is 1.6x. */
    dampF: number;
    dampR: number;
    /** Anti-roll bar rate at the wheel, N/m. */
    arbF: number;
    arbR: number;
    /** Total wheel travel, m. */
    travel: number;
  };
  /** Drag area Cd*A and lift area Cl*A (m²); positive Cl*A is downforce. */
  aero: { cdA: number; clA: number; balance: number };
  /** Maximum road-wheel steering angle (degrees) and steering ratio. */
  steering: { maxAngle: number; ratio: number };
  visual: CarVisual;
}
