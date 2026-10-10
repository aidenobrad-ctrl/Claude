// Four-wheel vehicle model, stepped at 240 Hz.
//
// Rigid body (6 DOF) on four raycast struts: springs, separate bump and
// rebound damping, bump stops and anti-roll bars. Per-wheel load, slip ratio
// and slip angle feed a combined-slip magic-formula tire (tires.ts). The
// engine, clutch, gearbox and differentials are solved together implicitly,
// which keeps the stiff wheel-spin dynamics stable at any speed. ABS, TCS,
// stability control and a steering limiter are optional driver aids.
import { V3, Quat, clamp, lerp, moveTowards, smoothstep } from '../engine/math';
import { datan, datan2, dcos, dsin, dtan } from '../engine/dmath';
import { newHit, type Ground, type GroundHit } from '../world/ground';
import { SURFACES, SURFACE, type SurfaceId } from '../world/surfaces';
import { tireForce, type TireResult } from './tires';
import { AIR_DENSITY, GRAVITY, curveTorque, type VehicleParams } from './params';

export interface DriverInput {
  steer: number;
  throttle: number;
  brake: number;
  handbrake: number;
  clutch: number;
  shiftUp: boolean;
  shiftDown: boolean;
}

export type SteeringAssist = 'off' | 'standard' | 'assisted';

export interface Aids {
  abs: boolean;
  tcs: boolean;
  esc: boolean;
  steering: SteeringAssist;
  gearbox: 'auto' | 'manual';
}

export function neutralInput(): DriverInput {
  return { steer: 0, throttle: 0, brake: 0, handbrake: 0, clutch: 0, shiftUp: false, shiftDown: false };
}

export function defaultAids(): Aids {
  return { abs: true, tcs: true, esc: true, steering: 'standard', gearbox: 'auto' };
}

export class WheelState {
  /** Road-wheel steering angle, rad, yaw sense (+ = left). */
  steer = 0;
  length = 0;
  prevLength = 0;
  grounded = false;
  /** Strut force before clamping, N. */
  strutForce = 0;
  load = 0;
  omega = 0;
  spin = 0;
  fx = 0;
  fy = 0;
  slipRatio = 0;
  slipAngle = 0;
  /** Contact-patch sliding speed, m/s (for smoke, marks, audio). */
  slideSpeed = 0;
  /** Normalized combined slip, 1 = peak grip. */
  slip = 0;
  surface: SurfaceId = SURFACE.asphalt;
  water = 0;
  readonly contact = new V3();
  readonly normal = new V3(0, 1, 0);
  readonly fwdDir = new V3(0, 0, -1);
  readonly sideDir = new V3(1, 0, 0);
  /** Implicit tire stiffness d(Fx r)/d omega, N·m per rad/s. */
  k = 0;
  /** Tire reaction torque Fx * r, N·m. */
  tireTorque = 0;
  brakeTorque = 0;
  absScale = 1;
  escBrake = 0;
  vLong = 0;
  /** Static-friction anchor for a braked tire at rest. */
  anchored = false;
  anchorX = 0;
  anchorZ = 0;
}

const tmpA = new V3();
const tmpB = new V3();
const tmpC = new V3();
const hp = new V3();
const force = new V3();
const torque = new V3();
const tireRes: TireResult = { fx: 0, fy: 0, dFxdV: 0, slip: 0 };
const hit: GroundHit = newHit();

export class Vehicle {
  readonly pos = new V3();
  readonly quat = new Quat();
  readonly vel = new V3();
  readonly angVel = new V3();
  readonly right = new V3(1, 0, 0);
  readonly up = new V3(0, 1, 0);
  readonly fwd = new V3(0, 0, -1);
  readonly wheels: WheelState[];
  input: DriverInput = neutralInput();
  aids: Aids = defaultAids();

  engineOmega = 0;
  /** -1 reverse, 0 neutral, 1..n forward. */
  gear = 1;
  private nextGear = 1;
  shiftTimer = 0;
  private shiftTotal = 0;
  clutchEngage = 1;
  clutchLocked = false;
  boost = 0;
  limiterCut = false;
  throttleEff = 0;
  tcsCut = 0;
  escCut = 0;
  absActive = false;
  tcsActive = false;
  escActive = false;
  steerAngle = 0;
  private reverseTimer = 0;
  timeSinceShift = 10;
  /** True while the clutch is slipping to launch the car. */
  launching = false;
  /** Gear being shifted into, for torque cut and rev matching. */
  private shiftUp = false;

  // Derived each step, for HUD, AI, audio and telemetry.
  speed = 0;
  vLong = 0;
  vLat = 0;
  yawRate = 0;
  accelLong = 0;
  accelLat = 0;
  airTime = 0;
  groundedCount = 4;
  /** Largest collision impulse this step, N·s (for sound and haptics). */
  impact = 0;
  /** Extra drag multiplier from drafting (set by the race/sim layer). */
  draftFactor = 1;
  odometer = 0;
  private readonly prevVel = new V3();
  private readonly frontAxle = new V3();

  constructor(public params: VehicleParams) {
    this.wheels = params.wheels.map(() => new WheelState());
    this.place(0, 0, 0, 0);
  }

  /** Engine speed in rpm. */
  get rpm(): number {
    return (this.engineOmega * 30) / Math.PI;
  }

