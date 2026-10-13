// How holdable a drift is, car by car: a handbrake entry at 60 km/h, then a
// driver aiming for 20, 30 and 40 degrees of slip for 8 s with ESC and TCS off.
// The driver countersteers to the slip angle plus a correction on the error
// and on how fast the slip is changing (a person sees the car rotate), and
// works the throttle the same way. Reports the time held within 10 degrees of
// the target and spins (over 75 degrees, or stopped).
//   node tools/run-ts.mjs tools/dev/drift-probe.ts [sim|standard] [compound=slide,E ...]
// "standard" uses the game's default steering aid with the same driver.
// Overrides like street=0.86,0.4 try other tyre curves without editing them.
import { CARS } from '../../src/vehicles/cars';
import { buildParams, defaultTune } from '../../src/vehicles/params';
import { newTestVehicle } from '../../src/vehicles/telemetry';
import { COMPOUNDS, type Compound } from '../../src/vehicles/tires';
import { FlatGround } from '../../src/world/ground';
import { SURFACE } from '../../src/world/surfaces';
import { SIM_DT } from '../../src/engine/loop';
import { datan2 } from '../../src/engine/dmath';

const args = process.argv.slice(2);
const mode = args.includes('standard') ? 'standard' : 'off';
const rally = args.includes('rally');
for (const a of args) {
  const m = /^(\w+)=([\d.]+),([\d.]+)$/.exec(a);
  if (!m || !(m[1] in COMPOUNDS || m[1] === "all")) continue;
  if (m[1] === 'all') {
    for (const c of Object.values(COMPOUNDS)) {
      c.slide = +m[2];
      c.E = +m[3];
    }
  } else {
    COMPOUNDS[m[1] as Compound].slide = +m[2];
    COMPOUNDS[m[1] as Compound].E = +m[3];
  }
}

const lockArg = args.find((a) => a.startsWith('lock='));
const awdArg = args.find((a) => a.startsWith('awd='));
const surfArg = args.find((a) => a.startsWith('surface='));
const flat = new FlatGround(surfArg ? () => SURFACE[surfArg.slice(8) as keyof typeof SURFACE] : undefined);
const DEG = 180 / Math.PI;
let total = 0;
let spins = 0;
const clamp = (x: number, a: number, b: number) => Math.max(a, Math.min(b, x));
for (const spec of CARS) {
  const params = buildParams(spec, awdArg ? { ...defaultTune(spec), awdFront: +awdArg.slice(4) } : undefined);
  if (lockArg && params.centerLock > 0) params.centerLock = +lockArg.slice(5);
  const row: string[] = [];
  for (const target of [20, 30, 40]) {
    const v = newTestVehicle(params, { esc: false, tcs: false, steering: mode });
    for (let i = 0; i < 0.3 / SIM_DT; i++) v.step(SIM_DT, flat);
    v.setSpeed(17);
    // Entry: turn in with a pull of the handbrake until the slip is half way
    // to the target (at most a second), as a person would.
    for (let i = 0; i < 1 / SIM_DT; i++) {
      if (datan2(v.vLat, Math.max(1, v.vLong)) * DEG > target * 0.5) break;
      v.input.steer = -0.6;
      v.input.handbrake = 1;
      v.input.throttle = 0;
      v.input.brake = 0;
      v.step(SIM_DT, flat);
    }
    let held = 0;
    let spun = false;
    let prev = datan2(v.vLat, Math.max(1, v.vLong)) * DEG;
    let rate = 0;
    for (let i = 0; i < 8 / SIM_DT; i++) {
      const beta = datan2(v.vLat, Math.max(1, v.vLong)) * DEG;
      // Slip rate in degrees per second, smoothed over about a tenth of a second.
      rate += ((beta - prev) / SIM_DT - rate) * Math.min(1, SIM_DT / 0.1);
      prev = beta;
      if (Math.abs(beta) > 75 || v.vLong < 1) spun = true;
      if (spun) break;
      const err = beta - target;
      // A rally driver's habit (option "rally"): power stays on and a dab of
      // the handbrake brings back rotation the throttle alone cannot.
      v.input.handbrake = rally && err < -8 && rate < 20 ? 1 : 0;
      if (mode === 'off') v.input.steer = clamp((beta + 0.5 * err + 0.12 * rate) / spec.steering.maxAngle, -1, 1);
      else v.input.steer = clamp((0.04 * err + 0.008 * rate), -1, 1);
      v.input.throttle = rally ? 1 : clamp(0.6 - 0.03 * err - 0.006 * rate, 0, 1);
      v.step(SIM_DT, flat);
      if (Math.abs(err) < 10 && v.speed > 6) held += SIM_DT;
    }
    total += held;
    if (spun) spins++;
    row.push(`${target}°: ${held.toFixed(1)} s${spun ? ' SPUN' : ''}`);
  }
  console.log(`${spec.id.padEnd(24)} ${spec.drivetrain.padEnd(3)} ${row.join('   ')}`);
}
console.log(`total held ${total.toFixed(1)} s of ${CARS.length * 3 * 8}; ${spins} spins`);
