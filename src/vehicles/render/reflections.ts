// Real-time reflections for the player's car: a small cube camera at the
// car renders the surroundings every few frames, PMREM-filtered so rough
// paint blurs them correctly while the clearcoat stays sharp.
import * as THREE from 'three';

export class CarReflections {
  private cubeRT = new THREE.WebGLCubeRenderTarget(128, { type: THREE.HalfFloatType });
  private cubeCam = new THREE.CubeCamera(0.4, 2500, this.cubeRT);
  private pmrem: THREE.PMREMGenerator;
  private envRT: THREE.WebGLRenderTarget | null = null;
  private frame = 0;
  private materials: THREE.MeshStandardMaterial[] = [];

  constructor(renderer: THREE.WebGLRenderer) {
    this.pmrem = new THREE.PMREMGenerator(renderer);
  }

  /** Materials that should reflect the live surroundings. */
  track(root: THREE.Object3D): void {
    this.materials = [];
    root.traverse((o) => {
      const m = (o as THREE.Mesh).material;
      const list = Array.isArray(m) ? m : m ? [m] : [];
      for (const x of list) {
        const s = x as THREE.MeshStandardMaterial;
        if (s.isMeshStandardMaterial && (s.metalness > 0.2 || (s as THREE.MeshPhysicalMaterial).clearcoat > 0)) this.materials.push(s);
      }
    });
  }

  update(renderer: THREE.WebGLRenderer, scene: THREE.Scene, car: THREE.Object3D, every: number): void {
    if (every <= 0 || this.frame++ % every !== 0) return;
    car.visible = false;
    this.cubeCam.position.copy(car.position);
    this.cubeCam.position.y += 0.7;
    const prevTarget = renderer.getRenderTarget();
    this.cubeCam.update(renderer, scene);
    renderer.setRenderTarget(prevTarget);
    car.visible = true;
    this.envRT = this.pmrem.fromCubemap(this.cubeRT.texture, this.envRT ?? undefined);
    for (const m of this.materials) {
      if (m.envMap !== this.envRT.texture) {
        m.envMap = this.envRT.texture;
        m.needsUpdate = true;
      }
    }
  }

  dispose(): void {
    this.cubeRT.dispose();
    this.envRT?.dispose();
    this.pmrem.dispose();
  }
}
