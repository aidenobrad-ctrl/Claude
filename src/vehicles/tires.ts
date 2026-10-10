// Tire model: a Pacejka "magic formula" curve on normalized combined slip,
// with a friction circle (ellipse), load sensitivity and per-surface grip.
//
// Normalized slips: sx = kappa / kappaPeak, sy = tan(alpha) / tan(alphaPeak).
// Combined s = |(sx, sy)|. Total force F = mu * Fz * MF(s), split along
// (sx, sy) so the force always opposes the contact patch's slip velocity.
// MF peaks at s = 1 and falls toward `slide` (sliding / peak grip) as s grows.
import { dasin, datan, dcos, dsin, dtan } from '../engine/dmath';
import type { GripClass } from '../world/surfaces';

export type Compound = 'eco' | 'street' | 'sport' | 'semislick' | 'slick' | 'rally' | 'offroad' | 'snow' | 'drag';

export interface CompoundInfo {
  name: string;
  /** Peak lateral friction coefficient on dry asphalt at nominal load. */
  mu: number;
  /** Longitudinal peak relative to lateral. */
  muLong: number;
  /** Peak slip angle, radians. */
  alphaPeak: number;
  /** Peak slip ratio. */
  kappaPeak: number;
  /** Sliding grip as a fraction of peak grip. */
  slide: number;
  /** Magic formula curvature E (higher = flatter after the peak). */
  E: number;
  /** Load sensitivity: grip coefficient drops by this per +100% load. */
  loadSens: number;
  /** Grip multiplier per surface class. */
  surface: Record<GripClass, number>;
}

const DEG = Math.PI / 180;

export const COMPOUNDS: Record<Compound, CompoundInfo> = {
  eco: {
    name: 'Eco', mu: 0.9, muLong: 1.12, alphaPeak: 9 * DEG, kappaPeak: 0.13, slide: 0.8, E: 0.2, loadSens: 0.14,
    surface: { asphalt: 1, wet: 0.74, dirt: 0.6, gravel: 0.56, sand: 0.42, grass: 0.5, snow: 0.33, ice: 0.12, mud: 0.34 },
  },
  street: {
    name: 'Street', mu: 1.0, muLong: 1.16, alphaPeak: 8 * DEG, kappaPeak: 0.12, slide: 0.8, E: 0.2, loadSens: 0.13,
    surface: { asphalt: 1, wet: 0.74, dirt: 0.6, gravel: 0.56, sand: 0.43, grass: 0.5, snow: 0.32, ice: 0.11, mud: 0.33 },
  },
  sport: {
    name: 'Sport', mu: 1.1, muLong: 1.22, alphaPeak: 7 * DEG, kappaPeak: 0.11, slide: 0.78, E: 0.15, loadSens: 0.12,
    surface: { asphalt: 1, wet: 0.7, dirt: 0.56, gravel: 0.53, sand: 0.4, grass: 0.47, snow: 0.29, ice: 0.1, mud: 0.31 },
  },
  semislick: {
    name: 'Semi-slick', mu: 1.22, muLong: 1.18, alphaPeak: 6.5 * DEG, kappaPeak: 0.1, slide: 0.76, E: 0.1, loadSens: 0.11,
    surface: { asphalt: 1, wet: 0.6, dirt: 0.5, gravel: 0.47, sand: 0.36, grass: 0.43, snow: 0.24, ice: 0.09, mud: 0.27 },
  },
  slick: {
    name: 'Slick', mu: 1.38, muLong: 1.14, alphaPeak: 6 * DEG, kappaPeak: 0.09, slide: 0.74, E: 0.05, loadSens: 0.1,
    surface: { asphalt: 1, wet: 0.45, dirt: 0.42, gravel: 0.4, sand: 0.32, grass: 0.38, snow: 0.2, ice: 0.07, mud: 0.24 },
  },
  rally: {
    name: 'Rally', mu: 0.98, muLong: 1.12, alphaPeak: 9 * DEG, kappaPeak: 0.16, slide: 0.84, E: 0.35, loadSens: 0.13,
    surface: { asphalt: 1, wet: 0.8, dirt: 0.86, gravel: 0.85, sand: 0.62, grass: 0.66, snow: 0.5, ice: 0.18, mud: 0.52 },
  },
  offroad: {
    name: 'Off-road', mu: 0.9, muLong: 1.1, alphaPeak: 10 * DEG, kappaPeak: 0.18, slide: 0.86, E: 0.4, loadSens: 0.14,
    surface: { asphalt: 1, wet: 0.8, dirt: 0.9, gravel: 0.9, sand: 0.78, grass: 0.78, snow: 0.56, ice: 0.2, mud: 0.74 },
  },
  snow: {
    name: 'Snow', mu: 0.88, muLong: 1.12, alphaPeak: 9.5 * DEG, kappaPeak: 0.15, slide: 0.85, E: 0.35, loadSens: 0.13,
    surface: { asphalt: 1, wet: 0.82, dirt: 0.72, gravel: 0.7, sand: 0.52, grass: 0.62, snow: 0.8, ice: 0.34, mud: 0.48 },
  },
  drag: {
    name: 'Drag radial', mu: 0.95, muLong: 1.5, alphaPeak: 8 * DEG, kappaPeak: 0.12, slide: 0.75, E: 0.1, loadSens: 0.1,
    surface: { asphalt: 1, wet: 0.5, dirt: 0.45, gravel: 0.42, sand: 0.34, grass: 0.4, snow: 0.2, ice: 0.08, mud: 0.25 },
  },
};

