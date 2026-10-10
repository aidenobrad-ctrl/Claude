// Sky, clouds, sun and atmospheric perspective.
//
// The sky is the Preetham scattering model (three's Sky). A procedural
// cloud layer floats above it. Every lit material is patched (see
// materials.ts) with height fog that thickens toward the ground and glows
// toward the sun, so distant hills fade into the sky like real air.
import * as THREE from 'three';
import { Sky as PreethamSky } from 'three/examples/jsm/objects/Sky.js';

export type TimeOfDay = 'morning' | 'noon' | 'golden' | 'sunset' | 'night';

/** Shared uniforms injected into every patched material. */
export const atmoUniforms = {
  uSunDir: { value: new THREE.Vector3(0.4, 0.5, -0.7).normalize() },
  uFogColor: { value: new THREE.Color(0.62, 0.72, 0.84) },
  uSunScatter: { value: new THREE.Color(1.0, 0.82, 0.6) },
  uFogDensity: { value: 0.00011 },
  uFogFalloff: { value: 0.0011 },
  uTime: { value: 0 },
  uWind: { value: new THREE.Vector2(1, 0.4) },
};

interface SkyPreset {
  elevation: number; // degrees
  azimuth: number; // degrees, 0 = north, clockwise
  turbidity: number;
  rayleigh: number;
  mie: number;
  mieG: number;
  sunColor: number;
  sunIntensity: number;
  ambientSky: number;
  ambientGround: number;
  ambient: number;
  fog: number;
  fogDensity: number;
  scatter: number;
  exposure: number;
  cloudCover: number;
  cloudLight: number;
  cloudShadow: number;
}

export const SKY_PRESETS: Record<TimeOfDay, SkyPreset> = {
  morning: { elevation: 16, azimuth: 105, turbidity: 3.2, rayleigh: 1.6, mie: 0.005, mieG: 0.82, sunColor: 0xffe2c0, sunIntensity: 3.0, ambientSky: 0xbfd6ff, ambientGround: 0x6a6450, ambient: 0.9, fog: 0xa9bfdc, fogDensity: 0.00014, scatter: 0xffd2a0, exposure: 1.0, cloudCover: 0.42, cloudLight: 0xfff0e0, cloudShadow: 0x8090a8 },
  noon: { elevation: 58, azimuth: 160, turbidity: 2.4, rayleigh: 1.1, mie: 0.004, mieG: 0.8, sunColor: 0xfff6ea, sunIntensity: 3.4, ambientSky: 0xc8dcff, ambientGround: 0x5f6a4a, ambient: 1.0, fog: 0xb4c8e2, fogDensity: 0.0001, scatter: 0xfff0d8, exposure: 0.85, cloudCover: 0.38, cloudLight: 0xffffff, cloudShadow: 0x9aa6b8 },
  golden: { elevation: 9, azimuth: 250, turbidity: 4.5, rayleigh: 2.2, mie: 0.006, mieG: 0.86, sunColor: 0xffc58a, sunIntensity: 3.6, ambientSky: 0xb9c3d8, ambientGround: 0x6b5a44, ambient: 0.75, fog: 0xc6b6ae, fogDensity: 0.00016, scatter: 0xffb070, exposure: 1.05, cloudCover: 0.46, cloudLight: 0xffd4a8, cloudShadow: 0x7c7890 },
  sunset: { elevation: 2.5, azimuth: 262, turbidity: 6, rayleigh: 2.8, mie: 0.007, mieG: 0.9, sunColor: 0xff9a5c, sunIntensity: 2.4, ambientSky: 0x8a9ccf, ambientGround: 0x5a4436, ambient: 0.62, fog: 0xc89a8a, fogDensity: 0.00019, scatter: 0xff8a50, exposure: 1.25, cloudCover: 0.5, cloudLight: 0xffa070, cloudShadow: 0x5e5a78 },
  night: { elevation: -14, azimuth: 280, turbidity: 2, rayleigh: 0.5, mie: 0.003, mieG: 0.8, sunColor: 0x9fb4ff, sunIntensity: 0.25, ambientSky: 0x2a3a66, ambientGround: 0x101420, ambient: 0.35, fog: 0x0e1424, fogDensity: 0.00012, scatter: 0x24345a, exposure: 1.6, cloudCover: 0.4, cloudLight: 0x5a6a90, cloudShadow: 0x101626 },
};

const cloudVert = /* glsl */ `
varying vec3 vWorld;
void main() {
  vec4 w = modelMatrix * vec4(position, 1.0);
  vWorld = w.xyz;
  gl_Position = projectionMatrix * viewMatrix * w;
}`;

