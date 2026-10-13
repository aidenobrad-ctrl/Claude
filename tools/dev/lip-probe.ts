// Lists the worst road-edge steps with their rail status.
import { Sim } from '../../src/sim';
import { newHit } from '../../src/world/ground';
import { railOffset } from '../../src/world/guardrails';

const sim = new Sim(1);
const world = sim.world;
const g = sim.ground;
const a = newHit();
const b = newHit();
const rows: string[] = [];
for (const e of world.edges) {
  const r = e.road;
  for (let i = 8; i < r.n - 8; i += 25) {
    if (e.bridge[i] || e.info.sidewalk) continue;
    const hw = e.info.halfWidth;
    for (const side of [-1, 1]) {
      const nx = -r.tz[i] * side;
      const nz = r.tx[i] * side;
      g.sample(r.x[i] + nx * (hw - 0.3), r.z[i] + nz * (hw - 0.3), r.y[i] + 2, a);
      g.sample(r.x[i] + nx * (hw + 0.6), r.z[i] + nz * (hw + 0.6), r.y[i] + 2, b);
      const step = a.y - b.y;
      if (Math.abs(step) < 0.3) continue;
      const off = railOffset(world, e.id);
      const near = world.terrainHeight(r.x[i] + nx * (off + 0.8), r.z[i] + nz * (off + 0.8));
      const far = world.terrainHeight(r.x[i] + nx * (off + 5), r.z[i] + nz * (off + 5));
      const t06 = world.terrainHeight(r.x[i] + nx * (hw + 0.6), r.z[i] + nz * (hw + 0.6));
      const mask = world.railOffsets(e.id)[i * 2 + (side > 0 ? 1 : 0)].toFixed(2);
      rows.push(
        `${world.nodes[e.a].id}->${world.nodes[e.b].id} i=${i} s=${r.s[i].toFixed(0)} side=${side} step=${step.toFixed(2)} roadY=${r.y[i].toFixed(1)} a=${a.y.toFixed(2)} b=${b.y.toFixed(2)} t(hw+.6)=${t06.toFixed(2)} near=${near.toFixed(1)} far=${far.toFixed(1)} mask=${mask}`,
      );
    }
  }
}
console.log(rows.length + ' steps >= 0.3');
for (const r of rows) console.log(r);