  /** Swap parameters (tuning, upgrades) keeping the current motion. */
  setParams(p: VehicleParams): void {
    this.params = p;
    for (let i = 0; i < this.wheels.length; i++) this.wheels[i].length = Math.min(this.wheels[i].length, p.wheels[i].maxLength);
  }

  /** Put the car at rest on flat ground at (x, groundY, z) facing yaw. */
  place(x: number, groundY: number, z: number, yaw: number): void {
    const p = this.params;
    this.quat.setYaw(yaw);
    this.pos.set(x, groundY + p.cgHeight + 0.01, z);
    this.vel.set(0, 0, 0);
    this.angVel.set(0, 0, 0);
    for (let i = 0; i < this.wheels.length; i++) {
      const w = this.wheels[i];
      w.omega = 0;
      w.length = w.prevLength = p.wheels[i].staticLength;
      w.absScale = 1;
      w.steer = 0;
    }
    this.gear = 1;
    this.nextGear = 1;
    this.shiftTimer = 0;
    this.engineOmega = (p.engine.idle * Math.PI) / 30;
    this.boost = 0;
    this.tcsCut = this.escCut = 0;
    this.steerAngle = 0;
    this.airTime = 0;
    this.updateAxes();
    this.prevVel.set(0, 0, 0);
  }

  /** Set a forward speed with wheels rolling and a sensible gear (tests, spawns). */
  setSpeed(v: number): void {
    const p = this.params;
    this.updateAxes();
    this.vel.copy(this.fwd).scale(v);
    for (let i = 0; i < this.wheels.length; i++) this.wheels[i].omega = v / p.wheels[i].radius;
    const g = p.gearbox;
    let gear = 1;
    const wheelOmega = v / p.wheels[0].radius;
    for (let n = 1; n < g.ratios.length; n++) {
      const rpm = (wheelOmega * g.ratios[n] * g.final * 30) / Math.PI;
      gear = n;
      if (rpm < g.upshiftRpm[n] * 0.92) break;
    }
    this.gear = this.nextGear = gear;
    this.engineOmega = Math.max((p.engine.idle * Math.PI) / 30, wheelOmega * g.ratios[gear] * g.final);
    this.prevVel.copy(this.vel);
  }

  updateAxes(): void {
    this.right.set(1, 0, 0).applyQuat(this.quat);
    this.up.set(0, 1, 0).applyQuat(this.quat);
    this.fwd.set(0, 0, -1).applyQuat(this.quat);
  }

  /** World position of a body-frame point. */
  toWorld(local: V3, out: V3): V3 {
    return out.copy(local).applyQuat(this.quat).add(this.pos);
  }

  /** Velocity of a world-space point attached to the body. */
  pointVelocity(p: V3, out: V3): V3 {
    tmpC.subVectors(p, this.pos);
    return out.crossVectors(this.angVel, tmpC).add(this.vel);
  }

  /** Apply an impulse (N·s) at a world point. */
  applyImpulse(point: V3, impulse: V3): void {
    const p = this.params;
    this.vel.addScaled(impulse, p.invMass);
    tmpC.subVectors(point, this.pos);
    tmpA.crossVectors(tmpC, impulse);
    this.applyInvInertia(tmpA, tmpA);
    this.angVel.add(tmpA);
  }

  /** out = I_world^-1 * v */
  applyInvInertia(v: V3, out: V3): V3 {
    const inv = this.params.invInertia;
    out.copy(v).applyQuatInv(this.quat);
    out.set(out.x * inv.x, out.y * inv.y, out.z * inv.z);
    return out.applyQuat(this.quat);
  }

