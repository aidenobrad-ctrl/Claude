// Trees and bushes around the camera. Placement comes from the world (the
// same trees the physics collides with), streamed in 256 m cells from the
// tile worker. Near trees are full models, instanced per kind, with wind and
// shadows; beyond `near` every tree is a baked camera-facing impostor, all
// kinds in one instanced draw. The vertex shaders cull by distance per
// instance, so the CPU only concatenates whole cells.
import * as THREE from 'three';
import { TREE_KINDS, TREE_STRIDE } from '../world';
import { VEG_CELL } from '../terrain-data';
import type { TileSource } from './tile-source';
import { patchMaterial } from '../../engine/render/materials';
import { atmoUniforms } from '../../engine/render/atmosphere';
import { buildTreeGeometries, drawAtlas, TREE_SIZE } from './tree-models';

export interface VegetationOptions {
  /** Full models out to this distance, m. */
  near: number;
  /** Impostors out to this distance, m. */
  far: number;
  /** Beyond this distance cells load thinned (a subset of their trees). */
  thinFrom: number;
  /** Fraction kept in thinned cells. */
  thinKeep: number;
  shadows: boolean;
}

interface Cell {
  key: number;
  cx: number;
  cz: number;
  keep: number;
  /** Per kind: instance data (x, y, z, scale) and (yaw, phase). */
  inst: Float32Array[];
  inst2: Float32Array[];
  /** All kinds together for impostors: (x, y, z, scale) and (kind, phase). */
  all: Float32Array;
  all2: Float32Array;
  loading: boolean;
}

const shared = {
  uCamPos: { value: new THREE.Vector3() },
  uNear: { value: 150 },
  uFar: { value: 1500 },
};

const INSTANCE_PARS = /* glsl */ `
attribute vec4 aInst;
attribute vec2 aInst2;
attribute vec3 aWind;
uniform vec3 uCamPos;
uniform float uNear;
uniform float uFar;
uniform float uTime;
uniform vec2 uWind;
varying float vAO;
varying vec3 vTreeWorld;
varying vec3 vTint;
`;

// Rotation by instance yaw (shared by normals and positions).
const INSTANCE_NORMAL = /* glsl */ `
float iy = aInst2.x;
float ic = cos(iy);
float is = sin(iy);
vec3 objectNormal = vec3(ic * normal.x + is * normal.z, normal.y, -is * normal.x + ic * normal.z);
#ifdef USE_TANGENT
  vec3 objectTangent = vec3( tangent.xyz );
#endif
`;

const INSTANCE_VERTEX = /* glsl */ `
vec3 transformed = vec3(ic * position.x + is * position.z, position.y, -is * position.x + ic * position.z) * aInst.w + aInst.xyz;
// Wind: the whole tree sways (more towards the top), leaves flutter.
float ph = aInst2.y;
float gust = 0.6 + 0.4 * sin(uTime * 0.31 + aInst.x * 0.004 + aInst.z * 0.003);
float sway = (sin(uTime * 1.25 + ph) * 0.6 + sin(uTime * 2.3 + ph * 1.7) * 0.25) * gust;
vec2 wd = normalize(uWind + vec2(1e-4));
transformed.xz += wd * sway * aWind.x * 0.32 * aInst.w;
transformed += objectNormal * aWind.y * 0.045 * sin(uTime * 7.0 + dot(position, vec3(2.1, 1.7, 2.9)) + ph);
vAO = aWind.z;
vTreeWorld = transformed;
// Per-tree colour: some yellower, some bluer, some darker.
float tr = fract(ph * 7.13);
vTint = mix(vec3(0.82, 0.92, 0.86), vec3(1.16, 1.08, 0.82), tr) * (0.85 + 0.3 * fract(ph * 3.71));
// Distance culling per instance (collapse the whole tree).
float dCam = distance(aInst.xz, uCamPos.xz);
if (dCam >= uNear || dCam > uFar) transformed = uCamPos + vec3(0.0, -1e4, 0.0);
`;

const FOLIAGE_FRAG_PARS = /* glsl */ `
varying float vAO;
varying vec3 vTint;
varying vec3 vTreeWorld;
uniform vec3 uSunDirT;
uniform vec3 uSunColorT;
uniform vec3 uCamPos;
`;

