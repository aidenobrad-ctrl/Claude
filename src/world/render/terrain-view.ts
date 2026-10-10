// Streamed terrain: a quadtree of tiles from 128 m (4 m spacing, matching
// the physics heightfield exactly) up to 2 km, each 32x32 quads with skirts
// that hide cracks between levels. One splat material for every tile.
import * as THREE from 'three';
import { WORLD_HALF, newSample, REGION } from '../island';
import { TILE, TILE_SAMPLES, TILE_SPACING, type World, type Layers } from '../world';
import { terrainTextures } from './terrain-textures';
import { patchMaterial } from '../../engine/render/materials';
import { atmoUniforms } from '../../engine/render/atmosphere';

const QUADS = 32;
const ROOT = 2048;
const LEAF = 128;
const MAX_LEVEL = 4; // 128 * 2^4 = 2048

interface TileMesh {
  key: string;
  mesh: THREE.Mesh;
  level: number;
  used: number;
}

const SPLAT_VERT_PARS = /* glsl */ `
attribute vec4 aSplat;
varying vec4 vSplat;
varying vec3 vTerrWorld;
varying vec3 vTerrNormal;
`;
const SPLAT_VERT = /* glsl */ `
vSplat = aSplat;
vTerrWorld = (modelMatrix * vec4(transformed, 1.0)).xyz;
vTerrNormal = normalize(mat3(modelMatrix) * objectNormal);
`;

const SPLAT_FRAG_PARS = /* glsl */ `
uniform sampler2D tGrass;
uniform sampler2D tRock;
uniform sampler2D tDirt;
uniform sampler2D tSand;
uniform sampler2D tSnow;
uniform sampler2D tDetailN;
uniform sampler2D tMacro;
uniform float uWaterLevel;
uniform float uTime;
varying vec4 vSplat;
varying vec3 vTerrWorld;
varying vec3 vTerrNormal;
float terrRough;
vec3 terrDetailN;
vec4 triplanar(sampler2D t, vec3 p, vec3 n, float scale) {
  vec3 w = pow(abs(n), vec3(4.0));
  w /= (w.x + w.y + w.z);
  return texture2D(t, p.zy * scale) * w.x + texture2D(t, p.xz * scale) * w.y + texture2D(t, p.xy * scale) * w.z;
}
`;

const SPLAT_MAP = /* glsl */ `
{
  vec3 p = vTerrWorld;
  vec3 n = normalize(vTerrNormal);
  float dist = length(p - cameraPosition);
  // Two scales of each layer hide tiling; the far scale takes over with distance.
  vec2 uA = p.xz * 0.16;
  vec2 uB = mat2(0.8, -0.6, 0.6, 0.8) * p.xz * 0.037;
  float far = smoothstep(40.0, 260.0, dist);
  vec4 g = mix(texture2D(tGrass, uA), texture2D(tGrass, uB), 0.35 + 0.4 * far);
  vec4 d = mix(texture2D(tDirt, uA * 0.8), texture2D(tDirt, uB), 0.35 + 0.4 * far);
  vec4 s = mix(texture2D(tSand, uA * 0.6), texture2D(tSand, uB), 0.3 + 0.4 * far);
  vec4 sn = mix(texture2D(tSnow, uA), texture2D(tSnow, uB), 0.4);
  vec4 r = mix(triplanar(tRock, p, n, 0.09), triplanar(tRock, p, n, 0.023), 0.4 + 0.3 * far);
  float wR = vSplat.x;
  float wD = vSplat.y;
  float wS = vSplat.z;
  float wN = vSplat.w;
  float wG = max(0.0, 1.0 - wR - wD - wS - wN);
  // Height-based blending: the taller texel wins where layers meet.
  vec4 hw = vec4(wG * (g.a + 0.3), wR * (r.a + 0.3), wD * (d.a + 0.3), wS * (s.a + 0.3));
  float hn = wN * (sn.a + 0.3);
  float mx = max(max(max(hw.x, hw.y), max(hw.z, hw.w)), hn) - 0.28;
  hw = max(hw - mx, 0.0);
  hn = max(hn - mx, 0.0);
  float sum = hw.x + hw.y + hw.z + hw.w + hn + 1e-4;
  vec3 col = (g.rgb * hw.x + r.rgb * hw.y + d.rgb * hw.z + s.rgb * hw.w + sn.rgb * hn) / sum;
  terrRough = (0.96 * hw.x + 0.86 * hw.y + 0.94 * hw.z + 0.9 * hw.w + 0.55 * hn) / sum;
  // Large-scale variation breaks up repetition across the island.
  float macro = texture2D(tMacro, p.xz / 640.0).r;
  float macro2 = texture2D(tMacro, p.xz / 2300.0 + 0.37).r;
  col *= 0.78 + 0.42 * macro * (0.6 + 0.8 * macro2);
  col *= vColor.rgb;
  // Shorelines: wet, darker and glossier just above the water.
  float wet = smoothstep(uWaterLevel + 1.4, uWaterLevel + 0.15, p.y) * step(uWaterLevel - 6.0, p.y);
  col *= 1.0 - 0.38 * wet;
  terrRough = mix(terrRough, 0.25, wet);
  // Foam lapping on the beach.
  float foam = smoothstep(uWaterLevel + 0.35, uWaterLevel + 0.05, p.y) * smoothstep(uWaterLevel - 0.25, uWaterLevel + 0.05, p.y);
  foam *= 0.5 + 0.5 * sin(uTime * 1.3 + p.x * 0.05 + p.z * 0.04);
  col = mix(col, vec3(0.92, 0.95, 0.97), foam * 0.7);
  diffuseColor.rgb = col;
  vec3 dn = texture2D(tDetailN, uA).xyz * 2.0 - 1.0;
  terrDetailN = vec3(dn.x, dn.z, dn.y) * vec3(1.0 - far * 0.7, 1.0, 1.0 - far * 0.7);
}
`;

