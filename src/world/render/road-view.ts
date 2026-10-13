// Island roads: one ribbon per road edge, merged into 512 m chunks for
// culling. Markings, wheel-track wear, patches and shoulders are drawn
// procedurally in the shader from (lateral, arc length) so they stay crisp
// at any distance. Bridges get decks, parapets and piers.
import * as THREE from 'three';
import type { World, NetEdge } from '../world';
import { patchMaterial } from '../../engine/render/materials';
import { fbmTile } from './terrain-textures';
import { PARAPET_W, CURB_H } from '../road-classes';
import { railFoot } from '../guardrails';
import { SURFACE } from '../surfaces';

const CHUNK = 512;
/** Deck thickness and parapet height for bridges, m. */
const DECK = 1.5;
const PARAPET_H = 0.85;
const PIER_SPACING = 30;

/** Marking styles, packed into a vertex attribute. */
const STYLE = { none: 0, dashed: 1, double: 2, solid: 3 } as const;

const ROAD_VERT_PARS = /* glsl */ `
attribute vec4 aRoad;
attribute vec3 aStyle;
varying vec4 vRoad;
varying vec3 vStyle;
varying vec3 vRoadWorld;
`;
const ROAD_VERT = /* glsl */ `
vRoad = aRoad;
vStyle = aStyle;
vRoadWorld = (modelMatrix * vec4(transformed, 1.0)).xyz;
`;

const ROAD_FRAG_PARS = /* glsl */ `
uniform sampler2D tAsphalt;
uniform sampler2D tNoise;
uniform float uWet;
varying vec4 vRoad;
varying vec3 vStyle;
varying vec3 vRoadWorld;
float roadRough;
// Anti-aliased band of half width hw around x = 0.
float band(float x, float hw) {
  float w = max(fwidth(x), 1e-4);
  return 1.0 - smoothstep(hw - w, hw + w, abs(x));
}
// Dashes: 'on' metres painted every 'period' metres along s.
float dashes(float s, float on, float period) {
  float f = mod(s, period);
  float w = max(fwidth(s), 1e-3);
  return smoothstep(0.0, w, f) * (1.0 - smoothstep(on - w, on + w, f));
}
`;

