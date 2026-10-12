// Sample-level synthesis building blocks for the game's procedural audio:
// noise, filters, resonators and shapers that render into Float32Arrays.
// Engine loops, tire and wind beds, impacts, UI sounds and the radio are all
// made from these. Audio is presentation, not simulation, so plain Math is
// fine here; every generator is still seeded, so a sound renders the same
// each time (and the C# port in Core/Audio renders the same design).

/** A small, fast seeded generator (xorshift32). */
export class AudioRng {
  private s: number;
  constructor(seed: number) {
    this.s = (seed >>> 0) || 0x9e3779b9;
  }
  /** Uniform in [0, 1). */
  next(): number {
    let x = this.s;
    x ^= x << 13;
    x >>>= 0;
    x ^= x >>> 17;
    x ^= x << 5;
    x >>>= 0;
    this.s = x;
    return x / 4294967296;
  }
  /** Uniform in [-1, 1). */
  signed(): number {
    return this.next() * 2 - 1;
  }
  /** Roughly normal, mean 0, deviation 1 (sum of four uniforms). */
  gauss(): number {
    return (this.next() + this.next() + this.next() + this.next() - 2) * 1.7320508;
  }
  /** An integer in [0, n). */
  int(n: number): number {
    return Math.floor(this.next() * n) % n;
  }
  pick<T>(a: readonly T[]): T {
    return a[this.int(a.length)];
  }
}

/** RBJ-cookbook biquad, transposed direct form II. */
export class Biquad {
  b0 = 1;
  b1 = 0;
  b2 = 0;
  a1 = 0;
  a2 = 0;
  private z1 = 0;
  private z2 = 0;

  static lowpass(sr: number, f: number, q = 0.7071): Biquad {
    return new Biquad().setLowpass(sr, f, q);
  }
  static highpass(sr: number, f: number, q = 0.7071): Biquad {
    return new Biquad().setHighpass(sr, f, q);
  }
  /** Band-pass with 0 dB peak gain. */
  static bandpass(sr: number, f: number, q: number): Biquad {
    return new Biquad().setBandpass(sr, f, q);
  }
  static peaking(sr: number, f: number, q: number, gainDb: number): Biquad {
    return new Biquad().setPeaking(sr, f, q, gainDb);
  }

