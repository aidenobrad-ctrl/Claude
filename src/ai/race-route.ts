// The road an event is run on: a centreline resampled every few metres with
// the drivable half width on each side, closed for circuits and open for
// sprints. Progress along it (arc length s) is how races are scored and how
// the AI finds its place on the racing line. A port of the Unity version's
// RaceRoute.cs (same arithmetic, so the two agree; tests/ai.test.ts).

/** Target spacing of the resampled centreline, m. */
export const ROUTE_SPACING = 3.0;

/** A point and tangent on a route (out parameter, reused). */
export interface RoutePose {
  x: number;
  y: number;
  z: number;
  tx: number;
  tz: number;
}

/** Nearest point on a route: arc length, signed lateral offset (+ right of travel) and distance. */
export interface RouteProjection {
  s: number;
  lateral: number;
  dist: number;
}

export function newPose(): RoutePose {
  return { x: 0, y: 0, z: 0, tx: 0, tz: 1 };
}

export function newProjection(): RouteProjection {
  return { s: 0, lateral: 0, dist: 0 };
}

export class RaceRoute {
  name = '';
  closed = false;
  n = 0;
  x: Float64Array = new Float64Array(0);
  y: Float64Array = new Float64Array(0);
  z: Float64Array = new Float64Array(0);
  /** Unit tangent along the direction of travel. */
  tx: Float64Array = new Float64Array(0);
  tz: Float64Array = new Float64Array(0);
  /** Arc length at each sample. */
  s: Float64Array = new Float64Array(0);
  /** Drivable half width left and right of the centreline (where a car's centre may go plus its half width). */
  wLeft: Float64Array = new Float64Array(0);
  wRight: Float64Array = new Float64Array(0);
  length = 0;

  private constructor() {}

  /** From a polyline (x, y, z) with half widths per point, resampled evenly; closed routes join the last point back to the first. */
  static fromPolyline(name: string, x: ArrayLike<number>, y: ArrayLike<number>, z: ArrayLike<number>, wLeft: ArrayLike<number>, wRight: ArrayLike<number>, closed: boolean): RaceRoute {
    const m = x.length;
    if (m < 2) throw new Error('route needs at least two points');
    const segs = closed ? m : m - 1;
    const cum = new Float64Array(segs + 1);
    for (let i = 0; i < segs; i++) {
      const j = (i + 1) % m;
      const dx = x[j] - x[i];
      const dz = z[j] - z[i];
      cum[i + 1] = cum[i] + Math.sqrt(dx * dx + dz * dz);
    }
    const L = cum[segs];
    const n = Math.max(4, roundHalfEven(L / ROUTE_SPACING));
    const r = new RaceRoute();
    r.name = name;
    r.closed = closed;
    r.length = L;
    const count = closed ? n : n + 1;
    r.n = count;
    r.x = new Float64Array(count);
    r.y = new Float64Array(count);
    r.z = new Float64Array(count);
    r.tx = new Float64Array(count);
    r.tz = new Float64Array(count);
    r.s = new Float64Array(count);
    r.wLeft = new Float64Array(count);
    r.wRight = new Float64Array(count);
    let seg = 0;
    for (let k = 0; k < count; k++) {
      const s = (L * k) / n;
      while (seg < segs - 1 && cum[seg + 1] < s) seg++;
      const i = seg;
      const j = (seg + 1) % m;
      const len = cum[seg + 1] - cum[seg];
      let t = len > 0 ? (s - cum[seg]) / len : 0;
      t = t < 0 ? 0 : t > 1 ? 1 : t;
      r.x[k] = x[i] + (x[j] - x[i]) * t;
      r.y[k] = y[i] + (y[j] - y[i]) * t;
      r.z[k] = z[i] + (z[j] - z[i]) * t;
      r.wLeft[k] = wLeft[i] + (wLeft[j] - wLeft[i]) * t;
      r.wRight[k] = wRight[i] + (wRight[j] - wRight[i]) * t;
      r.s[k] = s;
    }
    for (let k = 0; k < count; k++) {
      const a = closed ? (k - 1 + count) % count : Math.max(0, k - 1);
      const b = closed ? (k + 1) % count : Math.min(count - 1, k + 1);
      const dx = r.x[b] - r.x[a];
      const dz = r.z[b] - r.z[a];
      let l = Math.sqrt(dx * dx + dz * dz);
      if (l === 0) l = 1;
      r.tx[k] = dx / l;
      r.tz[k] = dz / l;
    }
    return r;
  }