const ROAD_MAP = /* glsl */ `
{
  float lat = vRoad.x;
  float s = vRoad.y;
  float hw = vRoad.z;
  float endFade = smoothstep(10.0, 22.0, vRoad.w);
  float centerStyle = vStyle.x;
  float lanes = vStyle.y;
  // z: 0 paved, 1 paved with edge lines, 2 dirt, 3 street with sidewalks.
  float dirt = step(1.5, vStyle.z) * step(vStyle.z, 2.5);
  float sidewalk = step(2.5, vStyle.z);
  float edgeLines = step(0.5, vStyle.z) * step(vStyle.z, 1.5);
  vec2 wp = vRoadWorld.xz;
  float dist = length(vRoadWorld - cameraPosition);
  float a = abs(lat);
  // Ragged pavement edge.
  float rag = (texture2D(tNoise, wp * 0.21).r - 0.5) * 0.35;
  float paved = 1.0 - smoothstep(hw - 0.05 + rag, hw + 0.08 + rag, a);
  vec3 col;
  float rough;
  if (dirt > 0.5) {
    // Packed dirt with two wheel ruts and a grassy crown.
    float n = texture2D(tNoise, wp * 0.35).r;
    float n2 = texture2D(tNoise, wp * 0.07).g;
    vec3 base = mix(vec3(0.30, 0.22, 0.15), vec3(0.42, 0.33, 0.23), n);
    base *= 0.85 + 0.3 * n2;
    float rut = band(a - 0.95, 0.38) * endFade;
    base = mix(base, base * 0.78, rut);
    float crown = band(lat, 0.35) * smoothstep(0.45, 0.7, n2);
    base = mix(base, vec3(0.20, 0.24, 0.11), crown * 0.7);
    float edge = smoothstep(hw - 0.6, hw + 0.4, a);
    col = mix(base, base * vec3(0.92, 1.0, 0.85), edge);
    rough = mix(0.95, 0.88, rut);
  } else {
    vec4 asph = texture2D(tAsphalt, wp * 0.22);
    vec4 asphFar = texture2D(tAsphalt, wp * 0.043);
    float far = smoothstep(30.0, 160.0, dist);
    vec3 base = mix(asph.rgb, asphFar.rgb, 0.3 + 0.4 * far);
    // Patches and repairs: slightly different shades in large blotches.
    float patchN = texture2D(tNoise, wp * 0.012).b;
    base *= 0.86 + 0.28 * smoothstep(0.35, 0.75, patchN);
    float repair = smoothstep(0.78, 0.8, texture2D(tNoise, wp * 0.02 + 0.3).r);
    base = mix(base, base * 0.62, repair);
    // Polished wheel tracks: two per lane, darker and smoother.
    float laneW = (2.0 * hw) / max(lanes, 1.0);
    float inLane = mod(lat + hw, laneW) - laneW * 0.5;
    float track = band(abs(inLane) - 0.85, 0.32) * paved * endFade;
    base *= 1.0 - 0.07 * track;
    rough = mix(asph.a * 0.12 + 0.8, 0.74, track);
    // Markings.
    float paint = 0.0;
    vec3 paintCol = vec3(0.86, 0.86, 0.82);
    float lw = 0.075;
    if (vStyle.x > 0.5) {
      if (centerStyle < 1.5) paint = band(lat, lw) * dashes(s, 3.0, 12.0);
      else if (centerStyle < 2.5) {
        paint = band(abs(lat) - 0.17, lw);
        paintCol = vec3(0.86, 0.66, 0.18);
      } else {
        paint = band(lat, lw);
        paintCol = vec3(0.86, 0.66, 0.18);
      }
    }
    // Lane dividers on multi-lane roads.
    if (lanes > 2.5) paint = max(paint, band(a - hw * 0.5, lw) * dashes(s + 4.0, 4.0, 14.0));
    // Edge lines.
    if (edgeLines > 0.5) paint = max(paint, band(a - (hw - 0.45), 0.1));
    paint *= endFade;
    // Worn paint: break it up a little.
    paint *= 0.75 + 0.25 * smoothstep(0.2, 0.6, texture2D(tNoise, wp * 1.3).g);
    col = mix(base, paintCol, paint);
    rough = mix(rough, 0.5, paint);
    if (sidewalk > 0.5) {
      // Kerb stones, then concrete paving slabs.
      float kerb = band(a - hw - 0.15, 0.15);
      float slabS = fract(s / 1.5);
      float slabL = fract((a - hw - 0.3) / 1.5);
      float joint = max(1.0 - smoothstep(0.0, 0.03, slabS) , 1.0 - smoothstep(0.0, 0.03, slabL));
      vec3 slab = vec3(0.52, 0.51, 0.49) * (0.85 + 0.25 * texture2D(tNoise, wp * 0.6).r);
      vec3 walk = mix(slab, slab * 0.7, joint * step(hw + 0.3, a));
      walk = mix(walk, vec3(0.6, 0.6, 0.58), kerb);
      float onWalk = step(hw, a);
      col = mix(col, walk, onWalk);
      rough = mix(rough, 0.8, onWalk);
    } else {
      // Gravel shoulder beyond the pavement.
      vec3 grav = mix(vec3(0.26, 0.23, 0.19), vec3(0.37, 0.34, 0.29), texture2D(tNoise, wp * 0.9).r);
      col = mix(grav, col, paved);
      rough = mix(0.95, rough, paved);
    }
  }
  // Rain: darker, glossier, with standing water in the tracks.
  col *= 1.0 - 0.35 * uWet;
  rough = mix(rough, 0.12, uWet * 0.85);
  diffuseColor.rgb = col;
  roadRough = rough;
}
`;

/** Tileable asphalt albedo (RGB) with roughness variation in alpha. */
function asphaltTexture(size = 512): THREE.DataTexture {
  const n1 = fbmTile(size, 71, 16, 4, 0.55);
  const n2 = fbmTile(size, 83, 128, 2, 0.5);
  const grit = fbmTile(size, 97, 256, 1, 0.5);
  const data = new Uint8Array(size * size * 4);
  for (let i = 0; i < size * size; i++) {
    const g = grit[i];
    const stone = g > 0.66 ? (g - 0.66) * 2.4 : 0;
    const k = 0.78 + n1[i] * 0.3 + n2[i] * 0.12 + stone * 0.5 - (g < 0.3 ? (0.3 - g) * 0.6 : 0);
    data[i * 4] = Math.min(255, 62 * k);
    data[i * 4 + 1] = Math.min(255, 61 * k);
    data[i * 4 + 2] = Math.min(255, 60 * k);
    data[i * 4 + 3] = Math.min(255, 150 + n2[i] * 90 - stone * 50);
  }
  const t = new THREE.DataTexture(data, size, size, THREE.RGBAFormat);
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.minFilter = THREE.LinearMipmapLinearFilter;
  t.magFilter = THREE.LinearFilter;
  t.generateMipmaps = true;
  t.anisotropy = 8;
  t.colorSpace = THREE.SRGBColorSpace;
  t.needsUpdate = true;
  return t;
}

