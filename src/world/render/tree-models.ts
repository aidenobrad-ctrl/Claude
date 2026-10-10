// Procedural tree models and their texture atlas. Foliage is built from
// alpha-tested cards carrying spherical normals (soft, volumetric light),
// trunks from tapered cylinders. Everything is drawn at startup: no assets.
import * as THREE from 'three';
import { RNG } from '../../engine/rng';
import { TREE, TREE_KINDS } from '../world';

/** Atlas regions (u0, v0, u1, v1). */
export const REGION_UV = {
  broad: [0, 0.5, 0.5, 1] as const,
  conifer: [0.5, 0.5, 1, 1] as const,
  frond: [0, 0.25, 0.5, 0.5] as const,
  bark: [0, 0, 0.5, 0.25] as const,
  bush: [0.5, 0, 1, 0.5] as const,
};
type Region = readonly [number, number, number, number];

/** Approximate size of each kind at scale 1 (width, height), m. */
export const TREE_SIZE: [number, number][] = [];
TREE_SIZE[TREE.broadleaf] = [10, 13];
TREE_SIZE[TREE.conifer] = [8, 19];
TREE_SIZE[TREE.palm] = [9, 11];
TREE_SIZE[TREE.cypress] = [3.4, 11.5];
TREE_SIZE[TREE.bush] = [3.6, 2.2];
TREE_SIZE[TREE.dead] = [6, 8];

function hsl(h: number, s: number, l: number, a = 1): string {
  return `hsla(${h.toFixed(1)}, ${(s * 100).toFixed(1)}%, ${(l * 100).toFixed(1)}%, ${a})`;
}