const SPLAT_ROUGH = /* glsl */ `
float roughnessFactor = terrRough;
`;

const SPLAT_NORMAL = /* glsl */ `
{
  // Perturb the world normal with the detail map (heightfield tangent frame).
  vec3 wn = normalize(vTerrNormal + vec3(terrDetailN.x, 0.0, terrDetailN.z) * 0.55);
  normal = normalize((viewMatrix * vec4(wn, 0.0)).xyz);
}
`;

export class TerrainView {
  readonly group = new THREE.Group();
  readonly material: THREE.MeshStandardMaterial;
  private tiles = new Map<string, TileMesh>();
  private frame = 0;
  private sample = newSample();
  private lay: Layers = { grass: 0, rock: 0, dirt: 0, sand: 0, snow: 0 };
  /** Tiles built in the last update (for perf counters). */
  built = 0;
  splitK = 1.65;

  constructor(readonly world: World, detailScale = 1) {
    this.group.name = 'terrain';
    this.splitK *= detailScale;
    const tex = terrainTextures();
    const mat = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.95, metalness: 0, envMapIntensity: 0.6 });
    const uniforms = {
      tGrass: { value: tex.grass },
      tRock: { value: tex.rock },
      tDirt: { value: tex.dirt },
      tSand: { value: tex.sand },
      tSnow: { value: tex.snow },
      tDetailN: { value: tex.normal },
      tMacro: { value: tex.macro },
      uWaterLevel: { value: 0 },
      uTime: atmoUniforms.uTime,
    };
    mat.onBeforeCompile = (shader) => {
      Object.assign(shader.uniforms, uniforms);
      shader.vertexShader = shader.vertexShader
        .replace('#include <common>', `#include <common>\n${SPLAT_VERT_PARS}`)
        .replace('#include <begin_vertex>', `#include <begin_vertex>\n${SPLAT_VERT}`);
      shader.fragmentShader = shader.fragmentShader
        .replace('#include <common>', `#include <common>\n${SPLAT_FRAG_PARS}`)
        .replace('#include <map_fragment>', SPLAT_MAP)
        .replace('#include <color_fragment>', '')
        .replace('#include <roughnessmap_fragment>', SPLAT_ROUGH)
        .replace('#include <normal_fragment_maps>', SPLAT_NORMAL);
    };
    mat.customProgramCacheKey = () => 'terrain-splat-v1';
    this.material = patchMaterial(mat, { key: 'terrain' });
  }

  /** Refine the quadtree around the camera; build at most `budgetMs` of new tiles. */
  update(camera: THREE.Vector3, budgetMs: number): void {
    this.frame++;
    this.built = 0;
    const want: { level: number; ix: number; iz: number }[] = [];
    const roots = Math.ceil((WORLD_HALF * 2) / ROOT);
    for (let rx = 0; rx < roots; rx++) for (let rz = 0; rz < roots; rz++) this.select(MAX_LEVEL, rx, rz, camera, want);
    const t0 = performance.now();
    // Near tiles first.
    want.sort((a, b) => a.level - b.level);
    const ready = new Set<string>();
    let pending = false;
    for (const w of want) {
      const key = `${w.level}:${w.ix}:${w.iz}`;
      let t = this.tiles.get(key);
      if (!t) {
        if (performance.now() - t0 > budgetMs && this.tiles.size > 0) {
          pending = true;
          continue;
        }
        t = { key, mesh: this.buildMesh(w.level, w.ix, w.iz), level: w.level, used: this.frame };
        this.tiles.set(key, t);
        this.group.add(t.mesh);
        this.built++;
      }
      t.used = this.frame;
      ready.add(key);
    }
    // Keep stale tiles until their replacements exist, then drop them.
    for (const [key, t] of this.tiles) {
      if (t.used === this.frame) {
        t.mesh.visible = true;
        continue;
      }
      if (!pending || this.frame - t.used > 120) {
        this.group.remove(t.mesh);
        t.mesh.geometry.dispose();
        this.tiles.delete(key);
      } else t.mesh.visible = true;
    }
  }

  /** Build every tile the camera needs right now (loading screens, tests). */
  prime(camera: THREE.Vector3): void {
    this.update(camera, 1e9);
  }

  private select(level: number, ix: number, iz: number, cam: THREE.Vector3, out: { level: number; ix: number; iz: number }[]): void {
    const size = LEAF * 2 ** level;
    const x0 = -WORLD_HALF + ix * size;
    const z0 = -WORLD_HALF + iz * size;
    const cx = Math.max(x0, Math.min(cam.x, x0 + size));
    const cz = Math.max(z0, Math.min(cam.z, z0 + size));
    const dx = cam.x - cx;
    const dz = cam.z - cz;
    const dy = Math.max(0, cam.y - 400);
    const d = Math.sqrt(dx * dx + dz * dz + dy * dy);
    if (level > 0 && d < size * this.splitK) {
      for (const [ox, oz] of [[0, 0], [1, 0], [0, 1], [1, 1]]) this.select(level - 1, ix * 2 + ox, iz * 2 + oz, cam, out);
    } else out.push({ level, ix, iz });
  }

  private buildMesh(level: number, ix: number, iz: number): THREE.Mesh {
    const size = LEAF * 2 ** level;
    const step = size / QUADS;
    const x0 = -WORLD_HALF + ix * size;
    const z0 = -WORLD_HALF + iz * size;
    const N = QUADS + 1;
    // Heights with a one-sample border for normals.
    const B = N + 2;
    const H = new Float32Array(B * B);
    for (let j = 0; j < B; j++) {
      for (let i = 0; i < B; i++) {
        H[j * B + i] = this.height(x0 + (i - 1) * step, z0 + (j - 1) * step, level);
      }
    }
    const skirtVerts = QUADS * 4;
    const vcount = N * N + skirtVerts;
    const pos = new Float32Array(vcount * 3);
    const nrm = new Float32Array(vcount * 3);
    const col = new Float32Array(vcount * 3);
    const spl = new Float32Array(vcount * 4);
    const s = this.sample;
    const lay = this.lay;
    for (let j = 0; j < N; j++) {
      for (let i = 0; i < N; i++) {
        const v = j * N + i;
        const x = x0 + i * step;
        const z = z0 + j * step;
        const h = H[(j + 1) * B + i + 1];
        pos[v * 3] = x;
        pos[v * 3 + 1] = h;
        pos[v * 3 + 2] = z;
        const hx = H[(j + 1) * B + i + 2] - H[(j + 1) * B + i];
        const hz = H[(j + 2) * B + i + 1] - H[j * B + i + 1];
        let nx = -hx / (2 * step);
        let nz = -hz / (2 * step);
        const l = Math.sqrt(nx * nx + 1 + nz * nz);
        nx /= l;
        nz /= l;
        nrm[v * 3] = nx;
        nrm[v * 3 + 1] = 1 / l;
        nrm[v * 3 + 2] = nz;
        this.world.island.sample(x, z, s);
        const slope = Math.sqrt(hx * hx + hz * hz) / (2 * step);
        const hit = roadHit;
        const rd = this.world.nearestRoad(x, z, 8, hit) ? Math.max(0, hit.dist - hit.halfWidth) : 99;
        this.world.layers(s, h, slope, rd, lay);
        spl[v * 4] = lay.rock;
        spl[v * 4 + 1] = lay.dirt;
        spl[v * 4 + 2] = lay.sand;
        spl[v * 4 + 3] = lay.snow;
        // Biome tint: lush forest floor, sun-bleached farmland, red desert.
        let r = 1;
        let g = 1;
        let b = 1;
        if (s.desert > 0) {
          r = 1 + 0.32 * s.desert;
          g = 1 - 0.1 * s.desert;
          b = 1 - 0.28 * s.desert;
        }
        if (s.forest > 0) {
          r *= 1 - 0.22 * s.forest;
          g *= 1 - 0.06 * s.forest;
          b *= 1 - 0.12 * s.forest;
        }
        if (s.region === REGION.farmland) {
          r *= 1.08;
          g *= 1.04;
        }
        // Darken hollows a little: cheap baked ambient occlusion from curvature.
        const lap = H[(j + 1) * B + i] + H[(j + 1) * B + i + 2] + H[j * B + i + 1] + H[(j + 2) * B + i + 1] - 4 * h;
        const ao = Math.max(0.7, Math.min(1.08, 1 - lap * 0.02 / Math.max(1, step / 4)));
        col[v * 3] = r * ao;
        col[v * 3 + 1] = g * ao;
        col[v * 3 + 2] = b * ao;
      }
    }
    // Skirts: duplicate the border ring, dropped down.
    const drop = Math.max(2, step * 1.5);
    let sv = N * N;
    const ring: number[] = [];
    for (let i = 0; i < QUADS; i++) ring.push(i);
    for (let j = 0; j < QUADS; j++) ring.push(j * N + QUADS);
    for (let i = QUADS; i > 0; i--) ring.push(QUADS * N + i);
    for (let j = QUADS; j > 0; j--) ring.push(j * N);
    const skirtOf: number[] = [];
    for (const v of ring) {
      pos[sv * 3] = pos[v * 3];
      pos[sv * 3 + 1] = pos[v * 3 + 1] - drop;
      pos[sv * 3 + 2] = pos[v * 3 + 2];
      for (let c = 0; c < 3; c++) {
        nrm[sv * 3 + c] = nrm[v * 3 + c];
        col[sv * 3 + c] = col[v * 3 + c] * 0.9;
      }
      for (let c = 0; c < 4; c++) spl[sv * 4 + c] = spl[v * 4 + c];
      skirtOf.push(sv);
      sv++;
    }
    const idx: number[] = [];
    for (let j = 0; j < QUADS; j++) {
      for (let i = 0; i < QUADS; i++) {
        const a = j * N + i;
        const b = a + 1;
        const c = a + N;
        const d = c + 1;
        // Same split as World.groundAt: (a,b,d) for u>=v and (a,d,c) for u<v.
        idx.push(a, d, b, a, c, d);
      }
    }
    for (let k = 0; k < ring.length; k++) {
      const a = ring[k];
      const b = ring[(k + 1) % ring.length];
      const sa = skirtOf[k];
      const sb = skirtOf[(k + 1) % ring.length];
      idx.push(a, b, sa, b, sb, sa, a, sa, b, b, sa, sb);
    }
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    geo.setAttribute('normal', new THREE.BufferAttribute(nrm, 3));
    geo.setAttribute('color', new THREE.BufferAttribute(col, 3));
    geo.setAttribute('aSplat', new THREE.BufferAttribute(spl, 4));
    geo.setIndex(idx);
    geo.computeBoundingSphere();
    const m = new THREE.Mesh(geo, this.material);
    m.receiveShadow = true;
    m.castShadow = level <= 1;
    m.matrixAutoUpdate = false;
    m.updateMatrix();
    return m;
  }

  /** Height for a mesh vertex: exact physics tiles at the finest level. */
  private height(x: number, z: number, level: number): number {
    if (level === 0) {
      const tx = Math.floor(x / TILE);
      const tz = Math.floor(z / TILE);
      const lx = Math.round((x - tx * TILE) / TILE_SPACING);
      const lz = Math.round((z - tz * TILE) / TILE_SPACING);
      if (lx >= 0 && lz >= 0 && lx < TILE_SAMPLES && lz < TILE_SAMPLES) return this.world.tile(tx, tz).h[lz * TILE_SAMPLES + lx];
    }
    return this.world.terrainHeight(x, z);
  }

  get tileCount(): number {
    return this.tiles.size;
  }
}

const roadHit = { edge: 0, i: 0, t: 0, lateral: 0, dist: 0, y: 0, halfWidth: 0, bridge: false };