  step(dt: number, ground: Ground): void {
    const p = this.params;
    this.updateAxes();
    this.vLong = this.vel.dot(this.fwd);
    this.vLat = this.vel.dot(this.right);
    this.yawRate = this.angVel.dot(this.up);
    this.impact = 0;

    this.updateTransmission(dt);
    const reversing = this.gear === -1;
    const throttleIn = clamp(reversing ? this.input.brake : this.input.throttle, 0, 1);
    const brakeIn = clamp(reversing ? this.input.throttle : this.input.brake, 0, 1);
    this.updateSteering(dt);

    // --- Suspension -----------------------------------------------------
    let grounded = 0;
    for (let i = 0; i < 4; i++) {
      if (this.suspension(i, dt, ground)) grounded++;
    }
    this.groundedCount = grounded;
    for (const ax of p.axles) {
      const L = this.wheels[ax.left];
      const R = this.wheels[ax.right];
      const d = (p.wheels[ax.left].staticLength - L.length) - (p.wheels[ax.right].staticLength - R.length);
      const f = ax.arb * d;
      if (L.grounded) L.strutForce += f;
      if (R.grounded) R.strutForce -= f;
    }

    // --- Tires ----------------------------------------------------------
    for (let i = 0; i < 4; i++) this.tire(i);

    // --- Driver aids ----------------------------------------------------
    this.updateAids(dt, throttleIn, brakeIn);

    // --- Drivetrain and wheel speeds -----------------------------------
    this.drivetrain(dt, brakeIn);

    // --- Forces on the body ---------------------------------------------
    force.set(0, -GRAVITY * p.mass, 0);
    torque.set(0, 0, 0);
    for (let i = 0; i < 4; i++) {
      const w = this.wheels[i];
      if (!w.grounded) continue;
      // Strut force along the body's up axis, tire forces in the ground plane.
      tmpA.copy(this.up).scale(w.load).addScaled(w.fwdDir, w.fx).addScaled(w.sideDir, w.fy);
      // Loose and soft surfaces add drag proportional to speed and load.
      const surf = SURFACES[w.surface];
      const drag = surf.drag + (w.water > 0.05 ? Math.min(w.water, 0.8) * 25 : 0);
      if (drag > 0) {
        this.pointVelocity(w.contact, tmpB);
        tmpB.addScaled(w.normal, -tmpB.dot(w.normal));
        tmpA.addScaled(tmpB, (-drag * w.load) / 1000);
      }
      tmpB.copy(w.contact).addScaled(w.normal, p.wheels[i].rollCenter).sub(this.pos);
      force.add(tmpA);
      torque.add(tmpC.crossVectors(tmpB, tmpA));
    }
    // Aerodynamics: drag at the COM, downforce at each axle.
    const v2 = this.vel.lenSq();
    if (v2 > 1e-4) {
      const sp = Math.sqrt(v2);
      const dragK = 0.5 * AIR_DENSITY * p.aero.cdA * this.draftFactor * sp;
      force.addScaled(this.vel, -dragK);
      if (p.aero.clA !== 0 && this.vLong > 0) {
        const down = 0.5 * AIR_DENSITY * p.aero.clA * this.vLong * this.vLong;
        this.applyBodyForce(0, -p.cgHeight * 0.3, -p.a, -down * p.aero.balance);
        this.applyBodyForce(0, -p.cgHeight * 0.3, p.b, -down * (1 - p.aero.balance));
      }
    }
    this.bodyContacts(ground);

    // --- Integrate (semi-implicit Euler) -------------------------------
    this.prevVel.copy(this.vel);
    this.vel.addScaled(force, p.invMass * dt);
    // Angular: work in the body frame where inertia is diagonal.
    const inv = p.invInertia;
    const I = p.inertia;
    tmpA.copy(torque).applyQuatInv(this.quat);
    tmpB.copy(this.angVel).applyQuatInv(this.quat);
    const lx = I.x * tmpB.x;
    const ly = I.y * tmpB.y;
    const lz = I.z * tmpB.z;
    const gx = tmpB.y * lz - tmpB.z * ly;
    const gy = tmpB.z * lx - tmpB.x * lz;
    const gz = tmpB.x * ly - tmpB.y * lx;
    tmpB.x += (tmpA.x - gx) * inv.x * dt;
    tmpB.y += (tmpA.y - gy) * inv.y * dt;
    tmpB.z += (tmpA.z - gz) * inv.z * dt;
    this.angVel.copy(tmpB.applyQuat(this.quat));
    // Gentle angular damping (air, chassis friction) keeps airborne spins sane.
    this.angVel.scale(1 - 0.08 * dt);
    this.pos.addScaled(this.vel, dt);
    this.quat.integrate(this.angVel, dt);

    for (const w of this.wheels) {
      w.spin += w.omega * dt;
      if (w.spin > 1e4 || w.spin < -1e4) w.spin %= Math.PI * 2;
    }
    this.speed = this.vel.len();
    this.odometer += this.speed * dt;
    // Accelerations in the body frame, lightly filtered for the HUD and AI.
    tmpA.subVectors(this.vel, this.prevVel).scale(1 / dt);
    this.accelLong += (tmpA.dot(this.fwd) - this.accelLong) * 0.1;
    this.accelLat += (tmpA.dot(this.right) - this.accelLat) * 0.1;
    this.airTime = grounded === 0 ? this.airTime + dt : 0;
    this.timeSinceShift += dt;
  }

  /** Force along body Y at a body-frame point (used for downforce). */
  private applyBodyForce(lx: number, ly: number, lz: number, fy: number): void {
    tmpA.set(lx, ly, lz).applyQuat(this.quat);
    tmpB.copy(this.up).scale(fy);
    force.add(tmpB);
    torque.add(tmpC.crossVectors(tmpA, tmpB));
  }

