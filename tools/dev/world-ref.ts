// Reference samples of the island's ground, roads and trees from the web
// version, for the C# port's equivalence test (dotnet/Halcyon.World.Tests).
//   node tools/run-ts.mjs tools/dev/world-ref.ts
import fs from 'node:fs';
import { Sim } from '../../src/sim';
import { RNG } from '../../src/engine/rng';
import { newHit } from '../../src/world/ground';
import { newHitRoad } from '../../src/world/world';

const sim = new Sim(1);
const world = sim.world;
const rng = new RNG(4242);
const g = newHit();
const rh = newHitRoad();
const samples: number[][] = [];
const add = (x: number, z: number, yRef: number): void => {
  const ok = sim.ground.sample(x, z, yRef, g);
  samples.push([x, z, yRef, ok ? 1 : 0, g.y, g.nx, g.ny, g.nz, g.surface, g.water]);
};
// Anywhere on the island, from above.
for (let k = 0; k < 4000; k++) add(rng.range(-5100, 5100), rng.range(-5100, 5100), 1e5);
// Across roads: centre, lanes, edges, shoulders, kerbs, beyond.
for (let k = 0; k < 6000; k++) {
  const e = world.edges[rng.int(0, world.edges.length)];
  const r = e.road;
  const i = rng.int(0, r.n - 1);
  const t = rng.next();
  const x = r.x[i] + (r.x[i + 1] - r.x[i]) * t;
  const z = r.z[i] + (r.z[i + 1] - r.z[i]) * t;
  const y = r.y[i] + (r.y[i + 1] - r.y[i]) * t;
  const lat = rng.range(-e.info.halfWidth - 7, e.info.halfWidth + 7);
  add(x - r.tz[i] * lat, z + r.tx[i] * lat, y + rng.range(-1, 3));
}
// Under bridges (the car is below the deck).
for (const e of world.edges) {
  const r = e.road;
  for (let i = 0; i < r.n; i += 3) if (e.bridge[i]) add(r.x[i] + 0.5, r.z[i] - 0.3, e.base[i] + 1);
}
// The proving ground.
const o = sim.track.origin;
for (let k = 0; k < 3000; k++) add(o.x + rng.range(-820, 740), o.z + rng.range(-150, 560), 1e5);

// Nearest-road queries.
const roads: number[][] = [];
for (let k = 0; k < 2000; k++) {
  const x = rng.range(-5000, 5000);
  const z = rng.range(-5000, 5000);
  const ok = world.nearestRoad(x, z, 4.2, rh);
  roads.push([x, z, ok ? 1 : 0, rh.edge, rh.i, rh.t, rh.lateral, rh.dist, rh.y, rh.halfWidth, rh.bridge ? 1 : 0]);
}
for (let k = 0; k < 2000; k++) {
  const e = world.edges[rng.int(0, world.edges.length)];
  const r = e.road;
  const i = rng.int(0, r.n - 1);
  const lat = rng.range(-12, 12);
  const x = r.x[i] - r.tz[i] * lat;
  const z = r.z[i] + r.tx[i] * lat;
  const ok = world.nearestRoad(x, z, 4.2, rh);
  roads.push([x, z, ok ? 1 : 0, rh.edge, rh.i, rh.t, rh.lateral, rh.dist, rh.y, rh.halfWidth, rh.bridge ? 1 : 0]);
}

// Tree trunks near random forest points.
const trees: number[][] = [];
for (let k = 0; k < 400; k++) {
  const x = rng.range(-4500, 4500);
  const z = rng.range(-4500, 4500);
  const found: number[] = [x, z];
  world.queryTrees(x, z, 6, (c) => {
    found.push(c.x, c.z, c.r, c.bottom, c.top, c.breakable ? 1 : 0);
  });
  trees.push(found);
}

// Static colliders near random points (counts by kind).
const cols: number[][] = [];
for (let k = 0; k < 500; k++) {
  const e = world.edges[rng.int(0, world.edges.length)];
  const i = rng.int(0, e.road.n);
  const x = e.road.x[i];
  const z = e.road.z[i];
  let segs = 0;
  let circ = 0;
  sim.colliders.query(x, z, 12, () => segs++, (c) => {
    if (c.kind !== 'tree') circ++;
  });
  cols.push([x, z, segs, circ]);
}

const out = { samples, roads, trees, cols };
fs.writeFileSync('dotnet/ref/world/world-ref.json', JSON.stringify(out));
console.log(`samples ${samples.length}, roads ${roads.length}, trees ${trees.length}, colliders ${cols.length}`);
