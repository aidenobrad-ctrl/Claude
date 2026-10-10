// Buildings: merged meshes per 512 m chunk, drawn with one facade shader
// that paints windows, doors, shopfronts, cladding and roof tiles from the
// wall's own metric UVs, so every building gets detail without textures.
import * as THREE from 'three';
import type { World } from '../world';
import { BUILDING, ROOF, type Building } from '../settlements';
import { patchMaterial } from '../../engine/render/materials';
import { fbmTile } from './terrain-textures';

const CHUNK = 512;
/** Wall parts, packed into the vertex attribute. */
const PART = { front: 0, wall: 1, roof: 2, gable: 3 } as const;

const BLD_VERT_PARS = /* glsl */ `
attribute vec4 aBld;
attribute vec2 aFace;
// Flat: building ids must not be interpolated (MSAA samples outside the
// triangle would otherwise read slightly different ids and pick a wrong look).
flat varying vec4 vBld;
varying vec2 vFace;
varying vec2 vBUv;
varying vec3 vBWorld;
`;
const BLD_VERT = /* glsl */ `
vBld = aBld;
vFace = aFace;
vBUv = uv;
vBWorld = (modelMatrix * vec4(transformed, 1.0)).xyz;
`;

const BLD_FRAG_PARS = /* glsl */ `
uniform sampler2D tBNoise;
uniform float uNight;
flat varying vec4 vBld;
varying vec2 vFace;
varying vec2 vBUv;
varying vec3 vBWorld;
float bRough;
float bMetal;
vec3 bEmit;
float bHash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float boxMask(vec2 p, vec2 lo, vec2 hi) {
  vec2 w = max(fwidth(p), vec2(1e-4));
  vec2 a = smoothstep(lo - w, lo + w, p) * (1.0 - smoothstep(hi - w, hi + w, p));
  return a.x * a.y;
}
vec3 palette(float k, float s) {
  // Plaster and paint colours, picked per building.
  if (k < 0.5) {
    vec3 c[6];
    c[0] = vec3(0.86, 0.83, 0.76); c[1] = vec3(0.92, 0.86, 0.68); c[2] = vec3(0.84, 0.66, 0.5);
    c[3] = vec3(0.7, 0.78, 0.82); c[4] = vec3(0.76, 0.8, 0.68); c[5] = vec3(0.93, 0.92, 0.9);
    return c[int(s * 5.99)];
  }
  // Brick and stone.
  vec3 c[4];
  c[0] = vec3(0.55, 0.32, 0.24); c[1] = vec3(0.62, 0.55, 0.47); c[2] = vec3(0.45, 0.44, 0.43); c[3] = vec3(0.7, 0.62, 0.52);
  return c[int(s * 3.99)];
}
`;