  private suspension(i: number, dt: number, ground: Ground): boolean {
    const p = this.params;
    const wp = p.wheels[i];
    const w = this.wheels[i];
    this.toWorld(wp.hardpoint, hp);
    const up = this.up;
    w.prevLength = w.length;
    let length = wp.maxLength;
    let grounded = false;
    if (ground.sample(hp.x, hp.z, hp.y, hit)) {
      let upN = up.x * hit.nx + up.y * hit.ny + up.z * hit.nz;
      if (upN > 0.25) {
        // Ray from the hardpoint along -up to the ground plane, refined once
        // at the contact point so slopes and curbs are hit where they are.
        let t = ((hp.y - hit.y) * hit.ny) / upN;
        const cx = hp.x - up.x * t;
        const cz = hp.z - up.z * t;
        if (ground.sample(cx, cz, hp.y, hit)) {
          upN = up.x * hit.nx + up.y * hit.ny + up.z * hit.nz;
          if (upN > 0.25) {
            t = ((hp.x - cx) * hit.nx + (hp.y - hit.y) * hit.ny + (hp.z - cz) * hit.nz) / upN;
            const l = t - wp.radius;
            if (l < wp.maxLength) {
              grounded = true;
              length = Math.max(0, l);
              w.normal.set(hit.nx, hit.ny, hit.nz);
              w.surface = hit.surface;
              w.water = hit.water;
            }
          }
        }
      }
    }
    w.length = length;
    w.grounded = grounded;
    if (!grounded) {
      w.strutForce = 0;
      w.load = 0;
      return false;
    }
    const compVel = (length - w.prevLength) / dt;
    let f = wp.spring * (wp.freeLength - length);
    f -= (compVel < 0 ? wp.damperBump : wp.damperRebound) * compVel;
    // Progressive bump stop.
    const bs = wp.bumpLength + 0.025;
    if (length < bs) {
      const d = bs - length;
      f += wp.spring * 12 * d + 3e6 * d * d - (compVel < 0 ? wp.damperBump * 2 * compVel : 0);
    }
    w.strutForce = f;
    // Contact point at the bottom of the wheel.
    w.contact.copy(hp).addScaled(up, -length).addScaled(w.normal, -wp.radius);
    return true;
  }

  private tire(i: number): void {
    const p = this.params;
    const wp = p.wheels[i];
    const w = this.wheels[i];
    w.load = w.grounded ? Math.max(0, w.strutForce) : 0;
    if (!w.grounded || w.load <= 0) {
      w.fx = w.fy = 0;
      w.k = 0;
      w.tireTorque = 0;
      w.slideSpeed = 0;
      w.slip = 0;
      w.slipRatio = 0;
      w.slipAngle = 0;
      return;
    }
    // Wheel heading: body forward rotated by the steer angle, projected onto the ground.
    const cs = dcos(w.steer);
    const sn = dsin(w.steer);
    const n = w.normal;
    w.fwdDir.copy(this.fwd).scale(cs).addScaled(this.right, -sn);
    w.fwdDir.addScaled(n, -w.fwdDir.dot(n)).normalize();
    w.sideDir.crossVectors(w.fwdDir, n);
    this.pointVelocity(w.contact, tmpA);
    const vL = tmpA.dot(w.fwdDir);
    const vS = tmpA.dot(w.sideDir);
    w.vLong = vL;
    const wheelSpeed = w.omega * wp.radius;
    const surf = SURFACES[w.surface];
    let grip = wp.tire.info.surface[surf.grip] * surf.gripScale;
    if (w.water > 0.02) grip *= Math.max(0.35, 1 - w.water * 1.5);
    tireForce(wp.tire, w.load, vL, vS, wheelSpeed, grip, tireRes);
    w.fx = tireRes.fx;
    w.fy = tireRes.fy;
    // Static friction at a standstill. A braked tire whose patch has all but
    // stopped is anchored to the ground like a stiff tread spring (with
    // damping), so parked cars do not creep down slopes. If the needed force
    // exceeds the grip limit the anchor slides along with the patch.
    const patchSpeed = Math.sqrt(vL * vL + vS * vS);
    if (patchSpeed < 0.3 && Math.abs(wheelSpeed) < 0.05 && w.brakeTorque > 0 && this.speed < 0.6) {
      if (!w.anchored) {
        w.anchored = true;
        w.anchorX = w.contact.x;
        w.anchorZ = w.contact.z;
      }
      const dx = w.contact.x - w.anchorX;
      const dz = w.contact.z - w.anchorZ;
      const dL = dx * w.fwdDir.x + dz * w.fwdDir.z;
      const dS = dx * w.sideDir.x + dz * w.sideDir.z;
      const kS = 1.2e5;
      const cS = 5000;
      let sx = -kS * dL - cS * vL;
      let sy = -kS * dS - cS * vS;
      const limit = wp.tire.info.mu * grip * w.load;
      const sl = Math.sqrt(sx * sx + sy * sy);
      if (sl > limit) {
        sx *= limit / sl;
        sy *= limit / sl;
        // Slide the anchor so the spring force equals the limit.
        w.anchorX = w.contact.x + (w.fwdDir.x * sx + w.sideDir.x * sy) / kS;
        w.anchorZ = w.contact.z + (w.fwdDir.z * sx + w.sideDir.z * sy) / kS;
      }
      w.fx = sx;
      w.fy = sy;
      w.k = 0;
      w.tireTorque = 0;
    } else w.anchored = false;
    w.slip = tireRes.slip;
    w.k = wp.radius * wp.radius * tireRes.dFxdV;
    w.tireTorque = w.fx * wp.radius;
    const vDen = Math.max(Math.abs(vL), 3);
    w.slipRatio = (wheelSpeed - vL) / vDen;
    w.slipAngle = datan2(vS, Math.abs(vL) + 0.5);
    const sx = wheelSpeed - vL;
    w.slideSpeed = Math.sqrt(sx * sx + vS * vS);
  }

