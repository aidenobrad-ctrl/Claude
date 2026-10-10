// Water: sea, lake and river share one material. Meshes come from the
// terrain tiles (same grid, per-vertex depth and flow), plus a ring of open
// sea around the map. Lighting is the standard PBR path (sky reflections from
// the environment map, sun glints from the cascades); the shader adds
// scrolling ripple normals, depth color, shallow transparency and foam, and
// keeps reflections at full strength where the water itself is see-through.
import * as THREE from 'three';
import { fbmTile } from './terrain-textures';
import { patchMaterial } from '../../engine/render/materials';
import { atmoUniforms } from '../../engine/render/atmosphere';
import { WORLD_HALF } from '../island';

const WATER_VERT_PARS = /* glsl */ `
attribute vec4 aWater;
varying vec4 vWater;
varying vec3 vWaterWorld;
`;
const WATER_VERT = /* glsl */ `
vWater = aWater;
vWaterWorld = (modelMatrix * vec4(transformed, 1.0)).xyz;
`;

const WATER_FRAG_PARS = /* glsl */ `
uniform sampler2D tRipple;
uniform sampler2D tFoam;
uniform float uTime;
uniform vec2 uWind;
varying vec4 vWater;
varying vec3 vWaterWorld;
float waterRough;
vec3 waterN;
vec3 rippleN(vec2 p, float t, vec2 flow, float kind) {
  vec2 w = kind > 1.5 ? flow * 1.4 : uWind;
  vec2 w2 = kind > 1.5 ? flow * 0.9 + vec2(-flow.y, flow.x) * 0.15 : vec2(-uWind.y, uWind.x) * 0.6 + uWind * 0.4;
  vec3 a = texture2D(tRipple, p * 0.045 + w * t * 0.018).xyz * 2.0 - 1.0;
  vec3 b = texture2D(tRipple, p * 0.11 - w2 * t * 0.031 + 0.37).xyz * 2.0 - 1.0;
  vec3 c = texture2D(tRipple, p * 0.012 + w * t * 0.006 + 0.71).xyz * 2.0 - 1.0;
  return vec3(a.xy * 0.55 + b.xy * 0.35 + c.xy * 0.6, 1.0);
}
`;

const WATER_MAP = /* glsl */ `
{
  float depth = vWater.x;
  float kind = vWater.w;
  vec2 p = vWaterWorld.xz;
  float dist = length(vWaterWorld - cameraPosition);
  // Ripples fade with distance so the far water does not shimmer.
  float fade = 1.0 - smoothstep(60.0, 900.0, dist);
  vec3 rn = rippleN(p, uTime, vWater.yz, kind);
  float amp = kind > 0.5 ? 0.22 : 0.42;
  waterN = normalize(vec3(rn.x * amp * (0.25 + 0.75 * fade), 1.0, rn.y * amp * (0.25 + 0.75 * fade)));
  // Sub-pixel waves in the distance read as a slightly rougher surface.
  waterRough = mix(0.08, 0.035, fade);
  float dpos = max(depth, 0.0);
  vec3 deep = kind > 0.5 ? vec3(0.010, 0.035, 0.030) : vec3(0.004, 0.030, 0.050);
  vec3 shallow = kind > 0.5 ? vec3(0.05, 0.14, 0.10) : vec3(0.03, 0.20, 0.19);
  vec3 col = mix(shallow, deep, 1.0 - exp(-dpos / 4.0));
  float alpha = 0.12 + 0.88 * smoothstep(0.0, kind > 1.5 ? 1.6 : 3.2, dpos);
  // Foam: a lapping band on the shore and sparse whitecaps on the open sea.
  float fn = texture2D(tFoam, p * 0.09 + rn.xy * 0.03 + uWind * uTime * 0.01).r;
  float lap = 0.5 + 0.5 * sin(uTime * 1.1 - dpos * 5.0 + fn * 4.0);
  float shore = (1.0 - smoothstep(0.0, 0.9, dpos)) * smoothstep(-0.2, 0.05, depth);
  float foam = shore * smoothstep(0.35, 0.75, fn * 0.7 + lap * 0.5);
  if (kind < 0.5) foam = max(foam, smoothstep(0.82, 0.9, texture2D(tFoam, p * 0.021 - uWind * uTime * 0.004).g) * smoothstep(6.0, 30.0, dpos) * 0.6 * fade);
  if (kind > 1.5) foam = max(foam, smoothstep(0.7, 0.85, fn) * 0.35 * shore);
  col = mix(col, vec3(0.75, 0.78, 0.78), foam);
  alpha = max(alpha, foam * 0.9);
  waterRough = mix(waterRough, 0.6, foam);
  diffuseColor.rgb = col;
  diffuseColor.a = alpha;
}
`;

const WATER_NORMAL = /* glsl */ `
normal = normalize((viewMatrix * vec4(waterN, 0.0)).xyz);
`;

