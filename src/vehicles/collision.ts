// Car collisions: each car is an oriented box in the ground plane with a
// vertical extent. Contacts come from the separating axis test; response is
// an impulse at the contact point (restitution plus Coulomb friction) and a
// positional correction so bodies never stay interpenetrated.
import { V3 } from '../engine/math';
import type { Colliders, Circle, Segment } from '../world/colliders';
import type { Vehicle } from './vehicle';

export interface Contact {
  /** Unit normal pointing from the obstacle (or car B) toward car A. */
  nx: number;
  nz: number;
  depth: number;
  /** Contact point (world, ground plane). */
  px: number;
  pz: number;
}

interface Box {
  cx: number;
  cz: number;
  /** Unit axes: forward (fx, fz) and right (rx, rz). */
  fx: number;
  fz: number;
  rx: number;
  rz: number;
  hl: number;
  hw: number;
  bottom: number;
  top: number;
}

const boxA: Box = { cx: 0, cz: 0, fx: 0, fz: -1, rx: 1, rz: 0, hl: 2, hw: 1, bottom: 0, top: 1 };
const boxB: Box = { cx: 0, cz: 0, fx: 0, fz: -1, rx: 1, rz: 0, hl: 2, hw: 1, bottom: 0, top: 1 };
const contact: Contact = { nx: 0, nz: 0, depth: 0, px: 0, pz: 0 };
const tmp = new V3();
const tmp2 = new V3();
const pt = new V3();

function carBox(v: Vehicle, out: Box): Box {
  const p = v.params;
  v.toWorld(p.boxCenter, tmp);
  out.cx = tmp.x;
  out.cz = tmp.z;
  // Project the body axes onto the ground plane.
  let fx = v.fwd.x;
  let fz = v.fwd.z;
  let l = Math.sqrt(fx * fx + fz * fz) || 1;
  fx /= l;
  fz /= l;
  out.fx = fx;
  out.fz = fz;
  out.rx = -fz;
  out.rz = fx;
  l = 1;
  out.hl = p.half.z * 0.98;
  out.hw = p.half.x * 0.96;
  out.bottom = tmp.y - p.half.y;
  out.top = tmp.y + p.half.y;
  return out;
}

function corners(b: Box, i: number): [number, number] {
  const sl = i & 1 ? 1 : -1;
  const sw = i & 2 ? 1 : -1;
  return [b.cx + b.fx * b.hl * sl + b.rx * b.hw * sw, b.cz + b.fz * b.hl * sl + b.rz * b.hw * sw];
}

/** Box vs segment. Returns the contact with the normal pushing the box out. */
function boxSegment(b: Box, s: Segment, out: Contact): boolean {
  // Closest point on the segment to the box center, then test along the
  // segment normal and the box axes (SAT on 3 axes, segment as a thin box).
  const dx = s.bx - s.ax;
  const dz = s.bz - s.az;
  const len = Math.sqrt(dx * dx + dz * dz);
  if (len < 1e-6) return false;
  const ux = dx / len;
  const uz = dz / len;
  let nx = -uz;
  let nz = ux;
  // Normal toward the box center.
  const side = (b.cx - s.ax) * nx + (b.cz - s.az) * nz;
  if (side < 0) {
    nx = -nx;
    nz = -nz;
  }
  // Box projection radius on the normal.
  const rN = b.hl * Math.abs(b.fx * nx + b.fz * nz) + b.hw * Math.abs(b.rx * nx + b.rz * nz);
  const dist = Math.abs(side);
  if (dist >= rN) return false;
  // Overlap along the segment direction: the box extent must reach the segment span.
  const along = (b.cx - s.ax) * ux + (b.cz - s.az) * uz;
  const rU = b.hl * Math.abs(b.fx * ux + b.fz * uz) + b.hw * Math.abs(b.rx * ux + b.rz * uz);
  if (along + rU < 0 || along - rU > len) return false;
  // Deepest box corner that lies within the segment span is the contact point.
  let best = Infinity;
  let px = 0;
  let pz = 0;
  for (let i = 0; i < 4; i++) {
    const [cx, cz] = corners(b, i);
    const a = (cx - s.ax) * ux + (cz - s.az) * uz;
    if (a < -0.3 || a > len + 0.3) continue;
    const d = (cx - s.ax) * nx + (cz - s.az) * nz;
    if (d < best) {
      best = d;
      px = cx;
      pz = cz;
    }
  }
  if (best === Infinity) {
    // Segment end poking into the box side: use the nearer endpoint.
    const ex = along < len / 2 ? s.ax : s.bx;
    const ez = along < len / 2 ? s.az : s.bz;
    return boxPoint(b, ex, ez, 0, out);
  }
  if (best >= 0) return false;
  out.nx = nx;
  out.nz = nz;
  out.depth = -best;
  out.px = px;
  out.pz = pz;
  return true;
}