  private updateAids(dt: number, throttleIn: number, brakeIn: number): void {
    const p = this.params;
    const aids = this.aids;
    // Traction control: cut throttle when driven wheels slip past the peak.
    let kMax = 0;
    let kPeak = 0.12;
    for (let i = 0; i < 4; i++) {
      if (p.wheels[i].driveWeight <= 0 || !this.wheels[i].grounded) continue;
      kMax = Math.max(kMax, this.gear === -1 ? -this.wheels[i].slipRatio : this.wheels[i].slipRatio);
      kPeak = p.wheels[i].tire.kappaPeak;
    }
    const tcsTarget = aids.tcs && this.input.handbrake < 0.5 ? clamp((kMax - kPeak) / (kPeak * 0.7), 0, 0.92) : 0;
    if (this.launching) {
      // While the clutch slips, the clutch cap does traction control's job.
      this.tcsCut += (0 - this.tcsCut) * Math.min(1, dt * 20);
    } else {
      this.tcsCut += (tcsTarget - this.tcsCut) * Math.min(1, dt * (tcsTarget > this.tcsCut ? 35 : 7));
    }
    this.tcsActive = this.tcsCut > 0.04 && throttleIn > 0.1;

    // Stability control.
    this.escCut = 0;
    for (const w of this.wheels) w.escBrake = 0;
    this.escActive = false;
    if (aids.esc && this.vLong > 6 && this.input.handbrake < 0.1) {
      const L = p.wheelbase;
      const v = this.vLong;
      let rt = (v * this.steerAngle) / (L * (1 + 0.0022 * v * v));
      const rMax = (0.85 * p.wheels[0].tire.info.mu * GRAVITY) / v;
      rt = clamp(rt, -rMax, rMax);
      const r = this.yawRate;
      const beta = datan2(this.vLat, Math.max(1, v));
      const excess = Math.abs(r) - Math.abs(rt) - 0.07;
      if (excess > 0 || (Math.sign(r) !== Math.sign(rt) && Math.abs(r) > 0.12)) {
        const e = Math.max(excess, Math.abs(r) * 0.5);
        const outerFront = r > 0 ? 1 : 0;
        this.wheels[outerFront].escBrake = clamp(e * 9000, 0, p.wheels[outerFront].maxBrake * 0.6);
        this.escCut = clamp(e * 2.2, 0, 0.75);
      }
      const absBeta = Math.abs(beta);
      if (absBeta > 0.1) this.escCut = Math.max(this.escCut, clamp((absBeta - 0.1) * 4, 0, 0.85));
      this.escActive = this.escCut > 0.05;
    }

    // ABS: release brake pressure on wheels that lock past the peak slip.
    this.absActive = false;
    for (let i = 0; i < 4; i++) {
      const w = this.wheels[i];
      let target = 1;
      if (aids.abs && brakeIn > 0.05 && w.grounded && Math.abs(w.vLong) > 1.5) {
        const kp = p.wheels[i].tire.kappaPeak;
        const lock = w.vLong > 0 ? -w.slipRatio : w.slipRatio;
        target = clamp(1 - (lock - kp * 1.05) / (kp * 0.9), 0.05, 1);
      }
      w.absScale += (target - w.absScale) * Math.min(1, dt * (target < w.absScale ? 50 : 25));
      if (w.absScale < 0.93) this.absActive = true;
    }

    const cut = Math.max(this.tcsCut, this.escCut);
    this.throttleEff = throttleIn * (1 - cut);
    // Launch governor: hold the engine near the launch rpm while the clutch slips.
    if (this.launching) this.throttleEff *= clamp(1 - (this.rpm - p.gearbox.launchRpm) / 350, 0, 1);
    if (this.shiftTimer > 0) {
      if (this.shiftUp) {
        // Ignition cut on upshifts so the engine drops to the new gear's speed.
        this.throttleEff = 0;
      } else if (this.nextGear >= 1) {
        // Blip on downshifts to match the lower gear's revs.
        const target = this.wheelRpm(this.nextGear);
        this.throttleEff = clamp((target - this.rpm) / 600, 0, 1);
      }
    }
    // Brake torque per wheel.
    for (let i = 0; i < 4; i++) {
      const wp = p.wheels[i];
      const w = this.wheels[i];
      let tb = brakeIn * wp.maxBrake * w.absScale + w.escBrake;
      if (wp.handbrake && this.input.handbrake > 0) tb = Math.max(tb, this.input.handbrake * wp.maxBrake * 2.2);
      w.brakeTorque = tb;
    }
  }

  private engineTorque(rpm: number, throttle: number, dt: number): number {
    const e = this.params.engine;
    if (rpm >= e.limiter) this.limiterCut = true;
    else if (rpm < e.limiter - (e.electric ? 20 : 140)) this.limiterCut = false;
    let thr = this.limiterCut ? 0 : throttle;
    if (e.electric && rpm > e.limiter - 300) thr *= clamp((e.limiter - rpm) / 300, 0, 1);
    let mult = 1;
    if (e.turbo) {
      const spool = smoothstep(e.turbo.spoolRpm, e.turbo.fullRpm, rpm);
      const target = thr * spool;
      const tau = target > this.boost ? e.turbo.lag : 0.3;
      this.boost += (target - this.boost) * Math.min(1, dt / tau);
      mult = 1 - e.turbo.share + e.turbo.share * this.boost;
    }
    const full = curveTorque(e, rpm) * mult;
    const brakeT = e.brakeTorque * clamp(rpm / e.redline, 0.12, 1.2);
    let T = thr * full - (1 - thr) * brakeT;
    if (!e.electric && rpm < e.idle) T += clamp((e.idle - rpm) * 0.9, 0, e.peakTorque * 0.4);
    if (rpm < 0) T = Math.max(T, 0);
    return T;
  }

