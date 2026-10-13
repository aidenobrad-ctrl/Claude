// Player cameras: chase, far chase and bonnet. The chase cameras damp their
// offset relative to the car (not their absolute position), so they never
// fall behind at speed, and swing toward the direction of travel in a slide.
import * as THREE from 'three';
import { fovForAspect } from './fov';

export type CamMode = 'chase' | 'far' | 'bonnet';
export const CAM_MODES: CamMode[] = ['chase', 'far', 'bonnet'];
export const CAM_LABELS: Record<CamMode, string> = { chase: 'Chase', far: 'Far chase', bonnet: 'Bonnet' };

export interface CamTarget {
  pos: THREE.Vector3;
  quat: THREE.Quaternion;
  vel: THREE.Vector3;
  /** Height of the bonnet camera above the COM, and its distance ahead of it. */
  bonnetY: number;
  bonnetZ: number;
}

const tmp = new THREE.Vector3();
const tmp2 = new THREE.Vector3();
const fwd = new THREE.Vector3();
const up = new THREE.Vector3(0, 1, 0);

export class CameraRig {
  readonly camera: THREE.PerspectiveCamera;
  mode: CamMode = 'chase';
  reducedShake = false;
  lookBack = false;
  private offset = new THREE.Vector3(0, 2, 6);
  private dir = new THREE.Vector3(0, 0, -1);
  private shakeT = 0;
  private kick = 0;
  private speedFov = 0;
  private snapNext = true;
  private bonnetQuat = new THREE.Quaternion();
  aspect = 16 / 9;

  constructor() {
    this.camera = new THREE.PerspectiveCamera(62, 16 / 9, 0.25, 6000);
  }

  cycle(): CamMode {
    this.mode = CAM_MODES[(CAM_MODES.indexOf(this.mode) + 1) % CAM_MODES.length];
    this.snapNext = true;
    return this.mode;
  }

  /** Jump straight to the target next update (after teleports and resets). */
  snap(): void {
    this.snapNext = true;
  }

  impact(strength: number): void {
    this.kick = Math.min(1, this.kick + strength);
  }

  setAspect(aspect: number): void {
    this.aspect = aspect;
    this.camera.aspect = aspect;
  }

  update(dt: number, t: CamTarget, groundY: (x: number, z: number) => number): void {
    const cam = this.camera;
    const speed = t.vel.length();
    fwd.set(0, 0, -1).applyQuaternion(t.quat);
    const portrait = this.aspect < 0.9;
    const snap = this.snapNext;
    this.snapNext = false;
    let baseFov = 60;

    if (this.mode === 'bonnet') {
      // Rigidly attached, with a little rotational smoothing against jitter.
      tmp.set(0, t.bonnetY, t.bonnetZ).applyQuaternion(t.quat).add(t.pos);
      cam.position.copy(tmp);
      this.bonnetQuat.slerp(t.quat, snap ? 1 : 1 - Math.exp(-dt * 25));
      cam.quaternion.copy(this.bonnetQuat);
      if (this.lookBack) cam.rotateY(Math.PI);
      baseFov = 68;
    } else {
      const far = this.mode === 'far';
      const dist = (far ? 8.6 : 5.6) * (portrait ? 1.35 : 1) + Math.min(speed, 70) * 0.012;
      const height = (far ? 2.9 : 1.75) * (portrait ? 1.25 : 1);
      // Aim along a blend of heading and travel so drifts stay readable.
      const flat = tmp.set(fwd.x, 0, fwd.z).normalize();
      if (speed > 4) {
        tmp2.set(t.vel.x, 0, t.vel.z).normalize();
        if (tmp2.dot(flat) > 0) flat.lerp(tmp2, 0.35).normalize();
      }
      if (snap) this.dir.copy(flat);
      else this.dir.lerp(flat, 1 - Math.exp(-dt * (far ? 4 : 5))).normalize();
      const back = this.lookBack ? -1 : 1;
      const desired = tmp2.copy(this.dir).multiplyScalar(-dist * back).addScaledVector(up, height);
      if (snap) this.offset.copy(desired);
      else this.offset.lerp(desired, 1 - Math.exp(-dt * 7));
      cam.position.copy(t.pos).add(this.offset);
      const gy = groundY(cam.position.x, cam.position.z);
      if (cam.position.y < gy + 0.6) cam.position.y = gy + 0.6;
      tmp.copy(t.pos).addScaledVector(up, far ? 1.1 : 0.85).addScaledVector(this.dir, 2.6 * back);
      cam.lookAt(tmp);
      baseFov = far ? 58 : 62;
    }

    // Speed adds a little FOV; impacts and high speed add a little shake.
    const targetFov = Math.min(14, Math.max(0, (speed - 15) * 0.16));
    this.speedFov += (targetFov - this.speedFov) * (1 - Math.exp(-dt * 2));
    cam.fov = fovForAspect(baseFov + this.speedFov, 72, this.aspect);
    this.shakeT += dt;
    const shakeAmp = this.reducedShake ? 0 : Math.max(0, speed - 45) * 0.0006 + this.kick * 0.06;
    if (shakeAmp > 0) {
      cam.position.x += Math.sin(this.shakeT * 37.1) * shakeAmp;
      cam.position.y += Math.sin(this.shakeT * 29.3 + 1.3) * shakeAmp * 0.7;
    }
    this.kick *= Math.exp(-dt * 6);
    cam.updateProjectionMatrix();
  }
}
