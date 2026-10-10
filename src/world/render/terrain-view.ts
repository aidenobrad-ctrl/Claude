// Streamed terrain: a quadtree of tiles from 256 m (4 m spacing, matching
// the physics heightfield exactly) up to 2 km, each 64x64 quads with skirts
// that hide cracks between levels, plus the water surface of each tile.
// Tile data is built by the tile worker (or synchronously when priming).
import * as THREE from 'three';
import { WORLD_HALF } from '../island';
import type { World } from '../world';
import { T_QUADS, T_LEAF, T_MAX_LEVEL, T_ROOT, skirtRing, type TerrainTileData } from '../terrain-data';
import { terrainTextures } from './terrain-textures';
import { patchMaterial } from '../../engine/render/materials';
import { atmoUniforms } from '../../engine/render/atmosphere';
import { createWaterMaterial, oceanRing } from './water';
import type { TileSource } from './tile-source';

interface TileMesh {
  key: string;
  mesh: THREE.Mesh | null;
  water: THREE.Mesh | null;
  level: number;
  used: number;
  ready: boolean;
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
  // Meadow variation: dry straw-coloured swathes and lush darker hollows.
  float mA = texture2D(tMacro, p.xz / 380.0 + 0.13).r;
  float mB = texture2D(tMacro, p.xz / 95.0 + 0.61).r;
  float dryness = smoothstep(0.42, 0.78, mA * 0.7 + mB * 0.3);
  float lush = smoothstep(0.55, 0.25, mA * 0.6 + mB * 0.4);
  g.rgb = mix(g.rgb, g.rgb * vec3(1.32, 1.14, 0.72), dryness * 0.75);
  g.rgb = mix(g.rgb, g.rgb * vec3(0.78, 0.92, 0.82), lush * 0.6);
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
  readonly waterMaterial: THREE.MeshStandardMaterial;
  private tiles = new Map<string, TileMesh>();
  private frame = 0;
  private terrainIndex: THREE.BufferAttribute;
  private waterIndex: THREE.BufferAttribute;
  /** Tiles that arrived in the last update (for perf counters). */
  built = 0;
  splitK = 1.5;
  /** Maximum worker requests outstanding at once (per worker). */
  maxInFlight = 6;
  /** Main-thread build budget per frame when no worker is available, ms. */
  syncBudgetMs = 6;

  constructor(
    readonly world: World,
    private source: TileSource,
    detailScale = 1,
  ) {
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
    mat.customProgramCacheKey = () => 'terrain-splat-v2';
    this.material = patchMaterial(mat, { key: 'terrain' });
    this.waterMaterial = createWaterMaterial();
    this.group.add(oceanRing(this.waterMaterial));
    // Shared index buffers: the grid (counter-clockwise from above, split
    // like World.groundAt) and the skirts.
    const N = T_QUADS + 1;
    const grid: number[] = [];
    for (let j = 0; j < T_QUADS; j++) {
      for (let i = 0; i < T_QUADS; i++) {
        const a = j * N + i;
        const b = a + 1;
        const c = a + N;
        const d = c + 1;
        grid.push(a, d, b, a, c, d);
      }
    }
    const idx = grid.slice();
    const ring = skirtRing();
    for (let k = 0; k < ring.length; k++) {
      const a = ring[k];
      const b = ring[(k + 1) % ring.length];
      const sa = N * N + k;
      const sb = N * N + ((k + 1) % ring.length);
      idx.push(a, b, sa, b, sb, sa, a, sa, b, b, sa, sb);
    }
    this.terrainIndex = new THREE.BufferAttribute(new Uint16Array(idx), 1);
    this.waterIndex = new THREE.BufferAttribute(new Uint16Array(grid), 1);
  }