/** Tileable multi-channel noise for patches, ruts and paint wear. */
function noiseTexture(size = 256): THREE.DataTexture {
  const a = fbmTile(size, 5, 8, 4, 0.55);
  const b = fbmTile(size, 6, 16, 3, 0.5);
  const c = fbmTile(size, 7, 4, 4, 0.6);
  const data = new Uint8Array(size * size * 4);
  for (let i = 0; i < size * size; i++) {
    data[i * 4] = a[i] * 255;
    data[i * 4 + 1] = b[i] * 255;
    data[i * 4 + 2] = c[i] * 255;
    data[i * 4 + 3] = 255;
  }
  const t = new THREE.DataTexture(data, size, size, THREE.RGBAFormat);
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.minFilter = THREE.LinearMipmapLinearFilter;
  t.magFilter = THREE.LinearFilter;
  t.generateMipmaps = true;
  t.needsUpdate = true;
  return t;
}

class Builder {
  pos: number[] = [];
  nrm: number[] = [];
  road: number[] = [];
  style: number[] = [];
  idx: number[] = [];
  get count(): number {
    return this.pos.length / 3;
  }
  geometry(): THREE.BufferGeometry {
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(this.pos, 3));
    g.setAttribute('normal', new THREE.Float32BufferAttribute(this.nrm, 3));
    if (this.road.length) g.setAttribute('aRoad', new THREE.Float32BufferAttribute(this.road, 4));
    if (this.style.length) g.setAttribute('aStyle', new THREE.Float32BufferAttribute(this.style, 3));
    g.setIndex(this.idx);
    g.computeBoundingSphere();
    return g;
  }
}

/** Simple solid geometry builder (bridges), positions + normals only. */
class Solid {
  pos: number[] = [];
  nrm: number[] = [];
  idx: number[] = [];
  /** Quad a-b-c-d facing along n; the winding is fixed up to match n. */
  quad(a: number[], b: number[], c: number[], d: number[], n: number[]): void {
    const base = this.pos.length / 3;
    for (const p of [a, b, c, d]) {
      this.pos.push(p[0], p[1], p[2]);
      this.nrm.push(n[0], n[1], n[2]);
    }
    const ux = b[0] - a[0];
    const uy = b[1] - a[1];
    const uz = b[2] - a[2];
    const vx = c[0] - a[0];
    const vy = c[1] - a[1];
    const vz = c[2] - a[2];
    const gx = uy * vz - uz * vy;
    const gy = uz * vx - ux * vz;
    const gz = ux * vy - uy * vx;
    if (gx * n[0] + gy * n[1] + gz * n[2] >= 0) this.idx.push(base, base + 1, base + 2, base, base + 2, base + 3);
    else this.idx.push(base, base + 2, base + 1, base, base + 3, base + 2);
  }
  /** Axis-aligned-in-yaw box: center (x, z), from y0 to y1, size w (across) x l (along). */
  box(x: number, z: number, y0: number, y1: number, w: number, l: number, tx: number, tz: number): void {
    const rx = -tz;
    const rz = tx;
    const hw = w / 2;
    const hl = l / 2;
    const c = (sa: number, sb: number, y: number): number[] => [x + rx * sa * hw + tx * sb * hl, y, z + rz * sa * hw + tz * sb * hl];
    // Sides.
    this.quad(c(1, -1, y0), c(1, 1, y0), c(1, 1, y1), c(1, -1, y1), [rx, 0, rz]);
    this.quad(c(-1, 1, y0), c(-1, -1, y0), c(-1, -1, y1), c(-1, 1, y1), [-rx, 0, -rz]);
    this.quad(c(-1, -1, y0), c(1, -1, y0), c(1, -1, y1), c(-1, -1, y1), [-tx, 0, -tz]);
    this.quad(c(1, 1, y0), c(-1, 1, y0), c(-1, 1, y1), c(1, 1, y1), [tx, 0, tz]);
    this.quad(c(-1, -1, y1), c(1, -1, y1), c(1, 1, y1), c(-1, 1, y1), [0, 1, 0]);
  }
  geometry(): THREE.BufferGeometry {
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(this.pos, 3));
    g.setAttribute('normal', new THREE.Float32BufferAttribute(this.nrm, 3));
    g.setIndex(this.idx);
    g.computeBoundingSphere();
    return g;
  }
}

