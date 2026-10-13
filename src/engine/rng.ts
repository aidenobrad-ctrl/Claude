import { dcos, dlog } from './dmath';

// Seeded, deterministic random numbers. Nothing in the simulation may call
// Math.random(); everything draws from an RNG seeded from the world seed.

/** FNV-1a 32-bit hash of a string. */
export function hashString(s: string): number {
  let h = 0x811c9dc5;
  for (let i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i);
    h = Math.imul(h, 0x01000193);
  }
  return h >>> 0;
}

/** Murmur3 finalizer: a good 32-bit integer mixer. */
export function mix32(x: number): number {
  x ^= x >>> 16;
  x = Math.imul(x, 0x85ebca6b);
  x ^= x >>> 13;
  x = Math.imul(x, 0xc2b2ae35);
  x ^= x >>> 16;
  return x >>> 0;
}

/** Hash of two integers and a seed, as an unsigned 32-bit integer. */
export function hash2i(x: number, y: number, seed: number): number {
  return mix32(Math.imul(x | 0, 0x27d4eb2d) ^ Math.imul(y | 0, 0x165667b1) ^ mix32(seed));
}

/** Hash of two integers and a seed, mapped to [0, 1). */
export function hash2f(x: number, y: number, seed: number): number {
  return hash2i(x, y, seed) / 4294967296;
}

/** Hash of one integer and a seed, mapped to [0, 1). */
export function hash1f(x: number, seed: number): number {
  return mix32(Math.imul(x | 0, 0x9e3779b1) ^ mix32(seed)) / 4294967296;
}

/** sfc32 generator (fast, small state, passes PractRand to large sizes). */
export class RNG {
  private a = 0;
  private b = 0;
  private c = 0;
  private d = 0;

  constructor(seed: number | string = 1) {
    this.seed(seed);
  }

  seed(seed: number | string): void {
    let s = typeof seed === 'string' ? hashString(seed) : seed >>> 0;
    const splitmix = (): number => {
      s = (s + 0x9e3779b9) | 0;
      let z = s;
      z = Math.imul(z ^ (z >>> 16), 0x85ebca6b);
      z = Math.imul(z ^ (z >>> 13), 0xc2b2ae35);
      return (z ^ (z >>> 16)) >>> 0;
    };
    this.a = splitmix();
    this.b = splitmix();
    this.c = splitmix();
    this.d = splitmix();
    for (let i = 0; i < 12; i++) this.u32();
  }

  u32(): number {
    const t = (((this.a + this.b) | 0) + this.d) | 0;
    this.d = (this.d + 1) | 0;
    this.a = this.b ^ (this.b >>> 9);
    this.b = (this.c + (this.c << 3)) | 0;
    this.c = (this.c << 21) | (this.c >>> 11);
    this.c = (this.c + t) | 0;
    return t >>> 0;
  }

  /** Uniform in [0, 1). */
  next(): number {
    return this.u32() / 4294967296;
  }

  range(min: number, max: number): number {
    return min + (max - min) * this.next();
  }

  /** Integer in [min, maxExclusive). */
  int(min: number, maxExclusive: number): number {
    return min + Math.floor(this.next() * (maxExclusive - min));
  }

  chance(p: number): boolean {
    return this.next() < p;
  }

  pick<T>(arr: readonly T[]): T {
    return arr[Math.floor(this.next() * arr.length)];
  }

  /** Normal distribution (Box-Muller). */
  gauss(mean = 0, sd = 1): number {
    const u = 1 - this.next();
    const v = this.next();
    return mean + sd * Math.sqrt(-2 * dlog(u)) * dcos(2 * Math.PI * v);
  }

  shuffle<T>(arr: T[]): T[] {
    for (let i = arr.length - 1; i > 0; i--) {
      const j = Math.floor(this.next() * (i + 1));
      const t = arr[i];
      arr[i] = arr[j];
      arr[j] = t;
    }
    return arr;
  }

  /** An independent generator derived from this one's state and a label. */
  fork(label: string | number): RNG {
    const l = typeof label === 'string' ? hashString(label) : label >>> 0;
    return new RNG(mix32(this.a ^ mix32(this.b ^ l)) ^ this.c);
  }

  getState(): [number, number, number, number] {
    return [this.a, this.b, this.c, this.d];
  }

  setState(s: readonly number[]): void {
    this.a = s[0] | 0;
    this.b = s[1] | 0;
    this.c = s[2] | 0;
    this.d = s[3] | 0;
  }
}
