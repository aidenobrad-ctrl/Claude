import { World, newHitRoad } from '../../src/world/world';
import { newHit } from '../../src/world/ground';
import { Sim } from '../../src/sim';
const w = new World(1);
const seen = new Set<number>([0]);
const q = [0];
while (q.length) {
  const u = q.shift() as number;
  for (const eid of w.nodes[u].edges) {
    const e = w.edges[eid];
    const v = e.a === u ? e.b : e.a;
    if (!seen.has(v)) { seen.add(v); q.push(v); }
  }
}
for (let i = 0; i < w.nodes.length; i++) if (!seen.has(i)) console.log('unreachable', w.nodes[i].id, w.nodes[i].x, w.nodes[i].z, 'edges', w.nodes[i].edges.map((e) => `${w.nodes[w.edges[e].a].id}-${w.nodes[w.edges[e].b].id}`));
// The lip.
const e = w.edges.find((x) => w.nodes[x.a].id === (process.argv[2] ?? 'passTop') && w.nodes[x.b].id === (process.argv[3] ?? 'northVillage'));
if (e) {
  const r = e.road;
  let i = 0;
  while (i < r.n && r.s[i] < Number(process.argv[4] ?? 427)) i++;
  console.log('bridge', e.bridge[i], 'i', i, 'n', r.n);
  const sim = new Sim(1);
  const h = newHit();
  for (const side of [-1, 1]) {
    for (const off of [e.info.halfWidth - 0.3, e.info.halfWidth + 0.6, e.info.halfWidth + 3, e.info.halfWidth + 8]) {
      const x = r.x[i] - r.tz[i] * side * off;
      const z = r.z[i] + r.tx[i] * side * off;
      sim.ground.sample(x, z, r.y[i] + 2, h);
      const hit = newHitRoad();
      w.nearestRoad(x, z, 60, hit);
      console.log(`side ${side} off ${off.toFixed(1)}: ground ${h.y.toFixed(2)} road y ${r.y[i].toFixed(2)} base ${e.base[i].toFixed(2)} terrain ${w.terrainHeight(x, z).toFixed(2)} nearest edge ${w.nodes[w.edges[hit.edge].a].id}-${w.nodes[w.edges[hit.edge].b].id} dist ${hit.dist.toFixed(1)} y ${hit.y.toFixed(2)} bridge ${hit.bridge}`);
    }
  }
}