/** Box vs a circle of radius r at (x, z). */
function boxPoint(b: Box, x: number, z: number, r: number, out: Contact): boolean {
  const dx = x - b.cx;
  const dz = z - b.cz;
  const lf = dx * b.fx + dz * b.fz;
  const lr = dx * b.rx + dz * b.rz;
  // Closest point on the box to the circle center.
  const cf = Math.max(-b.hl, Math.min(b.hl, lf));
  const cr = Math.max(-b.hw, Math.min(b.hw, lr));
  const inside = cf === lf && cr === lr;
  if (!inside) {
    const qx = b.cx + b.fx * cf + b.rx * cr;
    const qz = b.cz + b.fz * cf + b.rz * cr;
    const ex = qx - x;
    const ez = qz - z;
    const d = Math.sqrt(ex * ex + ez * ez);
    if (d >= r || d < 1e-9) return false;
    out.nx = ex / d;
    out.nz = ez / d;
    out.depth = r - d;
    out.px = qx;
    out.pz = qz;
    return true;
  }
  // Center inside the box: push out along the shallowest box axis.
  const pf = b.hl - Math.abs(lf);
  const pr = b.hw - Math.abs(lr);
  if (pf < pr) {
    const sgn = lf > 0 ? -1 : 1;
    out.nx = b.fx * sgn;
    out.nz = b.fz * sgn;
    out.depth = pf + r;
  } else {
    const sgn = lr > 0 ? -1 : 1;
    out.nx = b.rx * sgn;
    out.nz = b.rz * sgn;
    out.depth = pr + r;
  }
  out.px = x;
  out.pz = z;
  return true;
}

/** Oriented box vs oriented box (2D SAT, 4 axes). Normal points from B to A. */
function boxBox(a: Box, b: Box, out: Contact): boolean {
  const axes = [a.fx, a.fz, a.rx, a.rz, b.fx, b.fz, b.rx, b.rz];
  let minDepth = Infinity;
  let mnx = 0;
  let mnz = 0;
  const dx = a.cx - b.cx;
  const dz = a.cz - b.cz;
  for (let k = 0; k < 4; k++) {
    const nx = axes[k * 2];
    const nz = axes[k * 2 + 1];
    const ra = a.hl * Math.abs(a.fx * nx + a.fz * nz) + a.hw * Math.abs(a.rx * nx + a.rz * nz);
    const rb = b.hl * Math.abs(b.fx * nx + b.fz * nz) + b.hw * Math.abs(b.rx * nx + b.rz * nz);
    const d = dx * nx + dz * nz;
    const overlap = ra + rb - Math.abs(d);
    if (overlap <= 0) return false;
    if (overlap < minDepth) {
      minDepth = overlap;
      const sgn = d < 0 ? -1 : 1;
      mnx = nx * sgn;
      mnz = nz * sgn;
    }
  }
  // Contact point: the corner of either box that penetrates deepest.
  let bestPen = -Infinity;
  let px = (a.cx + b.cx) / 2;
  let pz = (a.cz + b.cz) / 2;
  for (let i = 0; i < 4; i++) {
    const [cx, cz] = corners(a, i);
    const pen = -((cx - b.cx) * mnx + (cz - b.cz) * mnz) + (b.hl * Math.abs(b.fx * mnx + b.fz * mnz) + b.hw * Math.abs(b.rx * mnx + b.rz * mnz));
    if (pen > bestPen) {
      bestPen = pen;
      px = cx;
      pz = cz;
    }
    const [dx2, dz2] = corners(b, i);
    const pen2 = (dx2 - a.cx) * mnx + (dz2 - a.cz) * mnz + (a.hl * Math.abs(a.fx * mnx + a.fz * mnz) + a.hw * Math.abs(a.rx * mnx + a.rz * mnz));
    if (pen2 > bestPen) {
      bestPen = pen2;
      px = dx2;
      pz = dz2;
    }
  }
  out.nx = mnx;
  out.nz = mnz;
  out.depth = minDepth;
  out.px = px;
  out.pz = pz;
  return true;
}

/** Impulse response for car A against a static obstacle. */
function resolveStatic(v: Vehicle, c: Contact, bounce: number, friction: number, height: number): number {
  pt.set(c.px, height, c.pz);
  v.pointVelocity(pt, tmp);
  const vn = tmp.x * c.nx + tmp.z * c.nz;
  // Positional correction: move the car out of the obstacle.
  const push = Math.max(0, c.depth - 0.005);
  v.pos.x += c.nx * push;
  v.pos.z += c.nz * push;
  if (vn >= 0) return 0;
  // Effective mass along the normal at the contact point.
  tmp2.set(c.nx, 0, c.nz);
  const k = effectiveInvMass(v, pt, tmp2);
  const jn = (-(1 + bounce) * vn) / k;
  // Friction impulse along the tangent, limited by the Coulomb cone.
  const tx = tmp.x - vn * c.nx;
  const tz = tmp.z - vn * c.nz;
  const tl = Math.sqrt(tx * tx + tz * tz);
  tmp2.set(c.nx * jn, 0, c.nz * jn);
  if (tl > 1e-4) {
    const ux = tx / tl;
    const uz = tz / tl;
    tmp.set(ux, 0, uz);
    const kt = effectiveInvMass(v, pt, tmp);
    const jt = Math.min(tl / kt, friction * jn);
    tmp2.x -= ux * jt;
    tmp2.z -= uz * jt;
  }
  v.applyImpulse(pt, tmp2);
  v.impact = Math.max(v.impact, jn);
  return jn;
}