  wrap(s: number): number {
    if (!this.closed) return s < 0 ? 0 : s > this.length ? this.length : s;
    s %= this.length;
    return s < 0 ? s + this.length : s;
  }

  /** Signed difference b - a along a closed route, in (-L/2, L/2]. */
  delta(a: number, b: number): number {
    let d = b - a;
    if (!this.closed) return d;
    d %= this.length;
    if (d > this.length / 2) d -= this.length;
    if (d <= -this.length / 2) d += this.length;
    return d;
  }

  private t = 0;

  /** Sample index at arc length s; the fraction to the next sample is left in this.t. */
  private index(s: number): number {
    const ss = this.wrap(s);
    const n = this.closed ? this.n : this.n - 1;
    const f = (ss / this.length) * n;
    let i = Math.floor(f);
    if (i >= n) i = n - 1;
    if (i < 0) i = 0;
    this.t = f - i;
    return i;
  }

  private next(i: number): number {
    return this.closed ? (i + 1) % this.n : Math.min(this.n - 1, i + 1);
  }

  /** Centreline point, height and unit tangent at arc length s. */
  at(s: number, out: RoutePose): RoutePose {
    const i = this.index(s);
    const t = this.t;
    const j = this.next(i);
    out.x = this.x[i] + (this.x[j] - this.x[i]) * t;
    out.y = this.y[i] + (this.y[j] - this.y[i]) * t;
    out.z = this.z[i] + (this.z[j] - this.z[i]) * t;
    let tx = this.tx[i] + (this.tx[j] - this.tx[i]) * t;
    let tz = this.tz[i] + (this.tz[j] - this.tz[i]) * t;
    let l = Math.sqrt(tx * tx + tz * tz);
    if (l === 0) l = 1;
    tx /= l;
    tz /= l;
    out.tx = tx;
    out.tz = tz;
    return out;
  }

  /** Linear interpolation of a per-sample array at arc length s. */
  sample(a: ArrayLike<number>, s: number): number {
    const i = this.index(s);
    return a[i] + (a[this.next(i)] - a[i]) * this.t;
  }

  /** Nearest point of the centreline to (px, pz), searching only arc lengths within [hint - back, hint + ahead]. */
  project(px: number, pz: number, hint: number, back: number, ahead: number, out: RouteProjection): RouteProjection {
    const i0 = this.index(hint - back);
    const steps = Math.ceil((back + ahead) / (this.length / (this.closed ? this.n : this.n - 1))) + 2;
    return this.projectRange(px, pz, i0, steps, out);
  }

  /** Nearest point anywhere on the route. */
  projectAny(px: number, pz: number, out: RouteProjection): RouteProjection {
    return this.projectRange(px, pz, 0, this.closed ? this.n : this.n - 1, out);
  }

  private projectRange(px: number, pz: number, i0: number, steps: number, out: RouteProjection): RouteProjection {
    const segs = this.closed ? this.n : this.n - 1;
    let best = Infinity;
    let s = 0;
    let lateral = 0;
    for (let k = 0; k < steps && k < segs; k++) {
      const i = this.closed ? (i0 + k) % this.n : i0 + k;
      if (i >= segs) break;
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
        let sl = Math.sqrt(l2);
        if (sl === 0) sl = 1;
        s = this.s[i] + (j === 0 && this.closed ? this.length - this.s[i] : this.s[j] - this.s[i]) * t;
        lateral = (px - cx) * (-dz / sl) + (pz - cz) * (dx / sl);
      }
    }
    out.dist = Math.sqrt(best);
    out.s = this.wrap(s);
    out.lateral = lateral;
    return out;
  }
}

/** C#'s Math.Round: halves to even. */
export function roundHalfEven(x: number): number {
  const f = Math.floor(x);
  const d = x - f;
  if (d > 0.5) return f + 1;
  if (d < 0.5) return f;
  return f % 2 === 0 ? f : f + 1;
}
