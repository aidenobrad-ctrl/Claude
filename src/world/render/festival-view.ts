// The Halcyon Festival: stage with an LED wall, the big wheel, striped
// pavilions, the entrance arch with its lit sign, pennants and string
// lights. Static parts are merged by material; the wheel turns.
import * as THREE from 'three';
import type { FestivalLayout, FestivalPiece } from '../festival';
import { FESTIVAL_COLORS } from '../festival';
import { patchMaterial } from '../../engine/render/materials';
import { atmoUniforms } from '../../engine/render/atmosphere';

class Merge {
  pos: number[] = [];
  nrm: number[] = [];
  col: number[] = [];
  idx: number[] = [];

  /** Oriented box: center, size (across, up, along facing), facing (fx, fz), colour. */
  box(cx: number, cy: number, cz: number, sx: number, sy: number, sz: number, fx: number, fz: number, color: THREE.Color): void {
    const rx = -fz;
    const rz = fx;
    const P = (a: number, b: number, c: number): number[] => [cx + rx * a * (sx / 2) + fx * c * (sz / 2), cy + b * (sy / 2), cz + rz * a * (sx / 2) + fz * c * (sz / 2)];
    const faces: [number[], number[][]][] = [
      [[rx, 0, rz], [P(1, -1, -1), P(1, -1, 1), P(1, 1, 1), P(1, 1, -1)]],
      [[-rx, 0, -rz], [P(-1, -1, 1), P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1)]],
      [[0, 1, 0], [P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1)]],
      [[0, -1, 0], [P(-1, -1, 1), P(1, -1, 1), P(1, -1, -1), P(-1, -1, -1)]],
      [[fx, 0, fz], [P(1, -1, 1), P(-1, -1, 1), P(-1, 1, 1), P(1, 1, 1)]],
      [[-fx, 0, -fz], [P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1)]],
    ];
    for (const [n, q] of faces) this.quad(q, n, color);
  }

  /** Quad or triangle, wound to face along n. */
  quad(q: number[][], n: number[], color: THREE.Color): void {
    const base = this.pos.length / 3;
    for (const p of q) {
      this.pos.push(p[0], p[1], p[2]);
      this.nrm.push(n[0], n[1], n[2]);
      this.col.push(color.r, color.g, color.b);
    }
    const u = [q[1][0] - q[0][0], q[1][1] - q[0][1], q[1][2] - q[0][2]];
    const v = [q[2][0] - q[0][0], q[2][1] - q[0][1], q[2][2] - q[0][2]];
    const g = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]];
    const ok = g[0] * n[0] + g[1] * n[1] + g[2] * n[2] >= 0;
    if (q.length === 4) this.idx.push(...(ok ? [base, base + 1, base + 2, base, base + 2, base + 3] : [base, base + 2, base + 1, base, base + 3, base + 2]));
    else this.idx.push(...(ok ? [base, base + 1, base + 2] : [base, base + 2, base + 1]));
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

const C = (hex: number): THREE.Color => new THREE.Color(hex);

function signTexture(): THREE.CanvasTexture {
  const cv = document.createElement('canvas');
  cv.width = 1024;
  cv.height = 160;
  const g = cv.getContext('2d') as CanvasRenderingContext2D;
  const grad = g.createLinearGradient(0, 0, 1024, 0);
  grad.addColorStop(0, '#ff6b2c');
  grad.addColorStop(0.5, '#e0337a');
  grad.addColorStop(1, '#14b8a6');
  g.fillStyle = '#0d1018';
  g.fillRect(0, 0, 1024, 160);
  g.fillStyle = grad;
  g.fillRect(0, 140, 1024, 20);
  g.font = '800 92px system-ui, -apple-system, "Segoe UI", sans-serif';
  g.textAlign = 'center';
  g.textBaseline = 'middle';
  g.fillStyle = '#fff4e6';
  g.fillText('HALCYON FESTIVAL', 512, 72);
  const t = new THREE.CanvasTexture(cv);
  t.colorSpace = THREE.SRGBColorSpace;
  t.anisotropy = 4;
  return t;
}