const r1 = new V3();
const r2 = new V3();
function effectiveInvMass(v: Vehicle, point: V3, n: V3): number {
  r1.subVectors(point, v.pos);
  r2.crossVectors(r1, n);
  v.applyInvInertia(r2, r2);
  r2.crossVectors(r2, r1);
  return v.params.invMass + r2.dot(n);
}

export interface CollisionEvent {
  impulse: number;
  x: number;
  z: number;
  kind: string;
}

/** Resolve one car against the static world. Returns the largest impact. */
export function collideWorld(v: Vehicle, world: Colliders, onBreak?: (c: Circle) => void): CollisionEvent | null {
  const b = carBox(v, boxA);
  const reach = Math.sqrt(b.hl * b.hl + b.hw * b.hw) + 2;
  let worst: CollisionEvent | null = null;
  const height = (b.bottom + b.top) / 2 - 0.15;
  world.query(b.cx, b.cz, reach, (s) => {
    if (b.bottom > s.top || b.top < s.bottom) return;
    if (boxSegment(carBox(v, boxA), s, contact)) {
      const j = resolveStatic(v, contact, s.bounce, s.friction, height);
      if (j > 0 && (!worst || j > worst.impulse)) worst = { impulse: j, x: contact.px, z: contact.pz, kind: s.kind };
    }
  }, (c) => {
    if (b.bottom > c.top || b.top < c.bottom) return;
    if (boxPoint(carBox(v, boxA), c.x, c.z, c.r, contact)) {
      if (c.breakable) {
        c.broken = true;
        // A breakable prop costs a little speed but does not stop the car.
        v.vel.scale(0.97);
        onBreak?.(c);
        return;
      }
      const j = resolveStatic(v, contact, c.kind === 'tree' ? 0.15 : 0.25, 0.4, height);
      if (j > 0 && (!worst || j > worst.impulse)) worst = { impulse: j, x: contact.px, z: contact.pz, kind: c.kind };
    }
  });
  return worst;
}

/** Resolve a pair of cars. Returns the impulse magnitude (0 if no contact). */
export function collideCars(a: Vehicle, b: Vehicle): number {
  const A = carBox(a, boxA);
  const B = carBox(b, boxB);
  const dx = A.cx - B.cx;
  const dz = A.cz - B.cz;
  const reach = A.hl + B.hl + 0.5;
  if (dx * dx + dz * dz > reach * reach) return 0;
  if (A.bottom > B.top || A.top < B.bottom) return 0;
  if (!boxBox(A, B, contact)) return 0;
  const height = Math.max(A.bottom, B.bottom) + 0.35;
  pt.set(contact.px, height, contact.pz);
  // Split the positional correction by inverse mass.
  const ima = a.params.invMass;
  const imb = b.params.invMass;
  const push = Math.max(0, contact.depth - 0.005) / (ima + imb);
  a.pos.x += contact.nx * push * ima;
  a.pos.z += contact.nz * push * ima;
  b.pos.x -= contact.nx * push * imb;
  b.pos.z -= contact.nz * push * imb;
  a.pointVelocity(pt, tmp);
  b.pointVelocity(pt, tmp2);
  const rvx = tmp.x - tmp2.x;
  const rvz = tmp.z - tmp2.z;
  const vn = rvx * contact.nx + rvz * contact.nz;
  if (vn >= 0) return 0;
  const n = r1.set(contact.nx, 0, contact.nz).clone();
  const k = effectiveInvMass(a, pt, n) + effectiveInvMass(b, pt, n);
  const bounce = 0.3;
  const jn = (-(1 + bounce) * vn) / k;
  const imp = n.clone().scale(jn);
  // Friction between the bodies.
  const tx = rvx - vn * contact.nx;
  const tz = rvz - vn * contact.nz;
  const tl = Math.sqrt(tx * tx + tz * tz);
  if (tl > 1e-4) {
    const t = new V3(tx / tl, 0, tz / tl);
    const kt = effectiveInvMass(a, pt, t) + effectiveInvMass(b, pt, t);
    const jt = Math.min(tl / kt, 0.3 * jn);
    imp.addScaled(t, -jt);
  }
  a.applyImpulse(pt, imp);
  b.applyImpulse(pt, imp.negate());
  a.impact = Math.max(a.impact, jn);
  b.impact = Math.max(b.impact, jn);
  return jn;
}