export interface RoadView {
  group: THREE.Group;
  material: THREE.MeshStandardMaterial;
  uniforms: { uWet: { value: number } };
}

export function buildRoadView(world: World): RoadView {
  const group = new THREE.Group();
  group.name = 'roads';
  const uniforms = {
    tAsphalt: { value: asphaltTexture() },
    tNoise: { value: noiseTexture() },
    uWet: { value: 0 },
  };
  const mat = new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 0.85, metalness: 0, envMapIntensity: 0.35 });
  // Decals over the terrain: pull towards the camera in depth so they win
  // at every distance without lifting the geometry.
  mat.polygonOffset = true;
  mat.polygonOffsetFactor = -2;
  mat.polygonOffsetUnits = -4;
  mat.onBeforeCompile = (shader) => {
    Object.assign(shader.uniforms, uniforms);
    shader.vertexShader = shader.vertexShader
      .replace('#include <common>', `#include <common>\n${ROAD_VERT_PARS}`)
      .replace('#include <begin_vertex>', `#include <begin_vertex>\n${ROAD_VERT}`);
    shader.fragmentShader = shader.fragmentShader
      .replace('#include <common>', `#include <common>\n${ROAD_FRAG_PARS}`)
      .replace('#include <map_fragment>', ROAD_MAP)
      .replace('#include <roughnessmap_fragment>', 'float roughnessFactor = roadRough;');
  };
  mat.customProgramCacheKey = () => 'road-v1';
  patchMaterial(mat, { key: 'road' });

  const concrete = patchMaterial(new THREE.MeshStandardMaterial({ color: 0x9a968e, roughness: 0.9, metalness: 0 }), { key: 'concrete' });

  const chunks = new Map<number, Builder>();
  const solids = new Map<number, Solid>();
  const keyOf = (x: number, z: number): number => Math.floor(x / CHUNK) * 4096 + Math.floor(z / CHUNK);
  const getB = (k: number): Builder => {
    let b = chunks.get(k);
    if (!b) chunks.set(k, (b = new Builder()));
    return b;
  };
  const getS = (k: number): Solid => {
    let b = solids.get(k);
    if (!b) solids.set(k, (b = new Solid()));
    return b;
  };

  for (const e of world.edges) {
    addRibbon(e, getB, keyOf);
    addBridges(e, getS, keyOf);
  }

  // Guardrails: a W-beam strip on posts every 2 m.
  const steel = patchMaterial(new THREE.MeshStandardMaterial({ color: 0xb9bdc2, roughness: 0.38, metalness: 0.75, envMapIntensity: 0.9 }), { key: 'rail' });
  const postPos: number[] = [];
  // One mesh for every beam and one for every retaining wall: a handful of
  // draw calls however many runs there are.
  const s = new Solid();
  const wall = new Solid();
  for (const run of world.rails) {
    const r = world.edges[run.edge].road;
    const info = world.edges[run.edge].info;
    const off = run.offset * run.side;
    let since = 0;
    for (let i = run.i0; i < run.i1; i++) {
      const p = (k: number, dy: number): number[] => [r.x[k] - r.tz[k] * off, r.y[k] + dy, r.z[k] + r.tx[k] * off];
      // Beam faces the road on its inner side and outward on the other.
      const nIn = [r.tz[i] * run.side, 0, -r.tx[i] * run.side];
      s.quad(p(i, 0.5), p(i + 1, 0.5), p(i + 1, 0.82), p(i, 0.82), nIn);
      s.quad(p(i + 1, 0.5), p(i, 0.5), p(i, 0.82), p(i + 1, 0.82), [-nIn[0], 0, -nIn[2]]);
      // Retaining wall: deck out to just past the rail, then a concrete face down to the ground.
      const w = (k: number, dy: number, extra: number): number[] => {
        const o = off + run.side * extra;
        return [r.x[k] - r.tz[k] * o, r.y[k] + dy, r.z[k] + r.tx[k] * o];
      };
      const bottom = (k: number): number => railFoot(world, run, k);
      const b0 = bottom(i);
      const b1 = bottom(i + 1);
      if (b0 < r.y[i] - 0.3 || b1 < r.y[i + 1] - 0.3) {
        const W = info.halfWidth + info.shoulder;
        const inner = (k: number): number[] => {
          const o = W * run.side;
          return [r.x[k] - r.tz[k] * o, r.y[k] - 0.07, r.z[k] + r.tx[k] * o];
        };
        if (Math.abs(off) + 0.2 > W) wall.quad(inner(i), inner(i + 1), w(i + 1, -0.07, 0.2), w(i, -0.07, 0.2), [0, 1, 0]);
        const t0 = w(i, -0.07, 0.2);
        const t1 = w(i + 1, -0.07, 0.2);
        wall.quad(t1, t0, [t0[0], Math.min(b0, t0[1]), t0[2]], [t1[0], Math.min(b1, t1[1]), t1[2]], [-nIn[0], 0, -nIn[2]]);
      }
      since += r.s[i + 1] - r.s[i];
      if (since >= 2) {
        since = 0;
        const q = p(i, 0);
        postPos.push(q[0] + nIn[0] * -0.12, q[1], q[2] + nIn[2] * -0.12);
      }
    }
  }
  if (postPos.length) {
    const postGeo = new THREE.BoxGeometry(0.12, 0.85, 0.16).translate(0, 0.42, 0);
    const posts = new THREE.InstancedMesh(postGeo, steel, postPos.length / 3);
    const m4 = new THREE.Matrix4();
    for (let k = 0; k < postPos.length / 3; k++) posts.setMatrixAt(k, m4.makeTranslation(postPos[k * 3], postPos[k * 3 + 1], postPos[k * 3 + 2]));
    posts.castShadow = true;
    posts.receiveShadow = true;
    group.add(posts);
  }
  if (world.rails.length) {
    const beams = new THREE.Mesh(s.geometry(), steel);
    beams.castShadow = true;
    beams.receiveShadow = true;
    beams.matrixAutoUpdate = false;
    group.add(beams);
    const walls = new THREE.Mesh(wall.geometry(), concrete);
    walls.castShadow = true;
    walls.receiveShadow = true;
    walls.matrixAutoUpdate = false;
    group.add(walls);
  }

  for (const b of chunks.values()) {
    const m = new THREE.Mesh(b.geometry(), mat);
    m.receiveShadow = true;
    m.matrixAutoUpdate = false;
    group.add(m);
  }
  for (const s of solids.values()) {
    const m = new THREE.Mesh(s.geometry(), concrete);
    m.receiveShadow = true;
    m.castShadow = true;
    m.matrixAutoUpdate = false;
    group.add(m);
  }
  return { group, material: mat, uniforms };
}