// Double-sided cards keep their spherical normal on both faces.
const NORMAL_BEGIN = /* glsl */ `
float faceDirection = gl_FrontFacing ? 1.0 : -1.0;
vec3 normal = normalize(vNormal);
vec3 nonPerturbedNormal = normal;
`;

// Light through the leaves when looking towards the sun.
const TRANSLUCENCY = /* glsl */ `
#include <emissivemap_fragment>
{
  vec3 vd = normalize(vTreeWorld - uCamPos);
  float back = pow(max(dot(vd, uSunDirT), 0.0), 4.0);
  totalEmissiveRadiance += diffuseColor.rgb * uSunColorT * back * 0.45 * vAO;
}
`;

const IMPOSTOR_PARS = /* glsl */ `
attribute vec4 aInst;
attribute vec2 aInst2;
uniform vec3 uCamPos;
uniform float uNear;
uniform float uFar;
uniform vec4 uKindUV[${TREE_KINDS}];
uniform vec2 uKindSize[${TREE_KINDS}];
varying vec2 vImpUv;
varying float vImpFade;
varying vec3 vTint;
`;

const IMPOSTOR_NORMAL = /* glsl */ `
int kind = int(aInst2.x + 0.5);
vec3 toCam = uCamPos - aInst.xyz;
toCam.y = 0.0;
toCam = normalize(toCam + vec3(1e-4, 0.0, 0.0));
vec3 right = vec3(toCam.z, 0.0, -toCam.x);
// A rounded normal across the billboard reads like a lit crown.
vec3 objectNormal = normalize(toCam * 0.75 + right * position.x * 1.1 + vec3(0.0, 0.55 + 0.4 * position.y, 0.0));
`;

const IMPOSTOR_VERTEX = /* glsl */ `
vec2 ks = uKindSize[kind] * aInst.w;
vec3 transformed = aInst.xyz + right * position.x * ks.x + vec3(0.0, position.y * ks.y - 0.4 * aInst.w, 0.0);
vec4 kuv = uKindUV[kind];
vImpUv = vec2(mix(kuv.x, kuv.z, position.x + 0.5), mix(kuv.y, kuv.w, position.y));
float dCam = distance(aInst.xz, uCamPos.xz);
vImpFade = 1.0 - smoothstep(uFar * 0.85, uFar, dCam);
float tr = fract(aInst2.y * 7.13);
vTint = mix(vec3(0.82, 0.92, 0.86), vec3(1.16, 1.08, 0.82), tr) * (0.85 + 0.3 * fract(aInst2.y * 3.71));
if (dCam < uNear || dCam > uFar) transformed = uCamPos + vec3(0.0, -1e4, 0.0);
`;

const IMPOSTOR_MAP = /* glsl */ `
vec4 imp = texture2D(map, vImpUv);
// Sharpen alpha so distant trees keep their body through the mip chain.
diffuseColor.rgb *= imp.rgb * vTint;
diffuseColor.a = smoothstep(0.18, 0.5, imp.a) * vImpFade;
`;

class Grow {
  data: Float32Array;
  n = 0;
  constructor(
    private stride: number,
    cap = 1024,
  ) {
    this.data = new Float32Array(cap * stride);
  }
  reset(): void {
    this.n = 0;
  }
  append(src: Float32Array): void {
    const need = (this.n * this.stride + src.length) / this.stride;
    if (need * this.stride > this.data.length) {
      const next = new Float32Array(Math.max(need, (this.data.length / this.stride) * 2) * this.stride);
      next.set(this.data.subarray(0, this.n * this.stride));
      this.data = next;
    }
    this.data.set(src, this.n * this.stride);
    this.n += src.length / this.stride;
  }
}

class InstanceSet {
  readonly geometry: THREE.InstancedBufferGeometry;
  private a = new Grow(4);
  private b = new Grow(2);
  private attrA: THREE.InstancedBufferAttribute;
  private attrB: THREE.InstancedBufferAttribute;

