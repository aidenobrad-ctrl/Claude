// A road centerline sampled every couple of meters from a smooth spline,
// with width, height and banking, and fast nearest-point queries.
import { datan2 } from '../engine/dmath';

export interface RoadPoint {
  x: number;
  y: number;
  z: number;
}

export interface RoadOptions {
  closed: boolean;
  /** Half the paved width, m. */
  halfWidth: number;
  /** Sample spacing, m. */
  spacing?: number;
}

export interface NearestResult {
  /** Segment index (sample i to i+1) and fraction along it. */
  i: number;
  t: number;
  /** Arc length along the road, m. */
  s: number;
  /** Signed lateral offset, + = right of the direction of travel. */
  lateral: number;
  /** Unsigned distance from the centerline, m. */
  dist: number;
  /** Road surface height at the projection. */
  y: number;
}

/**
 * Centripetal Catmull-Rom through control points, resampled at uniform arc
 * length. Centripetal parameterization never overshoots into loops.
 */
export function sampleSpline(ctrl: RoadPoint[], closed: boolean, spacing: number): RoadPoint[] {
  const n = ctrl.length;
  const get = (i: number): RoadPoint => {
    if (closed) return ctrl[((i % n) + n) % n];
    if (i < 0) {
      const a = ctrl[0];
      const b = ctrl[1];
      return { x: 2 * a.x - b.x, y: 2 * a.y - b.y, z: 2 * a.z - b.z };
    }
    if (i >= n) {
      const a = ctrl[n - 1];
      const b = ctrl[n - 2];
      return { x: 2 * a.x - b.x, y: 2 * a.y - b.y, z: 2 * a.z - b.z };
    }
    return ctrl[i];
  };
  const dense: RoadPoint[] = [];
  const segs = closed ? n : n - 1;
  const SUB = 24;
  for (let i = 0; i < segs; i++) {
    const p0 = get(i - 1);
    const p1 = get(i);
    const p2 = get(i + 1);
    const p3 = get(i + 2);
    const knot = (a: RoadPoint, b: RoadPoint): number => Math.sqrt(Math.sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z) + 1e-6));
    const t0 = 0;
    const t1 = t0 + knot(p0, p1);
    const t2 = t1 + knot(p1, p2);
    const t3 = t2 + knot(p2, p3);
    for (let k = 0; k < SUB; k++) {
      const t = t1 + ((t2 - t1) * k) / SUB;
      const lerp3 = (a: RoadPoint, b: RoadPoint, ta: number, tb: number): RoadPoint => {
        const w = (t - ta) / (tb - ta);
        return { x: a.x + (b.x - a.x) * w, y: a.y + (b.y - a.y) * w, z: a.z + (b.z - a.z) * w };
      };
      const A1 = lerp3(p0, p1, t0, t1);
      const A2 = lerp3(p1, p2, t1, t2);
      const A3 = lerp3(p2, p3, t2, t3);
      const B1 = lerp3(A1, A2, t0, t2);
      const B2 = lerp3(A2, A3, t1, t3);
      dense.push(lerp3(B1, B2, t1, t2));
    }
  }
  if (!closed) dense.push({ ...ctrl[n - 1] });
  // Resample at uniform arc length.
  const out: RoadPoint[] = [dense[0]];
  let carry = 0;
  for (let i = 1; i < dense.length + (closed ? 1 : 0); i++) {
    const a = dense[i - 1];
    const b = dense[i % dense.length];
    const d = Math.sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z));
    let pos = spacing - carry;
    while (pos <= d) {
      const w = pos / d;
      out.push({ x: a.x + (b.x - a.x) * w, y: a.y + (b.y - a.y) * w, z: a.z + (b.z - a.z) * w });
      pos += spacing;
    }
    carry = d - (pos - spacing);
  }
  if (closed) {
    // Drop a final sample that lands on top of the first.
    const f = out[0];
    const l = out[out.length - 1];
    if (dist2(f, l) < spacing * spacing * 0.25) out.pop();
  } else {
    const last = dense[dense.length - 1];
    const l = out[out.length - 1];
    if (dist2(last, l) > spacing * spacing * 0.09) out.push({ ...last });
  }
  return out;
}

function dist2(a: RoadPoint, b: RoadPoint): number {
  return (a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z);
}

export class Road {
  readonly n: number;
  readonly closed: boolean;
  readonly x: Float64Array;
  readonly y: Float64Array;
  readonly z: Float64Array;
  /** Unit tangent. */
  readonly tx: Float64Array;
  readonly tz: Float64Array;
  /** Arc length at each sample. */
  readonly s: Float64Array;
  /** Signed curvature, 1/m (+ = turning right). */
  readonly curvature: Float64Array;
  readonly halfWidth: Float64Array;
  /** Banking angle, rad (+ = right side lower). */
  readonly bank: Float64Array;
  readonly length: number;
  private grid = new Map<number, number[]>();
  private readonly cell = 32;
  private maxHalf = 0;

