// Tire effects: skid marks on the ground and smoke, dust and spray puffs.
import * as THREE from 'three';
import { puffTexture } from './textures';

/** Skid marks: a ring buffer of quads laid where tires slide. */
export class Skidmarks {
  readonly mesh: THREE.Mesh;
  private readonly max: number;
  private next = 0;
  private count = 0;
  private readonly pos: Float32Array;
  private readonly col: Float32Array;
  private readonly last = new Map<number, { x: number; y: number; z: number; lx: number; lz: number; a: number }>();
  private readonly geo: THREE.BufferGeometry;

  constructor(max = 4000) {
    this.max = max;
    this.pos = new Float32Array(max * 4 * 3);
    this.col = new Float32Array(max * 4 * 4);
    const idx = new Uint32Array(max * 6);
    for (let i = 0; i < max; i++) {
      // Counter-clockwise seen from above, so the quads face up.
      idx.set([i * 4, i * 4 + 1, i * 4 + 2, i * 4 + 1, i * 4 + 3, i * 4 + 2], i * 6);
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(this.pos, 3).setUsage(THREE.DynamicDrawUsage));
    g.setAttribute('color', new THREE.BufferAttribute(this.col, 4).setUsage(THREE.DynamicDrawUsage));
    g.setIndex(new THREE.BufferAttribute(idx, 1));
    g.setDrawRange(0, 0);
    this.geo = g;
    const mat = new THREE.MeshBasicMaterial({ color: 0x0b0b0b, vertexColors: true, transparent: true, depthWrite: false, polygonOffset: true, polygonOffsetFactor: -6, polygonOffsetUnits: -12 });
    this.mesh = new THREE.Mesh(g, mat);
    this.mesh.frustumCulled = false;
    this.mesh.renderOrder = 2;
  }

  /** Extend the mark for a tire key (car*4+wheel), or break it when intensity is 0. */
  add(key: number, x: number, y: number, z: number, dirX: number, dirZ: number, width: number, intensity: number, tint = 0): void {
    const prev = this.last.get(key);
    if (intensity <= 0.02) {
      this.last.delete(key);
      return;
    }
    // Lateral half-width vector.
    const lx = -dirZ * width * 0.5;
    const lz = dirX * width * 0.5;
    if (!prev) {
      this.last.set(key, { x, y, z, lx, lz, a: intensity });
      return;
    }
    const dx = x - prev.x;
    const dz = z - prev.z;
    if (dx * dx + dz * dz < 0.09) return;
    if (dx * dx + dz * dz > 25) {
      this.last.set(key, { x, y, z, lx, lz, a: intensity });
      return;
    }
    const i = this.next;
    const p = this.pos;
    const o = i * 12;
    const lift = 0.04;
    p[o] = prev.x - prev.lx;
    p[o + 1] = prev.y + lift;
    p[o + 2] = prev.z - prev.lz;
    p[o + 3] = prev.x + prev.lx;
    p[o + 4] = prev.y + lift;
    p[o + 5] = prev.z + prev.lz;
    p[o + 6] = x - lx;
    p[o + 7] = y + lift;
    p[o + 8] = z - lz;
    p[o + 9] = x + lx;
    p[o + 10] = y + lift;
    p[o + 11] = z + lz;
    const c = this.col;
    const co = i * 16;
    const shade = tint;
    for (let k = 0; k < 4; k++) {
      c[co + k * 4] = shade;
      c[co + k * 4 + 1] = shade * 0.85;
      c[co + k * 4 + 2] = shade * 0.7;
      c[co + k * 4 + 3] = (k < 2 ? prev.a : intensity) * 0.55;
    }
    this.last.set(key, { x, y, z, lx, lz, a: intensity });
    this.next = (this.next + 1) % this.max;
    this.count = Math.min(this.max, this.count + 1);
    const posAttr = this.geo.getAttribute('position') as THREE.BufferAttribute;
    const colAttr = this.geo.getAttribute('color') as THREE.BufferAttribute;
    posAttr.addUpdateRange(o, 12);
    colAttr.addUpdateRange(co, 16);
    posAttr.needsUpdate = true;
    colAttr.needsUpdate = true;
    this.geo.setDrawRange(0, this.count * 6);
  }

  clear(): void {
    this.count = 0;
    this.next = 0;
    this.last.clear();
    this.geo.setDrawRange(0, 0);
  }
}

const vert = /* glsl */ `
attribute float aSize;
attribute float aAlpha;
attribute vec3 aColor;
varying float vAlpha;
varying vec3 vColor;
uniform float uScale;
void main() {
  vAlpha = aAlpha;
  vColor = aColor;
  vec4 mv = modelViewMatrix * vec4(position, 1.0);
  gl_Position = projectionMatrix * mv;
  gl_PointSize = aSize * uScale / max(0.5, -mv.z);
}`;

