// Grass blades around the camera. A fixed lattice of clumps wraps
// toroidally with the camera, so every clump stays put in world space and
// gets a stable random look from its world cell. Heights and density come
// from a small ground texture (1 m texels, toroidally addressed) filled from
// the physics ground as the camera moves, so blades sit exactly on the
// terrain and stop at roads, water, rock and the proving ground's asphalt.
import * as THREE from 'three';
import type { Ground, GroundHit } from '../ground';
import { newHit } from '../ground';
import { SURFACE } from '../surfaces';
import { patchMaterial } from '../../engine/render/materials';
import { atmoUniforms } from '../../engine/render/atmosphere';
import { terrainTextures } from './terrain-textures';
import { RNG } from '../../engine/rng';

const RES = 128; // ground texels per side, 1 m each
const BLADES = 11;

const GRASS_PARS = /* glsl */ `
attribute vec2 aGrid;
attribute vec3 aBlade;
uniform vec3 uCamPos;
uniform float uRange;
uniform float uSpacing;
uniform float uFade;
uniform sampler2D tGround;
uniform float uTime;
uniform vec2 uWind;
varying float vBladeT;
varying vec3 vGrassWorld;
uint gHash(uvec2 v) {
  uint h = v.x * 1597334677u ^ v.y * 3812015801u;
  h = (h ^ (h >> 16u)) * 2246822519u;
  h = (h ^ (h >> 13u)) * 3266489917u;
  return h ^ (h >> 16u);
}
float gRand(uvec2 v, uint k) { return float(gHash(v + uvec2(k * 7919u, k * 104729u)) & 0xffffffu) / 16777216.0; }
vec4 groundTexel(ivec2 c) {
  ivec2 t = ivec2(mod(vec2(c), ${RES}.0));
  return texelFetch(tGround, t, 0);
}
vec4 groundAt(vec2 p) {
  vec2 f = p - 0.5;
  vec2 i = floor(f);
  vec2 w = f - i;
  ivec2 c = ivec2(i);
  vec4 a = groundTexel(c);
  vec4 b = groundTexel(c + ivec2(1, 0));
  vec4 d = groundTexel(c + ivec2(0, 1));
  vec4 e = groundTexel(c + ivec2(1, 1));
  return mix(mix(a, b, w.x), mix(d, e, w.x), w.y);
}
`;

const GRASS_NORMAL = /* glsl */ `
vec3 objectNormal = vec3(0.0, 1.0, 0.0);
`;

const GRASS_VERTEX = /* glsl */ `
// The copy of this clump's lattice point nearest the camera.
vec2 cell = aGrid + uRange * floor((uCamPos.xz - aGrid) / uRange + 0.5);
uvec2 q = uvec2(ivec2(floor(cell / uSpacing + 0.5)) + 1000000);
vec2 base = cell + (vec2(gRand(q, 1u), gRand(q, 2u)) - 0.5) * uSpacing * 0.9;
vec4 g = groundAt(base);
float dist = length(base - uCamPos.xz);
// Thin out with distance, then shrink to nothing at the edge.
float keep = g.y * (1.0 - smoothstep(uRange * 0.2, uRange * 0.5, dist) * 0.35);
float edge = 1.0 - smoothstep(uRange * 0.22, uRange * 0.5, dist);
float rot = gRand(q, 3u) * 6.2831853;
float hs = mix(0.65, 1.35, gRand(q, 4u)) * (0.55 + 0.45 * g.y) * edge;
float c = cos(rot);
float s = sin(rot);
vec3 p = position;
p.xz = vec2(c * p.x + s * p.z, -s * p.x + c * p.z);
p.y *= hs;
// Wind: gusts roll across the field; tips move most.
float t = aBlade.x;
float wave = sin(uTime * 1.7 + dot(base, normalize(uWind + vec2(1e-4))) * 0.35 + aBlade.z) * 0.5 + 0.5;
float gustN = sin(uTime * 0.45 + base.x * 0.031 + base.y * 0.027);
vec2 bend = normalize(uWind + vec2(1e-4)) * (0.12 + 0.22 * wave * (0.6 + 0.4 * gustN)) * t * t * hs;
p.xz += bend;
p.y -= dot(bend, bend) * 0.5;
vec3 transformed = vec3(base.x, g.x, base.y) + p;
if (gRand(q, 5u) > keep || hs < 0.02) transformed = uCamPos + vec3(0.0, -1e4, 0.0);
vBladeT = t;
vGrassWorld = transformed;
`;

