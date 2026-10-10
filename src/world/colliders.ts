// Static world colliders: walls and barriers as line segments, and posts,
// trees and rocks as circles, stored in a spatial hash.

export interface Segment {
  ax: number;
  az: number;
  bx: number;
  bz: number;
  /** Top of the barrier, m (cars higher than this pass over). */
  top: number;
  /** Bottom, m. */
  bottom: number;
  /** Restitution (bounciness) and friction for impacts. */
  bounce: number;
  friction: number;
  kind: 'wall' | 'barrier' | 'tires' | 'rail' | 'fence';
}

export interface Circle {
  x: number;
  z: number;
  r: number;
  top: number;
  bottom: number;
  kind: 'post' | 'tree' | 'rock' | 'pier' | 'building';
  /** Breakable props (cones, signs, fences) shatter instead of stopping the car. */
  breakable?: boolean;
  broken?: boolean;
  id?: number;
}

export class Colliders {
  readonly segments: Segment[] = [];
  readonly circles: Circle[] = [];
  private segGrid = new Map<number, number[]>();
  private circGrid = new Map<number, number[]>();
  constructor(readonly cell = 24) {}

  private key(gx: number, gz: number): number {
    return gx * 100003 + gz;
  }

  addSegment(s: Segment): void {
    const idx = this.segments.push(s) - 1;
    const c = this.cell;
    const x0 = Math.floor(Math.min(s.ax, s.bx) / c);
    const x1 = Math.floor(Math.max(s.ax, s.bx) / c);
    const z0 = Math.floor(Math.min(s.az, s.bz) / c);
    const z1 = Math.floor(Math.max(s.az, s.bz) / c);
    for (let gx = x0; gx <= x1; gx++) {
      for (let gz = z0; gz <= z1; gz++) {
        const k = this.key(gx, gz);
        let l = this.segGrid.get(k);
        if (!l) this.segGrid.set(k, (l = []));
        l.push(idx);
      }
    }
  }

  /** Add a polyline as consecutive segments. */
  addPolyline(pts: { x: number; z: number }[], proto: Omit<Segment, 'ax' | 'az' | 'bx' | 'bz'>, closed = false): void {
    for (let i = 0; i + 1 < pts.length + (closed ? 1 : 0); i++) {
      const a = pts[i];
      const b = pts[(i + 1) % pts.length];
      this.addSegment({ ...proto, ax: a.x, az: a.z, bx: b.x, bz: b.z });
    }
  }

  addCircle(c: Circle): void {
    const idx = this.circles.push(c) - 1;
    c.id = idx;
    const g = this.cell;
    for (let gx = Math.floor((c.x - c.r) / g); gx <= Math.floor((c.x + c.r) / g); gx++) {
      for (let gz = Math.floor((c.z - c.r) / g); gz <= Math.floor((c.z + c.r) / g); gz++) {
        const k = this.key(gx, gz);
        let l = this.circGrid.get(k);
        if (!l) this.circGrid.set(k, (l = []));
        l.push(idx);
      }
    }
  }

  /** Extra circles generated on demand (the island's trees), visited after the grid. */
  extra: ((x: number, z: number, r: number, onCircle: (c: Circle) => void) => void) | null = null;

  /** Visit colliders whose cells overlap a circle of radius r around (x, z). */
  query(x: number, z: number, r: number, onSeg: (s: Segment) => void, onCircle: (c: Circle) => void): void {
    this.queryGrid(x, z, r, onSeg, onCircle);
    this.extra?.(x, z, r, onCircle);
  }

  private queryGrid(x: number, z: number, r: number, onSeg: (s: Segment) => void, onCircle: (c: Circle) => void): void {
    const g = this.cell;
    const seenS = querySeen;
    seenS.clear();
    const gx0 = Math.floor((x - r) / g);
    const gx1 = Math.floor((x + r) / g);
    const gz0 = Math.floor((z - r) / g);
    const gz1 = Math.floor((z + r) / g);
    for (let gx = gx0; gx <= gx1; gx++) {
      for (let gz = gz0; gz <= gz1; gz++) {
        const k = this.key(gx, gz);
        const sl = this.segGrid.get(k);
        if (sl) {
          for (const i of sl) {
            if (seenS.has(i)) continue;
            seenS.add(i);
            onSeg(this.segments[i]);
          }
        }
        const cl = this.circGrid.get(k);
        if (cl) {
          for (const i of cl) {
            if (seenS.has(-1 - i)) continue;
            seenS.add(-1 - i);
            const c = this.circles[i];
            if (!c.broken) onCircle(c);
          }
        }
      }
    }
  }
}

const querySeen = new Set<number>();
