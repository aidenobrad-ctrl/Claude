// Deterministic transcendental functions for the simulation.
//
// Math.sin, Math.cos and Math.pow return different bits in different JS
// engines (and even between V8 versions: Node 22 and Chromium 141 disagree).
// These versions use only +, -, *, /, sqrt and exact bit manipulation, all
// of which IEEE 754 defines exactly, so every engine gets identical results.
// Coefficients come from fdlibm (Sun Microsystems, freely distributable).
//
// Simulation code must use these instead of Math.sin/cos/tan/atan/atan2/
// exp/log/pow/tanh; tests/determinism-lint.test.ts enforces that.

const f64 = new Float64Array(1);
const u32 = new Uint32Array(f64.buffer);
// Index of the high (sign/exponent) word in the platform's byte order.
const HI = new Uint8Array(new Uint16Array([1]).buffer)[0] === 1 ? 1 : 0;
const LO = 1 - HI;

export const PI = 3.141592653589793;
export const HALF_PI = 1.5707963267948966;
export const TAU = 6.283185307179586;

/** 2^k for integer k in the normal range, built from bits. */
function pow2i(k: number): number {
  if (k > 1023) return Infinity;
  if (k < -1022) return 0;
  u32[HI] = (k + 1023) << 20;
  u32[LO] = 0;
  return f64[0];
}

// --- sin / cos ---------------------------------------------------------------
const S1 = -1.66666666666666324348e-1;
const S2 = 8.33333333332248946124e-3;
const S3 = -1.98412698298579493134e-4;
const S4 = 2.75573137070700676789e-6;
const S5 = -2.50507602534068634195e-8;
const S6 = 1.58969099521155010221e-10;
const C1 = 4.16666666666666019037e-2;
const C2 = -1.38888888888741095749e-3;
const C3 = 2.48015872894767294178e-5;
const C4 = -2.75573143513906633035e-7;
const C5 = 2.08757232129817482790e-9;
const C6 = -1.13596475577881948265e-11;
const INV_PIO2 = 6.36619772367581382433e-1;
const PIO2_1 = 1.57079632673412561417;
const PIO2_2 = 6.07710050630396597660e-11;
const PIO2_3 = 2.02226624871116645580e-21;

function kSin(x: number): number {
  const z = x * x;
  return x + x * z * (S1 + z * (S2 + z * (S3 + z * (S4 + z * (S5 + z * S6)))));
}
function kCos(x: number): number {
  const z = x * x;
  return 1 - 0.5 * z + z * z * (C1 + z * (C2 + z * (C3 + z * (C4 + z * (C5 + z * C6)))));
}

let reducedN = 0;
/** Cody-Waite reduction to [-pi/4, pi/4]; quadrant in reducedN. */
function reduce(x: number): number {
  const n = Math.round(x * INV_PIO2);
  reducedN = n & 3;
  return x - n * PIO2_1 - n * PIO2_2 - n * PIO2_3;
}

export function dsin(x: number): number {
  if (!Number.isFinite(x)) return NaN;
  if (x > -0.7853981633974483 && x < 0.7853981633974483) return kSin(x);
  const r = reduce(x);
  switch (reducedN) {
    case 0:
      return kSin(r);
    case 1:
      return kCos(r);
    case 2:
      return -kSin(r);
    default:
      return -kCos(r);
  }
}

export function dcos(x: number): number {
  if (!Number.isFinite(x)) return NaN;
  if (x > -0.7853981633974483 && x < 0.7853981633974483) return kCos(x);
  const r = reduce(x);
  switch (reducedN) {
    case 0:
      return kCos(r);
    case 1:
      return -kSin(r);
    case 2:
      return -kCos(r);
    default:
      return kSin(r);
  }
}

export function dtan(x: number): number {
  return dsin(x) / dcos(x);
}

// --- atan / atan2 ------------------------------------------------------------
const ATANHI = [4.63647609000806093515e-1, 7.85398163397448278999e-1, 9.82793723247329054082e-1, 1.57079632679489655800];
const ATANLO = [2.26987774529616870924e-17, 3.06161699786838301793e-17, 1.39033110312309984516e-17, 6.12323399573676603587e-17];
const AT0 = 3.33333333333329318027e-1;
const AT1 = -1.99999999998764832476e-1;
const AT2 = 1.42857142725034663711e-1;
const AT3 = -1.11111104054623557880e-1;
const AT4 = 9.09088713343650656196e-2;
const AT5 = -7.69187620504482999495e-2;
const AT6 = 6.66107313738753120669e-2;
const AT7 = -5.83357013379057348645e-2;
const AT8 = 4.97687799461593236017e-2;
const AT9 = -3.65315727442169155270e-2;
const AT10 = 1.62858201153657823623e-2;

