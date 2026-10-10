import { Sim } from '../../src/sim';
import { newHit } from '../../src/world/ground';
import { newHitRoad } from '../../src/world/world';

const sim = new Sim(1);
const world = sim.world;
const [from, to, iStr, sideStr] = process.argv.slice(2);
const e = world.edges.find((q) => world.nodes[q.a].id === from && world.nodes[q.b].id === to)!;
const r = e.road;
const i = +iStr;
const side = +sideStr;
const hw = e.info.halfWidth;
console.log(`edge ${e.id} cls=${e.cls} hw=${hw} shoulder=${e.info.shoulder} y[i]=${r.y[i].toFixed(3)} y[i+1]=${r.y[i + 1].toFixed(3)} base=${e.base[i].toFixed(2)}`);
const g = newHit();
const h = newHitRoad();
for (const lat of [0, hw - 0.6, hw - 0.3, hw, hw + 0.3, hw + 0.6, hw + 1, hw + 2, hw + 3, hw + 4, hw + 6]) {
  const x = r.x[i] - r.tz[i] * side * lat;
  const z = r.z[i] + r.tx[i] * side * lat;
  const th = world.terrainHeight(x, z);
  const found = world.nearestRoad(x, z, 4.2, h);
  sim.ground.sample(x, z, r.y[i] + 2, g);
  console.log(
    `lat ${lat.toFixed(1)}: terrain=${th.toFixed(3)} ground=${g.y.toFixed(3)} hit=${found ? `e${h.edge} i${h.i} t${h.t.toFixed(2)} dist=${h.dist.toFixed(2)} y=${h.y.toFixed(3)} lat=${h.lateral.toFixed(2)}` : '-'}`,
  );
}
