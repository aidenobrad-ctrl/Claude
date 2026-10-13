// One drift probe run, traced: node tools/run-ts.mjs tools/dev/drift-trace.ts <car> <target> [standard]
import { CARS } from '../../src/vehicles/cars';
import { buildParams } from '../../src/vehicles/params';
import { newTestVehicle } from '../../src/vehicles/telemetry';
import { FlatGround } from '../../src/world/ground';
import { SURFACE } from '../../src/world/surfaces';
import { SIM_DT } from '../../src/engine/loop';
import { datan2 } from '../../src/engine/dmath';

const [id, tgt, md, surf] = process.argv.slice(2);
const spec = CARS.find((c) => c.id === id)!;
const target = +(tgt ?? 30);
const mode = md === 'standard' || md === 'rally' ? 'standard' : 'off';
const rally = md === 'rally';
const flat = new FlatGround(surf ? () => SURFACE[surf as keyof typeof SURFACE] : undefined);
const DEG = 180 / Math.PI;
const clamp = (x: number, a: number, b: number) => Math.max(a, Math.min(b, x));
const v = newTestVehicle(buildParams(spec), { esc: false, tcs: false, steering: mode });
for (let i = 0; i < 0.3 / SIM_DT; i++) v.step(SIM_DT, flat);
v.setSpeed(17);
const line = (t: number) => {
  const beta = datan2(v.vLat, Math.max(1, v.vLong)) * DEG;
  const w = v.wheels;
  console.log(`${t.toFixed(2)} v=${(v.speed * 3.6).toFixed(0)} beta=${beta.toFixed(1)} r=${(v.yawRate * DEG).toFixed(0)} steer=${(v.steerAngle * DEG).toFixed(1)} thr=${v.input.throttle.toFixed(2)} hb=${v.input.handbrake} g=${v.gear} rpm=${(v.engineOmega * 60 / 2 / Math.PI).toFixed(0)} slipF=${w[0].slip.toFixed(1)},${w[1].slip.toFixed(1)} slipR=${w[2].slip.toFixed(1)},${w[3].slip.toFixed(1)} kR=${w[2].slipRatio.toFixed(2)},${w[3].slipRatio.toFixed(2)} aR=${(w[2].slipAngle * DEG).toFixed(0)}`);
};
for (let i = 0; i < 1 / SIM_DT; i++) {
  if (datan2(v.vLat, Math.max(1, v.vLong)) * DEG > target * 0.5) break;
  v.input.steer = -0.6;
  v.input.handbrake = 1;
  v.input.throttle = 0;
  v.input.brake = 0;
  v.step(SIM_DT, flat);
  if (i % Math.round(0.1 / SIM_DT) === 0) line(i * SIM_DT);
}
let prev = datan2(v.vLat, Math.max(1, v.vLong)) * DEG;
let rate = 0;
for (let i = 0; i < 8 / SIM_DT; i++) {
  const beta = datan2(v.vLat, Math.max(1, v.vLong)) * DEG;
  rate += ((beta - prev) / SIM_DT - rate) * Math.min(1, SIM_DT / 0.1);
  prev = beta;
  if (Math.abs(beta) > 75 || v.vLong < 1) { console.log('SPUN'); break; }
  const err = beta - target;
  v.input.handbrake = rally && err < -8 && rate < 20 ? 1 : 0;
  if (mode === 'off') v.input.steer = clamp((beta + 0.5 * err + 0.12 * rate) / spec.steering.maxAngle, -1, 1);
  else v.input.steer = clamp(0.04 * err + 0.008 * rate, -1, 1);
  v.input.throttle = rally ? 1 : clamp(0.6 - 0.03 * err - 0.006 * rate, 0, 1);
  v.step(SIM_DT, flat);
  if (i % Math.round(0.1 / SIM_DT) === 0) line(i * SIM_DT);
}