  /** Refine the quadtree around the camera and request missing tiles. */
  update(camera: THREE.Vector3, sync = false): void {
    this.frame++;
    this.built = 0;
    const want: { level: number; ix: number; iz: number; d: number }[] = [];
    const roots = Math.ceil((WORLD_HALF * 2) / T_ROOT);
    for (let rx = 0; rx < roots; rx++) for (let rz = 0; rz < roots; rz++) this.select(T_MAX_LEVEL, rx, rz, camera, want);
    // Nearest first.
    want.sort((a, b) => a.d - b.d);
    let missing = false;
    const t0 = performance.now();
    for (const w of want) {
      const key = `${w.level}:${w.ix}:${w.iz}`;
      let t = this.tiles.get(key);
      if (!t) {
        if (!sync && this.source.async && this.source.terrainInFlight >= this.maxInFlight * this.source.workerCount) {
          missing = true;
          continue;
        }
        // Without workers, spread tile builds over frames (a few ms each).
        if (!sync && !this.source.async && performance.now() - t0 > this.syncBudgetMs && this.tiles.size > 0) {
          missing = true;
          continue;
        }
        t = { key, mesh: null, water: null, level: w.level, used: this.frame, ready: false };
        this.tiles.set(key, t);
        const tile = t;
        this.source.terrain(w.level, w.ix, w.iz, (d) => this.onTile(tile, d), sync);
      }
      t.used = this.frame;
      if (!t.ready) missing = true;
    }
    // Keep stale tiles until their replacements exist, then drop them.
    for (const [key, t] of this.tiles) {
      if (t.used === this.frame) continue;
      if (!missing || this.frame - t.used > 240) this.dropTile(key, t);
    }
  }

  private onTile(t: TileMesh, d: TerrainTileData): void {
    if (this.tiles.get(t.key) !== t) return;
    t.ready = true;
    this.built++;
    if (d.pos && d.nrm && d.col && d.spl) {
      const geo = new THREE.BufferGeometry();
      geo.setAttribute('position', new THREE.BufferAttribute(d.pos, 3));
      geo.setAttribute('normal', new THREE.BufferAttribute(d.nrm, 3));
      geo.setAttribute('color', new THREE.BufferAttribute(d.col, 3));
      geo.setAttribute('aSplat', new THREE.BufferAttribute(d.spl, 4));
      geo.setIndex(this.terrainIndex);
      geo.computeBoundingSphere();
      const m = new THREE.Mesh(geo, this.material);
      m.receiveShadow = true;
      m.castShadow = d.level <= 1;
      m.matrixAutoUpdate = false;
      this.group.add(m);
      t.mesh = m;
    }
    if (d.waterPos && d.waterAttr) {
      const geo = new THREE.BufferGeometry();
      geo.setAttribute('position', new THREE.BufferAttribute(d.waterPos, 3));
      const n = new Float32Array(d.waterPos.length);
      for (let i = 1; i < n.length; i += 3) n[i] = 1;
      geo.setAttribute('normal', new THREE.BufferAttribute(n, 3));
      geo.setAttribute('aWater', new THREE.BufferAttribute(d.waterAttr, 4));
      geo.setIndex(this.waterIndex);
      geo.computeBoundingSphere();
      const m = new THREE.Mesh(geo, this.waterMaterial);
      m.receiveShadow = true;
      m.matrixAutoUpdate = false;
      m.renderOrder = 1;
      this.group.add(m);
      t.water = m;
    }
  }

  private dropTile(key: string, t: TileMesh): void {
    if (t.mesh) {
      this.group.remove(t.mesh);
      t.mesh.geometry.dispose();
    }
    if (t.water) {
      this.group.remove(t.water);
      t.water.geometry.dispose();
    }
    this.tiles.delete(key);
  }

  /** Build every tile the camera needs right now (loading screens, tests). */
  prime(camera: THREE.Vector3): void {
    this.update(camera, true);
  }

  private select(level: number, ix: number, iz: number, cam: THREE.Vector3, out: { level: number; ix: number; iz: number; d: number }[]): void {
    const size = T_LEAF * (1 << level);
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
    } else out.push({ level, ix, iz, d });
  }

  get tileCount(): number {
    return this.tiles.size;
  }

  /** Fraction of the tiles in the current view that have arrived. */
  get progress(): number {
    let n = 0;
    let ready = 0;
    for (const t of this.tiles.values()) {
      if (t.used !== this.frame) continue;
      n++;
      if (t.ready) ready++;
    }
    return n ? ready / n : 0;
  }

  /** True when every wanted tile has arrived. */
  get settled(): boolean {
    for (const t of this.tiles.values()) if (!t.ready) return false;
    return true;
  }
}