  constructor(points: RoadPoint[], opts: RoadOptions) {
    this.closed = opts.closed;
    const n = points.length;
    this.n = n;
    this.x = new Float64Array(n);
    this.y = new Float64Array(n);
    this.z = new Float64Array(n);
    this.tx = new Float64Array(n);
    this.tz = new Float64Array(n);
    this.s = new Float64Array(n);
    this.curvature = new Float64Array(n);
    this.halfWidth = new Float64Array(n).fill(opts.halfWidth);
    this.bank = new Float64Array(n);
    for (let i = 0; i < n; i++) {
      this.x[i] = points[i].x;
      this.y[i] = points[i].y;
      this.z[i] = points[i].z;
    }
    let len = 0;
    for (let i = 0; i < n; i++) {
      this.s[i] = len;
      const j = this.next(i);
      if (j < 0) break;
      len += this.segLength(i, j);
    }
    this.length = len;
    for (let i = 0; i < n; i++) {
      const ia = this.closed ? (i - 1 + n) % n : Math.max(0, i - 1);
      const ib = this.closed ? (i + 1) % n : Math.min(n - 1, i + 1);
      let dx = this.x[ib] - this.x[ia];
      let dz = this.z[ib] - this.z[ia];
      const l = Math.sqrt(dx * dx + dz * dz) || 1;
      dx /= l;
      dz /= l;
      this.tx[i] = dx;
      this.tz[i] = dz;
    }
    for (let i = 0; i < n; i++) {
      const ia = this.closed ? (i - 2 + n) % n : Math.max(0, i - 2);
      const ib = this.closed ? (i + 2) % n : Math.min(n - 1, i + 2);
      const a1 = datan2(this.tz[ia], this.tx[ia]);
      const a2 = datan2(this.tz[ib], this.tx[ib]);
      let d = a2 - a1;
      while (d > Math.PI) d -= Math.PI * 2;
      while (d < -Math.PI) d += Math.PI * 2;
      const ds = this.closed ? 4 * (this.length / n) : Math.max(1e-3, this.s[ib] - this.s[ia]);
      // Heading angle atan2(tz, tx) increases clockwise when viewed from above (x east, z south), i.e. a right turn.
      this.curvature[i] = d / ds;
    }
    this.buildGrid();
  }

  segLength(i: number, j: number): number {
    const dx = this.x[j] - this.x[i];
    const dz = this.z[j] - this.z[i];
    return Math.sqrt(dx * dx + dz * dz);
  }

  next(i: number): number {
    if (i + 1 < this.n) return i + 1;
    return this.closed ? 0 : -1;
  }

  setHalfWidth(i: number, w: number): void {
    this.halfWidth[i] = w;
  }

  buildGrid(): void {
    this.grid.clear();
    this.maxHalf = 0;
    for (let i = 0; i < this.n; i++) this.maxHalf = Math.max(this.maxHalf, this.halfWidth[i]);
    const pad = this.maxHalf + 30;
    const c = this.cell;
    for (let i = 0; i < this.n; i++) {
      const j = this.next(i);
      if (j < 0) break;
      const x0 = Math.floor((Math.min(this.x[i], this.x[j]) - pad) / c);
      const x1 = Math.floor((Math.max(this.x[i], this.x[j]) + pad) / c);
      const z0 = Math.floor((Math.min(this.z[i], this.z[j]) - pad) / c);
      const z1 = Math.floor((Math.max(this.z[i], this.z[j]) + pad) / c);
      for (let gx = x0; gx <= x1; gx++) {
        for (let gz = z0; gz <= z1; gz++) {
          const key = gx * 100003 + gz;
          let list = this.grid.get(key);
          if (!list) this.grid.set(key, (list = []));
          list.push(i);
        }
      }
    }
  }