  private drivetrain(dt: number, brakeIn: number): void {
    const p = this.params;
    const g = p.gearbox;
    const e = p.engine;
    const W = this.wheels;
    const rpm = this.rpm;
    const Te = this.engineTorque(rpm, this.throttleEff, dt);
    // G is the kinematic ratio (engine speed per wheel speed); Gt = G * efficiency
    // is the torque ratio, so gearbox losses apply to whatever the clutch passes.
    const G = this.gear === 0 ? 0 : g.ratios[this.gear === -1 ? 0 : this.gear] * g.final;
    const Gt = G * g.efficiency;
    const engaged = G !== 0 && this.clutchEngage > 0.001;

    // Per-wheel torques opposing rotation: tire reaction, rolling
    // resistance and brakes. Brakes and rolling resistance act as friction.
    let A = 0;
    let B = 0;
    let wSum = 0;
    const brakeSign = brakeSigns;
    for (let i = 0; i < 4; i++) {
      const w = W[i];
      const wp = p.wheels[i];
      const a = 1 / (wp.inertia + dt * w.k);
      aCoef[i] = a;
      const rr = SURFACES[w.surface].rolling * w.load * wp.radius;
      const fr = rr + w.brakeTorque;
      let x = w.tireTorque;
      if (Math.abs(w.omega) > 0.05) {
        brakeSign[i] = Math.sign(w.omega);
        x += brakeSign[i] * fr;
      } else {
        brakeSign[i] = 0;
      }
      xTorque[i] = x;
      const dw = wp.driveWeight;
      if (dw > 0) {
        A += dw * dw * a;
        B += dw * a * x;
        wSum += dw * w.omega;
      }
    }
    let Tc = 0;
    this.clutchLocked = false;
    const Ie = e.inertia;
    if (engaged) {
      const wIn = G * wSum;
      const Tlock = (this.engineOmega - wIn + dt * (Te / Ie + G * B)) / (dt * (1 / Ie + G * Gt * A));
      const cap = this.clutchEngage * g.clutchMaxTorque;
      if (Math.abs(Tlock) <= cap) {
        Tc = Tlock;
        this.clutchLocked = true;
      } else {
        Tc = Math.sign(Tlock) * cap;
      }
    }
    this.engineOmega += (dt * (Te - Tc)) / Ie;
    for (let i = 0; i < 4; i++) {
      const w = W[i];
      const drive = Tc * Gt * p.wheels[i].driveWeight;
      w.omega += dt * aCoef[i] * (drive - xTorque[i]);
    }
    // Limited-slip differentials: transfer torque from the faster wheel to
    // the slower one, up to the locking torque.
    for (const ax of p.axles) {
      const d = ax.diff;
      if (d.type === 'open' || p.wheels[ax.left].driveWeight <= 0) continue;
      const iL = ax.left;
      const iR = ax.right;
      const delta = W[iL].omega - W[iR].omega;
      const tIn = Tc * Gt * (p.wheels[iL].driveWeight + p.wheels[iR].driveWeight);
      const lockMax = d.type === 'spool' ? 1e9 : d.preload + (tIn >= 0 ? d.accel : d.decel) * Math.abs(tIn);
      const tEq = delta / (dt * (aCoef[iL] + aCoef[iR]));
      const t = clamp(tEq, -lockMax, lockMax);
      W[iL].omega -= dt * aCoef[iL] * t;
      W[iR].omega += dt * aCoef[iR] * t;
    }
    // AWD center coupling: viscous, limited so it never overshoots.
    if (p.centerLock > 0 && engaged) {
      const front = (W[0].omega + W[1].omega) / 2;
      const rear = (W[2].omega + W[3].omega) / 2;
      const delta = front - rear;
      const aF = (aCoef[0] + aCoef[1]) / 2;
      const aR = (aCoef[2] + aCoef[3]) / 2;
      const tMax = Math.abs(delta) / (dt * (aF + aR));
      const t = clamp(p.centerLock * delta * 40, -tMax, tMax);
      for (let i = 0; i < 2; i++) W[i].omega -= dt * aCoef[i] * t * 0.5;
      for (let i = 2; i < 4; i++) W[i].omega += dt * aCoef[i] * t * 0.5;
    }
    // Brakes are friction: they stop a wheel but never spin it backwards.
    for (let i = 0; i < 4; i++) {
      const w = W[i];
      if (brakeSign[i] !== 0) {
        if (Math.sign(w.omega) !== brakeSign[i]) w.omega = 0;
      } else if (w.brakeTorque > 0) {
        const dw = dt * aCoef[i] * w.brakeTorque;
        if (Math.abs(w.omega) <= dw) w.omega = 0;
        else w.omega -= Math.sign(w.omega) * dw;
      }
    }
    if (this.clutchLocked) {
      let s = 0;
      for (let i = 0; i < 4; i++) s += p.wheels[i].driveWeight * W[i].omega;
      this.engineOmega = G * s;
    }
    const minOmega = e.electric ? -1e9 : (e.idle * 0.45 * Math.PI) / 30;
    const maxOmega = (e.limiter * 1.06 * Math.PI) / 30;
    if (!e.electric) this.engineOmega = clamp(this.engineOmega, minOmega, maxOmega);
    else this.engineOmega = clamp(this.engineOmega, -maxOmega, maxOmega);
    void brakeIn;
  }