/** Draw the foliage and bark atlas (1024 x 1024). */
export function drawAtlas(size = 1024): THREE.Texture {
  const cv = document.createElement('canvas');
  cv.width = cv.height = size;
  const g = cv.getContext('2d') as CanvasRenderingContext2D;
  g.clearRect(0, 0, size, size);
  const rng = new RNG('atlas');
  // Canvas y runs down; texture v runs up (flipY), so region v1 is the top.
  const rect = (r: Region): [number, number, number, number] => [r[0] * size, (1 - r[3]) * size, (r[2] - r[0]) * size, (r[3] - r[1]) * size];

  // Broadleaf: a dense, ragged cluster of small leaves with twigs.
  {
    const [x, y, w, h] = rect(REGION_UV.broad);
    const cx = x + w / 2;
    const cy = y + h / 2;
    g.strokeStyle = 'rgba(70, 52, 36, 1)';
    g.lineWidth = 3;
    for (let k = 0; k < 9; k++) {
      const a = rng.range(0, Math.PI * 2);
      g.beginPath();
      g.moveTo(cx, cy);
      g.lineTo(cx + Math.cos(a) * w * 0.38, cy + Math.sin(a) * h * 0.38);
      g.stroke();
    }
    // Lobes: several overlapping sub-clusters make a ragged outline.
    const lobes: [number, number, number][] = [];
    for (let k = 0; k < 9; k++) {
      const a = rng.range(0, Math.PI * 2);
      const r = rng.range(0, 0.22);
      lobes.push([cx + Math.cos(a) * w * r, cy + Math.sin(a) * h * r, rng.range(0.13, 0.21) * w]);
    }
    for (let k = 0; k < 3200; k++) {
      const [lx, ly, lr] = lobes[k % lobes.length];
      const a = rng.range(0, Math.PI * 2);
      const r = Math.sqrt(rng.next()) * lr;
      const px = lx + Math.cos(a) * r;
      const py = ly + Math.sin(a) * r;
      const depth = r / lr;
      // Lighter, warmer leaves on the outside; darker within.
      const light = 0.17 + 0.2 * depth * rng.range(0.6, 1.1) + (py < cy ? 0.04 : -0.02);
      g.fillStyle = hsl(rng.range(78, 104), rng.range(0.32, 0.55), light);
      g.save();
      g.translate(px, py);
      g.rotate(rng.range(0, Math.PI * 2));
      g.beginPath();
      g.ellipse(0, 0, rng.range(6, 11), rng.range(3, 5.5), 0, 0, Math.PI * 2);
      g.fill();
      g.restore();
    }
  }

  // Conifer: a branch seen from above, trunk at the left, tip at the right.
  {
    const [x, y, w, h] = rect(REGION_UV.conifer);
    const y0 = y + h / 2;
    g.strokeStyle = 'rgba(60, 44, 32, 1)';
    g.lineWidth = 5;
    g.beginPath();
    g.moveTo(x + 4, y0);
    g.lineTo(x + w - 10, y0);
    g.stroke();
    for (let k = 0; k < 26; k++) {
      const t = k / 26;
      const bx = x + 10 + t * (w - 30);
      const span = (1 - t * 0.75) * h * 0.42;
      for (const side of [-1, 1]) {
        const ex = bx + span * 0.55;
        const ey = y0 + side * span;
        g.strokeStyle = 'rgba(54, 46, 30, 1)';
        g.lineWidth = 2;
        g.beginPath();
        g.moveTo(bx, y0);
        g.lineTo(ex, ey);
        g.stroke();
        // Needle tufts along the twig.
        for (let n = 0; n < 34; n++) {
          const u = rng.next();
          const px = bx + (ex - bx) * u;
          const py = y0 + (ey - y0) * u;
          const a = Math.atan2(ey - y0, ex - bx) + rng.range(-1.1, 1.1);
          const len = rng.range(7, 14) * (1 - u * 0.3);
          g.strokeStyle = hsl(rng.range(118, 150), rng.range(0.28, 0.45), rng.range(0.12, 0.27));
          g.lineWidth = rng.range(1.6, 2.6);
          g.beginPath();
          g.moveTo(px, py);
          g.lineTo(px + Math.cos(a) * len, py + Math.sin(a) * len);
          g.stroke();
        }
      }
    }
  }

  // Palm frond: a curved rib with long leaflets, base at the left.
  {
    const [x, y, w, h] = rect(REGION_UV.frond);
    const y0 = y + h * 0.5;
    for (let k = 0; k < 60; k++) {
      const t = k / 60;
      const bx = x + 12 + t * (w - 24);
      const by = y0;
      const len = Math.sin(Math.PI * Math.min(1, t * 1.15)) * h * 0.46 + 6;
      for (const side of [-1, 1]) {
        g.strokeStyle = hsl(rng.range(85, 105), rng.range(0.38, 0.55), rng.range(0.2, 0.34));
        g.lineWidth = rng.range(3, 5);
        g.beginPath();
        g.moveTo(bx, by);
        g.quadraticCurveTo(bx + len * 0.25, by + side * len * 0.55, bx + len * 0.45, by + side * len);
        g.stroke();
      }
    }
    g.strokeStyle = 'rgba(120, 110, 60, 1)';
    g.lineWidth = 4;
    g.beginPath();
    g.moveTo(x + 4, y0);
    g.lineTo(x + w - 6, y0);
    g.stroke();
  }

  // Bark: vertical fissures over brown-grey.
  {
    const [x, y, w, h] = rect(REGION_UV.bark);
    g.fillStyle = 'rgb(84, 70, 58)';
    g.fillRect(x, y, w, h);
    for (let k = 0; k < 900; k++) {
      const px = x + rng.range(0, w);
      const py = y + rng.range(0, h);
      g.strokeStyle = rng.chance(0.5) ? `rgba(40, 32, 26, ${rng.range(0.4, 0.9)})` : `rgba(130, 116, 100, ${rng.range(0.2, 0.5)})`;
      g.lineWidth = rng.range(1, 3);
      g.beginPath();
      g.moveTo(px, py);
      g.lineTo(px + rng.range(-3, 3), py + rng.range(10, 40));
      g.stroke();
    }
  }

  // Bush and cypress: tight clusters of tiny dark leaves.
  {
    const [x, y, w, h] = rect(REGION_UV.bush);
    const cx = x + w / 2;
    const cy = y + h / 2;
    for (let k = 0; k < 4200; k++) {
      const a = rng.range(0, Math.PI * 2);
      const r = Math.sqrt(rng.next()) * w * 0.44 * (0.85 + 0.15 * Math.sin(a * 5));
      const px = cx + Math.cos(a) * r;
      const py = cy + Math.sin(a) * r;
      const depth = r / (w * 0.44);
      g.fillStyle = hsl(rng.range(95, 125), rng.range(0.3, 0.5), 0.12 + 0.18 * depth * rng.range(0.7, 1.1));
      g.save();
      g.translate(px, py);
      g.rotate(rng.range(0, Math.PI * 2));
      g.beginPath();
      g.ellipse(0, 0, rng.range(4, 7), rng.range(2.2, 3.6), 0, 0, Math.PI * 2);
      g.fill();
      g.restore();
    }
  }

  const tex = new THREE.CanvasTexture(cv);
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.anisotropy = 4;
  tex.generateMipmaps = true;
  tex.minFilter = THREE.LinearMipmapLinearFilter;
  return tex;
}

