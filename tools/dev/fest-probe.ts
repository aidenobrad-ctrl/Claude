import { World } from '../../src/world/world';
const w = new World(1);
const f = w.festival;
console.log(`displays ${f.displays.length} parked ${f.parked.length} trucks ${f.trucks.length} towers ${f.towers.length} barriers ${f.barriers.length} crowds ${f.crowds.length}`);
const hit = { edge: 0, i: 0, t: 0, lateral: 0, dist: 0, y: 0, halfWidth: 0, bridge: false };
for (const d of [...f.displays, ...f.parked]) {
  w.nearestRoad(d.x, d.z, 400, hit);
  console.log(`${d.specId.padEnd(24)} at ${d.x.toFixed(0)},${d.z.toFixed(0)} y ${d.y.toFixed(1)} road ${hit.dist.toFixed(1)} m (hw ${hit.halfWidth.toFixed(1)})`);
}
for (const b of f.barriers) console.log('barrier', b.map((p) => `${p.x.toFixed(0)},${p.z.toFixed(0)},${p.y.toFixed(1)}`).join(' '));
console.log('stage', f.stage.x.toFixed(0), f.stage.z.toFixed(0), f.stage.y.toFixed(1), 'spawn', f.spawn.x.toFixed(0), f.spawn.z.toFixed(0));
// Every car park spot: is it clear of roads?
{
  const hub = w.nodes.find((n) => n.id === 'hub') ?? w.nodes[0];
  const h2 = { ...hit };
  w.nearestRoad(hub.x + 1, hub.z + 1, 50, h2);
  const r = w.edges[h2.edge].road;
  let tx = r.tx[h2.i], tz = r.tz[h2.i];
  if (tx < 0) { tx = -tx; tz = -tz; }
  const nx = tz, nz = -tx;
  for (let row = 0; row < 2; row++) for (let k = -4; k <= 4; k++) {
    const along = k * 3.4 + (row ? 1.2 : 0), side = -162 - row * 5.6;
    const x = hub.x + tx * along + nx * side, z = hub.z + tz * along + nz * side;
    const h3 = { ...hit };
    const near = w.nearestRoad(x, z, 10, h3);
    console.log(`row ${row} k ${k}: ${x.toFixed(0)},${z.toFixed(0)} road within 10 m: ${near} ${near ? h3.dist.toFixed(1) + ' edge ' + h3.edge : ''}`);
  }
}
