// Tightest bends on the built pass roads (the runtime World): radius from the tangent's turn over 12 m.
//   node tools/run-ts.mjs tools/dev/pass-radius.ts
import { World } from '../../src/world/world';

const w = new World(1);
for (const e of w.edges) {
  if (e.cls !== 'pass') continue;
  const r = e.road;
  const radii: number[] = [];
  for (let i = 3; i < r.n - 3; i++) {
    const a0 = Math.atan2(r.tz[i - 3], r.tx[i - 3]), a1 = Math.atan2(r.tz[i + 3], r.tx[i + 3]);
    let d = a1 - a0;
    while (d > Math.PI) d -= 2 * Math.PI;
    while (d < -Math.PI) d += 2 * Math.PI;
    const ds = r.s[i + 3] - r.s[i - 3];
    radii.push(Math.abs(d) > 1e-6 ? ds / Math.abs(d) : 1e9);
  }
  radii.sort((p, q) => p - q);
  const under = (m: number) => radii.filter((x) => x < m).length;
  console.log(`${w.nodes[e.a].id}->${w.nodes[e.b].id}: ${(r.s[r.n - 1] / 1000).toFixed(2)} km, tightest ${radii.slice(0, 5).map((x) => x.toFixed(1)).join(', ')} m; samples under 10 m: ${under(10)}, under 15 m: ${under(15)}`);
}
