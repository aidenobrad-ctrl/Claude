import { Island, newSample } from '../../src/world/island';
import { World } from '../../src/world/world';
import { buildTerrainTile, buildTreeCell } from '../../src/world/terrain-data';
const isl = new Island(1);
const s = newSample();
let t0 = performance.now();
let acc = 0;
for (let i = 0; i < 200000; i++) acc += isl.sample(((i * 37) % 8000) - 4000, ((i * 91) % 8000) - 4000, s).h;
console.log('island.sample', ((performance.now() - t0) / 200).toFixed(2), 'µs', acc > 0);
const w = new World(1);
for (const level of [0, 1, 2, 3]) {
  t0 = performance.now();
  const n = level === 0 ? 4 : 3;
  for (let k = 0; k < n; k++) buildTerrainTile(w, level, 10 + k, 12);
  console.log('terrain tile level', level, ((performance.now() - t0) / n).toFixed(1), 'ms');
}
t0 = performance.now();
for (let k = 0; k < 4; k++) buildTreeCell(w, 3 + k, -4, 1);
console.log('tree cell', ((performance.now() - t0) / 4).toFixed(1), 'ms');
