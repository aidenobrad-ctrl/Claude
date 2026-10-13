import { dcos, dexp, dsin } from './dmath';

// Small, allocation-free vector math for the simulation. The simulation does
// not depend on three.js so it can run in Node for headless tests.

export interface V3Like {
  x: number;
  y: number;
  z: number;
}

export class V3 implements V3Like {
  constructor(public x = 0, public y = 0, public z = 0) {}

  set(x: number, y: number, z: number): this {
    this.x = x;
    this.y = y;
    this.z = z;
    return this;
  }
  copy(v: V3Like): this {
    this.x = v.x;
    this.y = v.y;
    this.z = v.z;
    return this;
  }
  clone(): V3 {
    return new V3(this.x, this.y, this.z);
  }
  add(v: V3Like): this {
    this.x += v.x;
    this.y += v.y;
    this.z += v.z;
    return this;
  }
  sub(v: V3Like): this {
    this.x -= v.x;
    this.y -= v.y;
    this.z -= v.z;
    return this;
  }
  subVectors(a: V3Like, b: V3Like): this {
    this.x = a.x - b.x;
    this.y = a.y - b.y;
    this.z = a.z - b.z;
    return this;
  }
  scale(s: number): this {
    this.x *= s;
    this.y *= s;
    this.z *= s;
    return this;
  }
  addScaled(v: V3Like, s: number): this {
    this.x += v.x * s;
    this.y += v.y * s;
    this.z += v.z * s;
    return this;
  }
  dot(v: V3Like): number {
    return this.x * v.x + this.y * v.y + this.z * v.z;
  }
  /** this = a x b (safe when this aliases a or b). */
  crossVectors(a: V3Like, b: V3Like): this {
    const x = a.y * b.z - a.z * b.y;
    const y = a.z * b.x - a.x * b.z;
    const z = a.x * b.y - a.y * b.x;
    this.x = x;
    this.y = y;
    this.z = z;
    return this;
  }
  len(): number {
    return Math.sqrt(this.x * this.x + this.y * this.y + this.z * this.z);
  }
  lenSq(): number {
    return this.x * this.x + this.y * this.y + this.z * this.z;
  }
  distTo(v: V3Like): number {
    const dx = this.x - v.x;
    const dy = this.y - v.y;
    const dz = this.z - v.z;
    return Math.sqrt(dx * dx + dy * dy + dz * dz);
  }
  normalize(): this {
    const l = this.len();
    if (l > 1e-12) this.scale(1 / l);
    return this;
  }
  negate(): this {
    this.x = -this.x;
    this.y = -this.y;
    this.z = -this.z;
    return this;
  }
  lerp(v: V3Like, t: number): this {
    this.x += (v.x - this.x) * t;
    this.y += (v.y - this.y) * t;
    this.z += (v.z - this.z) * t;
    return this;
  }
  /** Rotate by quaternion q. */
  applyQuat(q: Quat): this {
    const { x, y, z } = this;
    const qx = q.x;
    const qy = q.y;
    const qz = q.z;
    const qw = q.w;
    const tx = 2 * (qy * z - qz * y);
    const ty = 2 * (qz * x - qx * z);
    const tz = 2 * (qx * y - qy * x);
    this.x = x + qw * tx + qy * tz - qz * ty;
    this.y = y + qw * ty + qz * tx - qx * tz;
    this.z = z + qw * tz + qx * ty - qy * tx;
    return this;
  }
  /** Rotate by the inverse (conjugate) of unit quaternion q. */
  applyQuatInv(q: Quat): this {
    const { x, y, z } = this;
    const qx = -q.x;
    const qy = -q.y;
    const qz = -q.z;
    const qw = q.w;
    const tx = 2 * (qy * z - qz * y);
    const ty = 2 * (qz * x - qx * z);
    const tz = 2 * (qx * y - qy * x);
    this.x = x + qw * tx + qy * tz - qz * ty;
    this.y = y + qw * ty + qz * tx - qx * tz;
    this.z = z + qw * tz + qx * ty - qy * tx;
    return this;
  }
  isFinite(): boolean {
    return Number.isFinite(this.x) && Number.isFinite(this.y) && Number.isFinite(this.z);
  }
}

export class Quat {
  constructor(public x = 0, public y = 0, public z = 0, public w = 1) {}

