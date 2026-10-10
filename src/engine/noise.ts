// Seeded 2D simplex noise and fractal helpers for terrain and scenery.
import { RNG } from './rng';
import { dcos, dsin } from './dmath';

const F2 = 0.5 * (Math.sqrt(3) - 1);
const G2 = (3 - Math.sqrt(3)) / 6;
// 12 gradient directions spread evenly around the circle.
const GX = new Float64Array(12);
const GY = new Float64Array(12);
for (let i = 0; i < 12; i++) {
  const a = (i / 12) * Math.PI * 2 + 0.13;
  GX[i] = dcos(a);
  GY[i] = dsin(a);
}

export class Noise2 {
  private perm = new Uint8Array(512);
  private permMod12 = new Uint8Array(512);

  constructor(seed: number | string) {
    const rng = new RNG(seed);
    const p = new Uint8Array(256);
    for (let i = 0; i < 256; i++) p[i] = i;
    for (let i = 255; i > 0; i--) {
      const j = rng.int(0, i + 1);
      const t = p[i];
      p[i] = p[j];
      p[j] = t;
    }
    for (let i = 0; i < 512; i++) {
      this.perm[i] = p[i & 255];
      this.permMod12[i] = this.perm[i] % 12;
    }
  }

  /** Simplex noise in roughly [-1, 1]. */
  simplex(xin: number, yin: number): number {
    const perm = this.perm;
    const pm = this.permMod12;
    const s = (xin + yin) * F2;
    const i = Math.floor(xin + s);
    const j = Math.floor(yin + s);
    const t = (i + j) * G2;
    const x0 = xin - (i - t);
    const y0 = yin - (j - t);
    let i1 = 0;
    let j1 = 1;
    if (x0 > y0) {
      i1 = 1;
      j1 = 0;
    }
    const x1 = x0 - i1 + G2;
    const y1 = y0 - j1 + G2;
    const x2 = x0 - 1 + 2 * G2;
    const y2 = y0 - 1 + 2 * G2;
    const ii = i & 255;
    const jj = j & 255;
    let n = 0;
    let t0 = 0.5 - x0 * x0 - y0 * y0;
    if (t0 > 0) {
      const g = pm[ii + perm[jj]];
      t0 *= t0;
      n += t0 * t0 * (GX[g] * x0 + GY[g] * y0);
    }
    let t1 = 0.5 - x1 * x1 - y1 * y1;
    if (t1 > 0) {
      const g = pm[ii + i1 + perm[jj + j1]];
      t1 *= t1;
      n += t1 * t1 * (GX[g] * x1 + GY[g] * y1);
    }
    let t2 = 0.5 - x2 * x2 - y2 * y2;
    if (t2 > 0) {
      const g = pm[ii + 1 + perm[jj + 1]];
      t2 *= t2;
      n += t2 * t2 * (GX[g] * x2 + GY[g] * y2);
    }
    return 70 * n;
  }