const GRASS_FRAG_PARS = /* glsl */ `
uniform sampler2D tMacro;
uniform vec3 uCamPos;
uniform vec3 uSunDirG;
uniform vec3 uSunColorG;
varying float vBladeT;
varying vec3 vGrassWorld;
`;

const GRASS_MAP = /* glsl */ `
{
  vec2 wp = vGrassWorld.xz;
  // Same meadow variation as the terrain splat, so blades and ground agree.
  float mA = texture2D(tMacro, wp / 380.0 + 0.13).r;
  float mB = texture2D(tMacro, wp / 95.0 + 0.61).r;
  float dryness = smoothstep(0.42, 0.78, mA * 0.7 + mB * 0.3);
  float lush = smoothstep(0.55, 0.25, mA * 0.6 + mB * 0.4);
  vec3 root = vec3(0.06, 0.08, 0.02);
  vec3 tip = vec3(0.17, 0.21, 0.06);
  vec3 col = mix(root, tip, smoothstep(0.0, 1.0, vBladeT));
  col = mix(col, col * vec3(1.45, 1.2, 0.62), dryness * 0.8);
  col = mix(col, col * vec3(0.78, 0.95, 0.8), lush * 0.6);
  diffuseColor.rgb = col;
}
`;

const GRASS_EMISSIVE = /* glsl */ `
#include <emissivemap_fragment>
{
  // Sunlight through the blades when looking towards the sun.
  vec3 vd = normalize(vGrassWorld - uCamPos);
  float back = pow(max(dot(vd, uSunDirG), 0.0), 5.0);
  totalEmissiveRadiance += diffuseColor.rgb * uSunColorG * back * 0.6 * vBladeT;
}
`;