  constructor(base: THREE.BufferGeometry) {
    const g = new THREE.InstancedBufferGeometry();
    for (const name of Object.keys(base.attributes)) g.setAttribute(name, base.attributes[name]);
    g.setIndex(base.index);
    this.attrA = new THREE.InstancedBufferAttribute(this.a.data, 4);
    this.attrB = new THREE.InstancedBufferAttribute(this.b.data, 2);
    this.attrA.setUsage(THREE.DynamicDrawUsage);
    this.attrB.setUsage(THREE.DynamicDrawUsage);
    g.setAttribute('aInst', this.attrA);
    g.setAttribute('aInst2', this.attrB);
    g.instanceCount = 0;
    // Instances span the whole streamed area; culling happens per instance.
    g.boundingSphere = new THREE.Sphere(new THREE.Vector3(), 1e7);
    this.geometry = g;
  }

  begin(): void {
    this.a.reset();
    this.b.reset();
  }

  add(a: Float32Array, b: Float32Array): void {
    if (!a.length) return;
    this.a.append(a);
    this.b.append(b);
  }

  commit(): void {
    if (this.attrA.array !== this.a.data) {
      // Grown: replace the attributes (three re-uploads new arrays).
      this.attrA = new THREE.InstancedBufferAttribute(this.a.data, 4);
      this.attrB = new THREE.InstancedBufferAttribute(this.b.data, 2);
      this.attrA.setUsage(THREE.DynamicDrawUsage);
      this.attrB.setUsage(THREE.DynamicDrawUsage);
      this.geometry.setAttribute('aInst', this.attrA);
      this.geometry.setAttribute('aInst2', this.attrB);
      // three caches the instance limit from the first attribute it saw.
      (this.geometry as unknown as { _maxInstanceCount?: number })._maxInstanceCount = undefined;
    } else {
      this.attrA.clearUpdateRanges();
      this.attrB.clearUpdateRanges();
      this.attrA.addUpdateRange(0, this.a.n * 4);
      this.attrB.addUpdateRange(0, this.b.n * 2);
      this.attrA.needsUpdate = true;
      this.attrB.needsUpdate = true;
    }
    this.geometry.instanceCount = this.a.n;
  }

  get count(): number {
    return this.a.n;
  }
}

export class Vegetation {
  readonly group = new THREE.Group();
  private cells = new Map<number, Cell>();
  private nearSets: InstanceSet[] = [];
  private farSet: InstanceSet;
  private lastBuild = new THREE.Vector3(1e9, 0, 1e9);
  private dirty = true;
  private atlas: THREE.Texture;
  /** Instances drawn after the last rebuild (near, far). */
  nearCount = 0;
  farCount = 0;