  private set(b0: number, b1: number, b2: number, a0: number, a1: number, a2: number): this {
    this.b0 = b0 / a0;
    this.b1 = b1 / a0;
    this.b2 = b2 / a0;
    this.a1 = a1 / a0;
    this.a2 = a2 / a0;
    return this;
  }
  setLowpass(sr: number, f: number, q = 0.7071): this {
    const w = (2 * Math.PI * Math.min(f, sr * 0.45)) / sr;
    const c = Math.cos(w);
    const al = Math.sin(w) / (2 * q);
    return this.set((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + al, -2 * c, 1 - al);
  }
  setHighpass(sr: number, f: number, q = 0.7071): this {
    const w = (2 * Math.PI * Math.min(f, sr * 0.45)) / sr;
    const c = Math.cos(w);
    const al = Math.sin(w) / (2 * q);
    return this.set((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + al, -2 * c, 1 - al);
  }
  setBandpass(sr: number, f: number, q: number): this {
    const w = (2 * Math.PI * Math.min(f, sr * 0.45)) / sr;
    const c = Math.cos(w);
    const al = Math.sin(w) / (2 * q);
    return this.set(al, 0, -al, 1 + al, -2 * c, 1 - al);
  }
  setPeaking(sr: number, f: number, q: number, gainDb: number): this {
    const A = Math.pow(10, gainDb / 40);
    const w = (2 * Math.PI * Math.min(f, sr * 0.45)) / sr;
    const c = Math.cos(w);
    const al = Math.sin(w) / (2 * q);
    return this.set(1 + al * A, -2 * c, 1 - al * A, 1 + al / A, -2 * c, 1 - al / A);
  }
  process(x: number): number {
    const y = this.b0 * x + this.z1;
    this.z1 = this.b1 * x - this.a1 * y + this.z2;
    this.z2 = this.b2 * x - this.a2 * y;
    return y;
  }
  reset(): void {
    this.z1 = this.z2 = 0;
  }
}

/** One-pole low-pass (6 dB/octave). */
export class OnePole {
  private a: number;
  private y = 0;
  constructor(sr: number, f: number) {
    this.a = 1 - Math.exp((-2 * Math.PI * f) / sr);
  }
  setFreq(sr: number, f: number): void {
    this.a = 1 - Math.exp((-2 * Math.PI * f) / sr);
  }
  process(x: number): number {
    this.y += this.a * (x - this.y);
    return this.y;
  }
}

/** Pink noise (Paul Kellet's economy filter), roughly unit level. */
export class PinkNoise {
  private b0 = 0;
  private b1 = 0;
  private b2 = 0;
  constructor(private rng: AudioRng) {}
  next(): number {
    const w = this.rng.signed();
    this.b0 = 0.99765 * this.b0 + w * 0.099046;
    this.b1 = 0.963 * this.b1 + w * 0.2965164;
    this.b2 = 0.57 * this.b2 + w * 1.0526913;
    return (this.b0 + this.b1 + this.b2 + w * 0.1848) * 0.25;
  }
}

/** Brown (red) noise: integrated white noise with a leak, roughly unit level. */
export class BrownNoise {
  private y = 0;
  constructor(private rng: AudioRng) {}
  next(): number {
    this.y = (this.y + 0.02 * this.rng.signed()) * 0.998;
    return this.y * 3.5;
  }
}

/** Smooth saturation: tanh-like, unity slope at 0, approaches +-1. */
export function softClip(x: number): number {
  if (x > 3) return 1;
  if (x < -3) return -1;
  const x2 = x * x;
  return (x * (27 + x2)) / (27 + 9 * x2);
}

/** Root mean square of a buffer. */
export function rms(buf: Float32Array): number {
  let s = 0;
  for (let i = 0; i < buf.length; i++) s += buf[i] * buf[i];
  return Math.sqrt(s / Math.max(1, buf.length));
}

/** Largest absolute sample. */
export function peak(buf: Float32Array): number {
  let p = 0;
  for (let i = 0; i < buf.length; i++) p = Math.max(p, Math.abs(buf[i]));
  return p;
}

/** Scale a buffer in place to the given RMS level (peaks held under `ceiling`). */
export function normalize(buf: Float32Array, targetRms: number, ceiling = 0.98): Float32Array {
  const r = rms(buf);
  if (r <= 1e-9) return buf;
  let g = targetRms / r;
  const p = peak(buf) * g;
  if (p > ceiling) g *= ceiling / p;
  for (let i = 0; i < buf.length; i++) buf[i] *= g;
  return buf;
}

/** Fade the ends of a one-shot so it starts and stops without clicks. */
export function fadeEdges(buf: Float32Array, sr: number, inMs = 2, outMs = 8): Float32Array {
  const ni = Math.min(buf.length, Math.round((inMs / 1000) * sr));
  const no = Math.min(buf.length, Math.round((outMs / 1000) * sr));
  for (let i = 0; i < ni; i++) buf[i] *= i / ni;
  for (let i = 0; i < no; i++) buf[buf.length - 1 - i] *= i / no;
  return buf;
}

/**
 * Make a loop seamless: render `period` samples of a generator three times
 * over (filters settle in the first pass) and keep the middle pass. The
 * generator must repeat its excitation exactly every `period` samples.
 */
export function renderPeriodic(period: number, gen: (i: number, out: Float32Array) => void): Float32Array {
  const all = new Float32Array(period * 3);
  gen(period, all);
  return all.slice(period, period * 2);
}

/** Linear crossfade the loop seam of a noise bed (for loops that are not periodic by construction). */
export function crossfadeLoop(buf: Float32Array, fade: number): Float32Array {
  const n = buf.length - fade;
  const out = new Float32Array(n);
  for (let i = 0; i < n; i++) out[i] = buf[i];
  for (let i = 0; i < fade; i++) {
    const t = i / fade;
    // Equal-power blend of the tail into the head.
    out[i] = buf[i] * Math.sin(t * Math.PI * 0.5) + buf[n + i] * Math.cos(t * Math.PI * 0.5);
  }
  return out;
}
