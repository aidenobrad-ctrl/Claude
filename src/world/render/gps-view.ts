// The GPS line: a glowing chevron ribbon in the right-hand lane along the
// route, fading in just ahead of the car and out a few hundred metres on.
import * as THREE from 'three';
import type { Route } from '../route';

const VERT = /* glsl */ `
attribute vec2 aGps;
varying vec2 vGps;
void main() {
  vGps = aGps;
  gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
}`;

const FRAG = /* glsl */ `
uniform float uProgress;
uniform float uTime;
uniform float uReach;
varying vec2 vGps;
void main() {
  float s = vGps.x;
  float v = vGps.y;
  float ahead = s - uProgress;
  float fade = smoothstep(4.0, 14.0, ahead) * (1.0 - smoothstep(uReach * 0.7, uReach, ahead));
  if (fade <= 0.001) discard;
  // Chevrons flowing forward.
  float f = fract((s - uTime * 9.0) / 7.0 - abs(v) * 0.22);
  float chev = smoothstep(0.0, 0.08, f) * (1.0 - smoothstep(0.38, 0.5, f));
  float edge = 1.0 - smoothstep(0.75, 1.0, abs(v));
  vec3 col = vec3(0.22, 0.84, 1.0);
  float a = (0.28 + 0.72 * chev) * edge * fade;
  gl_FragColor = vec4(col * (1.2 + 1.6 * chev) * a, a);
}`;

export class GpsView {
  readonly mesh: THREE.Mesh;
  private uniforms = { uProgress: { value: 0 }, uTime: { value: 0 }, uReach: { value: 380 } };

  constructor() {
    const mat = new THREE.ShaderMaterial({
      uniforms: this.uniforms,
      vertexShader: VERT,
      fragmentShader: FRAG,
      transparent: true,
      depthWrite: false,
      side: THREE.DoubleSide,
      blending: THREE.CustomBlending,
      blendSrc: THREE.OneFactor,
      blendDst: THREE.OneMinusSrcAlphaFactor,
      polygonOffset: true,
      polygonOffsetFactor: -4,
      polygonOffsetUnits: -8,
    });
    this.mesh = new THREE.Mesh(new THREE.BufferGeometry(), mat);
    this.mesh.frustumCulled = false;
    this.mesh.renderOrder = 5;
    this.mesh.visible = false;
  }

  setRoute(route: Route | null): void {
    this.mesh.geometry.dispose();
    if (!route || route.points.length < 2) {
      this.mesh.visible = false;
      this.mesh.geometry = new THREE.BufferGeometry();
      return;
    }
    const pts = route.points;
    const n = pts.length;
    const pos = new Float32Array(n * 2 * 3);
    const gps = new Float32Array(n * 2 * 2);
    const idx: number[] = [];
    let s = 0;
    for (let i = 0; i < n; i++) {
      const a = pts[Math.max(0, i - 1)];
      const b = pts[Math.min(n - 1, i + 1)];
      let tx = b.x - a.x;
      let tz = b.z - a.z;
      const l = Math.sqrt(tx * tx + tz * tz) || 1;
      tx /= l;
      tz /= l;
      if (i > 0) s += Math.hypot(pts[i].x - pts[i - 1].x, pts[i].z - pts[i - 1].z);
      // Right of travel is (-tz, tx); ride in the middle of the right-hand lane.
      const lane = pts[i].hw * 0.45;
      const cx = pts[i].x - tz * lane;
      const cz = pts[i].z + tx * lane;
      for (const side of [-1, 1]) {
        const k = i * 2 + (side > 0 ? 1 : 0);
        pos[k * 3] = cx - tz * side * 0.75;
        pos[k * 3 + 1] = pts[i].y + 0.1;
        pos[k * 3 + 2] = cz + tx * side * 0.75;
        gps[k * 2] = s;
        gps[k * 2 + 1] = side;
      }
      if (i + 1 < n) {
        const q = i * 2;
        idx.push(q, q + 2, q + 1, q + 1, q + 2, q + 3);
      }
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    g.setAttribute('aGps', new THREE.BufferAttribute(gps, 2));
    g.setIndex(idx);
    this.mesh.geometry = g;
    this.mesh.visible = true;
  }

  update(progress: number, dt: number): void {
    this.uniforms.uProgress.value = progress;
    this.uniforms.uTime.value += dt;
  }
}