  constructor(
    private renderer: THREE.WebGLRenderer,
    private source: TileSource,
    private opts: VegetationOptions,
  ) {
    this.group.name = 'vegetation';
    shared.uNear.value = opts.near;
    shared.uFar.value = opts.far;
    this.atlas = drawAtlas();
    const geos = buildTreeGeometries();
    const sunDir = atmoUniforms.uSunDir;
    const sunColor = atmoUniforms.uSunColor;

    // Near models: one mesh per kind.
    const foliage = new THREE.MeshStandardMaterial({ map: this.atlas, alphaTest: 0.45, side: THREE.DoubleSide, roughness: 0.82, metalness: 0, envMapIntensity: 0.55 });
    foliage.onBeforeCompile = (shader) => {
      Object.assign(shader.uniforms, shared, { uTime: atmoUniforms.uTime, uWind: atmoUniforms.uWind, uSunDirT: sunDir, uSunColorT: sunColor });
      shader.vertexShader = shader.vertexShader
        .replace('#include <common>', `#include <common>\n${INSTANCE_PARS}`)
        .replace('#include <beginnormal_vertex>', INSTANCE_NORMAL)
        .replace('#include <begin_vertex>', INSTANCE_VERTEX);
      shader.fragmentShader = shader.fragmentShader
        .replace('#include <common>', `#include <common>\n${FOLIAGE_FRAG_PARS}`)
        .replace('#include <normal_fragment_begin>', NORMAL_BEGIN)
        .replace('#include <map_fragment>', '#include <map_fragment>\ndiffuseColor.rgb *= vAO * vTint;')
        .replace('#include <emissivemap_fragment>', TRANSLUCENCY);
    };
    foliage.customProgramCacheKey = () => 'tree-near-v1';
    patchMaterial(foliage, { key: 'tree' });
    const depth = new THREE.MeshDepthMaterial({ depthPacking: THREE.RGBADepthPacking, map: this.atlas, alphaTest: 0.45, side: THREE.DoubleSide });
    depth.onBeforeCompile = (shader) => {
      Object.assign(shader.uniforms, shared, { uTime: atmoUniforms.uTime, uWind: atmoUniforms.uWind });
      shader.vertexShader = shader.vertexShader
        .replace('#include <common>', `#include <common>\n${INSTANCE_PARS}`)
        .replace('#include <begin_vertex>', `${INSTANCE_NORMAL.replace('vec3 objectNormal', 'vec3 objectNormalI').replace('#ifdef USE_TANGENT', '#ifdef NEVER_TANGENT')}\nvec3 objectNormal = objectNormalI;\n${INSTANCE_VERTEX}`);
    };
    depth.customProgramCacheKey = () => 'tree-depth-v1';
    for (let k = 0; k < TREE_KINDS; k++) {
      const set = new InstanceSet(geos[k]);
      this.nearSets.push(set);
      const m = new THREE.Mesh(set.geometry, foliage);
      m.castShadow = opts.shadows;
      m.receiveShadow = true;
      m.customDepthMaterial = depth;
      m.frustumCulled = false;
      m.matrixAutoUpdate = false;
      this.group.add(m);
    }

    // Impostors: baked views of each kind, one billboard draw for all.
    const { texture, uv } = this.bakeImpostors(geos);
    const quad = new THREE.PlaneGeometry(1, 1).translate(0, 0.5, 0);
    this.farSet = new InstanceSet(quad);
    const imp = new THREE.MeshStandardMaterial({ map: texture, alphaTest: 0.5, roughness: 0.9, metalness: 0, envMapIntensity: 0.4 });
    const kindUV = uv.map((r) => new THREE.Vector4(r[0], r[1], r[2], r[3]));
    const kindSize = TREE_SIZE.map(([w, h]) => new THREE.Vector2(w * 1.1, h * 1.08));
    imp.onBeforeCompile = (shader) => {
      Object.assign(shader.uniforms, shared, { uKindUV: { value: kindUV }, uKindSize: { value: kindSize } });
      shader.vertexShader = shader.vertexShader
        .replace('#include <common>', `#include <common>\n${IMPOSTOR_PARS}`)
        .replace('#include <beginnormal_vertex>', IMPOSTOR_NORMAL)
        .replace('#include <begin_vertex>', IMPOSTOR_VERTEX);
      shader.fragmentShader = shader.fragmentShader
        .replace('#include <common>', '#include <common>\nvarying vec2 vImpUv;\nvarying float vImpFade;\nvarying vec3 vTint;')
        .replace('#include <map_fragment>', IMPOSTOR_MAP)
        .replace('#include <normal_fragment_begin>', NORMAL_BEGIN);
    };
    imp.customProgramCacheKey = () => 'tree-impostor-v1';
    patchMaterial(imp, { key: 'impostor' });
    const far = new THREE.Mesh(this.farSet.geometry, imp);
    far.frustumCulled = false;
    far.matrixAutoUpdate = false;
    far.receiveShadow = true;
    this.group.add(far);
  }