  private shiftTo(gear: number): void {
    if (gear === this.gear && this.shiftTimer <= 0) return;
    this.shiftUp = gear > this.gear && this.gear >= 1;
    this.nextGear = gear;
    this.shiftTotal = this.params.gearbox.shiftTime;
    this.shiftTimer = this.shiftTotal;
    this.timeSinceShift = 0;
  }

  /**
   * Clutch torque the driven tires can put down at peak longitudinal grip,
   * from their current loads and surfaces (used by launch control).
   */
  tractionTorque(): number {
    const p = this.params;
    const g = p.gearbox;
    const G = Math.abs(g.ratios[this.gear === -1 ? 0 : Math.max(1, this.gear)] * g.final);
    let wheelTorque = 0;
    for (let i = 0; i < 4; i++) {
      const wp = p.wheels[i];
      if (wp.driveWeight <= 0) continue;
      const w = this.wheels[i];
      const t = wp.tire;
      const surf = SURFACES[w.surface];
      const mu = t.info.mu * t.info.muLong * t.widthGrip * t.tuneGrip * t.info.surface[surf.grip] * surf.gripScale;
      // Limited by the driven wheel with the least grip relative to its torque share.
      const perWheel = (mu * w.load * wp.radius) / wp.driveWeight;
      wheelTorque = wheelTorque === 0 ? perWheel : Math.min(wheelTorque, perWheel);
    }
    return wheelTorque / Math.max(1e-6, G * g.efficiency);
  }

  /** Number of forward gears. */
  get gearCount(): number {
    return this.params.gearbox.ratios.length - 1;
  }

  /** Engine rpm the driven wheels imply in the given gear. */
  wheelRpm(gear: number): number {
    const p = this.params;
    let s = 0;
    for (let i = 0; i < 4; i++) s += p.wheels[i].driveWeight * this.wheels[i].omega;
    const ratio = p.gearbox.ratios[gear === -1 ? 0 : gear] * p.gearbox.final;
    return (Math.abs(s * ratio) * 30) / Math.PI;
  }

  private updateTransmission(dt: number): void {
    const p = this.params;
    const g = p.gearbox;
    const e = p.engine;
    const inp = this.input;
    const n = this.gearCount;

    // Reverse: hold the brake at a standstill; throttle returns to first.
    if (this.gear >= 1 && this.shiftTimer <= 0) {
      if (inp.brake > 0.4 && inp.throttle < 0.05 && this.vLong < 0.5) {
        this.reverseTimer += dt;
        if (this.reverseTimer > 0.35) {
          this.shiftTo(-1);
          this.reverseTimer = 0;
        }
      } else this.reverseTimer = 0;
    } else if (this.gear === -1 && this.shiftTimer <= 0) {
      if (inp.throttle > 0.2 && inp.brake < 0.1 && this.vLong > -0.6) this.shiftTo(1);
    }

    if (this.shiftTimer > 0) {
      this.shiftTimer -= dt;
      if (this.shiftTimer <= this.shiftTotal * 0.5 && this.gear !== this.nextGear) this.gear = this.nextGear;
      if (this.shiftTimer <= 0) {
        this.shiftTimer = 0;
        this.gear = this.nextGear;
      }
    } else if (this.gear >= 1) {
      if (this.aids.gearbox === 'auto') {
        const wheelRpm = this.wheelRpm(this.gear);
        const slipping = this.tcsCut > 0.3 || this.wheels.some((w, i) => p.wheels[i].driveWeight > 0 && w.slipRatio > 0.35);
        if (this.timeSinceShift > 0.4 && this.gear < n && wheelRpm > g.upshiftRpm[this.gear] && inp.throttle > 0.05 && !slipping) {
          this.shiftTo(this.gear + 1);
        } else if (this.timeSinceShift > 0.6 && this.gear > 1) {
          const thr = inp.throttle;
          const downAt = g.downshiftRpm[this.gear] * (thr > 0.6 ? 1.1 : thr > 0.15 ? 0.85 : inp.brake > 0.3 ? 1 : 0.7);
          if (wheelRpm < downAt) this.shiftTo(this.gear - 1);
        }
      } else {
        if (inp.shiftUp && this.gear < n) this.shiftTo(this.gear + 1);
        if (inp.shiftDown && this.gear > 1) {
          // Refuse a downshift that would over-rev the engine.
          if (this.wheelRpm(this.gear - 1) < e.limiter * 0.98) this.shiftTo(this.gear - 1);
        }
      }
    }

    // Clutch engagement.
    let engage = 1;
    if (this.shiftTimer > 0) {
      const t = 1 - this.shiftTimer / this.shiftTotal;
      engage = t < 0.5 ? 0 : (t - 0.5) * 2;
      // Blip the throttle on downshifts so the revs match the lower gear.
    }
    this.launching = false;
    if (!e.electric && this.gear !== 0 && this.shiftTimer <= 0) {
      const rpm = this.rpm;
      const pedal = this.gear === -1 ? inp.brake : inp.throttle;
      if (pedal > 0.05) {
        if (this.clutchLocked && rpm > e.idle + 150) {
          engage = 1;
        } else {
          // Launch, like launch control: a throttle governor holds the
          // launch rpm (see updateAids) and the slipping clutch passes the
          // engine's torque at that rpm. With traction control on, the
          // clutch is also capped at what the driven tires can put down.
          this.launching = true;
          const boostMult = e.turbo ? 1 - e.turbo.share + e.turbo.share * this.boost : 1;
          let cap = curveTorque(e, g.launchRpm) * boostMult * pedal + 0.5 * (rpm - g.launchRpm);
          if (this.aids.tcs) cap = Math.min(cap, this.tractionTorque() * 0.98);
          engage = clamp(cap / g.clutchMaxTorque, 0, 1);
        }
      } else {
        engage = Math.min(engage, clamp((rpm - (e.idle + 40)) / 450, 0, 1));
      }
    }
    if (inp.handbrake > 0.5 && Math.abs(this.vLong) < 7) engage = 0;
    engage *= 1 - clamp(inp.clutch, 0, 1);
    this.clutchEngage = engage;
  }