const cloudFrag = /* glsl */ `
uniform sampler2D uNoise;
uniform vec3 uSunDir;
uniform vec3 uLight;
uniform vec3 uShadow;
uniform vec3 uSky;
uniform float uCover;
uniform float uTime;
uniform vec2 uWind;
varying vec3 vWorld;
float density(vec2 p) {
  float n = texture2D(uNoise, p * 0.00011 + uWind * uTime * 0.0000045).r * 0.55;
  n += texture2D(uNoise, p * 0.00031 - uWind * uTime * 0.000009).r * 0.3;
  n += texture2D(uNoise, p * 0.0011 + uWind * uTime * 0.00002).r * 0.15;
  return smoothstep(uCover, uCover + 0.28, n);
}
void main() {
  vec3 rd = normalize(vWorld - cameraPosition);
  if (rd.y < 0.01) discard;
  vec2 p = vWorld.xz;
  float d = density(p);
  if (d < 0.01) discard;
  // Light marching toward the sun: thick clouds darken underneath.
  vec2 toSun = normalize(uSunDir.xz + 1e-4) * 260.0;
  float occ = density(p + toSun) * 0.6 + density(p + toSun * 2.2) * 0.4;
  float lit = clamp(1.0 - occ * 0.85, 0.0, 1.0);
  float view = max(dot(rd, uSunDir), 0.0);
  float silver = pow(view, 10.0) * (1.0 - d) * 2.6 + pow(view, 3.0) * 0.35;
  vec3 col = mix(uShadow, uLight, lit) + uLight * silver;
  // Fade into the horizon haze well before the edge of the layer.
  float hd = length(vWorld.xz - cameraPosition.xz);
  float fade = smoothstep(0.02, 0.2, rd.y) * (1.0 - smoothstep(9000.0, 26000.0, hd));
  col = mix(uSky, col, fade);
  gl_FragColor = vec4(col, d * fade * 0.96);
}`;

/** Tileable value-noise texture (fbm) drawn on a canvas, for clouds and detail. */
export function noiseTexture(size = 256, seed = 7): THREE.Texture {
  const c = document.createElement('canvas');
  c.width = c.height = size;
  const ctx = c.getContext('2d') as CanvasRenderingContext2D;
  const img = ctx.createImageData(size, size);
  let s = seed >>> 0;
  const rnd = (): number => {
    s = (s * 1664525 + 1013904223) >>> 0;
    return s / 4294967296;
  };
  const grids = [4, 8, 16, 32, 64].map((g) => {
    const a = new Float32Array(g * g);
    for (let i = 0; i < a.length; i++) a[i] = rnd();
    return { g, a };
  });
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      let v = 0;
      let amp = 0.5;
      let norm = 0;
      for (const { g, a } of grids) {
        const fx = (x / size) * g;
        const fy = (y / size) * g;
        const x0 = Math.floor(fx);
        const y0 = Math.floor(fy);
        const tx = fx - x0;
        const ty = fy - y0;
        const sx = tx * tx * (3 - 2 * tx);
        const sy = ty * ty * (3 - 2 * ty);
        const at = (i: number, j: number): number => a[((j % g) + g) % g * g + (((i % g) + g) % g)];
        const top = at(x0, y0) + (at(x0 + 1, y0) - at(x0, y0)) * sx;
        const bot = at(x0, y0 + 1) + (at(x0 + 1, y0 + 1) - at(x0, y0 + 1)) * sx;
        v += (top + (bot - top) * sy) * amp;
        norm += amp;
        amp *= 0.55;
      }
      const k = (y * size + x) * 4;
      const b = Math.round((v / norm) * 255);
      img.data[k] = img.data[k + 1] = img.data[k + 2] = b;
      img.data[k + 3] = 255;
    }
  }
  ctx.putImageData(img, 0, 0);
  const t = new THREE.CanvasTexture(c);
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.colorSpace = THREE.NoColorSpace;
  t.needsUpdate = true;
  return t;
}

export class Atmosphere {
  readonly sky: PreethamSky;
  readonly clouds: THREE.Mesh;
  readonly sun = new THREE.DirectionalLight(0xffffff, 3);
  readonly hemi = new THREE.HemisphereLight(0xcfe3ff, 0x5a6b45, 1);
  readonly sunDir = atmoUniforms.uSunDir.value;
  private cloudMat: THREE.ShaderMaterial;
  preset: SkyPreset = SKY_PRESETS.golden;
  time: TimeOfDay = 'golden';
  exposure = 1;
  private envRT: THREE.WebGLRenderTarget | null = null;

