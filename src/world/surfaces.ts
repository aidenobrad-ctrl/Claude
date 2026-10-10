// Driving surfaces. Grip comes from the tire compound's table (see
// vehicles/tires.ts); this file holds the properties that belong to the
// ground itself: rolling resistance, extra drag, small-scale bumps, effects.

export const SURFACE = {
  asphalt: 0,
  asphaltWet: 1,
  concrete: 2,
  dirt: 3,
  gravel: 4,
  sand: 5,
  grass: 6,
  snow: 7,
  ice: 8,
  mud: 9,
  curb: 10,
  water: 11,
  rock: 12,
} as const;

export type SurfaceId = (typeof SURFACE)[keyof typeof SURFACE];

/** Which column of a tire compound's grip table applies. */
export type GripClass = 'asphalt' | 'wet' | 'dirt' | 'gravel' | 'sand' | 'grass' | 'snow' | 'ice' | 'mud';

export type Particles = 'none' | 'dust' | 'gravel' | 'spray' | 'snow' | 'mud';

export interface SurfaceInfo {
  name: string;
  grip: GripClass;
  /** Extra multiplier on the compound's grip for this surface (e.g. curbs). */
  gripScale: number;
  /** Rolling resistance coefficient Crr (force = Crr * load). */
  rolling: number;
  /** Viscous drag per wheel, N per (m/s) per kN of load (sand, mud, water). */
  drag: number;
  /** Amplitude of deterministic small-scale bumps, m. */
  bump: number;
  /** Wavelength of the bumps, m. */
  bumpScale: number;
  particles: Particles;
  /** Whether sliding tires leave dark skid marks (vs. ruts or nothing). */
  skid: boolean;
  /** True for loose surfaces: grip on them recovers more gently past the peak. */
  loose: boolean;
}

export const SURFACES: readonly SurfaceInfo[] = [
  { name: 'asphalt', grip: 'asphalt', gripScale: 1, rolling: 0.012, drag: 0, bump: 0, bumpScale: 1, particles: 'none', skid: true, loose: false },
  { name: 'wet asphalt', grip: 'wet', gripScale: 1, rolling: 0.014, drag: 0.15, bump: 0, bumpScale: 1, particles: 'spray', skid: false, loose: false },
  { name: 'concrete', grip: 'asphalt', gripScale: 0.97, rolling: 0.011, drag: 0, bump: 0.002, bumpScale: 6, particles: 'none', skid: true, loose: false },
  { name: 'dirt', grip: 'dirt', gripScale: 1, rolling: 0.03, drag: 0.2, bump: 0.018, bumpScale: 1.6, particles: 'dust', skid: false, loose: true },
  { name: 'gravel', grip: 'gravel', gripScale: 1, rolling: 0.035, drag: 0.35, bump: 0.022, bumpScale: 1.1, particles: 'gravel', skid: false, loose: true },
  { name: 'sand', grip: 'sand', gripScale: 1, rolling: 0.09, drag: 2.2, bump: 0.012, bumpScale: 2.5, particles: 'dust', skid: false, loose: true },
  { name: 'grass', grip: 'grass', gripScale: 1, rolling: 0.05, drag: 0.6, bump: 0.02, bumpScale: 1.8, particles: 'dust', skid: false, loose: true },
  { name: 'snow', grip: 'snow', gripScale: 1, rolling: 0.045, drag: 0.9, bump: 0.012, bumpScale: 2, particles: 'snow', skid: false, loose: true },
  { name: 'ice', grip: 'ice', gripScale: 1, rolling: 0.01, drag: 0, bump: 0, bumpScale: 1, particles: 'none', skid: false, loose: false },
  { name: 'mud', grip: 'mud', gripScale: 1, rolling: 0.11, drag: 2.8, bump: 0.025, bumpScale: 1.4, particles: 'mud', skid: false, loose: true },
  { name: 'curb', grip: 'asphalt', gripScale: 0.9, rolling: 0.014, drag: 0, bump: 0.012, bumpScale: 0.45, particles: 'none', skid: true, loose: false },
  { name: 'water', grip: 'wet', gripScale: 0.55, rolling: 0.06, drag: 9, bump: 0.01, bumpScale: 3, particles: 'spray', skid: false, loose: false },
  { name: 'rock', grip: 'dirt', gripScale: 1.05, rolling: 0.025, drag: 0, bump: 0.03, bumpScale: 0.9, particles: 'dust', skid: false, loose: false },
];