const BLD_MAP = /* glsl */ `
{
  float kind = floor(vBld.x + 0.5);
  float part = floor(vBld.y + 0.5);
  float seed = vBld.z;
  float floorH = vBld.w;
  vec2 uv = vBUv;
  float n = texture2D(tBNoise, vBWorld.xz * 0.11 + vBWorld.y * 0.07).r;
  vec3 col;
  bRough = 0.85;
  bMetal = 0.0;
  bEmit = vec3(0.0);
  if (part > 1.5 && part < 2.5) {
    // Roofs.
    if (kind == 2.0 || kind == 6.0 || kind == 1.0 && vFace.y < 0.0) {
      col = vec3(0.32, 0.31, 0.3) * (0.85 + 0.3 * n);
      // Rooftop clutter: vents and plant read as darker blocks.
      float blk = boxMask(fract(vBWorld.xz / 9.0 + seed), vec2(0.2), vec2(0.42));
      col *= 1.0 - 0.35 * blk;
    } else if (kind == 3.0 || kind == 5.0) {
      float rib = 0.5 + 0.5 * sin(uv.x * 6.2831 / 0.25);
      col = vec3(0.55, 0.57, 0.58) * (0.85 + 0.15 * rib);
      bMetal = 0.6;
      bRough = 0.45;
    } else {
      // Clay or slate tiles in courses down the slope.
      vec3 tile = seed < 0.55 ? vec3(0.55, 0.25, 0.16) : seed < 0.8 ? vec3(0.26, 0.27, 0.3) : vec3(0.42, 0.3, 0.22);
      float course = fract(uv.y / 0.32);
      float joint = smoothstep(0.0, 0.08, course) * smoothstep(1.0, 0.86, course);
      float stag = fract(uv.x / 0.3 + floor(uv.y / 0.32) * 0.5);
      float vj = smoothstep(0.0, 0.06, stag) * smoothstep(1.0, 0.94, stag);
      col = tile * (0.72 + 0.28 * joint * vj) * (0.85 + 0.3 * n);
      bRough = 0.7;
    }
  } else if (uv.y < 0.0) {
    // Foundation below the floor line.
    col = vec3(0.42, 0.4, 0.37) * (0.8 + 0.3 * n);
  } else {
    float isFront = step(part, 0.5);
    if (kind == 2.0) {
      // Towers: curtain wall of tinted glass with mullions and slab lines.
      vec2 g = vec2(uv.x / 1.5, uv.y / floorH);
      vec2 f = fract(g);
      float mull = 1.0 - boxMask(f, vec2(0.04, 0.0), vec2(0.96, 0.86));
      vec3 glass = seed < 0.5 ? vec3(0.05, 0.09, 0.12) : vec3(0.07, 0.1, 0.09);
      col = mix(glass, vec3(0.5, 0.52, 0.55), mull);
      bRough = mix(0.05, 0.6, mull);
      bMetal = mix(0.75, 0.2, mull);
      float lit = step(0.62, bHash(floor(g) + seed * 31.0));
      bEmit = vec3(1.0, 0.85, 0.6) * lit * (1.0 - mull) * uNight * 1.6;
    } else if (kind == 3.0) {
      // Warehouses: ribbed cladding, a roller door on the front.
      float rib = 0.5 + 0.5 * sin(uv.x * 6.2831 / 0.3);
      vec3 clad = seed < 0.33 ? vec3(0.62, 0.64, 0.66) : seed < 0.66 ? vec3(0.32, 0.45, 0.55) : vec3(0.6, 0.34, 0.26);
      col = clad * (0.82 + 0.18 * rib) * (0.9 + 0.2 * n);
      bMetal = 0.4;
      bRough = 0.55;
      float doorW = min(6.0, vFace.x * 0.35);
      float door = isFront * boxMask(uv, vec2(vFace.x * 0.5 - doorW * 0.5, 0.0), vec2(vFace.x * 0.5 + doorW * 0.5, 4.8));
      float slat = 0.5 + 0.5 * sin(uv.y * 6.2831 / 0.18);
      col = mix(col, vec3(0.4, 0.41, 0.42) * (0.8 + 0.2 * slat), door);
    } else if (kind == 4.0) {
      // Barns: vertical boards, white trim, big doors.
      float board = fract(uv.x / 0.25);
      float seam = smoothstep(0.0, 0.08, board) * smoothstep(1.0, 0.92, board);
      vec3 wood = seed < 0.6 ? vec3(0.5, 0.13, 0.1) : vec3(0.36, 0.26, 0.18);
      col = wood * (0.75 + 0.25 * seam) * (0.85 + 0.3 * n);
      float door = isFront * boxMask(uv, vec2(vFace.x * 0.5 - 2.2, 0.0), vec2(vFace.x * 0.5 + 2.2, 4.2));
      float trim = door * (1.0 - boxMask(uv, vec2(vFace.x * 0.5 - 2.0, 0.0), vec2(vFace.x * 0.5 + 2.0, 4.0)));
      col = mix(col, vec3(0.85, 0.84, 0.8), trim);
    } else if (kind == 5.0) {
      float ring = 0.5 + 0.5 * sin(uv.y * 6.2831 / 0.6);
      col = vec3(0.7, 0.71, 0.7) * (0.85 + 0.15 * ring);
      bMetal = 0.55;
      bRough = 0.4;
    } else {
      // Houses, shops and mid-rises: a wall with a grid of windows.
      bool brick = kind == 6.0 ? seed > 0.35 : seed > 0.82;
      vec3 wall = palette(brick ? 1.0 : 0.0, fract(seed * 7.3));
      if (brick) {
        float course = fract(uv.y / 0.075);
        float bond = fract(uv.x / 0.23 + floor(uv.y / 0.075) * 0.5);
        float mortar = (1.0 - smoothstep(0.0, 0.14, course)) + (1.0 - smoothstep(0.0, 0.05, bond));
        wall = mix(wall * (0.85 + 0.3 * n), vec3(0.7, 0.68, 0.64), clamp(mortar, 0.0, 1.0) * 0.5);
      } else {
        wall *= 0.9 + 0.16 * n;
      }
      col = wall;
      float cellW = kind == 6.0 ? 3.0 : 3.3;
      float cells = max(1.0, floor(vFace.x / cellW));
      float cw = vFace.x / cells;
      vec2 g = vec2(uv.x / cw, uv.y / floorH);
      vec2 f = fract(g);
      vec2 lo = kind == 6.0 ? vec2(0.18, 0.26) : vec2(0.3, 0.3);
      vec2 hi = kind == 6.0 ? vec2(0.82, 0.84) : vec2(0.7, 0.8);
      float win = boxMask(f, lo, hi);
      if (part > 2.5) win = 0.0;
      // Shopfronts: a tall glazed ground floor with an awning stripe.
      float shopFloor = (kind == 1.0 || kind == 6.0) && g.y < 1.0 && isFront > 0.5 ? 1.0 : 0.0;
      if (shopFloor > 0.5) {
        win = boxMask(f, vec2(0.06, 0.08), vec2(0.94, 0.78));
        float awn = boxMask(f, vec2(0.0, 0.8), vec2(1.0, 0.92));
        vec3 awnC = fract(seed * 13.7) < 0.5 ? vec3(0.62, 0.12, 0.1) : vec3(0.1, 0.32, 0.4);
        col = mix(col, awnC, awn);
      }
      // A front door for houses.
      float door = 0.0;
      if (kind == 0.0 && isFront > 0.5 && g.y < 1.0) {
        door = boxMask(uv, vec2(vFace.x * 0.5 - 0.55, 0.0), vec2(vFace.x * 0.5 + 0.55, 2.15));
        win *= 1.0 - boxMask(uv, vec2(vFace.x * 0.5 - 1.6, 0.0), vec2(vFace.x * 0.5 + 1.6, floorH));
      }
      float frame = win * (1.0 - boxMask(f, lo + 0.035, hi - 0.035));
      if (shopFloor > 0.5) frame = 0.0;
      vec3 glass = vec3(0.06, 0.08, 0.1);
      col = mix(col, glass, win);
      col = mix(col, vec3(0.88, 0.87, 0.84), frame);
      col = mix(col, seed < 0.5 ? vec3(0.28, 0.16, 0.1) : vec3(0.15, 0.25, 0.32), door);
      bRough = mix(bRough, 0.08, win * (1.0 - frame));
      bMetal = mix(bMetal, 0.5, win * (1.0 - frame));
      float lit = step(0.55, bHash(floor(g) + seed * 17.0));
      bEmit = vec3(1.0, 0.78, 0.5) * lit * win * (1.0 - frame) * uNight * 1.4;
      // Eaves shadow line under the roof.
      col *= 1.0 - 0.25 * smoothstep(vFace.y - 0.5, vFace.y, uv.y);
    }
  }
  diffuseColor.rgb = col;
}
`;