/** Geometry under construction: positions, normals, uvs, ao and wind weights. */
class Geo {
  pos: number[] = [];
  nrm: number[] = [];
  uv: number[] = [];
  /** x: bend weight (0 at the base), y: leaf flutter, z: ambient occlusion. */
  wind: number[] = [];
  idx: number[] = [];

  vert(p: THREE.Vector3, n: THREE.Vector3, u: number, v: number, bend: number, leaf: number, ao: number): number {
    this.pos.push(p.x, p.y, p.z);
    this.nrm.push(n.x, n.y, n.z);
    this.uv.push(u, v);
    this.wind.push(bend, leaf, ao);
    return this.pos.length / 3 - 1;
  }

  build(): THREE.BufferGeometry {
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(this.pos, 3));
    g.setAttribute('normal', new THREE.Float32BufferAttribute(this.nrm, 3));
    g.setAttribute('uv', new THREE.Float32BufferAttribute(this.uv, 2));
    g.setAttribute('aWind', new THREE.Float32BufferAttribute(this.wind, 3));
    g.setIndex(this.idx);
    g.computeBoundingSphere();
    return g;
  }
}

const V = (x = 0, y = 0, z = 0): THREE.Vector3 => new THREE.Vector3(x, y, z);

/** A tapered cylinder between two points (bark region). */
function limb(geo: Geo, a: THREE.Vector3, b: THREE.Vector3, r0: number, r1: number, sides: number, height: number, region: Region = REGION_UV.bark): void {
  const axis = b.clone().sub(a);
  const len = axis.length();
  axis.normalize();
  const side = Math.abs(axis.y) < 0.9 ? V(0, 1, 0) : V(1, 0, 0);
  const u = side.clone().cross(axis).normalize();
  const w = axis.clone().cross(u).normalize();
  const base = geo.pos.length / 3;
  for (let ring = 0; ring < 2; ring++) {
    const c = ring === 0 ? a : b;
    const r = ring === 0 ? r0 : r1;
    for (let s = 0; s <= sides; s++) {
      const ang = (s / sides) * Math.PI * 2;
      const n = u.clone().multiplyScalar(Math.cos(ang)).addScaledVector(w, Math.sin(ang));
      const p = c.clone().addScaledVector(n, r);
      const tu = region[0] + (region[2] - region[0]) * (s / sides);
      const tv = region[1] + (region[3] - region[1]) * Math.min(1, (ring * len) / 6);
      const bend = Math.min(1, (p.y / height) ** 2);
      geo.vert(p, n, tu, tv, bend, 0, 0.8);
    }
  }
  for (let s = 0; s < sides; s++) {
    const i0 = base + s;
    const i1 = base + s + 1;
    const j0 = base + sides + 1 + s;
    const j1 = j0 + 1;
    geo.idx.push(i0, i1, j1, i0, j1, j0);
  }
}

