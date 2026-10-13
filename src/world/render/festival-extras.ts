// The festival's crowd and dressing: the cars on show on lit plinths and the
// car park (the cars' Blender models, baked by finish into four meshes, so
// two dozen cars cost a handful of draws), food trucks with striped awnings,
// the stage's light towers, crowd barriers, and the crowds themselves:
// instanced people who bounce and sway to the music.
import * as THREE from 'three';
import { BARRIER_HEIGHT, DISPLAY_PLINTH, type FestivalCar, type FestivalCrowd, type FestivalLayout, type FestivalPiece } from '../festival';
import { Merge, mergeInto } from './festival-view';
import { patchMaterial } from '../../engine/render/materials';
import { atmoUniforms } from '../../engine/render/atmosphere';
import { loadCarModel } from '../../vehicles/render/car-glb';
import { CAR_MODELS } from '../../vehicles/models';
import type { CarStyle } from '../../vehicles/render/car-style';
import { RNG } from '../../engine/rng';

const C = (hex: number | string): THREE.Color => new THREE.Color(hex);

type Finish = 'coat' | 'metal' | 'matte' | 'glass';

/** A Blender material slot's finish and colour on a standing car (lights off). */
function finish(name: string, style: CarStyle, paint: number): [Finish, THREE.ColorRepresentation] {
  const table: [string, Finish, THREE.ColorRepresentation][] = [
    ['Paint', 'coat', paint],
    ['Accent', 'coat', style.accent],
    ['Glass', 'glass', 0x0b1116],
    ['LightFront', 'coat', 0xd9e0e8],
    ['DRL', 'coat', 0xf2f5fa],
    ['LightRear', 'coat', 0x6e0a08],
    ['Gloss', 'coat', 0x060607],
    ['Chrome', 'metal', 0xdde1e6],
    ['Grille', 'matte', 0x0b0b0c],
    ['Gap', 'matte', 0x030303],
    ['Cladding', 'matte', 0x18181a],
    ['Carbon', 'coat', 0x101114],
    ['Plate', 'matte', 0xd6d6ce],
    ['Trim', 'matte', 0x101113],
    ['Rubber', 'matte', 0x101011],
    ['Rim', 'metal', style.rim],
    ['Brake', 'metal', 0x5d5f63],
    ['Caliper', 'coat', style.caliper],
    ['Dark', 'matte', 0x060607],
    ['Interior', 'matte', style.interior],
    ['Fabric', 'matte', 0x0c0c0d],
    ['Mirror', 'metal', 0xeaedf1],
  ];
  for (const [k, f, c] of table) if (name.includes(k)) return [f, c];
  return ['matte', 0x101113];
}

class Baked {
  pos: number[] = [];
  nrm: number[] = [];
  col: number[] = [];
  idx: number[] = [];

  add(g: THREE.BufferGeometry, world: THREE.Matrix4, color: THREE.Color): void {
    const pos = g.getAttribute('position');
    const nrm = g.getAttribute('normal');
    const nm = new THREE.Matrix3().getNormalMatrix(world);
    const v = new THREE.Vector3();
    const base = this.pos.length / 3;
    for (let i = 0; i < pos.count; i++) {
      v.fromBufferAttribute(pos, i).applyMatrix4(world);
      this.pos.push(v.x, v.y, v.z);
      if (nrm) v.fromBufferAttribute(nrm, i).applyMatrix3(nm).normalize();
      else v.set(0, 1, 0);
      this.nrm.push(v.x, v.y, v.z);
      this.col.push(color.r, color.g, color.b);
    }
    const idx = g.getIndex();
    if (idx) for (let i = 0; i < idx.count; i++) this.idx.push(base + idx.getX(i));
    else for (let i = 0; i < pos.count; i++) this.idx.push(base + i);
  }

  geometry(): THREE.BufferGeometry {
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(this.pos, 3));
    g.setAttribute('normal', new THREE.Float32BufferAttribute(this.nrm, 3));
    g.setAttribute('color', new THREE.Float32BufferAttribute(this.col, 3));
    g.setIndex(this.idx);
    g.computeBoundingSphere();
    return g;
  }
}

