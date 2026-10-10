import { test, assert, assertRange, assertNear } from './kit';
import { RNG, hash2f } from '../src/engine/rng';
import { Noise2 } from '../src/engine/noise';
import { FixedStepper, SIM_DT } from '../src/engine/loop';
import { Quat, V3, wrapPi } from '../src/engine/math';
import { neutralControls, shapeAxis } from '../src/engine/input';
import { Sim } from '../src/sim';

test('rng is deterministic per seed and differs across seeds', () => {
  const a = new RNG(42);
  const b = new RNG(42);
  const c = new RNG(43);
  const sa = Array.from({ length: 100 }, () => a.u32());
  const sb = Array.from({ length: 100 }, () => b.u32());
  const sc = Array.from({ length: 100 }, () => c.u32());
  assert(sa.every((v, i) => v === sb[i]), 'same seed must give the same sequence');
  assert(sa.some((v, i) => v !== sc[i]), 'different seeds must differ');
  const s = new RNG('festival');
  const state = s.getState();
  const x = s.next();
  s.setState(state);
  assert(s.next() === x, 'setState must restore the sequence');
});

test('rng distribution is roughly uniform and gaussian is sane', () => {
  const r = new RNG(7);
  const bins = new Array(10).fill(0);
  let sum = 0;
  let sq = 0;
  const n = 100000;
  for (let i = 0; i < n; i++) {
    bins[Math.floor(r.next() * 10)]++;
    const g = r.gauss();
    sum += g;
    sq += g * g;
  }
  for (const b of bins) assertRange(b / n, 0.095, 0.105, 'bin share');
  assertNear(sum / n, 0, 0.02, 'gauss mean');
  assertNear(Math.sqrt(sq / n), 1, 0.02, 'gauss sd');
  assertRange(hash2f(3, 4, 5), 0, 1, 'hash2f');
});

test('simplex noise stays in range and is seed-dependent', () => {
  const n1 = new Noise2(1);
  const n2 = new Noise2(2);
  let min = Infinity;
  let max = -Infinity;
  let diff = 0;
  for (let i = 0; i < 20000; i++) {
    const x = (i % 200) * 0.173;
    const y = Math.floor(i / 200) * 0.191;
    const v = n1.simplex(x, y);
    min = Math.min(min, v);
    max = Math.max(max, v);
    diff += Math.abs(v - n2.simplex(x, y));
  }
  assertRange(min, -1.05, -0.5, 'noise min');
  assertRange(max, 0.5, 1.05, 'noise max');
  assert(diff > 1000, 'different seeds must give different noise');
  const r = n1.ridged(10.5, 3.25, 5);
  assertRange(r, 0, 1, 'ridged');
});

test('fixed stepper runs exact 240 Hz steps and clamps long frames', () => {
  const s = new FixedStepper();
  let n = 0;
  s.advance(1 / 60, () => n++);
  assert(n === 4, `expected 4 steps for a 60 Hz frame, got ${n}`);
  n = 0;
  s.advance(5, () => n++);
  assert(n === Math.round(0.1 / SIM_DT) || n === Math.round(0.1 / SIM_DT) - 1, `long frame must clamp to 0.1 s, got ${n} steps`);
  n = 0;
  s.advance(NaN, () => n++);
  s.advance(-1, () => n++);
  assert(n === 0, 'invalid frame times must not step');
  assertRange(s.alpha, 0, 1, 'alpha');
});

test('quaternion integration preserves unit length and rotates correctly', () => {
  const q = new Quat();
  for (let i = 0; i < 2400; i++) q.integrate({ x: 0, y: Math.PI / 2, z: 0 }, SIM_DT);
  const len = Math.hypot(q.x, q.y, q.z, q.w);
  assertNear(len, 1, 1e-9, 'quat length');
  // 10 s at 90°/s = 900° = 180° net: forward (-Z) should face +Z.
  const f = new V3(0, 0, -1).applyQuat(q);
  assertNear(f.z, 1, 1e-3, 'rotated forward z');
  const back = f.clone().applyQuatInv(q);
  assertNear(back.z, -1, 1e-9, 'inverse rotation');
  assertNear(wrapPi(3 * Math.PI), Math.PI, 1e-9, 'wrapPi');
});

test('gamepad deadzone shaping', () => {
  const s = { deadzone: 0.1, outerDeadzone: 0.05, triggerDeadzone: 0.05, steerLinearity: 1 };
  assert(shapeAxis(0.05, s) === 0, 'inside deadzone is zero');
  assertNear(shapeAxis(0.95, s), 1, 1e-9, 'outer deadzone reaches 1');
  assertNear(shapeAxis(-0.525, s), -0.5, 1e-9, 'midpoint maps linearly');
});

test('simulation is bit-for-bit deterministic', () => {
  const run = (): string => {
    const sim = new Sim(1234);
    const c = neutralControls();
    for (let i = 0; i < 240 * 20; i++) {
      c.throttle = i % 700 < 500 ? 1 : 0;
      c.brake = i % 700 >= 600 ? 1 : 0;
      c.steer = Math.sin(i * 0.01);
      sim.step(c);
    }
    return sim.hash();
  };
  const h1 = run();
  const h2 = run();
  assert(h1 === h2, `hash mismatch ${h1} vs ${h2}`);
});
