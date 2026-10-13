// Dry against wet for a few cars: 0-100, 100-0 and skidpad at three wetness levels.
//   node tools/run-ts.mjs tools/dev/wet-probe.ts
import { CARS } from '../../src/vehicles/cars';
import { buildParams } from '../../src/vehicles/params';
import { accelTest, brakeTest, skidpadTest } from '../../src/vehicles/telemetry';

const ids = process.argv.slice(2);
for (const s of CARS.filter((c) => !ids.length || ids.includes(c.id))) {
  const p = buildParams(s);
  const row: string[] = [];
  for (const w of [0, 0.5, 1]) {
    const a = accelTest(p, { wetness: w, maxTime: 20 });
    const b = brakeTest(p, 100, { wetness: w });
    const k = skidpadTest(p, 40, { wetness: w });
    row.push(`w${w}: ${a.t100.toFixed(2)} s ${b.distance.toFixed(1)} m ${k.g.toFixed(3)} g`);
  }
  console.log(`${s.id.padEnd(22)} ${row.join(' | ')}`);
}