  set(x: number, y: number, z: number, w: number): this {
    this.x = x;
    this.y = y;
    this.z = z;
    this.w = w;
    return this;
  }
  copy(q: Quat): this {
    this.x = q.x;
    this.y = q.y;
    this.z = q.z;
    this.w = q.w;
    return this;
  }
  clone(): Quat {
    return new Quat(this.x, this.y, this.z, this.w);
  }
  identity(): this {
    return this.set(0, 0, 0, 1);
  }
  setAxisAngle(ax: V3Like, angle: number): this {
    const h = angle / 2;
    const s = dsin(h);
    return this.set(ax.x * s, ax.y * s, ax.z * s, dcos(h));
  }
  /** Rotation about +Y (heading). Heading 0 faces +Z. */
  setYaw(yaw: number): this {
    return this.set(0, dsin(yaw / 2), 0, dcos(yaw / 2));
  }
  /** this = this * q */
  multiply(q: Quat): this {
    return this.multiplyQuats(this, q);
  }
  /** this = q * this */
  premultiply(q: Quat): this {
    return this.multiplyQuats(q, this);
  }
  multiplyQuats(a: Quat, b: Quat): this {
    const ax = a.x;
    const ay = a.y;
    const az = a.z;
    const aw = a.w;
    const bx = b.x;
    const by = b.y;
    const bz = b.z;
    const bw = b.w;
    this.x = ax * bw + aw * bx + ay * bz - az * by;
    this.y = ay * bw + aw * by + az * bx - ax * bz;
    this.z = az * bw + aw * bz + ax * by - ay * bx;
    this.w = aw * bw - ax * bx - ay * by - az * bz;
    return this;
  }
  normalize(): this {
    let l = Math.sqrt(this.x * this.x + this.y * this.y + this.z * this.z + this.w * this.w);
    if (l < 1e-12) return this.identity();
    l = 1 / l;
    this.x *= l;
    this.y *= l;
    this.z *= l;
    this.w *= l;
    return this;
  }
  conjugate(): this {
    this.x = -this.x;
    this.y = -this.y;
    this.z = -this.z;
    return this;
  }
  /** Integrate a world-space angular velocity over dt. */
  integrate(w: V3Like, dt: number): this {
    const hx = w.x * dt * 0.5;
    const hy = w.y * dt * 0.5;
    const hz = w.z * dt * 0.5;
    const { x, y, z } = this;
    const qw = this.w;
    this.x += hx * qw + hy * z - hz * y;
    this.y += hy * qw + hz * x - hx * z;
    this.z += hz * qw + hx * y - hy * x;
    this.w += -hx * x - hy * y - hz * z;
    return this.normalize();
  }
  isFinite(): boolean {
    return Number.isFinite(this.x) && Number.isFinite(this.y) && Number.isFinite(this.z) && Number.isFinite(this.w);
  }
}

export const clamp = (v: number, lo: number, hi: number): number => (v < lo ? lo : v > hi ? hi : v);
export const clamp01 = (v: number): number => (v < 0 ? 0 : v > 1 ? 1 : v);
export const lerp = (a: number, b: number, t: number): number => a + (b - a) * t;
export const invLerp = (a: number, b: number, v: number): number => (b === a ? 0 : (v - a) / (b - a));
export const remap = (v: number, a0: number, a1: number, b0: number, b1: number): number =>
  lerp(b0, b1, clamp01(invLerp(a0, a1, v)));
export const smoothstep = (e0: number, e1: number, x: number): number => {
  const t = clamp01((x - e0) / (e1 - e0));
  return t * t * (3 - 2 * t);
};
export const sign = (v: number): number => (v > 0 ? 1 : v < 0 ? -1 : 0);
/** Wrap an angle to (-PI, PI]. */
export const wrapPi = (a: number): number => {
  a %= Math.PI * 2;
  if (a > Math.PI) a -= Math.PI * 2;
  else if (a <= -Math.PI) a += Math.PI * 2;
  return a;
};
export const moveTowards = (v: number, target: number, maxDelta: number): number =>
  Math.abs(target - v) <= maxDelta ? target : v + Math.sign(target - v) * maxDelta;
/** Frame-rate independent exponential smoothing. */
export const damp = (current: number, target: number, lambda: number, dt: number): number =>
  lerp(current, target, 1 - dexp(-lambda * dt));

export const KMH = 3.6;
export const MPH = 2.2369363;
export const G = 9.81;
export const DEG = Math.PI / 180;