export interface ExtrasOptions {
  /** Cars in the car park (the cars on show are always there). */
  parked: boolean;
  /** Fraction of each crowd drawn. */
  crowd: number;
}

export class FestivalExtras {
  readonly group = new THREE.Group();
  /** Settles when the standing cars are in the scene. */
  readonly ready: Promise<void>;

  constructor(f: FestivalLayout, opts: ExtrasOptions) {
    this.group.name = 'festival-extras';
    const metal = patchMaterial(new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.45, metalness: 0.55 }), { key: 'fx-metal' });
    const paint = patchMaterial(new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.5, metalness: 0.1 }), { key: 'fx-paint' });
    const solid = new Merge();
    const painted = new Merge();
    const glow = new Merge();
    for (const d of f.displays) this.plinth(solid, glow, d, f.displays.indexOf(d));
    for (const t of f.trucks) this.truck(painted, glow, t);
    for (const t of f.towers) this.tower(solid, glow, t);
    for (const b of f.barriers) this.barrier(solid, b);
    if (opts.parked && f.parked.length) this.carPark(painted, f);
    const sm = new THREE.Mesh(solid.geometry(), metal);
    const pm = new THREE.Mesh(painted.geometry(), paint);
    for (const m of [sm, pm]) m.castShadow = m.receiveShadow = true;
    const lamps = patchMaterial(new THREE.MeshStandardMaterial({ vertexColors: true, emissive: 0xffffff, emissiveIntensity: 1.6, roughness: 0.4, toneMapped: true }), { key: 'fx-glow' });
    // Emissive in the vertex colour: the lamp faces glow with their own colours.
    lamps.onBeforeCompile = (shader) => {
      shader.fragmentShader = shader.fragmentShader.replace('#include <emissivemap_fragment>', '#include <emissivemap_fragment>\ntotalEmissiveRadiance *= vColor;');
    };
    lamps.customProgramCacheKey = () => 'fx-glow-v';
    const gm = new THREE.Mesh(glow.geometry(), lamps);
    this.group.add(sm, pm, gm);
    for (const c of f.crowds) this.crowd(c, opts.crowd);
    // The cars on show cast real shadows; the parked ones a soft blob each (their shadows were most of the dressing's cost).
    const shows = this.bakeCars(f.displays.map((car) => ({ car, lift: DISPLAY_PLINTH.h })), true);
    const parked = opts.parked ? this.bakeCars(f.parked.map((car) => ({ car, lift: 0 })), false) : Promise.resolve();
    if (opts.parked) this.blobs(f.parked);
    this.ready = Promise.all([shows, parked]).then(() => undefined);
  }

  /** A round plinth, dark and glossy, with a lit ring at its foot. */
  private plinth(solid: Merge, glow: Merge, d: FestivalCar, k: number): void {
    const g = new THREE.CylinderGeometry(DISPLAY_PLINTH.r, DISPLAY_PLINTH.r + 0.12, DISPLAY_PLINTH.h + 0.4, 48, 1);
    g.translate(d.x, d.y + (DISPLAY_PLINTH.h - 0.4) / 2, d.z);
    mergeInto(solid, g, C(0x15171b));
    const ring = new THREE.TorusGeometry(DISPLAY_PLINTH.r + 0.13, 0.05, 6, 64).rotateX(Math.PI / 2);
    ring.translate(d.x, d.y + 0.08, d.z);
    mergeInto(glow, ring, C(k % 2 ? 0x14b8a6 : 0xe0337a));
  }

  /** A food truck: body, cab, a hatch with a striped awning towards the court, a lit sign on the roof. */
  private truck(m: Merge, glow: Merge, t: FestivalPiece): void {
    const { x, y, z, fx, fz, w, d, h } = t;
    const rx = -fz;
    const rz = fx;
    const at = (a: number, up: number, f: number): [number, number, number] => [x + rx * a + fx * f, y + up, z + rz * a + fz * f];
    const body = C(t.color);
    m.box(...at(-0.6, 0.55 + (h - 0.55) / 2, 0), w - 1.6, h - 0.55, d, fx, fz, body);
    m.box(...at(w / 2 - 0.7, 0.55 + 0.95, 0), 1.4, 1.9, d * 0.96, fx, fz, C(0xf1efe8));
    m.box(...at(w / 2 - 0.15, 1.9, 0), 0.06, 0.7, d * 0.8, fx, fz, C(0x1a2128));
    // The hatch: a dark opening with a counter.
    m.box(...at(-0.8, 1.95, d / 2 + 0.02), w - 3.2, 1.1, 0.05, fx, fz, C(0x15120f));
    m.box(...at(-0.8, 1.38, d / 2 + 0.22), w - 3.0, 0.08, 0.42, fx, fz, C(0xb8b2a6));
    // Awning: alternating stripes, hinged above the hatch and tilted out.
    const stripes = 8;
    for (let i = 0; i < stripes; i++) {
      const a = -0.8 - (w - 3.2) / 2 + ((i + 0.5) / stripes) * (w - 3.2);
      const col = i % 2 ? C(0xf5f0e6) : body;
      const p0 = at(a - (w - 3.2) / stripes / 2, 2.7, d / 2);
      const p1 = at(a + (w - 3.2) / stripes / 2, 2.7, d / 2);
      const p2 = at(a + (w - 3.2) / stripes / 2, 2.45, d / 2 + 1.3);
      const p3 = at(a - (w - 3.2) / stripes / 2, 2.45, d / 2 + 1.3);
      m.quad([p0, p1, p2, p3], [fx * 0.2, 0.98, fz * 0.2], col);
      m.quad([p3, p2, p1, p0], [-fx * 0.2, -0.98, -fz * 0.2], col);
    }
    for (const a of [-w / 2 + 1.2, w / 2 - 1.1]) {
      for (const side of [-1, 1]) {
        const g = new THREE.CylinderGeometry(0.42, 0.42, 0.28, 14).rotateX(Math.PI / 2).rotateY(Math.atan2(fx, fz));
        const [px, py, pz] = at(a, 0.42, side * (d / 2 - 0.1));
        g.translate(px, py, pz);
        mergeInto(m, g, C(0x121214));
      }
    }
    // The lit sign on the roof.
    glow.box(...at(-0.6, h + 0.35, 0), w - 2.4, 0.6, 0.16, fx, fz, C(0xfff1d6));
  }

  /** A truss tower with a bar of lamps aimed at the stage. */
  private tower(m: Merge, glow: Merge, t: FestivalPiece): void {
    const { x, y, z, fx, fz, h } = t;
    const rx = -fz;
    const rz = fx;
    const grey = C(0x2b2e33);
    for (const [a, b] of [
      [-0.45, -0.45],
      [0.45, -0.45],
      [0.45, 0.45],
      [-0.45, 0.45],
    ]) {
      m.box(x + rx * a + fx * b, y + h / 2, z + rz * a + fz * b, 0.08, h, 0.08, fx, fz, grey);
    }
    for (let k = 1; k < h / 1.5; k++) m.box(x, y + k * 1.5, z, 0.96, 0.06, 0.96, fx, fz, grey);
    m.box(x, y + 0.15, z, 1.6, 0.3, 1.6, fx, fz, C(0x1a1c20));
    // The lamp bar, facing the stage (the tower's facing).
    m.box(x + fx * 0.6, y + h - 0.4, z + fz * 0.6, 2.4, 0.16, 0.16, fx, fz, grey);
    for (let i = -1.5; i <= 1.5; i++) {
      const lx = x + rx * i * 0.6 + fx * 0.8;
      const lz = z + rz * i * 0.6 + fz * 0.8;
      m.box(lx, y + h - 0.7, lz, 0.4, 0.4, 0.5, fx, fz, C(0x111214));
      glow.box(lx + fx * 0.26, y + h - 0.7, lz + fz * 0.26, 0.3, 0.3, 0.02, fx, fz, C(0xfff4e0));
    }
    // A speaker stack under the lamps.
    m.box(x + fx * 0.75, y + h - 3.2, z + fz * 0.75, 1.1, 2.2, 0.8, fx, fz, C(0x0e0f11));
  }

  /** Steel crowd barriers: a panel every 2.3 m with rails and feet. */
  private barrier(m: Merge, pts: { x: number; z: number; y: number }[]): void {
    const steel = C(0x9aa0a6);
    for (let i = 0; i + 1 < pts.length; i++) {
      const p = pts[i];
      const q = pts[i + 1];
      const len = Math.hypot(q.x - p.x, q.z - p.z);
      const n = Math.max(1, Math.round(len / 2.3));
      const fx = (q.x - p.x) / len;
      const fz = (q.z - p.z) / len;
      // Panels run along the segment: the box's "facing" is across it.
      const ax = fz;
      const az = -fx;
      for (let k = 0; k < n; k++) {
        const t = (k + 0.5) / n;
        const cx = p.x + (q.x - p.x) * t;
        const cz = p.z + (q.z - p.z) * t;
        const cy = p.y + (q.y - p.y) * t;
        const pw = len / n - 0.08;
        m.box(cx, cy + BARRIER_HEIGHT - 0.03, cz, pw, 0.05, 0.05, ax, az, steel);
        m.box(cx, cy + 0.25, cz, pw, 0.05, 0.05, ax, az, steel);
        for (let b = 1; b < 8; b++) {
          const s = (b / 8 - 0.5) * pw;
          m.box(cx + fx * s, cy + 0.67, cz + fz * s, 0.025, 0.85, 0.025, ax, az, steel);
        }
        m.box(cx - fx * pw * 0.45, cy + 0.03, cz - fz * pw * 0.45, 0.12, 0.06, 0.7, ax, az, C(0x6d7278));
      }
    }
  }

  /** The car park's gravel and its white bays. */
  private carPark(m: Merge, f: FestivalLayout): void {
    const p = f.carPark;
    const gravel = new THREE.PlaneGeometry(p.w, p.d, Math.ceil(p.w / 4), Math.ceil(p.d / 4)).rotateX(-Math.PI / 2).rotateY(Math.atan2(p.fx, p.fz));
    gravel.translate(p.x, p.y + 0.05, p.z);
    mergeInto(m, gravel, C(0x7d776c));
    for (const c of f.parked) {
      for (const side of [-1, 1]) {
        const lx = c.x + -c.fz * side * 1.65;
        const lz = c.z + c.fx * side * 1.65;
        m.box(lx, c.y + 0.07, lz, 0.1, 0.02, 4.8, c.fx, c.fz, C(0xe9e6de));
      }
    }
  }

  /** People: legs, a shirt and a head, instanced, bouncing to the music. */
  private crowd(c: FestivalCrowd, share: number): void {
    const count = Math.round(c.count * share);
    if (count <= 0) return;
    const rng = new RNG(`crowd:${c.x.toFixed(1)}:${c.z.toFixed(1)}`);
    // Rounded, low-poly people: two legs, a torso with arms at its sides, a head.
    const legs = mergeGeometries([new THREE.CylinderGeometry(0.08, 0.065, 0.88, 7).translate(-0.095, 0.44, 0), new THREE.CylinderGeometry(0.08, 0.065, 0.88, 7).translate(0.095, 0.44, 0)]);
    const torso = mergeGeometries([
      new THREE.CapsuleGeometry(0.17, 0.36, 3, 9).scale(1.22, 1, 0.72).translate(0, 1.17, 0),
      new THREE.CapsuleGeometry(0.052, 0.46, 2, 6).rotateZ(0.08).translate(-0.27, 1.1, 0),
      new THREE.CapsuleGeometry(0.052, 0.46, 2, 6).rotateZ(-0.08).translate(0.27, 1.1, 0),
    ]);
    const head = mergeGeometries([new THREE.SphereGeometry(0.105, 10, 8).scale(0.92, 1.08, 1).translate(0, 1.64, 0), new THREE.CylinderGeometry(0.05, 0.06, 0.1, 6).translate(0, 1.5, 0)]);
    const mat = (key: string): THREE.MeshStandardMaterial => {
      const m = new THREE.MeshStandardMaterial({ roughness: 0.85 });
      m.onBeforeCompile = (shader) => {
        shader.uniforms.uTime = atmoUniforms.uTime;
        shader.vertexShader = shader.vertexShader.replace('#include <common>', '#include <common>\nuniform float uTime;').replace(
          '#include <begin_vertex>',
          `#include <begin_vertex>
          {
            vec2 ip = vec2(instanceMatrix[3][0], instanceMatrix[3][2]);
            float ph = fract(sin(dot(ip, vec2(12.9898, 78.233))) * 43758.5453);
            float beat = uTime * 6.6 * (0.94 + 0.12 * ph) + ph * 6.2832;
            // Half the crowd jumps on the beat; everyone sways.
            transformed.y += max(0.0, sin(beat)) * 0.14 * step(0.5, ph);
            transformed.x += sin(beat * 0.5) * 0.035 * position.y;
          }`,
        );
      };
      m.customProgramCacheKey = () => `crowd-${key}`;
      return patchMaterial(m, { key: `crowd-${key}` });
    };
    const parts = [
      { g: legs, palette: [0x1c2433, 0x23232a, 0x3a3f4a, 0x8a7a5c, 0x2b3a55], key: 'legs' },
      { g: torso, palette: [0xe0337a, 0x14b8a6, 0xffc23d, 0xf5f0e6, 0x3b82f6, 0xff6b2c, 0x1b1d22, 0x7c4dff, 0xd9d9d9, 0x2e7d32], key: 'torso' },
      { g: head, palette: [0xf1c9a5, 0xd8a77f, 0xa8754f, 0x7a4e2d, 0x4b2e1c, 0xe8b996], key: 'head' },
    ];
    const meshes = parts.map((p) => {
      const m = new THREE.InstancedMesh(p.g, mat(p.key), count);
      m.castShadow = true;
      m.receiveShadow = true;
      return m;
    });
    const d = new THREE.Object3D();
    const rx = -c.fz;
    const rz = c.fx;
    const base = Math.atan2(c.fx, c.fz);
    let placed = 0;
    for (let tries = 0; placed < count && tries < count * 8; tries++) {
      const a = rng.range(-0.5, 0.5) * c.w;
      const b = rng.range(-0.5, 0.5) * c.d;
      // Denser towards what they watch (the far side along the facing).
      if (rng.next() > 0.45 + 0.55 * (b / c.d + 0.5)) continue;
      d.position.set(c.x + rx * a + c.fx * b, c.y, c.z + rz * a + c.fz * b);
      d.rotation.set(0, base + rng.range(-0.5, 0.5), 0);
      const s = rng.range(0.9, 1.08);
      d.scale.set(s, s, s);
      d.updateMatrix();
      meshes.forEach((m, k) => {
        m.setMatrixAt(placed, d.matrix);
        m.setColorAt(placed, C(rng.pick(parts[k].palette)));
      });
      placed++;
    }
    for (const m of meshes) {
      m.count = placed;
      m.computeBoundingSphere();
      this.group.add(m);
    }
  }

  /** A soft shadow on the ground under each parked car. */
  private blobs(cars: FestivalCar[]): void {
    const c = document.createElement('canvas');
    c.width = c.height = 64;
    const g = c.getContext('2d') as CanvasRenderingContext2D;
    const grad = g.createRadialGradient(32, 32, 6, 32, 32, 31);
    grad.addColorStop(0, 'rgba(255,255,255,1)');
    grad.addColorStop(0.6, 'rgba(255,255,255,0.6)');
    grad.addColorStop(1, 'rgba(255,255,255,0)');
    g.fillStyle = grad;
    g.fillRect(0, 0, 64, 64);
    const parts: THREE.BufferGeometry[] = [];
    for (const car of cars) {
      const q = new THREE.PlaneGeometry(2.6, 5.4).rotateX(-Math.PI / 2).rotateY(Math.atan2(car.fx, car.fz));
      q.translate(car.x, car.y + 0.08, car.z);
      parts.push(q);
    }
    const geo = mergeUv(parts);
    const mat = new THREE.MeshBasicMaterial({ map: new THREE.CanvasTexture(c), transparent: true, depthWrite: false, color: 0x000000, opacity: 0.6, polygonOffset: true, polygonOffsetFactor: -4, polygonOffsetUnits: -8 });
    const m = new THREE.Mesh(geo, mat);
    m.renderOrder = 1;
    this.group.add(m);
  }

  /** The standing cars, baked by finish into four meshes once their models have loaded. */
  private async bakeCars(cars: { car: FestivalCar; lift: number }[], shadows: boolean): Promise<void> {
    const ids = [...new Set(cars.map((c) => c.car.specId))];
    const models = new Map<string, THREE.Object3D | null>();
    await Promise.all(ids.map(async (id) => models.set(id, await loadCarModel(id))));
    const out: Record<Finish, Baked> = { coat: new Baked(), metal: new Baked(), matte: new Baked(), glass: new Baked() };
    const place = new THREE.Matrix4();
    const q = new THREE.Quaternion();
    const up = new THREE.Vector3(0, 1, 0);
    const color = new THREE.Color();
    for (const { car, lift } of cars) {
      const src = models.get(car.specId);
      const entry = CAR_MODELS[car.specId];
      if (!src || !entry) continue;
      const paint = car.paint >= 0 ? car.paint : C(entry.style.paint).getHex();
      // The models face +z: turn that to the car's facing.
      q.setFromAxisAngle(up, Math.atan2(car.fx, car.fz));
      place.compose(new THREE.Vector3(car.x, car.y + lift, car.z), q, new THREE.Vector3(1, 1, 1));
      src.updateMatrixWorld(true);
      src.traverse((o) => {
        const mesh = o as THREE.Mesh;
        if (!mesh.isMesh) return;
        const mats = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
        const [kind, col] = finish(mats[0].name, entry.style, paint);
        color.set(col);
        out[kind].add(mesh.geometry, new THREE.Matrix4().multiplyMatrices(place, mesh.matrixWorld), color);
      });
    }
    const mats: Record<Finish, THREE.Material> = {
      coat: new THREE.MeshPhysicalMaterial({ vertexColors: true, roughness: 0.3, metalness: 0.35, clearcoat: 1, clearcoatRoughness: 0.04 }),
      metal: new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.22, metalness: 1 }),
      matte: new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.75, metalness: 0 }),
      glass: new THREE.MeshPhysicalMaterial({ color: 0x0b1116, roughness: 0.04, metalness: 0, clearcoat: 1, transparent: true, opacity: 0.7, depthWrite: false }),
    };
    for (const kind of Object.keys(out) as Finish[]) {
      if (!out[kind].idx.length) continue;
      const mesh = new THREE.Mesh(out[kind].geometry(), patchMaterial(mats[kind], { key: `fx-car-${kind}` }));
      mesh.castShadow = shadows && kind !== 'glass';
      mesh.receiveShadow = true;
      mesh.name = `festival-cars-${kind}`;
      this.group.add(mesh);
    }
  }
}

function mergeGeometries(list: THREE.BufferGeometry[]): THREE.BufferGeometry {
  const m = new Merge();
  for (const g of list) mergeInto(m, g, new THREE.Color(1, 1, 1));
  const out = m.geometry();
  out.deleteAttribute('color');
  return out;
}

/** Merge planes keeping their UVs (for the blob shadows). */
function mergeUv(list: THREE.BufferGeometry[]): THREE.BufferGeometry {
  const pos: number[] = [];
  const uv: number[] = [];
  const idx: number[] = [];
  for (const g of list) {
    const base = pos.length / 3;
    const p = g.getAttribute('position');
    const t = g.getAttribute('uv');
    for (let i = 0; i < p.count; i++) {
      pos.push(p.getX(i), p.getY(i), p.getZ(i));
      uv.push(t.getX(i), t.getY(i));
    }
    const ix = g.getIndex();
    if (ix) for (let i = 0; i < ix.count; i++) idx.push(base + ix.getX(i));
  }
  const out = new THREE.BufferGeometry();
  out.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  out.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
  out.setIndex(idx);
  out.computeBoundingSphere();
  return out;
}
