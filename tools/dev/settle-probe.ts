import { World } from '../../src/world/world';
const t0 = performance.now();
const w = new World(1);
const ms = performance.now() - t0;
const kinds = new Map<number, number>();
for (const b of w.buildings.list) kinds.set(b.kind, (kinds.get(b.kind) ?? 0) + 1);
console.log(`world in ${ms.toFixed(0)} ms, ${w.buildings.list.length} buildings`, [...kinds.entries()]);
const tall = w.buildings.list.filter((b) => b.h > 40).length;
console.log('towers over 40 m:', tall);