/** One clump: BLADES tapered, slightly curved blades in a small disc. */
function clumpGeometry(): THREE.BufferGeometry {
  const rng = new RNG('grass');
  const pos: number[] = [];
  const blade: number[] = [];
  const idx: number[] = [];
  for (let b = 0; b < BLADES; b++) {
    const a = rng.range(0, Math.PI * 2);
    const r = Math.sqrt(rng.next()) * 0.46;
    const ox = Math.cos(a) * r;
    const oz = Math.sin(a) * r;
    const h = rng.range(0.22, 0.58);
    const w = rng.range(0.03, 0.065);
    const face = rng.range(0, Math.PI * 2);
    const fx = Math.cos(face);
    const fz = Math.sin(face);
    // Lean outward a little.
    const lean = rng.range(0.05, 0.22);
    const lx = (ox / (r + 1e-3)) * lean;
    const lz = (oz / (r + 1e-3)) * lean;
    const phase = rng.range(0, 6.28);
    const base = pos.length / 3;
    const levels = [0, 0.55, 1];
    for (let k = 0; k < levels.length; k++) {
      const t = levels[k];
      const cx = ox + lx * t * t;
      const cz = oz + lz * t * t;
      const y = h * t;
      if (k < levels.length - 1) {
        const hw = w * (1 - t * 0.7);
        pos.push(cx - fz * hw, y, cz + fx * hw, cx + fz * hw, y, cz - fx * hw);
        blade.push(t, -1, phase, t, 1, phase);
      } else {
        pos.push(cx, y, cz);
        blade.push(t, 0, phase);
      }
    }
    idx.push(base, base + 1, base + 3, base, base + 3, base + 2, base + 2, base + 3, base + 4);
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  g.setAttribute('normal', new THREE.Float32BufferAttribute(new Array(pos.length).fill(0).map((_, i) => (i % 3 === 1 ? 1 : 0)), 3));
  g.setAttribute('aBlade', new THREE.Float32BufferAttribute(blade, 3));
  g.setIndex(idx);
  return g;
}

export class Grass {
  readonly mesh: THREE.Mesh;
  private data: Float32Array<ArrayBuffer>;
  private texture: THREE.DataTexture;
  /** World cell held by each texel (x, z), or a sentinel when empty. */
  private heldX = new Int32Array(RES * RES).fill(-1 << 30);
  private heldZ = new Int32Array(RES * RES).fill(-1 << 30);
  private hit: GroundHit = newHit();
  private uniforms: Record<string, THREE.IUniform>;
  /** Texels refreshed last update (perf counter). */
  updated = 0;
  readonly range: number;

  constructor(
    private ground: Ground,
    density: number,
  ) {
    // Clump spacing from the quality's density; the lattice covers the range.
    const spacing = 0.66 / Math.sqrt(Math.max(0.2, density));
    this.range = Math.min(RES - 16, 70 + 40 * density);
    const n = Math.floor(this.range / spacing);
    const grid = new Float32Array(n * n * 2);
    for (let j = 0; j < n; j++) {
      for (let i = 0; i < n; i++) {
        grid[(j * n + i) * 2] = (i + 0.5) * spacing;
        grid[(j * n + i) * 2 + 1] = (j + 0.5) * spacing;
      }
    }
    const lattice = n * spacing;
    this.data = new Float32Array(RES * RES * 4);
    this.texture = new THREE.DataTexture(this.data, RES, RES, THREE.RGBAFormat, THREE.FloatType);
    this.texture.minFilter = this.texture.magFilter = THREE.NearestFilter;
    this.texture.generateMipmaps = false;
    this.texture.needsUpdate = true;
    const base = clumpGeometry();
    const geo = new THREE.InstancedBufferGeometry();
    for (const name of Object.keys(base.attributes)) geo.setAttribute(name, base.attributes[name]);
    geo.setIndex(base.index);
    geo.setAttribute('aGrid', new THREE.InstancedBufferAttribute(grid, 2));
    geo.instanceCount = n * n;
    geo.boundingSphere = new THREE.Sphere(new THREE.Vector3(), 1e7);
    this.uniforms = {
      uCamPos: { value: new THREE.Vector3() },
      uRange: { value: lattice },
      uSpacing: { value: spacing },
      uFade: { value: 0.72 },
      tGround: { value: this.texture },
      tMacro: { value: terrainTextures().macro },
      uTime: atmoUniforms.uTime,
      uWind: atmoUniforms.uWind,
      uSunDirG: atmoUniforms.uSunDir,
      uSunColorG: atmoUniforms.uSunColor,
    };
    const mat = new THREE.MeshStandardMaterial({ roughness: 0.78, metalness: 0, side: THREE.DoubleSide, envMapIntensity: 0.5 });
    mat.onBeforeCompile = (shader) => {
      Object.assign(shader.uniforms, this.uniforms);
      shader.vertexShader = shader.vertexShader
        .replace('#include <common>', `#include <common>\n${GRASS_PARS}`)
        .replace('#include <beginnormal_vertex>', GRASS_NORMAL)
        .replace('#include <begin_vertex>', GRASS_VERTEX);
      shader.fragmentShader = shader.fragmentShader
        .replace('#include <common>', `#include <common>\n${GRASS_FRAG_PARS}`)
        .replace('#include <map_fragment>', GRASS_MAP)
        .replace('#include <emissivemap_fragment>', GRASS_EMISSIVE)
        .replace('#include <normal_fragment_begin>', 'float faceDirection = 1.0;\nvec3 normal = normalize(vNormal);\nvec3 nonPerturbedNormal = normal;');
    };
    mat.customProgramCacheKey = () => 'grass-v1';
    patchMaterial(mat, { key: 'grass' });
    this.mesh = new THREE.Mesh(geo, mat);
    this.mesh.frustumCulled = false;
    this.mesh.receiveShadow = true;
    this.mesh.matrixAutoUpdate = false;
    this.mesh.name = 'grass';
  }

  /** Refill ground texels that scrolled into range; cheap when the camera is still. */
  update(camera: THREE.Vector3): void {
    (this.uniforms.uCamPos.value as THREE.Vector3).copy(camera);
    const cx = Math.floor(camera.x);
    const cz = Math.floor(camera.z);
    const half = RES >> 1;
    const hit = this.hit;
    let n = 0;
    for (let wz = cz - half; wz < cz + half; wz++) {
      const tz = ((wz % RES) + RES) % RES;
      for (let wx = cx - half; wx < cx + half; wx++) {
        const tx = ((wx % RES) + RES) % RES;
        const k = tz * RES + tx;
        if (this.heldX[k] === wx && this.heldZ[k] === wz) continue;
        this.heldX[k] = wx;
        this.heldZ[k] = wz;
        const x = wx + 0.5;
        const z = wz + 0.5;
        let h = 0;
        let d = 0;
        if (this.ground.sample(x, z, 1e5, hit)) {
          h = hit.y;
          if (hit.water <= 0) {
            if (hit.surface === SURFACE.grass) d = 1;
            else if (hit.surface === SURFACE.dirt) d = 0.22;
          }
        }
        this.data[k * 4] = h;
        this.data[k * 4 + 1] = d;
        n++;
      }
    }
    this.updated = n;
    if (n) this.texture.needsUpdate = true;
  }
}
