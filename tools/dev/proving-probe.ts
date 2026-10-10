// Probe: is the proving ground flat, and do island roads cross it?
import { Island, newSample, PROVING_ORIGIN as P } from '../../src/world/island';
import { World } from '../../src/world/world';

const isl = new Island(1);
const s = newSample();
let minH = Infinity, maxH = -Infinity, minCoast = Infinity;
for (let lx = -800; lx <= 720; lx += 10) {
  for (let lz = -130; lz <= 540; lz += 10) {
    isl.sample(P.x + lx, P.z + lz, s);
    minH = Math.min(minH, s.h);
    maxH = Math.max(maxH, s.h);
    minCoast = Math.min(minCoast, s.coast);
  }
}
console.log('facility base h', minH.toFixed(2), '..', maxH.toFixed(2), 'min coast', minCoast.toFixed(0));
const w = new World(1);
for (const e of w.edges) {
  let inside = 0;
  let first = -1;
  for (let i = 0; i < e.road.n; i++) {
    const lx = e.road.x[i] - P.x, lz = e.road.z[i] - P.z;
    if (lx > -800 && lx < 720 && lz > -130 && lz < 540) {
      inside++;
      if (first < 0) first = i;
    }
  }
  if (inside) console.log('edge', e.info.name ?? e.info, w.nodes[e.a].id, '->', w.nodes[e.b].id, 'samples inside', inside, 'at', (e.road.x[first] - P.x).toFixed(0), (e.road.z[first] - P.z).toFixed(0));
}