  /** Render each tree kind from the side into an atlas (albedo times baked occlusion). */
  private bakeImpostors(geos: THREE.BufferGeometry[]): { texture: THREE.Texture; uv: [number, number, number, number][] } {
    const cellW = 256;
    const cellH = 512;
    const rt = new THREE.WebGLRenderTarget(cellW * TREE_KINDS, cellH, { generateMipmaps: true, minFilter: THREE.LinearMipmapLinearFilter, magFilter: THREE.LinearFilter });
    rt.texture.colorSpace = THREE.NoColorSpace;
    const scene = new THREE.Scene();
    const mat = new THREE.MeshBasicMaterial({ map: this.atlas, alphaTest: 0.45, side: THREE.DoubleSide });
    mat.onBeforeCompile = (shader) => {
      shader.vertexShader = shader.vertexShader
        .replace('#include <common>', '#include <common>\nattribute vec3 aWind;\nvarying float vAO;')
        .replace('#include <begin_vertex>', '#include <begin_vertex>\nvAO = aWind.z;');
      shader.fragmentShader = shader.fragmentShader
        .replace('#include <common>', '#include <common>\nvarying float vAO;')
        .replace('#include <map_fragment>', '#include <map_fragment>\ndiffuseColor.rgb *= vAO;');
    };
    const prevTarget = this.renderer.getRenderTarget();
    const prevClear = this.renderer.getClearColor(new THREE.Color());
    const prevAlpha = this.renderer.getClearAlpha();
    const prevAuto = this.renderer.autoClear;
    this.renderer.setRenderTarget(rt);
    this.renderer.setClearColor(0x000000, 0);
    this.renderer.clear();
    this.renderer.autoClear = false;
    const uv: [number, number, number, number][] = [];
    for (let k = 0; k < TREE_KINDS; k++) {
      const [w, h] = TREE_SIZE[k];
      const W = w * 1.1;
      const Hh = h * 1.08;
      const cam = new THREE.OrthographicCamera(-W / 2, W / 2, Hh - 0.4, -0.4, 0.1, 100);
      cam.position.set(0, 0, 40);
      cam.lookAt(0, 0, 0);
      cam.position.y = 0;
      const mesh = new THREE.Mesh(geos[k], mat);
      scene.add(mesh);
      // Render-target viewports are taken from the target on bind.
      rt.viewport.set(k * cellW, 0, cellW, cellH);
      this.renderer.setRenderTarget(rt);
      this.renderer.render(scene, cam);
      scene.remove(mesh);
      uv.push([k / TREE_KINDS, 0, (k + 1) / TREE_KINDS, 1]);
    }
    this.renderer.setRenderTarget(prevTarget);
    this.renderer.setClearColor(prevClear, prevAlpha);
    this.renderer.autoClear = prevAuto;
    mat.dispose();
    return { texture: rt.texture, uv };
  }

  /** Stream cells around the camera and refresh the instance buffers. */
  update(camera: THREE.Vector3, sync = false): void {
    shared.uCamPos.value.copy(camera);
    const { far, thinFrom, thinKeep } = this.opts;
    const reach = far + VEG_CELL;
    const c0x = Math.floor((camera.x - reach) / VEG_CELL);
    const c1x = Math.floor((camera.x + reach) / VEG_CELL);
    const c0z = Math.floor((camera.z - reach) / VEG_CELL);
    const c1z = Math.floor((camera.z + reach) / VEG_CELL);
    const want: { cx: number; cz: number; d: number }[] = [];
    for (let cx = c0x; cx <= c1x; cx++) {
      for (let cz = c0z; cz <= c1z; cz++) {
        const d = this.cellDist(cx, cz, camera);
        if (d <= far) want.push({ cx, cz, d });
      }
    }
    want.sort((a, b) => a.d - b.d);
    const t0 = performance.now();
    for (const w of want) {
      const key = w.cx * 4096 + w.cz;
      const keep = w.d > thinFrom ? thinKeep : 1;
      const c = this.cells.get(key);
      if (c && (c.keep >= keep || c.loading)) continue;
      if (!sync && this.source.async && this.source.treesInFlight >= 6 * this.source.workerCount) break;
      // Without workers, a few milliseconds of cells per frame.
      if (!sync && !this.source.async && performance.now() - t0 > 4) break;
      const cell: Cell = c ?? { key, cx: w.cx, cz: w.cz, keep, inst: [], inst2: [], all: new Float32Array(0), all2: new Float32Array(0), loading: true };
      cell.loading = true;
      this.cells.set(key, cell);
      this.source.trees(w.cx, w.cz, keep, (t) => this.onCell(cell, t, keep), sync);
    }
    // Drop cells well outside the range.
    for (const [key, c] of this.cells) {
      if (this.cellDist(c.cx, c.cz, camera) > far + VEG_CELL * 2) {
        this.cells.delete(key);
        this.dirty = true;
      }
    }
    const dx = camera.x - this.lastBuild.x;
    const dz = camera.z - this.lastBuild.z;
    this.sinceBuild++;
    // While cells stream in, rebuild a few times a second rather than per cell.
    if ((this.dirty && (this.sinceBuild >= 6 || sync)) || dx * dx + dz * dz > 48 * 48) this.rebuild(camera);
  }