  /**
   * Nearest point on the centerline within about (maxHalfWidth + 30) m.
   * Returns false when the point is farther than that from the road.
   */
  nearest(px: number, pz: number, out: NearestResult): boolean {
    const list = this.grid.get(Math.floor(px / this.cell) * 100003 + Math.floor(pz / this.cell));
    if (!list) return false;
    let best = Infinity;
    for (let k = 0; k < list.length; k++) {
      const i = list[k];
      const j = this.next(i);
      const ax = this.x[i];
      const az = this.z[i];
      const dx = this.x[j] - ax;
      const dz = this.z[j] - az;
      const l2 = dx * dx + dz * dz;
      let t = l2 > 0 ? ((px - ax) * dx + (pz - az) * dz) / l2 : 0;
      t = t < 0 ? 0 : t > 1 ? 1 : t;
      const cx = ax + dx * t;
      const cz = az + dz * t;
      const d2 = (px - cx) * (px - cx) + (pz - cz) * (pz - cz);
      if (d2 < best) {
        best = d2;
        out.i = i;
        out.t = t;
      }
    }
    if (best === Infinity) return false;
    const i = out.i;
    const j = this.next(i);
    const t = out.t;
    const segLen = this.segLength(i, j);
    out.s = this.s[i] + segLen * t;
    const cx = this.x[i] + (this.x[j] - this.x[i]) * t;
    const cz = this.z[i] + (this.z[j] - this.z[i]) * t;
    // Lateral sign from the segment direction: right = (-tz, tx).
    const sx = (this.x[j] - this.x[i]) / (segLen || 1);
    const sz = (this.z[j] - this.z[i]) / (segLen || 1);
    out.lateral = (px - cx) * -sz + (pz - cz) * sx;
    out.dist = Math.sqrt(best);
    out.y = this.y[i] + (this.y[j] - this.y[i]) * t;
    return true;
  }

  /** Nearest point on the road from anywhere: the grid first, then a full scan. */
  nearestAny(px: number, pz: number, out: NearestResult): void {
    if (this.nearest(px, pz, out)) return;
    let best = Infinity;
    let bi = 0;
    for (let i = 0; i < this.n; i++) {
      const dx = this.x[i] - px;
      const dz = this.z[i] - pz;
      const d2 = dx * dx + dz * dz;
      if (d2 < best) {
        best = d2;
        bi = i;
      }
    }
    // Refine on the two segments around the closest sample.
    const segs = [bi, this.closed ? (bi - 1 + this.n) % this.n : Math.max(0, bi - 1)];
    best = Infinity;
    for (const i of segs) {
      const j = this.next(i);
      if (j < 0) continue;
      const ax = this.x[i];
      const az = this.z[i];
      const dx = this.x[j] - ax;
      const dz = this.z[j] - az;
      const l2 = dx * dx + dz * dz;
      let t = l2 > 0 ? ((px - ax) * dx + (pz - az) * dz) / l2 : 0;
      t = t < 0 ? 0 : t > 1 ? 1 : t;
      const cx = ax + dx * t;
      const cz = az + dz * t;
      const d2 = (px - cx) * (px - cx) + (pz - cz) * (pz - cz);
      if (d2 < best) {
        best = d2;
        out.i = i;
        out.t = t;
      }
    }
    const i = out.i;
    const j = this.next(i) < 0 ? i : this.next(i);
    const segLen = this.segLength(i, j) || 1;
    out.s = this.s[i] + segLen * out.t;
    const cx = this.x[i] + (this.x[j] - this.x[i]) * out.t;
    const cz = this.z[i] + (this.z[j] - this.z[i]) * out.t;
    const sx = (this.x[j] - this.x[i]) / segLen;
    const sz = (this.z[j] - this.z[i]) / segLen;
    out.lateral = (px - cx) * -sz + (pz - cz) * sx;
    out.dist = Math.sqrt(best);
    out.y = this.y[i] + (this.y[j] - this.y[i]) * out.t;
  }

  /** Position and tangent at arc length s (wraps on closed roads). */
  at(s: number, out: { x: number; y: number; z: number; tx: number; tz: number; i: number }): void {
    let ss = s;
    if (this.closed) {
      ss %= this.length;
      if (ss < 0) ss += this.length;
    } else ss = Math.max(0, Math.min(this.length, ss));
    // Binary search for the segment.
    let lo = 0;
    let hi = this.n - 1;
    while (lo < hi) {
      const mid = (lo + hi + 1) >> 1;
      if (this.s[mid] <= ss) lo = mid;
      else hi = mid - 1;
    }
    const i = lo;
    const j = this.next(i) < 0 ? i : this.next(i);
    const segLen = j === i ? 1 : (j === 0 ? this.length : this.s[j]) - this.s[i];
    const t = j === i ? 0 : Math.max(0, Math.min(1, (ss - this.s[i]) / segLen));
    out.x = this.x[i] + (this.x[j] - this.x[i]) * t;
    out.y = this.y[i] + (this.y[j] - this.y[i]) * t;
    out.z = this.z[i] + (this.z[j] - this.z[i]) * t;
    out.tx = this.tx[i] + (this.tx[j] - this.tx[i]) * t;
    out.tz = this.tz[i] + (this.tz[j] - this.tz[i]) * t;
    const l = Math.sqrt(out.tx * out.tx + out.tz * out.tz) || 1;
    out.tx /= l;
    out.tz /= l;
    out.i = i;
  }
}

export function newNearest(): NearestResult {
  return { i: 0, t: 0, s: 0, lateral: 0, dist: 0, y: 0 };
}
