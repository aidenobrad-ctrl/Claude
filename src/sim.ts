// The simulation: everything that advances on the fixed 240 Hz clock.
// It must not touch the DOM or three.js so it can run headless in Node.
import { SIM_DT } from './engine/loop';
import { V3 } from './engine/math';
import { dcos, dsin } from './engine/dmath';
import { RNG } from './engine/rng';
import type { Controls } from './engine/input';

/** M0 placeholder body: a point mass that the M1 vehicle model replaces. */
export interface Probe {
  pos: V3;
  vel: V3;
  yaw: number;
}

export interface SimState {
  tick: number;
  time: number;
  pos: [number, number, number];
  vel: [number, number, number];
  yaw: number;
  speed: number;
}

export class Sim {
  tick = 0;
  time = 0;
  readonly seed: number;
  rng: RNG;
  probe: Probe = { pos: new V3(), vel: new V3(), yaw: 0 };

  constructor(seed = 1) {
    this.seed = seed;
    this.rng = new RNG(seed);
  }

  reset(): void {
    this.tick = 0;
    this.time = 0;
    this.rng = new RNG(this.seed);
    this.probe = { pos: new V3(), vel: new V3(), yaw: 0 };
  }

  step(c: Controls): void {
    const p = this.probe;
    const dt = SIM_DT;
    const speed = p.vel.len();
    p.yaw -= c.steer * 1.2 * dt * Math.min(1, speed / 5);
    const fx = -dsin(p.yaw);
    const fz = -dcos(p.yaw);
    const accel = c.throttle * 6 - c.brake * 9 * Math.sign(speed);
    p.vel.x += fx * accel * dt;
    p.vel.z += fz * accel * dt;
    // Keep velocity aligned with the heading and add drag.
    const along = p.vel.x * fx + p.vel.z * fz;
    p.vel.set(fx * along, 0, fz * along).scale(1 - 0.05 * dt);
    p.pos.addScaled(p.vel, dt);
    this.tick++;
    this.time += dt;
  }

  teleport(x: number, z: number, yaw = 0, y = 0): void {
    if (![x, z, yaw, y].every(Number.isFinite)) throw new Error(`teleport: non-finite argument (${x}, ${z}, ${yaw}, ${y})`);
    this.probe.pos.set(x, y, z);
    this.probe.vel.set(0, 0, 0);
    this.probe.yaw = yaw;
  }

  getState(): SimState {
    const p = this.probe;
    return {
      tick: this.tick,
      time: this.time,
      pos: [p.pos.x, p.pos.y, p.pos.z],
      vel: [p.vel.x, p.vel.y, p.vel.z],
      yaw: p.yaw,
      speed: p.vel.len(),
    };
  }

  /** Bit-exact hash of the simulation state, for determinism tests. */
  hash(): string {
    const p = this.probe;
    return hashFloats([this.tick, p.pos.x, p.pos.y, p.pos.z, p.vel.x, p.vel.y, p.vel.z, p.yaw]);
  }
}

export function hashFloats(values: ArrayLike<number>): string {
  const buf = new DataView(new ArrayBuffer(8));
  let h1 = 0x811c9dc5;
  let h2 = 0x01000193;
  for (let i = 0; i < values.length; i++) {
    buf.setFloat64(0, values[i]);
    for (let b = 0; b < 8; b++) {
      const byte = buf.getUint8(b);
      h1 = Math.imul(h1 ^ byte, 0x01000193);
      h2 = Math.imul(h2 ^ byte, 0x5bd1e995);
    }
  }
  return (h1 >>> 0).toString(16).padStart(8, '0') + (h2 >>> 0).toString(16).padStart(8, '0');
}
