// Prints the docs/CARS.md table from the measurements of the last physics
// test run (artifacts/metrics/physics.json), plus each car's measured values
// against its real-world references for the "Reference values" paragraphs.
//
//   node tools/run-sim-tests.mjs physics --grep validation
//   node tools/run-ts.mjs tools/dev/cars-table.ts
import fs from 'node:fs';
import { CARS } from '../../src/vehicles/cars';
import { buildParams } from '../../src/vehicles/params';
import type { Category } from '../../src/vehicles/spec';

interface Measured {
  t100: number;
  t200: number | null;
  quarter: number;
  quarterKmh: number;
  topKmh: number;
  brake100: number;
  skidpadG: number;
  ref: { t100: number; t200?: number; brake100: number; skidpad: number; top: number };
}

const FILE = 'artifacts/metrics/physics.json';
if (!fs.existsSync(FILE)) {
  console.error(`${FILE} not found: run node tools/run-sim-tests.mjs physics --grep validation first`);
  process.exit(1);
}
const metrics = JSON.parse(fs.readFileSync(FILE, 'utf8')) as Record<string, Measured>;
const missing = CARS.filter((c) => !metrics[c.id]?.ref).map((c) => c.id);
if (missing.length) {
  console.error(`no measurements for ${missing.join(', ')}: run node tools/run-sim-tests.mjs physics --grep validation first`);
  process.exit(1);
}

const CLASS: Record<Category, string> = {
  compact: 'Compact', hothatch: 'Hot hatch', classic: 'Classic', muscle: 'Muscle', sports: 'Sports', gt: 'GT',
  super: 'Super', hyper: 'Hyper', rally: 'Rally', offroad: 'Off-road', truck: 'Truck', suv: 'SUV',
};
const dev = (v: number, r: number): string => {
  const d = Math.round((v / r - 1) * 100);
  return d === 0 ? '±0%' : `${d > 0 ? '+' : '−'}${Math.abs(d)}%`;
};

console.log('| Car | Class | Drive | Power | Mass | 0–100 | 0–200 | ¼ mile | Top speed | 100–0 | Skidpad |');
console.log('|---|---|---|---|---|---|---|---|---|---|---|');
for (const c of CARS) {
  const m = metrics[c.id];
  const kw = buildParams(c).engine.peakPower / 1000;
  const t200 = m.ref.t200 !== undefined && m.t200 !== null ? `${m.t200.toFixed(2)} s` : '—';
  console.log(
    `| ${c.make} ${c.model} | ${CLASS[c.category]} | ${c.drivetrain} | ${kw.toFixed(0)} kW | ${c.mass} kg | ${m.t100.toFixed(2)} s | ${t200} | ` +
      `${m.quarter.toFixed(2)} s @ ${m.quarterKmh.toFixed(0)} km/h | ${m.topKmh.toFixed(1)} km/h | ${m.brake100.toFixed(1)} m | ${m.skidpadG.toFixed(3)} g |`,
  );
}

console.log('\nMeasured against the references (rounded deviation; every value must be within ±10%):');
for (const c of CARS) {
  const m = metrics[c.id];
  const r = m.ref;
  const parts = [`0–100 ${m.t100.toFixed(2)} s vs ${r.t100} (${dev(m.t100, r.t100)})`];
  if (r.t200 !== undefined && m.t200 !== null) parts.push(`0–200 ${m.t200.toFixed(2)} s vs ${r.t200} (${dev(m.t200, r.t200)})`);
  parts.push(`100–0 ${m.brake100.toFixed(1)} m vs ${r.brake100} (${dev(m.brake100, r.brake100)})`);
  parts.push(`skidpad ${m.skidpadG.toFixed(3)} g vs ${r.skidpad} (${dev(m.skidpadG, r.skidpad)})`);
  parts.push(`top ${m.topKmh.toFixed(1)} km/h vs ${r.top} (${dev(m.topKmh, r.top)})`);
  console.log(`- ${c.make} ${c.model}: ${parts.join(', ')}`);
}
