// Report roads whose far-apart parts (along the road) come close in space.
import { World } from '../../src/world/world';
const w = new World(1);
for (const e of w.edges) {
  const r = e.road;
  const gap = 2 * e.info.halfWidth + 10;
  let worst = Infinity;
  let wi = 0;
  let wj = 0;
  for (let i = 0; i < r.n; i += 2) {
    for (let j = i + 1; j < r.n; j += 2) {
      if (r.s[j] - r.s[i] < 60) continue;
      const d = Math.sqrt((r.x[i] - r.x[j]) ** 2 + (r.z[i] - r.z[j]) ** 2);
      if (d < worst) {
        worst = d;
        wi = i;
        wj = j;
      }
    }
  }
  if (worst < gap) console.log(`${w.nodes[e.a].id}->${w.nodes[e.b].id}: legs ${worst.toFixed(1)} m apart at s=${r.s[wi].toFixed(0)} and s=${r.s[wj].toFixed(0)} (dy ${(r.y[wj] - r.y[wi]).toFixed(1)} m)`);
}