/** Magic formula coefficients normalized so the peak is at x = 1. */
export interface Curve {
  B: number;
  C: number;
  E: number;
}

/**
 * Build a normalized curve with the given sliding ratio and curvature.
 * C follows from the asymptote sin(C*pi/2) = slide; B is solved so that
 * the peak, where C*atan(phi(x)) = pi/2, lands at x = 1.
 */
export function makeCurve(slide: number, E: number): Curve {
  const C = 2 - (2 / Math.PI) * dasin(Math.min(0.999, Math.max(0.3, slide)));
  const target = dtan(Math.PI / (2 * C));
  // Solve B(1 - E) + E*atan(B) = target with Newton's method.
  let B = target;
  for (let i = 0; i < 30; i++) {
    const f = B * (1 - E) + E * datan(B) - target;
    const df = 1 - E + E / (1 + B * B);
    const nb = B - f / df;
    if (Math.abs(nb - B) < 1e-12) {
      B = nb;
      break;
    }
    B = nb;
  }
  return { B, C, E };
}

export function mf(c: Curve, x: number): number {
  const bx = c.B * x;
  const phi = bx - c.E * (bx - datan(bx));
  return dsin(c.C * datan(phi));
}

/** d MF / dx. */
export function mfSlope(c: Curve, x: number): number {
  const bx = c.B * x;
  const phi = bx - c.E * (bx - datan(bx));
  const dphi = c.B * (1 - c.E + c.E / (1 + bx * bx));
  return dcos(c.C * datan(phi)) * c.C * (dphi / (1 + phi * phi));
}

/** Per-axle tire setup derived from compound, width and tuning. */
export interface TireSetup {
  compound: Compound;
  info: CompoundInfo;
  curve: Curve;
  /** Grip multiplier from tire width relative to load (wider = more grip). */
  widthGrip: number;
  /** Grip multiplier from camber, pressure and other tuning. */
  tuneGrip: number;
  /** Lateral-only multiplier from camber. */
  tuneLateral: number;
  alphaPeak: number;
  /** tan(alphaPeak), precomputed. */
  tanAlphaPeak: number;
  kappaPeak: number;
  /** Nominal (static) load used for load sensitivity, N. */
  nominalLoad: number;
}

export interface TireResult {
  fx: number;
  fy: number;
  /** d(Fx)/d(slip velocity), for the implicit wheel-speed update. N per (m/s). */
  dFxdV: number;
  /** Normalized combined slip (1 = peak). */
  slip: number;
}

/**
 * Tire force for one contact.
 * vLong, vLat: contact patch velocity along the wheel's forward and right axes.
 * wheelSpeed: omega * radius (m/s). Fz: load, N. surfaceGrip: multiplier.
 */
export function tireForce(t: TireSetup, Fz: number, vLong: number, vLat: number, wheelSpeed: number, surfaceGrip: number, out: TireResult): TireResult {
  if (Fz <= 0) {
    out.fx = 0;
    out.fy = 0;
    out.dFxdV = 0;
    out.slip = 0;
    return out;
  }
  // Below about 3 m/s slips use a floor speed. That keeps forces stable at
  // a standstill, where they behave like a stiff viscous friction.
  const vDen = Math.max(Math.abs(vLong), 3);
  const slipVx = wheelSpeed - vLong; // + when driving (wheel faster than ground)
  const kappa = slipVx / vDen;
  const tanA = vLat / vDen;
  const sx = kappa / t.kappaPeak;
  const sy = tanA / t.tanAlphaPeak;
  const s = Math.sqrt(sx * sx + sy * sy);
  // Load sensitivity: the friction coefficient falls as load rises.
  const loadRatio = Fz / t.nominalLoad;
  const sens = Math.max(0.6, Math.min(1.25, 1 - t.info.loadSens * (loadRatio - 1)));
  const mu = t.info.mu * sens * surfaceGrip * t.widthGrip * t.tuneGrip;
  const muX = mu * t.info.muLong;
  const muY = mu * t.tuneLateral;
  if (s < 1e-9) {
    const slope0 = mfSlope(t.curve, 0);
    out.fx = 0;
    out.fy = 0;
    out.dFxdV = (muX * Fz * slope0) / (t.kappaPeak * vDen);
    out.slip = 0;
    return out;
  }
  const m = mf(t.curve, s);
  const ms = Math.max(0, mfSlope(t.curve, s));
  const nx = sx / s;
  const ny = sy / s;
  out.fx = muX * Fz * m * nx;
  out.fy = -muY * Fz * m * ny;
  // dFx/dsx = muX*Fz*[ (m/s)*ny^2 + m'(s)*nx^2 ], and dsx/dslipVx = 1/(kappaPeak*vDen).
  out.dFxdV = (muX * Fz * ((m / s) * ny * ny + ms * nx * nx)) / (t.kappaPeak * vDen);
  out.slip = s;
  return out;
}