const SCREEN_FRAG = /* glsl */ `
uniform float uTime;
varying vec2 vUv;
void main() {
  // A slow festival visual: colour bands and a pulsing ring.
  vec2 p = vUv - 0.5;
  p.x *= 2.2;
  float r = length(p);
  float bands = 0.5 + 0.5 * sin(p.x * 9.0 + uTime * 1.6 + sin(p.y * 6.0 + uTime) * 1.5);
  vec3 a = vec3(1.0, 0.42, 0.17);
  vec3 b = vec3(0.08, 0.72, 0.65);
  vec3 c = vec3(0.88, 0.2, 0.48);
  vec3 col = mix(mix(a, b, bands), c, smoothstep(0.2, 0.0, abs(r - 0.25 - 0.08 * sin(uTime * 2.0))));
  // Pixel grid of the LED wall.
  vec2 cell = fract(vUv * vec2(160.0, 72.0));
  float led = smoothstep(0.0, 0.2, cell.x) * smoothstep(1.0, 0.8, cell.x) * smoothstep(0.0, 0.2, cell.y) * smoothstep(1.0, 0.8, cell.y);
  gl_FragColor = vec4(col * (0.35 + 0.65 * led) * 2.2, 1.0);
}
`;

export class FestivalView {
  readonly group = new THREE.Group();
  private wheelRot = new THREE.Group();
  private gondolas: THREE.InstancedMesh;
  private gondolaCount = 18;
  private wheelR: number;
  private screenUniforms = { uTime: atmoUniforms.uTime };
  private dummy = new THREE.Object3D();