  private sinceBuild = 0;

  private cellDist(cx: number, cz: number, p: THREE.Vector3): number {
    const x0 = cx * VEG_CELL;
    const z0 = cz * VEG_CELL;
    const dx = Math.max(x0 - p.x, 0, p.x - (x0 + VEG_CELL));
    const dz = Math.max(z0 - p.z, 0, p.z - (z0 + VEG_CELL));
    return Math.sqrt(dx * dx + dz * dz);
  }

  private onCell(cell: Cell, t: Float32Array, keep: number): void {
    if (this.cells.get(cell.key) !== cell) return;
    cell.loading = false;
    cell.keep = keep;
    const n = t.length / TREE_STRIDE;
    const counts = new Array(TREE_KINDS).fill(0);
    for (let k = 0; k < n; k++) counts[t[k * TREE_STRIDE + 5]]++;
    cell.inst = counts.map((c) => new Float32Array(c * 4));
    cell.inst2 = counts.map((c) => new Float32Array(c * 2));
    cell.all = new Float32Array(n * 4);
    cell.all2 = new Float32Array(n * 2);
    const fill = new Array(TREE_KINDS).fill(0);
    for (let k = 0; k < n; k++) {
      const o = k * TREE_STRIDE;
      const kind = t[o + 5];
      const f = fill[kind]++;
      const a = cell.inst[kind];
      a[f * 4] = t[o];
      a[f * 4 + 1] = t[o + 1];
      a[f * 4 + 2] = t[o + 2];
      a[f * 4 + 3] = t[o + 3];
      const phase = (t[o] * 0.37 + t[o + 2] * 0.61) % 6.283;
      cell.inst2[kind][f * 2] = t[o + 4];
      cell.inst2[kind][f * 2 + 1] = phase;
      cell.all.set(a.subarray(f * 4, f * 4 + 4), k * 4);
      cell.all2[k * 2] = kind;
      cell.all2[k * 2 + 1] = phase;
    }
    this.dirty = true;
  }

  private rebuild(camera: THREE.Vector3): void {
    this.dirty = false;
    this.sinceBuild = 0;
    this.lastBuild.copy(camera);
    for (const s of this.nearSets) s.begin();
    this.farSet.begin();
    const nearReach = this.opts.near + 64;
    for (const c of this.cells.values()) {
      if (c.loading && !c.all.length) continue;
      const d = this.cellDist(c.cx, c.cz, camera);
      if (d <= nearReach) for (let k = 0; k < TREE_KINDS; k++) this.nearSets[k].add(c.inst[k], c.inst2[k]);
      if (d <= this.opts.far) this.farSet.add(c.all, c.all2);
    }
    this.nearCount = 0;
    for (const s of this.nearSets) {
      s.commit();
      this.nearCount += s.count;
    }
    this.farSet.commit();
    this.farCount = this.farSet.count;
  }

  /** True when every cell within the full-model range has loaded. */
  nearReady(camera: THREE.Vector3): boolean {
    return this.progress(camera, this.opts.near + VEG_CELL) >= 1;
  }

  /** Fraction of cells within `range` (default: impostor range) that have loaded. */
  progress(camera: THREE.Vector3, range = this.opts.far): number {
    const c0x = Math.floor((camera.x - range) / VEG_CELL);
    const c1x = Math.floor((camera.x + range) / VEG_CELL);
    const c0z = Math.floor((camera.z - range) / VEG_CELL);
    const c1z = Math.floor((camera.z + range) / VEG_CELL);
    let n = 0;
    let ok = 0;
    for (let cx = c0x; cx <= c1x; cx++) {
      for (let cz = c0z; cz <= c1z; cz++) {
        if (this.cellDist(cx, cz, camera) > range) continue;
        n++;
        const c = this.cells.get(cx * 4096 + cz);
        if (c && !c.loading) ok++;
      }
    }
    return n ? ok / n : 1;
  }

  get cellCount(): number {
    return this.cells.size;
  }
}