  /**
   * Simplex noise with its analytic gradient: returns the value and writes
   * d/dx, d/dy into out[0], out[1].
   */
  simplexD(xin: number, yin: number, out: Float64Array): number {
    const perm = this.perm;
    const pm = this.permMod12;
    const s = (xin + yin) * F2;
    const i = Math.floor(xin + s);
    const j = Math.floor(yin + s);
    const t = (i + j) * G2;
    const x0 = xin - (i - t);
    const y0 = yin - (j - t);
    let i1 = 0;
    let j1 = 1;
    if (x0 > y0) {
      i1 = 1;
      j1 = 0;
    }
    const x1 = x0 - i1 + G2;
    const y1 = y0 - j1 + G2;
    const x2 = x0 - 1 + 2 * G2;
    const y2 = y0 - 1 + 2 * G2;
    const ii = i & 255;
    const jj = j & 255;
    let n = 0;
    let dx = 0;
    let dy = 0;
    // Each corner contributes t^4 (g . d) with t = 0.5 - |d|^2 (inlined, no allocation).
    let tt = 0.5 - x0 * x0 - y0 * y0;
    if (tt > 0) {
      const gi = pm[ii + perm[jj]];
      const gd = GX[gi] * x0 + GY[gi] * y0;
      const t2 = tt * tt;
      const t4 = t2 * t2;
      n += t4 * gd;
      const k = -8 * t2 * tt * gd;
      dx += k * x0 + t4 * GX[gi];
      dy += k * y0 + t4 * GY[gi];
    }
    tt = 0.5 - x1 * x1 - y1 * y1;
    if (tt > 0) {
      const gi = pm[ii + i1 + perm[jj + j1]];
      const gd = GX[gi] * x1 + GY[gi] * y1;
      const t2 = tt * tt;
      const t4 = t2 * t2;
      n += t4 * gd;
      const k = -8 * t2 * tt * gd;
      dx += k * x1 + t4 * GX[gi];
      dy += k * y1 + t4 * GY[gi];
    }
    tt = 0.5 - x2 * x2 - y2 * y2;
    if (tt > 0) {
      const gi = pm[ii + 1 + perm[jj + 1]];
      const gd = GX[gi] * x2 + GY[gi] * y2;
      const t2 = tt * tt;
      const t4 = t2 * t2;
      n += t4 * gd;
      const k = -8 * t2 * tt * gd;
      dx += k * x2 + t4 * GX[gi];
      dy += k * y2 + t4 * GY[gi];
    }
    out[0] = 70 * dx;
    out[1] = 70 * dy;
    return 70 * n;
  }

  /**
   * Eroded fBm: each octave is damped by the slope accumulated so far, so
   * steep flanks stay smooth and detail gathers in valleys and on crests,
   * which reads like water-carved mountains. Roughly [-1, 1].
   */
  eroded(x: number, y: number, octaves: number, gain = 0.5): number {
    let sum = 0;
    let amp = 1;
    let norm = 0;
    let gx = 0;
    let gy = 0;
    let px = x;
    let py = y;
    const d = this.dScratch;
    for (let o = 0; o < octaves; o++) {
      const v = this.simplexD(px + o * 17.13, py - o * 9.71, d);
      gx += d[0] * amp;
      gy += d[1] * amp;
      sum += (amp * v) / (1 + gx * gx + gy * gy);
      norm += amp;
      amp *= gain;
      // Rotate each octave so the grid never lines up.
      const nx = 1.6 * px - 1.2 * py;
      const ny = 1.2 * px + 1.6 * py;
      px = nx;
      py = ny;
    }
    return sum / norm;
  }

  private dScratch = new Float64Array(2);

  /** Fractal Brownian motion, normalized to roughly [-1, 1]. */
  fbm(x: number, y: number, octaves: number, lacunarity = 2, gain = 0.5): number {
    let sum = 0;
    let amp = 1;
    let norm = 0;
    let f = 1;
    for (let o = 0; o < octaves; o++) {
      sum += amp * this.simplex(x * f + o * 17.13, y * f - o * 9.71);
      norm += amp;
      amp *= gain;
      f *= lacunarity;
    }
    return sum / norm;
  }

  /** Ridged multifractal in [0, 1]: sharp crests, good for mountains. */
  ridged(x: number, y: number, octaves: number, lacunarity = 2, gain = 0.5): number {
    let sum = 0;
    let amp = 1;
    let norm = 0;
    let f = 1;
    let weight = 1;
    for (let o = 0; o < octaves; o++) {
      let v = 1 - Math.abs(this.simplex(x * f + o * 31.7, y * f + o * 11.3));
      v *= v;
      v *= weight;
      weight = Math.min(1, Math.max(0, v * 1.5));
      sum += amp * v;
      norm += amp;
      amp *= gain;
      f *= lacunarity;
    }
    return sum / norm;
  }
}