  constructor(private scene: THREE.Scene, withClouds: boolean) {
    this.sky = new PreethamSky();
    this.sky.scale.setScalar(4500);
    this.sky.frustumCulled = false;
    this.sky.renderOrder = -2;
    (this.sky.material as THREE.ShaderMaterial).depthWrite = false;
    scene.add(this.sky, this.sun, this.sun.target, this.hemi);
    this.cloudMat = new THREE.ShaderMaterial({
      vertexShader: cloudVert,
      fragmentShader: cloudFrag,
      uniforms: {
        uNoise: { value: noiseTexture(256, 99) },
        uSunDir: atmoUniforms.uSunDir,
        uLight: { value: new THREE.Color() },
        uShadow: { value: new THREE.Color() },
        uSky: { value: new THREE.Color() },
        uCover: { value: 0.45 },
        uTime: atmoUniforms.uTime,
        uWind: atmoUniforms.uWind,
      },
      transparent: true,
      depthWrite: false,
      fog: false,
    });
    this.clouds = new THREE.Mesh(new THREE.PlaneGeometry(60000, 60000, 1, 1).rotateX(Math.PI / 2), this.cloudMat);
    this.clouds.userData.noAO = true;
    this.sky.userData.noAO = true;
    this.clouds.renderOrder = -1;
    this.clouds.frustumCulled = false;
    this.clouds.visible = withClouds;
    scene.add(this.clouds);
    this.setTime('golden');
  }

  setTime(t: TimeOfDay): void {
    this.time = t;
    const p = SKY_PRESETS[t];
    this.preset = p;
    const el = (p.elevation * Math.PI) / 180;
    const az = (p.azimuth * Math.PI) / 180;
    // Azimuth clockwise from north (-Z).
    this.sunDir.set(Math.sin(az) * Math.cos(el), Math.sin(el), -Math.cos(az) * Math.cos(el)).normalize();
    const u = (this.sky.material as THREE.ShaderMaterial).uniforms;
    u.turbidity.value = p.turbidity;
    u.rayleigh.value = p.rayleigh;
    u.mieCoefficient.value = p.mie;
    u.mieDirectionalG.value = p.mieG;
    u.sunPosition.value.copy(this.sunDir).multiplyScalar(1000);
    this.sun.color.set(p.sunColor);
    this.sun.intensity = p.elevation > 0 ? p.sunIntensity : p.sunIntensity * 0.4;
    this.hemi.color.set(p.ambientSky);
    this.hemi.groundColor.set(p.ambientGround);
    this.hemi.intensity = p.ambient;
    atmoUniforms.uFogColor.value.set(p.fog);
    atmoUniforms.uSunScatter.value.set(p.scatter);
    atmoUniforms.uFogDensity.value = p.fogDensity;
    const cu = this.cloudMat.uniforms;
    (cu.uLight.value as THREE.Color).set(p.cloudLight);
    (cu.uShadow.value as THREE.Color).set(p.cloudShadow);
    (cu.uSky.value as THREE.Color).set(p.fog);
    cu.uCover.value = p.cloudCover;
    this.exposure = p.exposure;
  }

  update(dt: number, camera: THREE.Camera, focus: THREE.Vector3, shadowDistance: number): void {
    atmoUniforms.uTime.value += dt;
    this.sky.position.copy(camera.position);
    this.clouds.position.set(camera.position.x, 1600, camera.position.z);
    this.sun.target.position.copy(focus);
    this.sun.position.copy(focus).addScaledVector(this.sunDir, shadowDistance);
  }

  /** PMREM environment from the sky and clouds, for reflections. */
  buildEnvironment(renderer: THREE.WebGLRenderer): THREE.Texture {
    const pm = new THREE.PMREMGenerator(renderer);
    const env = new THREE.Scene();
    const sky = new PreethamSky();
    sky.scale.setScalar(900);
    (sky.material as THREE.ShaderMaterial).uniforms = (this.sky.material as THREE.ShaderMaterial).uniforms;
    env.add(sky);
    const cl = this.clouds.clone();
    cl.position.set(0, 160, 0);
    cl.scale.setScalar(0.1);
    env.add(cl);
    // Darker ground below the horizon, tinted by the ambient ground color.
    const g = new THREE.Mesh(new THREE.CircleGeometry(800, 32).rotateX(-Math.PI / 2), new THREE.MeshBasicMaterial({ color: new THREE.Color(this.preset.ambientGround).multiplyScalar(0.6) }));
    g.position.y = -5;
    env.add(g);
    this.envRT?.dispose();
    this.envRT = pm.fromScene(env, 0.03, 1, 2000);
    pm.dispose();
    return this.envRT.texture;
  }
}