/**
 * A foliage card: centered at c, spanning right r and up u (half sizes
 * included), lit with normal n, textured from region.
 */
function card(geo: Geo, c: THREE.Vector3, r: THREE.Vector3, u: THREE.Vector3, n: THREE.Vector3, region: Region, height: number, ao: number): void {
  const corners: [number, number][] = [
    [-1, -1],
    [1, -1],
    [1, 1],
    [-1, 1],
  ];
  const base = geo.pos.length / 3;
  for (const [sx, sy] of corners) {
    const p = c.clone().addScaledVector(r, sx).addScaledVector(u, sy);
    const tu = region[0] + (region[2] - region[0]) * (sx * 0.5 + 0.5);
    const tv = region[1] + (region[3] - region[1]) * (sy * 0.5 + 0.5);
    const bend = Math.min(1, Math.max(0, p.y / height) ** 2);
    // Corners at the card's edge flutter more than its middle.
    geo.vert(p, n, tu, tv, bend, 1, ao);
  }
  geo.idx.push(base, base + 1, base + 2, base, base + 2, base + 3);
}

/** Random unit vector. */
function randDir(rng: RNG): THREE.Vector3 {
  const z = rng.range(-1, 1);
  const a = rng.range(0, Math.PI * 2);
  const r = Math.sqrt(1 - z * z);
  return V(r * Math.cos(a), z, r * Math.sin(a));
}

/** A canopy of cards scattered in an ellipsoid. */
function canopy(geo: Geo, rng: RNG, center: THREE.Vector3, rx: number, ry: number, cards: number, cardSize: number, region: Region, height: number): void {
  for (let k = 0; k < cards; k++) {
    const d = randDir(rng);
    const depth = Math.pow(rng.next(), 0.5);
    const p = V(center.x + d.x * rx * depth, center.y + d.y * ry * depth, center.z + d.z * rx * depth);
    // Spherical normal from the canopy center, flattened a little upward.
    const n = p.clone().sub(center);
    n.y = n.y * (rx / ry) + 0.25 * rx;
    n.normalize();
    // Card faces roughly outward with a random twist.
    const f = n.clone().add(randDir(rng).multiplyScalar(0.6)).normalize();
    const side = Math.abs(f.y) < 0.95 ? V(0, 1, 0) : V(1, 0, 0);
    const r = side.clone().cross(f).normalize();
    const u = f.clone().cross(r).normalize();
    const tw = rng.range(0, Math.PI * 2);
    const rr = r.clone().multiplyScalar(Math.cos(tw)).addScaledVector(u, Math.sin(tw));
    const uu = f.clone().cross(rr).normalize();
    const s = cardSize * rng.range(0.75, 1.2);
    card(geo, p, rr.multiplyScalar(s), uu.multiplyScalar(s), n, region, height, 0.55 + 0.45 * depth);
  }
}

function broadleaf(rng: RNG): THREE.BufferGeometry {
  const geo = new Geo();
  const H = TREE_SIZE[TREE.broadleaf][1];
  const top = V(rng.range(-0.3, 0.3), H * 0.46, rng.range(-0.3, 0.3));
  limb(geo, V(0, -0.5, 0), top, 0.38, 0.24, 8, H);
  // Main branches into the crown.
  for (let k = 0; k < 4; k++) {
    const a = (k / 4) * Math.PI * 2 + rng.range(-0.4, 0.4);
    const end = V(Math.cos(a) * rng.range(2.2, 3.2), H * rng.range(0.66, 0.78), Math.sin(a) * rng.range(2.2, 3.2));
    limb(geo, top, end, 0.2, 0.08, 5, H);
  }
  // A crown of overlapping lobes: irregular, wider than tall.
  canopy(geo, rng, V(0, H * 0.64, 0), 4.0, 3.4, 55, 1.6, REGION_UV.broad, H);
  for (let k = 0; k < 3; k++) {
    const a = (k / 3) * Math.PI * 2 + rng.range(-0.5, 0.5);
    const c = V(Math.cos(a) * 2.4, H * rng.range(0.56, 0.72), Math.sin(a) * 2.4);
    canopy(geo, rng, c, 2.7, 2.3, 26, 1.4, REGION_UV.broad, H);
  }
  return geo.build();
}

