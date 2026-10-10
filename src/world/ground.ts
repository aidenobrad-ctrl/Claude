// The ground interface the vehicle physics queries, and a flat ground for
// test tracks and headless validation runs.
import { hash2f } from '../engine/rng';
import { SURFACE, SURFACES, type SurfaceId } from './surfaces';

export interface GroundHit {
  /** Surface height at the query point, m. */
  y: number;
  /** Unit surface normal. */
  nx: number;
  ny: number;
  nz: number;
  surface: SurfaceId;
  /** Depth of standing water above the surface, m (0 when dry). */
  water: number;
}

export interface Ground {
  /**
   * The highest surface at (x, z) that lies no more than about 1.5 m above
   * yRef, so a car under a bridge does not snap up onto the deck.
   * Returns false where there is no ground at all (outside the world).
   */
  sample(x: number, z: number, yRef: number, out: GroundHit): boolean;
}

export function newHit(): GroundHit {
  return { y: 0, nx: 0, ny: 1, nz: 0, surface: SURFACE.asphalt, water: 0 };
}

/** Smooth deterministic value noise in [-1, 1] with unit wavelength. */
function valueNoise(x: number, z: number, seed: number): number {
  const xi = Math.floor(x);
  const zi = Math.floor(z);
  const fx = x - xi;
  const fz = z - zi;
  const u = fx * fx * (3 - 2 * fx);
  const v = fz * fz * (3 - 2 * fz);
  const a = hash2f(xi, zi, seed);
  const b = hash2f(xi + 1, zi, seed);
  const c = hash2f(xi, zi + 1, seed);
  const d = hash2f(xi + 1, zi + 1, seed);
  return (a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v) * 2 - 1;
}

/**
 * Add a surface's small-scale bumps to a hit, tilting the normal to match.
 * The bumps are too small to model in the render mesh; they exist so loose
 * surfaces feel rough through the suspension.
 */
export function applyBumps(x: number, z: number, hit: GroundHit): void {
  const s = SURFACES[hit.surface];
  if (s.bump <= 0) return;
  const k = 1 / s.bumpScale;
  const seed = 0x51f3 + hit.surface * 977;
  const e = 0.2;
  const h0 = valueNoise(x * k, z * k, seed);
  const hx = valueNoise((x + e) * k, z * k, seed);
  const hz = valueNoise(x * k, (z + e) * k, seed);
  hit.y += h0 * s.bump;
  // Tilt the normal by the bump gradient.
  const gx = ((hx - h0) / e) * s.bump;
  const gz = ((hz - h0) / e) * s.bump;
  let nx = hit.nx - gx * hit.ny;
  let ny = hit.ny;
  let nz = hit.nz - gz * hit.ny;
  const l = Math.sqrt(nx * nx + ny * ny + nz * nz);
  nx /= l;
  ny /= l;
  nz /= l;
  hit.nx = nx;
  hit.ny = ny;
  hit.nz = nz;
}

/** Infinite flat ground at a fixed height, with a surface chosen per point. */
export class FlatGround implements Ground {
  constructor(
    public surfaceAt: (x: number, z: number) => SurfaceId = () => SURFACE.asphalt,
    public height = 0,
  ) {}

  sample(x: number, z: number, _yRef: number, out: GroundHit): boolean {
    out.y = this.height;
    out.nx = 0;
    out.ny = 1;
    out.nz = 0;
    out.surface = this.surfaceAt(x, z);
    out.water = 0;
    applyBumps(x, z, out);
    return true;
  }
}

/** A tilted plane, for slope tests: rises along +X with the given grade. */
export class SlopeGround implements Ground {
  constructor(
    public grade: number,
    public surface: SurfaceId = SURFACE.asphalt,
  ) {}

  sample(x: number, _z: number, _yRef: number, out: GroundHit): boolean {
    out.y = x * this.grade;
    const l = Math.sqrt(1 + this.grade * this.grade);
    out.nx = -this.grade / l;
    out.ny = 1 / l;
    out.nz = 0;
    out.surface = this.surface;
    out.water = 0;
    return true;
  }
}
