// Sky dome with a sun, horizon haze and a time-of-day palette, plus the
// sun and ambient lights and a reflection environment built from the sky.
import * as THREE from 'three';

const vert = /* glsl */ `
varying vec3 vDir;
void main() {
  vDir = normalize(position);
  vec4 p = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
  gl_Position = p.xyww;
}`;

const frag = /* glsl */ `
uniform vec3 uZenith;
uniform vec3 uHorizon;
uniform vec3 uGround;
uniform vec3 uSunDir;
uniform vec3 uSunColor;
uniform float uSunSize;
varying vec3 vDir;
void main() {
  vec3 d = normalize(vDir);
  float h = d.y;
  vec3 col = mix(uHorizon, uZenith, pow(clamp(h, 0.0, 1.0), 0.55));
  col = mix(col, uGround, smoothstep(0.0, -0.08, h));
  float s = max(dot(d, uSunDir), 0.0);
  // Glow around the sun and a crisp disc.
  col += uSunColor * (pow(s, 8.0) * 0.25 + pow(s, 64.0) * 0.6);
  col += uSunColor * smoothstep(1.0 - uSunSize, 1.0 - uSunSize * 0.6, s) * 8.0;
  // Horizon haze brightening toward the sun.
  col += uSunColor * pow(1.0 - abs(h), 6.0) * pow(s, 2.0) * 0.18;
  gl_FragColor = vec4(col, 1.0);
  #include <tonemapping_fragment>
  #include <colorspace_fragment>
}`;

export interface SkyState {
  zenith: THREE.Color;
  horizon: THREE.Color;
  ground: THREE.Color;
  sunDir: THREE.Vector3;
  sunColor: THREE.Color;
  sunIntensity: number;
  ambient: number;
  fogColor: THREE.Color;
}

export function daySky(): SkyState {
  return {
    zenith: new THREE.Color(0x2a68b8),
    horizon: new THREE.Color(0xb4cfe6),
    ground: new THREE.Color(0x6e7c86),
    sunDir: new THREE.Vector3(0.45, 0.62, -0.64).normalize(),
    sunColor: new THREE.Color(0xfff2dc),
    sunIntensity: 3.0,
    ambient: 1.0,
    fogColor: new THREE.Color(0xc4d6e6),
  };
}

export class Sky {
  readonly mesh: THREE.Mesh;
  readonly sun = new THREE.DirectionalLight(0xffffff, 3);
  readonly hemi = new THREE.HemisphereLight(0xcfe3ff, 0x5a6b45, 1);
  readonly material: THREE.ShaderMaterial;
  state: SkyState = daySky();
  private envRT: THREE.WebGLRenderTarget | null = null;

  constructor(private scene: THREE.Scene) {
    this.material = new THREE.ShaderMaterial({
      vertexShader: vert,
      fragmentShader: frag,
      uniforms: {
        uZenith: { value: new THREE.Color() },
        uHorizon: { value: new THREE.Color() },
        uGround: { value: new THREE.Color() },
        uSunDir: { value: new THREE.Vector3() },
        uSunColor: { value: new THREE.Color() },
        uSunSize: { value: 0.0006 },
      },
      side: THREE.BackSide,
      depthWrite: false,
      fog: false,
    });
    this.mesh = new THREE.Mesh(new THREE.SphereGeometry(1, 32, 16), this.material);
    this.mesh.scale.setScalar(3000);
    this.mesh.renderOrder = -1;
    this.mesh.frustumCulled = false;
    scene.add(this.mesh, this.sun, this.sun.target, this.hemi);
    this.apply();
  }

  apply(): void {
    const s = this.state;
    const u = this.material.uniforms;
    (u.uZenith.value as THREE.Color).copy(s.zenith);
    (u.uHorizon.value as THREE.Color).copy(s.horizon);
    (u.uGround.value as THREE.Color).copy(s.ground);
    (u.uSunDir.value as THREE.Vector3).copy(s.sunDir);
    (u.uSunColor.value as THREE.Color).copy(s.sunColor);
    this.sun.color.copy(s.sunColor);
    this.sun.intensity = s.sunIntensity;
    this.hemi.intensity = s.ambient;
    if (this.scene.fog) (this.scene.fog as THREE.Fog).color.copy(s.fogColor);
  }

  /** Keep the dome and the shadow camera centered on the viewer. */
  follow(center: THREE.Vector3, camera: THREE.Camera): void {
    this.mesh.position.copy(camera.position);
    this.sun.target.position.copy(center);
    this.sun.position.copy(center).addScaledVector(this.state.sunDir, 120);
  }

  /** Render the sky into a PMREM environment map for reflections. */
  buildEnvironment(renderer: THREE.WebGLRenderer): THREE.Texture {
    const pm = new THREE.PMREMGenerator(renderer);
    const envScene = new THREE.Scene();
    const dome = new THREE.Mesh(new THREE.SphereGeometry(1, 32, 16), this.material.clone());
    dome.scale.setScalar(100);
    envScene.add(dome);
    // A ground disc so reflections show a darker lower hemisphere.
    const ground = new THREE.Mesh(new THREE.CircleGeometry(100, 24).rotateX(-Math.PI / 2), new THREE.MeshBasicMaterial({ color: this.state.ground.clone().multiplyScalar(0.55) }));
    ground.position.y = -2;
    envScene.add(ground);
    this.envRT?.dispose();
    this.envRT = pm.fromScene(envScene, 0.02);
    pm.dispose();
    return this.envRT.texture;
  }
}