function noiseTex(): THREE.DataTexture {
  const size = 128;
  const a = fbmTile(size, 911, 8, 4, 0.55);
  const data = new Uint8Array(size * size * 4);
  for (let i = 0; i < size * size; i++) {
    data[i * 4] = data[i * 4 + 1] = data[i * 4 + 2] = a[i] * 255;
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

class Mesher {
  pos: number[] = [];
  nrm: number[] = [];
  uv: number[] = [];
  bld: number[] = [];
  face: number[] = [];
  idx: number[] = [];

  /** Quad a-b-c-d (counter-clockwise seen from outside) with per-vertex uvs. */
  quad(p: number[][], uvs: number[][], n: number[], attr: number[], face: number[]): void {
    const base = this.pos.length / 3;
    for (let k = 0; k < p.length; k++) {
      this.pos.push(p[k][0], p[k][1], p[k][2]);
      this.nrm.push(n[0], n[1], n[2]);
      this.uv.push(uvs[k][0], uvs[k][1]);
      this.bld.push(attr[0], attr[1], attr[2], attr[3]);
      this.face.push(face[0], face[1]);
    }
    // Fix the winding to face along n, whatever order the corners came in.
    const ux = p[1][0] - p[0][0];
    const uy = p[1][1] - p[0][1];
    const uz = p[1][2] - p[0][2];
    const vx = p[2][0] - p[0][0];
    const vy = p[2][1] - p[0][1];
    const vz = p[2][2] - p[0][2];
    const ok = (uy * vz - uz * vy) * n[0] + (uz * vx - ux * vz) * n[1] + (ux * vy - uy * vx) * n[2] >= 0;
    if (p.length === 4) {
      if (ok) this.idx.push(base, base + 1, base + 2, base, base + 2, base + 3);
      else this.idx.push(base, base + 2, base + 1, base, base + 3, base + 2);
    } else if (ok) this.idx.push(base, base + 1, base + 2);
    else this.idx.push(base, base + 2, base + 1);
  }

  geometry(): THREE.BufferGeometry {
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(this.pos, 3));
    g.setAttribute('normal', new THREE.Float32BufferAttribute(this.nrm, 3));
    g.setAttribute('uv', new THREE.Float32BufferAttribute(this.uv, 2));
    g.setAttribute('aBld', new THREE.Float32BufferAttribute(this.bld, 4));
    g.setAttribute('aFace', new THREE.Float32BufferAttribute(this.face, 2));
    g.setIndex(this.idx);
    g.computeBoundingSphere();
    return g;
  }
}

function floorHeight(b: Building): number {
  switch (b.kind) {
    case BUILDING.tower:
      return 3.6;
    case BUILDING.midrise:
      return 3.3;
    case BUILDING.shop:
      return 3.4;
    default:
      return 2.9;
  }
}

function addBuilding(m: Mesher, b: Building): void {
  const rx = -b.fz;
  const rz = b.fx;
  const hw = b.w / 2;
  const hd = b.d / 2;
  const top = b.floor + b.h;
  const fh = floorHeight(b);
  const corner = (sa: number, sb: number, y: number): number[] => [b.x + rx * sa * hw + b.fx * sb * hd, y, b.z + rz * sa * hw + b.fz * sb * hd];
  if (b.kind === BUILDING.silo) {
    addSilo(m, b);
    return;
  }
  // Walls: front (+f), right, back, left, each counter-clockwise from outside.
  const faces: { a: [number, number]; b: [number, number]; n: number[]; len: number; part: number }[] = [
    { a: [1, 1], b: [-1, 1], n: [b.fx, 0, b.fz], len: b.w, part: PART.front },
    { a: [-1, 1], b: [-1, -1], n: [-rx, 0, -rz], len: b.d, part: PART.wall },
    { a: [-1, -1], b: [1, -1], n: [-b.fx, 0, -b.fz], len: b.w, part: PART.wall },
    { a: [1, -1], b: [1, 1], n: [rx, 0, rz], len: b.d, part: PART.wall },
  ];
  for (const f of faces) {
    const attr = [b.kind, f.part, b.seed, fh];
    const face = [f.len, b.h];
    const lo = b.y0 - b.floor;
    m.quad(
      [corner(f.a[0], f.a[1], b.y0), corner(f.b[0], f.b[1], b.y0), corner(f.b[0], f.b[1], top), corner(f.a[0], f.a[1], top)],
      [
        [0, lo],
        [f.len, lo],
        [f.len, b.h],
        [0, b.h],
      ],
      f.n,
      attr,
      face,
    );
  }
  const roofAttr = [b.kind, PART.roof, b.seed, fh];
  if (b.roof === ROOF.flat) {
    m.quad(
      [corner(1, -1, top), corner(-1, -1, top), corner(-1, 1, top), corner(1, 1, top)],
      [
        [0, 0],
        [b.w, 0],
        [b.w, b.d],
        [0, b.d],
      ],
      [0, 1, 0],
      roofAttr,
      [b.w, -1],
    );
    return;
  }
  // Pitched roofs: the ridge runs along the front (width), with overhangs.
  const pitch = b.kind === BUILDING.warehouse ? 0.22 : b.kind === BUILDING.barn ? 0.75 : 0.62;
  const rise = hd * pitch;
  const o = 0.45;
  const eave = top - o * pitch;
  const ridgeY = top + rise;
  const ow = hw + o;
  const od = hd + o;
  const pt = (a: number, bb: number, y: number): number[] => [b.x + rx * a + b.fx * bb, y, b.z + rz * a + b.fz * bb];
  const slopeLen = Math.sqrt(od * od + (rise + o * pitch) * (rise + o * pitch));
  const nl = Math.sqrt(1 + pitch * pitch);
  if (b.roof === ROOF.gable || b.kind !== BUILDING.house) {
    // Front slope (faces +f and up), back slope.
    m.quad([pt(ow, od, eave), pt(-ow, od, eave), pt(-ow, 0, ridgeY), pt(ow, 0, ridgeY)], [[0, 0], [b.w + 2 * o, 0], [b.w + 2 * o, slopeLen], [0, slopeLen]], [(b.fx * pitch) / nl, 1 / nl, (b.fz * pitch) / nl], roofAttr, [b.w, 1]);
    m.quad([pt(-ow, -od, eave), pt(ow, -od, eave), pt(ow, 0, ridgeY), pt(-ow, 0, ridgeY)], [[0, 0], [b.w + 2 * o, 0], [b.w + 2 * o, slopeLen], [0, slopeLen]], [(-b.fx * pitch) / nl, 1 / nl, (-b.fz * pitch) / nl], roofAttr, [b.w, 1]);
    // Gable ends: triangles on the side walls.
    const gAttr = [b.kind, PART.gable, b.seed, fh];
    m.quad([pt(-hw, hd, top), pt(-hw, -hd, top), pt(-hw, 0, ridgeY)], [[0, b.h], [b.d, b.h], [hd, b.h + rise]], [-rx, 0, -rz], gAttr, [b.d, b.h + rise]);
    m.quad([pt(hw, -hd, top), pt(hw, hd, top), pt(hw, 0, ridgeY)], [[0, b.h], [b.d, b.h], [hd, b.h + rise]], [rx, 0, rz], gAttr, [b.d, b.h + rise]);
  } else {
    // Hipped: four slopes up to a short ridge.
    const rl = Math.max(0.5, hw - hd);
    const nf = [(b.fx * pitch) / nl, 1 / nl, (b.fz * pitch) / nl];
    const nb = [(-b.fx * pitch) / nl, 1 / nl, (-b.fz * pitch) / nl];
    const pitchS = rise / Math.max(0.5, hw - rl);
    const ns = Math.sqrt(1 + pitchS * pitchS);
    m.quad([pt(ow, od, eave), pt(-ow, od, eave), pt(-rl, 0, ridgeY), pt(rl, 0, ridgeY)], [[0, 0], [b.w, 0], [b.w, slopeLen], [0, slopeLen]], nf, roofAttr, [b.w, 1]);
    m.quad([pt(-ow, -od, eave), pt(ow, -od, eave), pt(rl, 0, ridgeY), pt(-rl, 0, ridgeY)], [[0, 0], [b.w, 0], [b.w, slopeLen], [0, slopeLen]], nb, roofAttr, [b.w, 1]);
    m.quad([pt(-ow, od, eave), pt(-ow, -od, eave), pt(-rl, 0, ridgeY)], [[0, 0], [b.d, 0], [hd, slopeLen]], [(-rx * pitchS) / ns, 1 / ns, (-rz * pitchS) / ns], roofAttr, [b.d, 1]);
    m.quad([pt(ow, -od, eave), pt(ow, od, eave), pt(rl, 0, ridgeY)], [[0, 0], [b.d, 0], [hd, slopeLen]], [(rx * pitchS) / ns, 1 / ns, (rz * pitchS) / ns], roofAttr, [b.d, 1]);
  }
}

function addSilo(m: Mesher, b: Building): void {
  const r = b.w / 2;
  const sides = 14;
  const top = b.floor + b.h;
  const attr = [b.kind, PART.wall, b.seed, 3];
  for (let k = 0; k < sides; k++) {
    const a0 = (k / sides) * Math.PI * 2;
    const a1 = ((k + 1) / sides) * Math.PI * 2;
    const p0 = [b.x + Math.cos(a0) * r, b.z + Math.sin(a0) * r];
    const p1 = [b.x + Math.cos(a1) * r, b.z + Math.sin(a1) * r];
    const am = (a0 + a1) / 2;
    const n = [Math.cos(am), 0, Math.sin(am)];
    const u0 = (k / sides) * Math.PI * 2 * r;
    const u1 = ((k + 1) / sides) * Math.PI * 2 * r;
    m.quad([[p1[0], b.y0, p1[1]], [p0[0], b.y0, p0[1]], [p0[0], top, p0[1]], [p1[0], top, p1[1]]], [[u1, b.y0 - b.floor], [u0, b.y0 - b.floor], [u0, b.h], [u1, b.h]], n, attr, [Math.PI * 2 * r, b.h]);
    // Conical cap.
    m.quad([[p1[0], top, p1[1]], [p0[0], top, p0[1]], [b.x, top + r * 0.7, b.z]], [[u1, 0], [u0, 0], [(u0 + u1) / 2, r]], [n[0] * 0.6, 0.8, n[2] * 0.6], [b.kind, PART.roof, b.seed, 3], [1, 1]);
  }
}

export interface BuildingsView {
  group: THREE.Group;
  uniforms: { uNight: { value: number } };
}

export function buildBuildingsView(world: World): BuildingsView {
  const group = new THREE.Group();
  group.name = 'buildings';
  const uniforms = { tBNoise: { value: noiseTex() }, uNight: { value: 0 } };
  const mat = new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 0.85, metalness: 0, envMapIntensity: 0.8 });
  mat.onBeforeCompile = (shader) => {
    Object.assign(shader.uniforms, uniforms);
    shader.vertexShader = shader.vertexShader
      .replace('#include <common>', `#include <common>\n${BLD_VERT_PARS}`)
      .replace('#include <begin_vertex>', `#include <begin_vertex>\n${BLD_VERT}`);
    shader.fragmentShader = shader.fragmentShader
      .replace('#include <common>', `#include <common>\n${BLD_FRAG_PARS}`)
      .replace('#include <map_fragment>', BLD_MAP)
      .replace('#include <roughnessmap_fragment>', 'float roughnessFactor = bRough;')
      .replace('#include <metalnessmap_fragment>', 'float metalnessFactor = bMetal;')
      .replace('#include <emissivemap_fragment>', '#include <emissivemap_fragment>\ntotalEmissiveRadiance += bEmit;');
  };
  mat.customProgramCacheKey = () => 'buildings-v1';
  patchMaterial(mat, { key: 'buildings' });
  const chunks = new Map<number, Mesher>();
  for (const b of world.buildings.list) {
    const k = Math.floor(b.x / CHUNK) * 4096 + Math.floor(b.z / CHUNK);
    let m = chunks.get(k);
    if (!m) chunks.set(k, (m = new Mesher()));
    addBuilding(m, b);
  }
  for (const m of chunks.values()) {
    const mesh = new THREE.Mesh(m.geometry(), mat);
    mesh.castShadow = true;
    mesh.receiveShadow = true;
    mesh.matrixAutoUpdate = false;
    group.add(mesh);
  }
  return { group, uniforms };
}
