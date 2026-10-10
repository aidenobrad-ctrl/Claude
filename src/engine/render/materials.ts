// Material patching: every lit material gets atmospheric height fog (and
// cascaded shadows when enabled). Patches chain, so a material can also
// carry its own onBeforeCompile (terrain splats, wind).
import * as THREE from 'three';
import { atmoUniforms } from './atmosphere';
import type { CSM } from 'three/examples/jsm/csm/CSM.js';

const FOG_PARS_VERTEX = /* glsl */ `
#ifdef USE_FOG
  varying vec3 vAtmoWorld;
#endif`;

const FOG_VERTEX = /* glsl */ `
#ifdef USE_FOG
  vec4 atmoW = vec4(transformed, 1.0);
  #ifdef USE_INSTANCING
    atmoW = instanceMatrix * atmoW;
  #endif
  vAtmoWorld = (modelMatrix * atmoW).xyz;
#endif`;

const FOG_PARS_FRAGMENT = /* glsl */ `
#ifdef USE_FOG
  varying vec3 vAtmoWorld;
  uniform vec3 uSunDir;
  uniform vec3 uFogColor;
  uniform vec3 uSunScatter;
  uniform float uFogDensity;
  uniform float uFogFalloff;
#endif`;

/** Height fog with sun in-scattering (analytic integral of a*exp(-k*h) along the ray). */
export const FOG_FRAGMENT = /* glsl */ `
#ifdef USE_FOG
  {
    vec3 ray = vAtmoWorld - cameraPosition;
    float dist = length(ray);
    vec3 dir = ray / max(dist, 1e-3);
    float k = uFogFalloff;
    float e = k * dir.y * dist;
    float integral = abs(e) > 1e-4 ? (1.0 - exp(-e)) / e : 1.0 - 0.5 * e;
    float optical = uFogDensity * exp(-k * max(cameraPosition.y, -50.0)) * dist * integral;
    float f = 1.0 - exp(-optical);
    float sunAmt = pow(max(dot(dir, uSunDir), 0.0), 7.0);
    vec3 fogCol = mix(uFogColor, uSunScatter, sunAmt);
    gl_FragColor.rgb = mix(gl_FragColor.rgb, fogCol, f);
  }
#endif`;

export interface PatchOptions {
  /** Extra shader edits applied after the fog patch. */
  extra?: (shader: THREE.WebGLProgramParametersWithUniforms) => void;
  key?: string;
}

let csm: CSM | null = null;
export function setCsm(c: CSM | null): void {
  csm = c;
}

const patched = new WeakSet<THREE.Material>();

/** Patch a material for atmosphere fog (and CSM if active). Idempotent. */
export function patchMaterial<T extends THREE.Material>(mat: T, opts: PatchOptions = {}): T {
  if (patched.has(mat)) return mat;
  if ((mat as unknown as THREE.ShaderMaterial).isShaderMaterial) return mat;
  patched.add(mat);
  (mat as unknown as { fog: boolean }).fog = true;
  // Every lit material must go through CSM, or it would see all cascade
  // lights at full strength (found in M2: Lambert grass was 4x too bright).
  const lit = mat as unknown as { isMeshStandardMaterial?: boolean; isMeshLambertMaterial?: boolean; isMeshPhongMaterial?: boolean; isMeshToonMaterial?: boolean };
  // CSM.setupMaterial replaces onBeforeCompile, so keep the material's own
  // hook (the terrain splat, wind sway) and run it first.
  const own = mat.onBeforeCompile;
  if (csm && (lit.isMeshStandardMaterial || lit.isMeshLambertMaterial || lit.isMeshPhongMaterial || lit.isMeshToonMaterial)) csm.setupMaterial(mat);
  const viaCsm = mat.onBeforeCompile !== own ? mat.onBeforeCompile : null;
  mat.onBeforeCompile = (shader, renderer) => {
    own?.call(mat, shader, renderer);
    viaCsm?.call(mat, shader, renderer);
    shader.uniforms.uSunDir = atmoUniforms.uSunDir;
    shader.uniforms.uFogColor = atmoUniforms.uFogColor;
    shader.uniforms.uSunScatter = atmoUniforms.uSunScatter;
    shader.uniforms.uFogDensity = atmoUniforms.uFogDensity;
    shader.uniforms.uFogFalloff = atmoUniforms.uFogFalloff;
    shader.vertexShader = shader.vertexShader
      .replace('#include <fog_pars_vertex>', FOG_PARS_VERTEX)
      .replace('#include <fog_vertex>', FOG_VERTEX);
    shader.fragmentShader = shader.fragmentShader
      .replace('#include <fog_pars_fragment>', FOG_PARS_FRAGMENT)
      .replace('#include <fog_fragment>', FOG_FRAGMENT);
    opts.extra?.(shader);
  };
  const key = `atmo:${opts.key ?? ''}`;
  const prevKey = mat.customProgramCacheKey.bind(mat);
  mat.customProgramCacheKey = () => `${prevKey()}|${key}`;
  mat.needsUpdate = true;
  return mat;
}

/** Patch every material under an object. */
export function patchTree(root: THREE.Object3D): void {
  root.traverse((o) => {
    const m = (o as THREE.Mesh).material;
    if (!m) return;
    if (Array.isArray(m)) m.forEach((x) => patchMaterial(x));
    else patchMaterial(m);
  });
}