function addRibbon(e: NetEdge, getB: (k: number) => Builder, keyOf: (x: number, z: number) => number): void {
  const r = e.road;
  const n = r.n;
  const info = e.info;
  const hw = info.halfWidth;
  const W = hw + info.shoulder;
  const style = [STYLE[info.center], info.lanes, info.sidewalk ? 3 : info.surface === SURFACE.dirt ? 2 : info.edgeLines ? 1 : 0];
  // Sidewalk streets get a kerb: the edge vertex appears twice, at both heights.
  const lats = info.sidewalk ? [-W, -hw, -hw, hw, hw, W] : [-W, -hw, hw, W];
  const raised = info.sidewalk ? [1, 1, 0, 0, 1, 1] : [0, 0, 0, 0];
  let cur: Builder | null = null;
  let curKey = -1;
  let prevBase = -1;
  const emit = (b: Builder, i: number): number => {
    const base = b.count;
    const tx = r.tx[i];
    const tz = r.tz[i];
    const ds = (r.s[Math.min(n - 1, i + 1)] - r.s[Math.max(0, i - 1)]) || 1;
    const gy = (r.y[Math.min(n - 1, i + 1)] - r.y[Math.max(0, i - 1)]) / ds;
    const l = Math.sqrt(1 + gy * gy);
    const end = Math.min(r.s[i], r.length - r.s[i]);
    for (let q = 0; q < lats.length; q++) {
      const lat = lats[q];
      const outer = Math.abs(lat) > hw + 0.01;
      // The shoulder dips to meet the cut terrain unless it is a bridge deck or a sidewalk.
      const y = r.y[i] + 0.02 + raised[q] * CURB_H - (outer && !e.bridge[i] && !info.sidewalk ? 0.07 : 0);
      b.pos.push(r.x[i] - tz * lat, y, r.z[i] + tx * lat);
      b.nrm.push((-tx * gy) / l, 1 / l, (-tz * gy) / l);
      b.road.push(lat, r.s[i], hw, end);
      b.style.push(style[0], style[1], style[2]);
    }
    return base;
  };
  for (let i = 0; i + 1 < n; i++) {
    const k = keyOf((r.x[i] + r.x[i + 1]) / 2, (r.z[i] + r.z[i + 1]) / 2);
    if (k !== curKey || !cur) {
      cur = getB(k);
      curKey = k;
      prevBase = emit(cur, i);
    }
    const nb = emit(cur, i + 1);
    for (let q = 0; q < lats.length - 1; q++) {
      const a = prevBase + q;
      const b = a + 1;
      const c = nb + q;
      const d = c + 1;
      // Counter-clockwise seen from above (left-to-right across, forward along).
      cur.idx.push(a, b, c, b, d, c);
    }
    prevBase = nb;
  }
}

