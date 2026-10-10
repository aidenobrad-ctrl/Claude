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

  constructor(readonly vehicle: Vehicle, spec: CarSpec, opts: ModelOptions = {}) {
    this.model = buildCarModel(spec, vehicle.params, opts);
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
    const braking = v.gear === -1 ? v.input.throttle > 0.05 : v.input.brake > 0.05;
    this.model.brakeLights.emissiveIntensity = braking ? 3.2 : this.headlightsOn ? 1.1 : 0.45;
    this.model.reverseLights.emissiveIntensity = v.gear === -1 ? 2 : 0;
    this.model.headLights.emissiveIntensity = this.headlightsOn ? 3 : 0.5;
  }
}