/** Premultiplied output: the diffuse body fades with alpha, reflections do not. */
const WATER_OUT = /* glsl */ `
{
  // Grazing reflections mostly see the far shore, not open sky: darken the
  // environment reflection for rays near the horizon.
  vec3 rv = reflect(-geometryViewDir, normal);
  vec3 rw = (vec4(rv, 0.0) * viewMatrix).xyz;
  float horizon = mix(0.42, 1.0, smoothstep(0.0, 0.2, rw.y));
  vec3 spec = reflectedLight.indirectSpecular * horizon + reflectedLight.directSpecular;
  gl_FragColor = vec4(totalDiffuse * diffuseColor.a + spec + totalEmissiveRadiance, diffuseColor.a);
}
`;

function rippleTexture(size = 256): THREE.DataTexture {
  // Smooth, tileable height field: long swells plus short chop.
  const a = fbmTile(size, 301, 4, 3, 0.5);
  const b = fbmTile(size, 302, 8, 4, 0.55);
  const h = new Float32Array(size * size);
  for (let i = 0; i < h.length; i++) h[i] = a[i] * 0.65 + b[i] * 0.35;
  const data = new Uint8Array(size * size * 4);
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const i = y * size + x;
      const dx = (h[y * size + ((x + 1) % size)] - h[y * size + ((x + size - 1) % size)]) * 14;
      const dy = (h[((y + 1) % size) * size + x] - h[((y + size - 1) % size) * size + x]) * 14;
      const l = Math.sqrt(dx * dx + dy * dy + 1);
      data[i * 4] = (-dx / l) * 127 + 128;
      data[i * 4 + 1] = (-dy / l) * 127 + 128;
      data[i * 4 + 2] = (1 / l) * 127 + 128;
      data[i * 4 + 3] = 255;
    }
  }
  const t = new THREE.DataTexture(data, size, size, THREE.RGBAFormat);
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.minFilter = THREE.LinearMipmapLinearFilter;
  t.magFilter = THREE.LinearFilter;
  t.generateMipmaps = true;
  t.needsUpdate = true;
  return t;
}

function foamTexture(size = 256): THREE.DataTexture {
  const a = fbmTile(size, 401, 16, 4, 0.6);
  const b = fbmTile(size, 402, 8, 5, 0.55);
  const data = new Uint8Array(size * size * 4);
  for (let i = 0; i < size * size; i++) {
    data[i * 4] = a[i] * 255;
    data[i * 4 + 1] = b[i] * 255;
    data[i * 4 + 2] = 0;
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

export function createWaterMaterial(): THREE.MeshStandardMaterial {
  const uniforms = {
    tRipple: { value: rippleTexture() },
    tFoam: { value: foamTexture() },
    uTime: atmoUniforms.uTime,
    uWind: { value: new THREE.Vector2(0.8, 0.6) },
  };
  const mat = new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 0.05, metalness: 0, transparent: true, envMapIntensity: 1 });
  mat.blending = THREE.CustomBlending;
  mat.blendSrc = THREE.OneFactor;
  mat.blendDst = THREE.OneMinusSrcAlphaFactor;
  mat.depthWrite = true;
  mat.onBeforeCompile = (shader) => {
    Object.assign(shader.uniforms, uniforms);
    shader.vertexShader = shader.vertexShader
      .replace('#include <common>', `#include <common>\n${WATER_VERT_PARS}`)
      .replace('#include <begin_vertex>', `#include <begin_vertex>\n${WATER_VERT}`);
    shader.fragmentShader = shader.fragmentShader
      .replace('#include <common>', `#include <common>\n${WATER_FRAG_PARS}`)
      .replace('#include <map_fragment>', WATER_MAP)
      .replace('#include <roughnessmap_fragment>', 'float roughnessFactor = waterRough;')
      .replace('#include <normal_fragment_maps>', WATER_NORMAL)
      .replace('#include <opaque_fragment>', WATER_OUT);
  };
  mat.customProgramCacheKey = () => 'water-v1';
  return patchMaterial(mat, { key: 'water' });
}

/** Open sea around the square map, out to the horizon. */
export function oceanRing(mat: THREE.Material, extent = 60000): THREE.Mesh {
  const a = WORLD_HALF;
  const b = extent;
  // Four quads around the inner square (outer square minus inner square).
  const rects: [number, number, number, number][] = [
    [-b, -b, b, -a],
    [-b, a, b, b],
    [-b, -a, -a, a],
    [a, -a, b, a],
  ];
  const pos: number[] = [];
  const attr: number[] = [];
  const idx: number[] = [];
  for (const [x0, z0, x1, z1] of rects) {
    const base = pos.length / 3;
    pos.push(x0, 0, z0, x1, 0, z0, x1, 0, z1, x0, 0, z1);
    for (let k = 0; k < 4; k++) attr.push(60, 0, 0, 0);
    // Counter-clockwise from above.
    idx.push(base, base + 2, base + 1, base, base + 3, base + 2);
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  g.setAttribute('normal', new THREE.Float32BufferAttribute(new Array((pos.length / 3) * 3).fill(0).map((_, i) => (i % 3 === 1 ? 1 : 0)), 3));
  g.setAttribute('aWater', new THREE.Float32BufferAttribute(attr, 4));
  g.setIndex(idx);
  g.computeBoundingSphere();
  const m = new THREE.Mesh(g, mat);
  m.receiveShadow = false;
  m.frustumCulled = false;
  m.renderOrder = 1;
  return m;
}
