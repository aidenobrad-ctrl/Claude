// Reference drive on the island for the C# Simulation port: the same
// scripted controls from the festival spawn, positions every 0.25 s.
//   node tools/run-ts.mjs tools/dev/sim-ref.ts
import fs from 'node:fs';
import { Sim } from '../../src/sim';
import { neutralControls } from '../../src/engine/input';

export const SCRIPT = [
  // [until s, throttle, brake, steer]
  [6, 1, 0, 0],
  [9, 0.7, 0, 0.35],
  [12, 1, 0, -0.25],
  [15, 0, 0.8, 0],
  [20, 0.8, 0, 0.6],
];
const sim = new Sim(1);
const c = neutralControls();
const out: number[][] = [];
let t = 0;
const total = SCRIPT[SCRIPT.length - 1][0] * 240;
for (let k = 0; k < total; k++) {
  const s = k / 240;
  const seg = SCRIPT.find((r) => s < r[0]) ?? SCRIPT[SCRIPT.length - 1];
  c.throttle = seg[1];
  c.brake = seg[2];
  c.steer = seg[3];
  c.analogSteer = true;
  c.source = 'gamepad';
  sim.step(c);
  t++;
  if (t % 60 === 0) {
    const v = sim.player.vehicle;
    out.push([s + 1 / 240, v.pos.x, v.pos.y, v.pos.z, v.speed, v.gear]);
  }
}
fs.writeFileSync('dotnet/ref/world/sim-ref.json', JSON.stringify({ script: SCRIPT, samples: out }));
console.log(`${out.length} samples; end at (${out[out.length - 1].slice(1, 4).map((x) => x.toFixed(2)).join(', ')}) ${(out[out.length - 1][4] * 3.6).toFixed(1)} km/h`);
