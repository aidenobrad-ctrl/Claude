// Final post-processing pass: radial speed blur, exposure, AgX tone mapping
// with a punchy look, color grading, chromatic aberration, vignette, grain
// and dithering, written straight to the sRGB screen.
import * as THREE from 'three';

export const GradeShader = {
  uniforms: {
    tDiffuse: { value: null as THREE.Texture | null },
    uExposure: { value: 1.0 },
    uSpeedBlur: { value: 0.0 },
    uAberration: { value: 0.0 },
    uVignette: { value: 0.32 },
    uGrain: { value: 0.025 },
    uSaturation: { value: 1.04 },
    uContrast: { value: 1.06 },
    uShadowTint: { value: new THREE.Color(0.985, 1.0, 1.03) },
    uHighlightTint: { value: new THREE.Color(1.05, 1.0, 0.94) },
    uTime: { value: 0 },
    uResolution: { value: new THREE.Vector2(1280, 720) },
  },
  vertexShader: /* glsl */ `
varying vec2 vUv;
void main() {
  vUv = uv;
  gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
}`,
  fragmentShader: /* glsl */ `
uniform sampler2D tDiffuse;
uniform float uExposure;
uniform float uSpeedBlur;
uniform float uAberration;
uniform float uVignette;
uniform float uGrain;
uniform float uSaturation;
uniform float uContrast;
uniform vec3 uShadowTint;
uniform vec3 uHighlightTint;
uniform float uTime;
uniform vec2 uResolution;
varying vec2 vUv;

// AgX (Troy Sobotka), polynomial fit by Benjamin Wrensch, with a punchy look.
vec3 agxContrast(vec3 x) {
  vec3 x2 = x * x;
  vec3 x4 = x2 * x2;
  return 15.5 * x4 * x2 - 40.14 * x4 * x + 31.96 * x4 - 6.868 * x2 * x + 0.4298 * x2 + 0.1191 * x - 0.00232;
}
vec3 agx(vec3 c) {
  const mat3 m = mat3(0.842479062253094, 0.0423282422610123, 0.0423756549057051,
                      0.0784335999999992, 0.878468636469772, 0.0784336,
                      0.0792237451477643, 0.0791661274605434, 0.879142973793104);
  const float minEv = -12.47393;
  const float maxEv = 4.026069;
  c = m * c;
  c = clamp(log2(max(c, 1e-10)), minEv, maxEv);
  c = (c - minEv) / (maxEv - minEv);
  return agxContrast(c);
}
vec3 agxEotf(vec3 c) {
  const mat3 inv = mat3(1.19687900512017, -0.0528968517574562, -0.0529716355144438,
                        -0.0980208811401368, 1.15190312990417, -0.0980434501171241,
                        -0.0990297440797205, -0.0989611768448433, 1.15107367264116);
  return inv * c;
}
vec3 agxLook(vec3 c) {
  float l = dot(c, vec3(0.2126, 0.7152, 0.0722));
  vec3 slope = vec3(1.0);
  vec3 power = vec3(1.35);
  c = pow(c * slope, power);
  return l + 1.22 * (c - l);
}
float hash(vec2 p) {
  return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}
vec3 sampleScene(vec2 uv) {
  vec3 c = texture2D(tDiffuse, uv).rgb;
  if (uAberration > 0.0) {
    vec2 d = (uv - 0.5) * uAberration;
    c.r = texture2D(tDiffuse, uv - d).r;
    c.b = texture2D(tDiffuse, uv + d).b;
  }
  return c;
}
void main() {
  vec2 uv = vUv;
  vec3 col = sampleScene(uv);
  // Radial speed blur, strongest at the edges.
  if (uSpeedBlur > 0.0005) {
    vec2 toC = uv - 0.5;
    float edge = smoothstep(0.1, 0.75, length(toC * vec2(uResolution.x / uResolution.y, 1.0)));
    vec2 stepv = toC * uSpeedBlur * edge / 6.0;
    vec3 acc = col;
    for (int i = 1; i < 7; i++) acc += sampleScene(uv - stepv * float(i));
    col = acc / 7.0;
  }
  col *= uExposure;
  col = agx(col);
  col = agxLook(col);
  col = agxEotf(col);
  col = clamp(col, 0.0, 1.0);
  // Grade in display space: split toning, contrast and saturation.
  float luma = dot(col, vec3(0.2126, 0.7152, 0.0722));
  col *= mix(uShadowTint, uHighlightTint, smoothstep(0.15, 0.85, luma));
  col = (col - 0.5) * uContrast + 0.5;
  col = mix(vec3(dot(col, vec3(0.2126, 0.7152, 0.0722))), col, uSaturation);
  // Vignette.
  vec2 v = (vUv - 0.5) * vec2(uResolution.x / uResolution.y, 1.0);
  col *= 1.0 - uVignette * smoothstep(0.35, 1.05, length(v));
  col = clamp(col, 0.0, 1.0);
  // AgX output above is already display-referred (sRGB-like transfer).
  float n = hash(vUv * uResolution + fract(uTime) * 61.0) - 0.5;
  col += n * uGrain;
  col += (hash(vUv * uResolution * 1.3 + 7.0) - 0.5) / 255.0;
  gl_FragColor = vec4(col, 1.0);
}`,
};