  private updateSteering(dt: number): void {
    const p = this.params;
    const s = clamp(this.input.steer, -1, 1);
    const maxS = p.maxSteer;
    const raw = -s * maxS;
    let target = raw;
    if (this.aids.steering !== 'off' && this.vLong > 2) {
      // Direction the front axle is actually moving, relative to the body.
      this.frontAxle.set(0, 0, -p.a);
      this.toWorld(this.frontAxle, tmpA);
      this.pointVelocity(tmpA, tmpB);
      const vfl = tmpB.dot(this.fwd);
      const vfs = tmpB.dot(this.right);
      const theta = datan2(-vfs, Math.max(vfl, 0.5));
      const aLim = p.wheels[0].tire.alphaPeak * 1.2;
      const leftLim = Math.min(maxS, theta + aLim);
      const rightLim = Math.max(-maxS, theta - aLim);
      let center = this.aids.steering === 'assisted' ? theta * 0.8 : 0;
      center = clamp(center, rightLim, leftLim);
      const assisted = s > 0 ? center + (rightLim - center) * s : center + (leftLim - center) * -s;
      target = lerp(raw, assisted, smoothstep(2, 8, this.vLong));
    }
    // Steering rack speed limit.
    this.steerAngle = moveTowards(this.steerAngle, target, 5.5 * dt);
    const d = this.steerAngle;
    // Partial Ackermann: the inner wheel turns more.
    const L = p.wheelbase;
    const t = p.spec.trackFront;
    const ack = p.ackermann;
    let dl = d;
    let dr = d;
    if (Math.abs(d) > 1e-4) {
      const R = L / dtan(Math.abs(d));
      const inner = datan(L / Math.max(0.5, R - t / 2));
      const outer = datan(L / (R + t / 2));
      const sgn = Math.sign(d);
      if (d > 0) {
        dl = sgn * lerp(Math.abs(d), inner, ack);
        dr = sgn * lerp(Math.abs(d), outer, ack);
      } else {
        dr = sgn * lerp(Math.abs(d), inner, ack);
        dl = sgn * lerp(Math.abs(d), outer, ack);
      }
    }
    const wf = p.wheels;
    // Toe-in points both front wheels slightly toward the centerline.
    this.wheels[0].steer = dl - wf[0].toe;
    this.wheels[1].steer = dr + wf[1].toe;
    this.wheels[2].steer = -wf[2].toe;
    this.wheels[3].steer = wf[3].toe;
  }

  /** Body corners pushing out of the ground: rollovers, hard landings, bottoming. */
  private bodyContacts(ground: Ground): void {
    const p = this.params;
    for (const lp of p.bodyPoints) {
      this.toWorld(lp, tmpA);
      if (!ground.sample(tmpA.x, tmpA.z, tmpA.y + 0.5, hit)) continue;
      const depth = (hit.y - tmpA.y) * hit.ny;
      if (depth <= 0) continue;
      tmpB.set(hit.nx, hit.ny, hit.nz);
      this.pointVelocity(tmpA, tmpC);
      const vn = tmpC.dot(tmpB);
      let fn = 2.4e5 * depth + 1e5 * depth * depth * 100 - 9000 * vn;
      if (fn <= 0) continue;
      fn = Math.min(fn, p.mass * 60);
      // Friction against sliding along the ground.
      const vtx = tmpC.x - tmpB.x * vn;
      const vty = tmpC.y - tmpB.y * vn;
      const vtz = tmpC.z - tmpB.z * vn;
      const vt = Math.sqrt(vtx * vtx + vty * vty + vtz * vtz);
      const mu = 0.45;
      const ff = vt > 0.3 ? (mu * fn) / vt : (mu * fn) / 0.3;
      bodyF.set(tmpB.x * fn - vtx * ff, tmpB.y * fn - vty * ff, tmpB.z * fn - vtz * ff);
      force.add(bodyF);
      tmpB.subVectors(tmpA, this.pos);
      torque.add(tmpC.crossVectors(tmpB, bodyF));
    }
  }
}

const aCoef = new Float64Array(4);
const xTorque = new Float64Array(4);
const brakeSigns = new Float64Array(4);
const bodyF = new V3();