function addBridges(e: NetEdge, getS: (k: number) => Solid, keyOf: (x: number, z: number) => number): void {
  const r = e.road;
  const n = r.n;
  const W = e.info.halfWidth + e.info.shoulder;
  let sinceP = PIER_SPACING / 2;
  for (let i = 0; i + 1 < n; i++) {
    if (!e.bridge[i] && !e.bridge[i + 1]) {
      sinceP = PIER_SPACING / 2;
      continue;
    }
    const s = getS(keyOf(r.x[i], r.z[i]));
    const p = (j: number, lat: number, dy: number): number[] => [r.x[j] - r.tz[j] * lat, r.y[j] + dy, r.z[j] + r.tx[j] * lat];
    const j = i + 1;
    // Deck sides and underside.
    for (const side of [-1, 1]) {
      const lat = side * W;
      const nrm = [-r.tz[i] * side, 0, r.tx[i] * side];
      if (side > 0) s.quad(p(i, lat, -DECK), p(j, lat, -DECK), p(j, lat, 0.02), p(i, lat, 0.02), nrm);
      else s.quad(p(j, lat, -DECK), p(i, lat, -DECK), p(i, lat, 0.02), p(j, lat, 0.02), nrm);
      // Parapet: inner face, top, outer face.
      const li = side * (W - PARAPET_W);
      const top = PARAPET_H;
      if (side > 0) {
        s.quad(p(i, li, 0), p(j, li, 0), p(j, li, top), p(i, li, top), [r.tz[i], 0, -r.tx[i]]);
        s.quad(p(i, li, top), p(j, li, top), p(j, lat, top), p(i, lat, top), [0, 1, 0]);
        s.quad(p(i, lat, 0), p(j, lat, 0), p(j, lat, top), p(i, lat, top), nrm);
      } else {
        s.quad(p(j, li, 0), p(i, li, 0), p(i, li, top), p(j, li, top), [-r.tz[i], 0, r.tx[i]]);
        s.quad(p(j, li, top), p(i, li, top), p(i, lat, top), p(j, lat, top), [0, 1, 0]);
        s.quad(p(j, lat, 0), p(i, lat, 0), p(i, lat, top), p(j, lat, top), nrm);
      }
    }
    s.quad(p(i, W, -DECK), p(i, -W, -DECK), p(j, -W, -DECK), p(j, W, -DECK), [0, -1, 0]);
    // Piers down to the base terrain (or the river bed).
    sinceP += r.s[j] - r.s[i];
    if (sinceP >= PIER_SPACING && e.bridge[i] && e.bridge[j]) {
      sinceP = 0;
      const bottom = e.base[i] - 3;
      const topY = r.y[i] - DECK;
      if (topY - bottom > 1.5) {
        for (const side of [-0.55, 0.55]) {
          const lat = side * W;
          s.box(r.x[i] - r.tz[i] * lat, r.z[i] + r.tx[i] * lat, bottom, topY, 1.3, 1.3, r.tx[i], r.tz[i]);
        }
        // Cap beam across the piers.
        s.box(r.x[i], r.z[i], topY - 0.9, topY, W * 2 - 0.6, 1.6, r.tx[i], r.tz[i]);
      }
    }
  }
}