  constructor(private f: FestivalLayout) {
    this.group.name = 'festival';
    const solid = new Merge();
    const fabric = new Merge();
    this.buildStage(solid, f.stage);
    this.buildArch(solid, f.arch);
    for (const t of f.tents) this.buildTent(solid, fabric, t);
    for (const p of f.poles) solid.box(p.x, p.y + p.h / 2, p.z, 0.16, p.h, 0.16, p.fx, p.fz, C(0xd9d6cf));
    const metal = patchMaterial(new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.55, metalness: 0.35 }), { key: 'fest-metal' });
    const cloth = patchMaterial(new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.85, metalness: 0, side: THREE.DoubleSide }), { key: 'fest-cloth' });
    const sm = new THREE.Mesh(solid.geometry(), metal);
    sm.castShadow = sm.receiveShadow = true;
    const fm = new THREE.Mesh(fabric.geometry(), cloth);
    fm.castShadow = fm.receiveShadow = true;
    this.group.add(sm, fm);
    this.addScreen(f.stage);
    this.addSign(f.arch);
    this.wheelR = f.wheel.h * 0.45;
    this.gondolas = this.buildWheel(f.wheel, metal);
    this.addPennants(f);
    this.addStringLights(f);
  }

  private buildStage(m: Merge, s: FestivalPiece): void {
    const dark = C(0x1d2027);
    const truss = C(0x8c8f96);
    // The front faces (fx, fz): the deck, back wall, truss towers and roof.
    m.box(s.x, s.y + 0.75, s.z, s.w, 1.5, s.d, s.fx, s.fz, dark);
    const bx = s.x - s.fx * (s.d / 2 - 0.6);
    const bz = s.z - s.fz * (s.d / 2 - 0.6);
    m.box(bx, s.y + s.h / 2, bz, s.w, s.h, 1.2, s.fx, s.fz, dark);
    const rx = -s.fz;
    const rz = s.fx;
    for (const a of [-1, 1]) {
      for (const b of [-1, 1]) {
        const tx = s.x + rx * a * (s.w / 2 + 0.6) + s.fx * b * (s.d / 2);
        const tz = s.z + rz * a * (s.w / 2 + 0.6) + s.fz * b * (s.d / 2);
        m.box(tx, s.y + s.h / 2 + 1, tz, 1.1, s.h + 2, 1.1, s.fx, s.fz, truss);
      }
      // Speaker stacks.
      const kx = s.x + rx * a * (s.w / 2 + 3.5) + s.fx * (s.d / 2 - 2);
      const kz = s.z + rz * a * (s.w / 2 + 3.5) + s.fz * (s.d / 2 - 2);
      m.box(kx, s.y + 3, kz, 2.4, 6, 2.2, s.fx, s.fz, dark);
    }
    // Roof canopy with front and back beams.
    m.box(s.x, s.y + s.h + 2.2, s.z, s.w + 3, 0.7, s.d + 2, s.fx, s.fz, C(0x2a2d34));
    m.box(s.x + s.fx * (s.d / 2), s.y + s.h + 1.5, s.z + s.fz * (s.d / 2), s.w + 3, 1.1, 1.1, s.fx, s.fz, truss);
    // Light towers out front.
    for (const a of [-1, 1]) {
      const lx = s.x + rx * a * (s.w * 0.75) + s.fx * (s.d + 20);
      const lz = s.z + rz * a * (s.w * 0.75) + s.fz * (s.d + 20);
      m.box(lx, s.y + 7, lz, 1.2, 14, 1.2, s.fx, s.fz, truss);
      m.box(lx, s.y + 14.5, lz, 4, 1.2, 1.4, s.fx, s.fz, dark);
    }
  }

  private addScreen(s: FestivalPiece): void {
    const geo = new THREE.PlaneGeometry(s.w * 0.62, s.h * 0.52);
    const mat = new THREE.ShaderMaterial({
      uniforms: this.screenUniforms,
      vertexShader: 'varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }',
      fragmentShader: SCREEN_FRAG,
    });
    const m = new THREE.Mesh(geo, mat);
    const bx = s.x - s.fx * (s.d / 2 - 1.25);
    const bz = s.z - s.fz * (s.d / 2 - 1.25);
    m.position.set(bx, s.y + 1.5 + s.h * 0.4, bz);
    m.lookAt(bx + s.fx, s.y + 1.5 + s.h * 0.4, bz + s.fz);
    this.group.add(m);
  }

  private buildArch(m: Merge, a: FestivalPiece): void {
    const rx = -a.fz;
    const rz = a.fx;
    const white = C(0xece8e1);
    for (const side of [-1, 1]) {
      const px = a.x + rx * side * (a.w / 2);
      const pz = a.z + rz * side * (a.w / 2);
      m.box(px, a.y + a.h / 2, pz, 2, a.h, 2, a.fx, a.fz, white);
    }
    m.box(a.x, a.y + a.h - 1.4, a.z, a.w + 2, 2.8, 2.4, a.fx, a.fz, C(0x15181f));
  }

  private addSign(a: FestivalPiece): void {
    const tex = signTexture();
    const mat = new THREE.MeshBasicMaterial({ map: tex, toneMapped: true });
    mat.color.setScalar(2.2);
    for (const side of [-1, 1]) {
      const geo = new THREE.PlaneGeometry(a.w, a.w * (160 / 1024));
      const m = new THREE.Mesh(geo, mat);
      const ox = a.x + a.fx * side * 1.25;
      const oz = a.z + a.fz * side * 1.25;
      m.position.set(ox, a.y + a.h - 1.4, oz);
      m.lookAt(ox + a.fx * side, a.y + a.h - 1.4, oz + a.fz * side);
      this.group.add(m);
    }
  }

  private buildTent(solid: Merge, cloth: Merge, t: FestivalPiece): void {
    const rx = -t.fz;
    const rz = t.fx;
    const hw = t.w / 2;
    const eave = t.y + t.h * 0.55;
    const peak = t.y + t.h;
    const corner = (a: number, b: number, y: number): number[] => [t.x + rx * a * hw + t.fx * b * hw, y, t.z + rz * a * hw + t.fz * b * hw];
    for (const [a, b] of [
      [-1, -1],
      [1, -1],
      [1, 1],
      [-1, 1],
    ]) {
      const c = corner(a, b, t.y);
      solid.box(c[0], t.y + t.h * 0.275, c[2], 0.14, t.h * 0.55, 0.14, t.fx, t.fz, C(0xdedad2));
    }
    // Striped pyramid roof: each side split into strips of colour and white.
    const col = C(t.color);
    const white = C(0xf4f1ea);
    const top = [t.x, peak, t.z];
    const sides: [number, number, number, number][] = [
      [-1, 1, 1, 1],
      [1, 1, 1, -1],
      [1, -1, -1, -1],
      [-1, -1, -1, 1],
    ];
    const strips = 6;
    for (const [a0, b0, a1, b1] of sides) {
      for (let k = 0; k < strips; k++) {
        const u0 = k / strips;
        const u1 = (k + 1) / strips;
        const p0 = corner(a0 + (a1 - a0) * u0, b0 + (b1 - b0) * u0, eave);
        const p1 = corner(a0 + (a1 - a0) * u1, b0 + (b1 - b0) * u1, eave);
        const ex = (p0[0] + p1[0]) / 2 - t.x;
        const ez = (p0[2] + p1[2]) / 2 - t.z;
        const el = Math.sqrt(ex * ex + ez * ez) || 1;
        const n = [ex / el, 0.9, ez / el];
        cloth.quad([p0, p1, top], n, k % 2 ? white : col);
      }
      // Scalloped valance hanging from the eave.
      const v0 = corner(a0, b0, eave);
      const v1 = corner(a1, b1, eave);
      const ex = (v0[0] + v1[0]) / 2 - t.x;
      const ez = (v0[2] + v1[2]) / 2 - t.z;
      const el = Math.sqrt(ex * ex + ez * ez) || 1;
      cloth.quad([v0, v1, [v1[0], eave - 0.6, v1[2]], [v0[0], eave - 0.6, v0[2]]], [ex / el, 0, ez / el], col);
    }
  }

  private buildWheel(w: FestivalPiece, metal: THREE.Material): THREE.InstancedMesh {
    const R = this.wheelR;
    const hubY = w.y + R + 4;
    const rx = -w.fz;
    const rz = w.fx;
    // Static A-frame legs from the ground up to the axle ends.
    const legC = C(0xe8e4dc);
    const legMat = patchMaterial(new THREE.MeshStandardMaterial({ color: legC, roughness: 0.5, metalness: 0.4 }), { key: 'wheel-leg' });
    for (const side of [-1, 1]) {
      const top = new THREE.Vector3(w.x + w.fx * side * 4, hubY, w.z + w.fz * side * 4);
      for (const lean of [-1, 1]) {
        const foot = new THREE.Vector3(top.x + rx * lean * R * 0.6, w.y, top.z + rz * lean * R * 0.6);
        const dir = top.clone().sub(foot);
        const len = dir.length();
        const leg = new THREE.Mesh(new THREE.BoxGeometry(0.9, len, 0.9), legMat);
        leg.position.copy(foot).addScaledVector(dir, 0.5);
        leg.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir.normalize());
        leg.castShadow = true;
        this.group.add(leg);
      }
    }
    // The rotating rim and spokes, built in the wheel's own plane (x right, y up).
    const rim = new Merge();
    const segs = 48;
    const white = C(0xf2efe8);
    for (const off of [-3.2, 3.2]) {
      for (let k = 0; k < segs; k++) {
        const a0 = (k / segs) * Math.PI * 2;
        const a1 = ((k + 1) / segs) * Math.PI * 2;
        const cx = (Math.cos(a0) + Math.cos(a1)) * 0.5 * R;
        const cy = (Math.sin(a0) + Math.sin(a1)) * 0.5 * R;
        const len = 2 * R * Math.sin(Math.PI / segs) + 0.1;
        const ang = (a0 + a1) / 2 + Math.PI / 2;
        // A box along the tangent in the local plane.
        const g = new THREE.BoxGeometry(len, 0.45, 0.45);
        g.rotateZ(ang);
        g.translate(cx, cy, off);
        mergeInto(rim, g, white);
      }
      for (let k = 0; k < 24; k++) {
        const a = (k / 24) * Math.PI * 2;
        const g = new THREE.BoxGeometry(R, 0.18, 0.18);
        g.translate(R / 2, 0, 0);
        g.rotateZ(a);
        g.translate(0, 0, off);
        mergeInto(rim, g, C(0xc9c5bd));
      }
    }
    const axle = new THREE.BoxGeometry(1.2, 1.2, 8);
    mergeInto(rim, axle, C(0x6b6e75));
    const rimMesh = new THREE.Mesh(rim.geometry(), metal);
    rimMesh.castShadow = true;
    this.wheelRot.add(rimMesh);
    // Rim bulbs (bloom at dusk).
    const bulbGeo = new THREE.SphereGeometry(0.22, 6, 4);
    const bulbMat = new THREE.MeshBasicMaterial({ color: new THREE.Color(1, 0.75, 0.45).multiplyScalar(3) });
    const bulbs = new THREE.InstancedMesh(bulbGeo, bulbMat, segs);
    const d = this.dummy;
    for (let k = 0; k < segs; k++) {
      const a = (k / segs) * Math.PI * 2;
      d.position.set(Math.cos(a) * (R + 0.4), Math.sin(a) * (R + 0.4), 3.4);
      d.updateMatrix();
      bulbs.setMatrixAt(k, d.matrix);
    }
    this.wheelRot.add(bulbs);
    // Place the wheel: its local z axis along the facing direction.
    const holder = new THREE.Group();
    holder.position.set(w.x, hubY, w.z);
    holder.lookAt(w.x + w.fx, hubY, w.z + w.fz);
    holder.add(this.wheelRot);
    this.group.add(holder);
    // Gondolas stay upright: instanced and positioned each frame.
    const gGeo = new THREE.BoxGeometry(2.4, 2.2, 2.2);
    gGeo.translate(0, -1.6, 0);
    const gMat = patchMaterial(new THREE.MeshStandardMaterial({ roughness: 0.5, metalness: 0.2 }), { key: 'gondola' });
    const gondolas = new THREE.InstancedMesh(gGeo, gMat, this.gondolaCount);
    gondolas.castShadow = true;
    for (let k = 0; k < this.gondolaCount; k++) gondolas.setColorAt(k, C(FESTIVAL_COLORS[k % FESTIVAL_COLORS.length]));
    holder.add(gondolas);
    this.update(0);
    return gondolas;
  }

  private addPennants(f: FestivalLayout): void {
    // A triangular pennant at the top of every pole, flapping in the wind.
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.Float32BufferAttribute([0, 0, 0, 0, -1.6, 0, 2.6, -0.8, 0], 3));
    geo.setAttribute('normal', new THREE.Float32BufferAttribute([0, 0, 1, 0, 0, 1, 0, 0, 1], 3));
    const mat = new THREE.MeshStandardMaterial({ side: THREE.DoubleSide, roughness: 0.8 });
    mat.onBeforeCompile = (shader) => {
      shader.uniforms.uTime = atmoUniforms.uTime;
      shader.vertexShader = shader.vertexShader
        .replace('#include <common>', '#include <common>\nuniform float uTime;')
        .replace('#include <begin_vertex>', '#include <begin_vertex>\nfloat fl = position.x / 2.6;\ntransformed.z += sin(uTime * 6.0 + position.x * 1.8 + instanceMatrix[3].x * 0.3) * 0.35 * fl;');
    };
    mat.customProgramCacheKey = () => 'pennant';
    patchMaterial(mat, { key: 'pennant' });
    const m = new THREE.InstancedMesh(geo, mat, f.poles.length);
    const d = this.dummy;
    f.poles.forEach((p, k) => {
      d.position.set(p.x, p.y + p.h, p.z);
      // Fly downwind.
      d.rotation.set(0, Math.atan2(-atmoUniforms.uWind.value.y, atmoUniforms.uWind.value.x), 0);
      d.updateMatrix();
      m.setMatrixAt(k, d.matrix);
      m.setColorAt(k, C(p.color));
    });
    this.group.add(m);
  }

  private addStringLights(f: FestivalLayout): void {
    // Catenaries of warm bulbs between neighbouring poles on each side.
    const spans: [FestivalPiece, FestivalPiece][] = [];
    for (let k = 0; k + 2 < f.poles.length; k++) spans.push([f.poles[k], f.poles[k + 2]]);
    const per = 14;
    const geo = new THREE.SphereGeometry(0.12, 6, 4);
    const mat = new THREE.MeshBasicMaterial({ color: new THREE.Color(1, 0.72, 0.4).multiplyScalar(4) });
    const m = new THREE.InstancedMesh(geo, mat, spans.length * per);
    const d = this.dummy;
    let i = 0;
    for (const [a, b] of spans) {
      for (let k = 0; k < per; k++) {
        const t = (k + 0.5) / per;
        const sag = 1.4 * 4 * t * (1 - t);
        d.position.set(a.x + (b.x - a.x) * t, a.y + a.h - 0.8 + (b.y - a.y) * t - sag, a.z + (b.z - a.z) * t);
        d.updateMatrix();
        m.setMatrixAt(i++, d.matrix);
      }
    }
    this.group.add(m);
  }

  /** Turn the wheel (gondolas stay level). */
  update(dt: number): void {
    this.wheelRot.rotation.z -= dt * 0.06;
    const R = this.wheelR;
    const d = this.dummy;
    for (let k = 0; k < this.gondolaCount; k++) {
      const a = (k / this.gondolaCount) * Math.PI * 2 + this.wheelRot.rotation.z;
      d.position.set(Math.cos(a) * R, Math.sin(a) * R, 0);
      d.rotation.set(0, 0, 0);
      d.updateMatrix();
      this.gondolas?.setMatrixAt(k, d.matrix);
    }
    if (this.gondolas) this.gondolas.instanceMatrix.needsUpdate = true;
  }
}

/** Append a BufferGeometry (non-indexed or indexed) to a Merge with one colour. */
function mergeInto(m: Merge, g: THREE.BufferGeometry, color: THREE.Color): void {
  const pos = g.getAttribute('position');
  const nrm = g.getAttribute('normal');
  const base = m.pos.length / 3;
  for (let i = 0; i < pos.count; i++) {
    m.pos.push(pos.getX(i), pos.getY(i), pos.getZ(i));
    m.nrm.push(nrm.getX(i), nrm.getY(i), nrm.getZ(i));
    m.col.push(color.r, color.g, color.b);
  }
  const idx = g.getIndex();
  if (idx) for (let i = 0; i < idx.count; i++) m.idx.push(base + idx.getX(i));
  else for (let i = 0; i < pos.count; i++) m.idx.push(base + i);
}