function conifer(rng: RNG): THREE.BufferGeometry {
  const geo = new Geo();
  const H = TREE_SIZE[TREE.conifer][1];
  limb(geo, V(0, -0.5, 0), V(0, H, 0), 0.38, 0.06, 7, H);
  const tiers = 13;
  for (let t = 0; t < tiers; t++) {
    const f = t / (tiers - 1);
    const y = H * (0.16 + 0.8 * f);
    const R = 3.9 * (1 - f) ** 0.9 + 0.5;
    const n = Math.max(4, Math.round(9 - f * 4));
    const a0 = rng.range(0, Math.PI * 2);
    for (let k = 0; k < n; k++) {
      const a = a0 + (k / n) * Math.PI * 2 + rng.range(-0.2, 0.2);
      const out = V(Math.cos(a), 0, Math.sin(a));
      // The branch card runs from the trunk outward and droops.
      const droop = rng.range(0.25, 0.45);
      const dir = V(out.x, -droop, out.z).normalize();
      const c = V(0, y, 0).addScaledVector(dir, R * 0.5);
      const r = dir.clone().multiplyScalar(R * 0.5);
      const side = V(-out.z, 0, out.x).multiplyScalar(R * 0.42);
      const nrm = V(out.x * 0.7, 0.75, out.z * 0.7).normalize();
      card(geo, c, r, side, nrm, REGION_UV.conifer, H, 0.6 + 0.4 * f);
    }
  }
  // Crossed cards at the leader.
  for (let k = 0; k < 2; k++) {
    const a = k * Math.PI * 0.5;
    card(geo, V(0, H * 0.94, 0), V(Math.cos(a) * 0.9, 0, Math.sin(a) * 0.9), V(0, 1.2, 0), V(Math.cos(a), 0.6, Math.sin(a)).normalize(), REGION_UV.bush, H, 1);
  }
  return geo.build();
}

function palm(rng: RNG): THREE.BufferGeometry {
  const geo = new Geo();
  const H = TREE_SIZE[TREE.palm][1];
  // A leaning, slightly curved trunk.
  const lean = V(rng.range(0.4, 0.9), 0, rng.range(-0.3, 0.3));
  let prev = V(0, -0.5, 0);
  const segs = 6;
  for (let k = 1; k <= segs; k++) {
    const t = k / segs;
    const p = V(lean.x * t * t * 1.6, H * 0.86 * t, lean.z * t * t * 1.6);
    limb(geo, prev, p, 0.24 - 0.06 * (t - 1 / segs), 0.24 - 0.06 * t, 7, H);
    prev = p;
  }
  const crown = prev.clone();
  const fronds = 11;
  for (let k = 0; k < fronds; k++) {
    const a = (k / fronds) * Math.PI * 2 + rng.range(-0.15, 0.15);
    const out = V(Math.cos(a), 0, Math.sin(a));
    const len = rng.range(3.8, 4.6);
    const rise = rng.range(0.2, 0.75);
    // Two segments: up and out, then drooping.
    const mid = crown.clone().addScaledVector(out, len * 0.45).add(V(0, rise, 0));
    const tip = crown.clone().addScaledVector(out, len).add(V(0, rise - 1.6, 0));
    const side = V(-out.z, 0, out.x).multiplyScalar(0.62);
    const nrm = V(out.x * 0.3, 1, out.z * 0.3).normalize();
    for (const [p0, p1, u0, u1] of [
      [crown, mid, 0, 0.45],
      [mid, tip, 0.45, 1],
    ] as [THREE.Vector3, THREE.Vector3, number, number][]) {
      const base = geo.pos.length / 3;
      const R = REGION_UV.frond;
      const tu0 = R[0] + (R[2] - R[0]) * u0;
      const tu1 = R[0] + (R[2] - R[0]) * u1;
      geo.vert(p0.clone().sub(side), nrm, tu0, R[1], 1, 1, 0.9);
      geo.vert(p1.clone().sub(side), nrm, tu1, R[1], 1, 1, 1);
      geo.vert(p1.clone().add(side), nrm, tu1, R[3], 1, 1, 1);
      geo.vert(p0.clone().add(side), nrm, tu0, R[3], 1, 1, 0.9);
      geo.idx.push(base, base + 1, base + 2, base, base + 2, base + 3);
    }
  }
  return geo.build();
}