export function datan(x: number): number {
  if (x !== x) return NaN;
  const neg = x < 0;
  let ax = neg ? -x : x;
  if (ax > 1e17) return neg ? -HALF_PI : HALF_PI;
  let id = -1;
  if (ax >= 0.4375) {
    if (ax < 1.1875) {
      if (ax < 0.6875) {
        id = 0;
        ax = (2 * ax - 1) / (2 + ax);
      } else {
        id = 1;
        ax = (ax - 1) / (ax + 1);
      }
    } else if (ax < 2.4375) {
      id = 2;
      ax = (ax - 1.5) / (1 + 1.5 * ax);
    } else {
      id = 3;
      ax = -1 / ax;
    }
  }
  const z = ax * ax;
  const w = z * z;
  const s1 = z * (AT0 + w * (AT2 + w * (AT4 + w * (AT6 + w * (AT8 + w * AT10)))));
  const s2 = w * (AT1 + w * (AT3 + w * (AT5 + w * (AT7 + w * AT9))));
  let r: number;
  if (id < 0) r = ax - ax * (s1 + s2);
  else r = ATANHI[id] - (ax * (s1 + s2) - ATANLO[id] - ax);
  return neg ? -r : r;
}

export function datan2(y: number, x: number): number {
  if (x !== x || y !== y) return NaN;
  // Signed zeros follow Math.atan2: atan2(-0, -1) = -PI, atan2(0, -0) = PI.
  const yPos = y > 0 || (y === 0 && 1 / y > 0);
  if (x === 0) {
    if (y !== 0) return yPos ? HALF_PI : -HALF_PI;
    return 1 / x > 0 ? y : yPos ? PI : -PI;
  }
  const a = datan(y / x);
  if (x > 0) return a;
  return yPos ? a + PI : a - PI;
}

export function dasin(x: number): number {
  if (x >= 1) return HALF_PI;
  if (x <= -1) return -HALF_PI;
  return datan(x / Math.sqrt(1 - x * x));
}

export function dacos(x: number): number {
  return HALF_PI - dasin(x);
}

// --- exp / log / pow ---------------------------------------------------------
const LN2_HI = 6.93147180369123816490e-1;
const LN2_LO = 1.90821492927058770002e-10;
const INV_LN2 = 1.44269504088896338700;
const P1 = 1.66666666666666019037e-1;
const P2 = -2.77777777770155933842e-3;
const P3 = 6.61375632143793436117e-5;
const P4 = -1.65339022054652515390e-6;
const P5 = 4.13813679705723846039e-8;

export function dexp(x: number): number {
  if (x !== x) return NaN;
  if (x > 709.7) return Infinity;
  if (x < -745) return 0;
  if (x > -3.7e-9 && x < 3.7e-9) return 1 + x;
  const k = Math.round(x * INV_LN2);
  const hi = x - k * LN2_HI;
  const lo = k * LN2_LO;
  const r = hi - lo;
  const t = r * r;
  const c = r - t * (P1 + t * (P2 + t * (P3 + t * (P4 + t * P5))));
  const y = 1 - (lo - (r * c) / (2 - c) - hi);
  if (k < -1021) return y * pow2i(k + 1000) * pow2i(-1000);
  return y * pow2i(k);
}

const LG1 = 6.666666666666735130e-1;
const LG2 = 3.999999999940941908e-1;
const LG3 = 2.857142874366239149e-1;
const LG4 = 2.222219843214978396e-1;
const LG5 = 1.818357216161805012e-1;
const LG6 = 1.531383769920937332e-1;
const LG7 = 1.479819860511658591e-1;

export function dlog(x: number): number {
  if (x !== x || x < 0) return NaN;
  if (x === 0) return -Infinity;
  if (x === Infinity) return Infinity;
  f64[0] = x;
  let hx = u32[HI];
  let k = 0;
  if (hx < 0x00100000) {
    // Subnormal: scale up by 2^54.
    f64[0] = x * 18014398509481984;
    hx = u32[HI];
    k = -54;
  }
  k += (hx >> 20) - 1023;
  hx &= 0x000fffff;
  // Normalize the mantissa into [sqrt(2)/2, sqrt(2)).
  const i = (hx + 0x95f64) & 0x100000;
  u32[HI] = hx | (i ^ 0x3ff00000);
  k += i >> 20;
  const f = f64[0] - 1;
  const s = f / (2 + f);
  const z = s * s;
  const w = z * z;
  const t1 = w * (LG2 + w * (LG4 + w * LG6));
  const t2 = z * (LG1 + w * (LG3 + w * (LG5 + w * LG7)));
  const R = t2 + t1;
  const hfsq = 0.5 * f * f;
  return k * LN2_HI - (hfsq - (s * (hfsq + R) + k * LN2_LO) - f);
}

/** x^y. Exact for small integer y; otherwise exp(y log x), ~1e-15 relative. */
export function dpow(x: number, y: number): number {
  if (y === 0) return 1;
  if (y === 1) return x;
  if (y === 2) return x * x;
  if (y === 0.5) return Math.sqrt(x);
  if (Number.isInteger(y) && Math.abs(y) <= 32) {
    let r = 1;
    let b = x;
    let e = Math.abs(y);
    while (e > 0) {
      if (e & 1) r *= b;
      b *= b;
      e >>= 1;
    }
    return y < 0 ? 1 / r : r;
  }
  if (x === 0) return y > 0 ? 0 : Infinity;
  if (x < 0) return NaN;
  return dexp(y * dlog(x));
}

export function dtanh(x: number): number {
  if (x > 20) return 1;
  if (x < -20) return -1;
  if (x > -1e-5 && x < 1e-5) return x;
  const e = dexp(2 * x);
  return (e - 1) / (e + 1);
}

export function dhypot(x: number, y: number): number {
  return Math.sqrt(x * x + y * y);
}
