// Probe: tree placement counts and tile build cost.
import { World, TREE_STRIDE } from '../../src/world/world';
const w = new World(1);
const t0 = performance.now();
let n = 0;
const kinds = [0, 0, 0, 0, 0, 0];
let tiles = 0;
for (let tx = -60; tx < 60; tx += 3) {
  for (let tz = -60; tz < 60; tz += 3) {
    const t = w.tile(tx, tz);
    tiles++;
    for (let k = 0; k < t.trees.length; k += TREE_STRIDE) {
      n++;
      kinds[t.trees[k + 5]]++;
    }
  }
}
const ms = performance.now() - t0;
console.log(`${tiles} tiles in ${ms.toFixed(0)} ms (${(ms / tiles).toFixed(2)} ms/tile), ${n} trees (${(n / tiles).toFixed(1)}/tile), kinds`, kinds);
// Extrapolate to the whole island: 160x160 tiles.
console.log('island estimate', Math.round((n / tiles) * 160 * 160), 'trees');