const frag = /* glsl */ `
uniform sampler2D uMap;
varying float vAlpha;
varying vec3 vColor;
void main() {
  vec4 t = texture2D(uMap, gl_PointCoord);
  gl_FragColor = vec4(vColor, t.a * vAlpha);
  #include <colorspace_fragment>
}`;

/** Billboard puffs (tire smoke, dust, spray) as one point cloud. */
export class Puffs {
  readonly points: THREE.Points;
  private readonly max: number;
  private readonly pos: Float32Array;
  private readonly vel: Float32Array;
  private readonly size: Float32Array;
  private readonly grow: Float32Array;
  private readonly alpha: Float32Array;
  private readonly alpha0: Float32Array;
  private readonly life: Float32Array;
  private readonly age: Float32Array;
  private readonly color: Float32Array;
  private next = 0;
  private readonly geo: THREE.BufferGeometry;
  readonly material: THREE.ShaderMaterial;

  constructor(max = 600) {
    this.max = max;
    this.pos = new Float32Array(max * 3);
    this.vel = new Float32Array(max * 3);
    this.size = new Float32Array(max);
    this.grow = new Float32Array(max);
    this.alpha = new Float32Array(max);
    this.alpha0 = new Float32Array(max);
    this.life = new Float32Array(max);
    this.age = new Float32Array(max).fill(1e9);
    this.color = new Float32Array(max * 3);
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(this.pos, 3).setUsage(THREE.DynamicDrawUsage));
    g.setAttribute('aSize', new THREE.BufferAttribute(this.size, 1).setUsage(THREE.DynamicDrawUsage));
    g.setAttribute('aAlpha', new THREE.BufferAttribute(this.alpha, 1).setUsage(THREE.DynamicDrawUsage));
    g.setAttribute('aColor', new THREE.BufferAttribute(this.color, 3).setUsage(THREE.DynamicDrawUsage));
    this.geo = g;
    this.material = new THREE.ShaderMaterial({
      vertexShader: vert,
      fragmentShader: frag,
      uniforms: { uMap: { value: puffTexture() }, uScale: { value: 400 } },
      transparent: true,
      depthWrite: false,
    });
    this.points = new THREE.Points(g, this.material);
    this.points.frustumCulled = false;
    this.points.renderOrder = 3;
  }

  setViewportHeight(px: number): void {
    this.material.uniforms.uScale.value = px * 0.55;
  }

  emit(x: number, y: number, z: number, vx: number, vy: number, vz: number, size: number, grow: number, life: number, alpha: number, r: number, g: number, b: number): void {
    const i = this.next;
    this.next = (this.next + 1) % this.max;
    this.pos[i * 3] = x;
    this.pos[i * 3 + 1] = y;
    this.pos[i * 3 + 2] = z;
    this.vel[i * 3] = vx;
    this.vel[i * 3 + 1] = vy;
    this.vel[i * 3 + 2] = vz;
    this.size[i] = size;
    this.grow[i] = grow;
    this.life[i] = life;
    this.age[i] = 0;
    this.alpha0[i] = alpha;
    this.alpha[i] = alpha;
    this.color[i * 3] = r;
    this.color[i * 3 + 1] = g;
    this.color[i * 3 + 2] = b;
  }

  update(dt: number): void {
    for (let i = 0; i < this.max; i++) {
      if (this.age[i] >= this.life[i]) {
        this.alpha[i] = 0;
        continue;
      }
      this.age[i] += dt;
      const k = this.age[i] / this.life[i];
      const drag = Math.exp(-dt * 1.8);
      this.vel[i * 3] *= drag;
      this.vel[i * 3 + 1] = this.vel[i * 3 + 1] * drag + dt * 0.6;
      this.vel[i * 3 + 2] *= drag;
      this.pos[i * 3] += this.vel[i * 3] * dt;
      this.pos[i * 3 + 1] += this.vel[i * 3 + 1] * dt;
      this.pos[i * 3 + 2] += this.vel[i * 3 + 2] * dt;
      this.size[i] += this.grow[i] * dt;
      this.alpha[i] = this.alpha0[i] * (1 - k) * Math.min(1, k * 8);
    }
    (this.geo.getAttribute('position') as THREE.BufferAttribute).needsUpdate = true;
    (this.geo.getAttribute('aSize') as THREE.BufferAttribute).needsUpdate = true;
    (this.geo.getAttribute('aAlpha') as THREE.BufferAttribute).needsUpdate = true;
    (this.geo.getAttribute('aColor') as THREE.BufferAttribute).needsUpdate = true;
  }
}