function cypress(rng: RNG): THREE.BufferGeometry {
  const geo = new Geo();
  const H = TREE_SIZE[TREE.cypress][1];
  limb(geo, V(0, -0.5, 0), V(0, H * 0.4, 0), 0.22, 0.14, 6, H);
  for (let k = 0; k < 70; k++) {
    const t = rng.next();
    const y = H * (0.08 + 0.9 * t);
    // Narrow flame shape: widest a third of the way up.
    const R = 1.5 * Math.sin(Math.PI * Math.min(1, (t + 0.08) * 0.95)) ** 0.8 * (1 - t * 0.35);
    const a = rng.range(0, Math.PI * 2);
    const d = Math.sqrt(rng.next());
    const p = V(Math.cos(a) * R * d, y, Math.sin(a) * R * d);
    const n = V(Math.cos(a), 0.35, Math.sin(a)).normalize();
    const f = n.clone().add(randDir(rng).multiplyScalar(0.5)).normalize();
    const side = Math.abs(f.y) < 0.95 ? V(0, 1, 0) : V(1, 0, 0);
    const r = side.clone().cross(f).normalize().multiplyScalar(0.9);
    const u = f.clone().cross(r).normalize().multiplyScalar(1.1);
    card(geo, p, r, u, n, REGION_UV.bush, H, 0.6 + 0.4 * d);
  }
  return geo.build();
}

function bush(rng: RNG): THREE.BufferGeometry {
  const geo = new Geo();
  const H = TREE_SIZE[TREE.bush][1];
  canopy(geo, rng, V(0, H * 0.42, 0), 1.55, 1.0, 22, 0.9, REGION_UV.bush, H);
  return geo.build();
}

function dead(rng: RNG): THREE.BufferGeometry {
  const geo = new Geo();
  const H = TREE_SIZE[TREE.dead][1];
  const top = V(0.2, H * 0.55, 0.1);
  limb(geo, V(0, -0.5, 0), top, 0.3, 0.16, 6, H);
  const grow = (from: THREE.Vector3, dir: THREE.Vector3, len: number, r: number, depth: number): void => {
    const to = from.clone().addScaledVector(dir, len);
    limb(geo, from, to, r, r * 0.6, 4, H);
    if (depth <= 0) return;
    for (let k = 0; k < 2; k++) {
      const d = dir.clone().add(randDir(rng).multiplyScalar(0.7)).add(V(0, 0.35, 0)).normalize();
      grow(to, d, len * 0.68, r * 0.6, depth - 1);
    }
  };
  for (let k = 0; k < 3; k++) {
    const a = (k / 3) * Math.PI * 2;
    grow(top, V(Math.cos(a), 0.9, Math.sin(a)).normalize(), 2.2, 0.13, 2);
  }
  return geo.build();
}

/** One geometry per tree kind (index = TREE kind). */
export function buildTreeGeometries(): THREE.BufferGeometry[] {
  const rng = new RNG('trees');
  const out: THREE.BufferGeometry[] = new Array(TREE_KINDS);
  out[TREE.broadleaf] = broadleaf(rng);
  out[TREE.conifer] = conifer(rng);
  out[TREE.palm] = palm(rng);
  out[TREE.cypress] = cypress(rng);
  out[TREE.bush] = bush(rng);
  out[TREE.dead] = dead(rng);
  return out;
}
