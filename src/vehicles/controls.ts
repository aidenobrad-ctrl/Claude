// Turns raw player controls into driver input. Digital (keyboard) steering
// is ramped at a speed-dependent rate so it feels like turning a wheel;
// analog steering passes through with light smoothing.
import { clamp, moveTowards } from '../engine/math';
import type { Controls } from '../engine/input';
import { neutralInput, type DriverInput, type Vehicle } from './vehicle';

export interface ControlSettings {
  /** Steering sensitivity multiplier (0.5 .. 1.5). */
  steerSensitivity: number;
  /** Throttle stays applied while no brake is pressed (touch accessibility). */
  autoThrottle: boolean;
}

export class PlayerControlFilter {
  steer = 0;
  settings: ControlSettings = { steerSensitivity: 1, autoThrottle: false };
  readonly out: DriverInput = neutralInput();

  reset(): void {
    this.steer = 0;
  }

  update(c: Controls, v: Vehicle, dt: number): DriverInput {
    const sens = this.settings.steerSensitivity;
    if (c.analogSteer) {
      // Light smoothing removes jitter from sticks and touch without lag.
      this.steer += (clamp(c.steer * sens, -1, 1) - this.steer) * Math.min(1, dt * 30);
    } else {
      const target = clamp(c.steer, -1, 1);
      const speed = Math.abs(v.vLong);
      // Turning in slows from 4.5/s at a crawl to 1.6/s at 200 km/h;
      // returning to center is always quicker.
      const inRate = (4.5 - Math.min(1, speed / 55) * 2.9) * sens;
      const returning = target === 0 || Math.sign(target) !== Math.sign(this.steer);
      this.steer = moveTowards(this.steer, target, (returning ? 6 : inRate) * dt);
    }
    const o = this.out;
    o.steer = this.steer;
    o.throttle = clamp(c.throttle, 0, 1);
    if (this.settings.autoThrottle && c.brake < 0.05 && c.handbrake < 0.05) o.throttle = Math.max(o.throttle, 0.85);
    o.brake = clamp(c.brake, 0, 1);
    o.handbrake = clamp(c.handbrake, 0, 1);
    o.clutch = clamp(c.clutch, 0, 1);
    o.shiftUp = c.pressed.shiftUp;
    o.shiftDown = c.pressed.shiftDown;
    return o;
  }
}
