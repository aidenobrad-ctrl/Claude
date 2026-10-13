import { World, WorldGround } from '../../src/world/world';
import { newHit } from '../../src/world/ground';
const t0 = performance.now();
const w = new World(1);
const t1 = performance.now();
console.log(`world built in ${(t1 - t0).toFixed(0)} ms: ${w.nodes.length} nodes, ${w.edges.length} edges, ${(w.edges.reduce((s, e) => s + e.road.length, 0) / 1000).toFixed(1)} km`);
let bridges = 0, maxGrade = 0;
for (const e of w.edges) {
  for (let i = 0; i + 1 < e.road.n; i++) {
    if (e.bridge[i]) bridges += e.road.s[i + 1] - e.road.s[i];
    const g = Math.abs(e.road.y[i + 1] - e.road.y[i]) / Math.max(0.1, e.road.s[i + 1] - e.road.s[i]);
    if (g > maxGrade) maxGrade = g;
  }
}
console.log(`bridge length ${(bridges).toFixed(0)} m, max grade ${(maxGrade * 100).toFixed(1)}%`);
const g = new WorldGround(w);
const hit = newHit();
const t2 = performance.now();
let n = 0;
for (let x = -2000; x < 2000; x += 3.7) for (let z = 2500; z < 2600; z += 3.3) { g.sample(x, z, 200, hit); n++; }
const t3 = performance.now();
console.log(`${n} ground samples in ${(t3 - t2).toFixed(0)} ms (${((t3 - t2) * 1000 / n).toFixed(2)} µs each, incl. tile builds)`);
const t4 = performance.now();
for (let k = 0; k < 20000; k++) g.sample(-1000 + (k % 100) * 0.37, 2550 + (k % 37), 200, hit);
console.log(`cached: ${((performance.now() - t4) * 1000 / 20000).toFixed(2)} µs/sample`);
// Physics height vs. render-grid consistency at a vertex.
const tile = w.tile(10, 40);
const terr = { y: 0, nx: 0, ny: 1, nz: 0, surface: 0 as any, water: 0 };
w.groundAt(10 * 64 + 8, 40 * 64 + 12, terr);
console.log('vertex check', terr.y.toFixed(3), tile.h[3 * 17 + 2].toFixed(3));
