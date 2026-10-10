// Draws one simulated car: interpolates the body between simulation steps
// and poses the wheels (suspension travel, steering, spin) and lights.
import * as THREE from 'three';
import { buildCarModel, type CarModel, type ModelOptions } from './car-model';
import type { Vehicle } from '../vehicle';
import type { CarSpec } from '../spec';

export class CarView {
  readonly model: CarModel;
  private readonly prevPos = new THREE.Vector3();
  private readonly prevQuat = new THREE.Quaternion();
  private readonly curPos = new THREE.Vector3();
  private readonly curQuat = new THREE.Quaternion();
  private readonly prevLen = [0, 0, 0, 0];
  private readonly prevSpin = [0, 0, 0, 0];
  headlightsOn = false;
  /** Soft blob on the ground under the car, for grounding at any shadow quality. */
  readonly contactShadow: THREE.Mesh;

  constructor(readonly vehicle: Vehicle, spec: CarSpec, opts: ModelOptions = {}) {
    this.model = buildCarModel(spec, vehicle.params, opts);
    this.contactShadow = new THREE.Mesh(new THREE.PlaneGeometry(1, 1).rotateX(-Math.PI / 2), new THREE.MeshBasicMaterial({ map: blobTexture(), transparent: true, depthWrite: false, color: 0x000000, opacity: 0.55, polygonOffset: true, polygonOffsetFactor: -8, polygonOffsetUnits: -16 }));
    this.contactShadow.scale.set(spec.width * 1.35, 1, spec.length * 1.2);
    this.contactShadow.renderOrder = 1;
    this.snapshot();
    this.snapshot();
  }

  get root(): THREE.Group {
    return this.model.root;
  }

  /** Record the current physics pose as the previous one (call before each step). */
  snapshot(): void {
    const v = this.vehicle;
    this.prevPos.set(v.pos.x, v.pos.y, v.pos.z);
    this.prevQuat.set(v.quat.x, v.quat.y, v.quat.z, v.quat.w);
    for (let i = 0; i < 4; i++) {
      this.prevLen[i] = v.wheels[i].length;
      this.prevSpin[i] = v.wheels[i].spin;
    }
  }

  /** Pose the model between the previous and current physics states. */
  update(alpha: number): void {
    const v = this.vehicle;
    this.curPos.set(v.pos.x, v.pos.y, v.pos.z);
    this.curQuat.set(v.quat.x, v.quat.y, v.quat.z, v.quat.w);
    const root = this.model.root;
    root.position.lerpVectors(this.prevPos, this.curPos, alpha);
    root.quaternion.slerpQuaternions(this.prevQuat, this.curQuat, alpha);
    const p = v.params;
    for (let i = 0; i < 4; i++) {
      const w = v.wheels[i];
      const wp = p.wheels[i];
      const parts = this.model.wheels[i];
      const len = this.prevLen[i] + (w.length - this.prevLen[i]) * alpha;
      parts.pivot.position.set(wp.hardpoint.x, wp.hardpoint.y - len, wp.hardpoint.z);
      parts.pivot.rotation.y = w.steer;
      const spin = this.prevSpin[i] + (w.spin - this.prevSpin[i]) * alpha;
      parts.spin.rotation.x = -spin;
    }
    // Contact shadow: under the body on the ground plane through the wheels' contacts.
    let gy = 0;
    let n = 0;
    for (const w of v.wheels) {
      if (w.grounded) {
        gy += w.contact.y;
        n++;
      }
    }
    const cs = this.contactShadow;
    cs.visible = n > 0;
    if (n > 0) {
      cs.position.set(root.position.x, gy / n + 0.03, root.position.z);
      cs.rotation.y = Math.atan2(-v.fwd.x, -v.fwd.z);
      const lift = Math.max(0, root.position.y - gy / n - p.cgHeight);
      (cs.material as THREE.MeshBasicMaterial).opacity = 0.55 * Math.max(0, 1 - lift * 1.2);
    }
    const braking = v.gear === -1 ? v.input.throttle > 0.05 : v.input.brake > 0.05;
    this.model.brakeLights.emissiveIntensity = braking ? 3.2 : this.headlightsOn ? 1.1 : 0.45;
    this.model.reverseLights.emissiveIntensity = v.gear === -1 ? 2 : 0;
    this.model.headLights.emissiveIntensity = this.headlightsOn ? 3 : 0.5;
  }
}

let blob: THREE.Texture | null = null;
function blobTexture(): THREE.Texture {
  if (blob) return blob;
  const c = document.createElement('canvas');
  c.width = c.height = 64;
  const g = c.getContext('2d') as CanvasRenderingContext2D;
  const grad = g.createRadialGradient(32, 32, 4, 32, 32, 31);
  grad.addColorStop(0, 'rgba(255,255,255,1)');
  grad.addColorStop(0.55, 'rgba(255,255,255,0.65)');
  grad.addColorStop(1, 'rgba(255,255,255,0)');
  g.fillStyle = grad;
  g.fillRect(0, 0, 64, 64);
  blob = new THREE.CanvasTexture(c);
  return blob;
}
