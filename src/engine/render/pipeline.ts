// Render pipeline: HDR scene render, optional ambient occlusion and bloom,
// and a final grading pass. Falls back to a direct render (with three's
// built-in tone mapping) on the Low preset.
import * as THREE from 'three';
import { EffectComposer } from 'three/examples/jsm/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/examples/jsm/postprocessing/RenderPass.js';
import { UnrealBloomPass } from 'three/examples/jsm/postprocessing/UnrealBloomPass.js';
import { ShaderPass } from 'three/examples/jsm/postprocessing/ShaderPass.js';
import { GTAOPass } from 'three/examples/jsm/postprocessing/GTAOPass.js';
import { CSM } from 'three/examples/jsm/csm/CSM.js';
import { GradeShader } from './grade';
import { setCsm } from './materials';
import type { Quality } from './quality';

export class Pipeline {
  composer: EffectComposer | null = null;
  grade: ShaderPass | null = null;
  bloom: UnrealBloomPass | null = null;
  ao: GTAOPass | null = null;
  csm: CSM | null = null;
  exposure = 1;
  speedBlur = 0;
  aberration = 0;
  private w = 1;
  private h = 1;

  constructor(
    readonly renderer: THREE.WebGLRenderer,
    readonly scene: THREE.Scene,
    readonly camera: THREE.PerspectiveCamera,
    public quality: Quality,
  ) {
    this.build();
  }

  build(): void {
    this.composer?.dispose();
    this.composer = null;
    this.grade = this.bloom = null;
    this.ao = null;
    const q = this.quality;
    const r = this.renderer;
    if (!q.post) {
      r.toneMapping = THREE.AgXToneMapping;
      return;
    }
    // Scene output stays linear HDR; the grade pass tone maps.
    r.toneMapping = THREE.NoToneMapping;
    const target = new THREE.WebGLRenderTarget(this.w, this.h, { type: THREE.HalfFloatType, samples: q.msaa });
    const composer = new EffectComposer(r, target);
    composer.addPass(new RenderPass(this.scene, this.camera));
    if (q.ao) {
      const ao = new GTAOPass(this.scene, this.camera, this.w, this.h);
      // Skip the sky dome and cloud layer (userData.noAO) in the AO depth pass.
      const original = ao.overrideVisibility.bind(ao);
      const scene = this.scene;
      ao.overrideVisibility = () => {
        original();
        scene.traverse((o) => {
          if (o.userData.noAO) o.visible = false;
        });
      };
      ao.blendIntensity = 0.85;
      ao.updateGtaoMaterial({ radius: 0.6, distanceExponent: 1.4, thickness: 1.2, scale: 1.0, samples: 12 });
      composer.addPass(ao);
      this.ao = ao;
    }
    if (q.bloom) {
      const b = new UnrealBloomPass(new THREE.Vector2(this.w / 2, this.h / 2), 0.24, 0.55, 1.6);
      composer.addPass(b);
      this.bloom = b;
    }
    const grade = new ShaderPass(GradeShader);
    composer.addPass(grade);
    this.grade = grade;
    this.composer = composer;
    composer.setSize(this.w, this.h);
  }

  /** Cascaded shadow maps for the sun (High/Ultra). Call before materials are patched. */
  setupShadows(sunDir: THREE.Vector3, intensity: number, color: THREE.Color): void {
    this.csm?.dispose();
    this.csm = null;
    setCsm(null);
    const q = this.quality;
    if (!q.csm) return;
    const csm = new CSM({
      maxFar: 700,
      cascades: q.cascades,
      mode: 'practical',
      parent: this.scene,
      shadowMapSize: q.shadowSize,
      lightDirection: sunDir.clone().negate(),
      camera: this.camera,
      lightIntensity: intensity,
      lightNear: 1,
      lightFar: 2200,
      lightMargin: 220,
      shadowBias: -0.00025,
    });
    csm.fade = true;
    for (const l of csm.lights) {
      l.color.copy(color);
      l.shadow.normalBias = 0.04;
    }
    this.csm = csm;
    setCsm(csm);
  }

  setSun(dir: THREE.Vector3, intensity: number, color: THREE.Color): void {
    if (!this.csm) return;
    this.csm.lightDirection.copy(dir).negate();
    for (const l of this.csm.lights) {
      l.intensity = intensity;
      l.color.copy(color);
    }
  }

  setSize(w: number, h: number, pixelRatio: number): void {
    this.w = Math.max(1, Math.round(w * pixelRatio));
    this.h = Math.max(1, Math.round(h * pixelRatio));
    if (this.composer) {
      this.composer.setPixelRatio(1);
      this.composer.setSize(this.w, this.h);
      if (this.grade) (this.grade.uniforms.uResolution.value as THREE.Vector2).set(this.w, this.h);
      this.ao?.setSize(this.w, this.h);
    }
    this.csm?.updateFrustums();
  }

  render(dt: number): void {
    this.csm?.update();
    if (!this.composer || !this.grade) {
      this.renderer.toneMappingExposure = this.exposure;
      this.renderer.render(this.scene, this.camera);
      return;
    }
    const u = this.grade.uniforms;
    u.uExposure.value = this.exposure;
    u.uSpeedBlur.value = this.speedBlur;
    u.uAberration.value = this.aberration;
    u.uTime.value = (u.uTime.value as number) + dt;
    this.composer.render(dt);
  }
}
