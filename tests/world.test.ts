// The island (M2): road network sanity, scenery that keeps off the roads,
// continuous ground at road edges, bridge rails, and a bot that follows a
// GPS route across the island on the real roads.
import { test, assert, assertRange, log, metric, QUICK } from './kit';
import { Sim } from '../src/sim';
import { World, TREE_STRIDE, TREE, newHitRoad } from '../src/world/world';
import { findRoute } from '../src/world/route';
import { neutralControls } from '../src/engine/input';
import { datan2 } from '../src/engine/dmath';
import { newHit } from '../src/world/ground';
import { newSample } from '../src/world/island';

const world = new World(1);

test('road network: every node on dry land, one connected graph, grades within class limits', () => {
  for (const n of world.nodes) {
    const s = world.island.sample(n.x, n.z, newSample());
    assert(s.coast > 0, `node ${n.id} is offshore`);
  }
  // Breadth-first search from the festival.
  const seen = new Set<number>([0]);
  const queue = [0];
  while (queue.length) {
    const u = queue.shift() as number;
    for (const eid of world.nodes[u].edges) {
      const e = world.edges[eid];
      const v = e.a === u ? e.b : e.a;
      if (!seen.has(v)) {
        seen.add(v);
        queue.push(v);
      }
    }
  }
  assert(seen.size === world.nodes.length, `${world.nodes.length - seen.size} nodes unreachable`);
  let worst = 0;
  let worstEdge = '';
  let km = 0;
  for (const e of world.edges) {
    const r = e.road;
    km += r.length / 1000;
    for (let i = 1; i < r.n; i++) {
      const g = Math.abs(r.y[i] - r.y[i - 1]) / Math.max(0.5, r.s[i] - r.s[i - 1]);
      const over = g / e.info.maxGrade;
      if (over > worst) {
        worst = over;
        worstEdge = `${world.nodes[e.a].id}->${world.nodes[e.b].id} (${e.cls}) ${(g * 100).toFixed(1)}%`;
      }
    }
  }
  log(`${world.nodes.length} nodes, ${world.edges.length} edges, ${km.toFixed(1)} km; steepest vs class limit: ${worstEdge}`);
  metric('roadKm', km);
  assert(km > 60, `only ${km.toFixed(1)} km of road`);
  assert(worst <= 1.05, `grade over the class limit: ${worstEdge}`);
});

test('trees and buildings keep off the roads', () => {
  const hit = newHitRoad();
  let trees = 0;
  let onRoad = 0;
  const step = QUICK ? 7 : 3;
  for (let tx = -70; tx < 70; tx += step) {
    for (let tz = -70; tz < 70; tz += step) {
      const t = world.tile(tx, tz).trees;
      for (let k = 0; k < t.length; k += TREE_STRIDE) {
        trees++;
        if (world.nearestRoad(t[k], t[k + 2], 1.5, hit) && t[k + 5] !== TREE.bush) onRoad++;
      }
    }
  }
  let blocked = 0;
  for (const b of world.buildings.list) {
    for (const [sa, sb] of [
      [-1, -1],
      [1, -1],
      [1, 1],
      [-1, 1],
    ]) {
      const x = b.x - b.fz * sa * (b.w / 2) + b.fx * sb * (b.d / 2);
      const z = b.z + b.fx * sa * (b.w / 2) + b.fz * sb * (b.d / 2);
      if (world.nearestRoad(x, z, 0, hit)) blocked++;
    }
  }
  log(`${trees} trees sampled, ${onRoad} within 1.5 m of a road; ${world.buildings.list.length} buildings, ${blocked} corners on a road`);
  metric('buildings', world.buildings.list.length);
  assert(trees > 1000, 'too few trees to judge');
  assert(onRoad === 0, `${onRoad} trees stand on or right beside a road`);
  assert(blocked === 0, `${blocked} building corners sit on roads`);
});

test('road edges: no lips to the ground (guardrails at drops), bridges have parapets', () => {
  const sim = new Sim(1);
  const g = sim.ground;
  const a = newHit();
  const b = newHit();
  const near = newHitRoad();
  let worst = 0;
  let where = '';
  let checked = 0;
  // Steps onto the surface of another road (stacked hairpin legs) are a
  // layout matter, tracked separately.
  let legSteps = 0;
  let worstLeg = 0;
  let walled = 0;
  for (const e of world.edges) {
    const r = e.road;
    for (let i = 8; i < r.n - 8; i += 25) {
      if (e.bridge[i] || e.info.sidewalk) continue;
      const hw = e.info.halfWidth;
      for (const side of [-1, 1]) {
        const nx = -r.tz[i] * side;
        const nz = r.tx[i] * side;
        const bx = r.x[i] + nx * (hw + 0.6);
        const bz = r.z[i] + nz * (hw + 0.6);
        g.sample(r.x[i] + nx * (hw - 0.3), r.z[i] + nz * (hw - 0.3), r.y[i] + 2, a);
        g.sample(bx, bz, r.y[i] + 2, b);
        const step = Math.abs(a.y - b.y);
        checked++;
        if (step < 0.3) continue;
        if (world.nearestRoad(bx, bz, 1, near) && (near.edge !== e.id || Math.abs(near.i - i) > 2) && near.dist <= near.halfWidth + 1) {
          legSteps++;
          worstLeg = Math.max(worstLeg, step);
          continue;
        }
        // A guardrail or wall right there makes the step a barrier, not a lip.
        let guarded = false;
        sim.colliders.query(bx, bz, 1.5, (seg) => {
          if (seg.kind === 'rail' || seg.kind === 'barrier') guarded = true;
        }, () => {});
        if (guarded) {
          walled++;
          continue;
        }
        if (step > worst) {
          worst = step;
          where = `${world.nodes[e.a].id}->${world.nodes[e.b].id} at s=${r.s[i].toFixed(0)}`;
        }
      }
    }
  }
  log(`${checked} road-edge profiles, worst step to the ground ${worst.toFixed(3)} m ${where}; ${legSteps} steps onto another road (worst ${worstLeg.toFixed(2)} m), ${walled} behind rails or walls`);
  assert(worst < 0.3, `step of ${worst.toFixed(2)} m at a road edge (${where})`);
  assert(legSteps <= 24, `${legSteps} steps between stacked road legs`);
  // Every bridge sample has a parapet segment within reach.
  let missing = 0;
  let spans = 0;
  for (const e of world.edges) {
    const r = e.road;
    for (let i = 1; i + 1 < r.n; i += 6) {
      if (!e.bridge[i] || !e.bridge[i + 1]) continue;
      spans++;
      let near = 0;
      sim.colliders.query(r.x[i], r.z[i], e.info.halfWidth + 3, (s) => {
        if (s.kind === 'barrier') near++;
      }, () => {});
      if (near < 2) missing++;
    }
  }
  log(`${spans} bridge samples, ${missing} without both parapets`);
  assert(missing === 0, `${missing} bridge samples lack parapets`);
});

test('GPS bot drives from the festival to the city on the roads', () => {
  const sim = new Sim(11);
  const v = sim.player.vehicle;
  const route = findRoute(sim.world, v.pos.x, v.pos.z, 3200, -160);
  assert(route && route.points.length > 10, 'no route');
  const pts = route.points;
  const S = new Float64Array(pts.length);
  for (let i = 1; i < pts.length; i++) S[i] = S[i - 1] + Math.sqrt((pts[i].x - pts[i - 1].x) ** 2 + (pts[i].z - pts[i - 1].z) ** 2); // det-ok: test geometry
  let idx = 0;
  let offRoad = 0;
  let maxDev = 0;
  let ticks = 0;
  const limit = (QUICK ? 520 : 700) * 240;
  const c = neutralControls();
  let arrived = false;
  for (; ticks < limit; ticks++) {
    // Progress along the route.
    let best = Infinity;
    for (let i = Math.max(0, idx - 5); i < Math.min(pts.length, idx + 60); i++) {
      const d = (pts[i].x - v.pos.x) ** 2 + (pts[i].z - v.pos.z) ** 2;
      if (d < best) {
        best = d;
        idx = i;
      }
    }
    const dev = Math.sqrt(best);
    maxDev = Math.max(maxDev, dev);
    if (S[S.length - 1] - S[idx] < 20) {
      arrived = true;
      break;
    }
    // Pure pursuit on a point ahead in the right-hand lane.
    const speed = v.speed;
    const look = Math.min(40, Math.max(10, 8 + speed * 0.9));
    let j = idx;
    while (j + 1 < pts.length && S[j] - S[idx] < look) j++;
    const p = pts[j];
    const q = pts[Math.min(pts.length - 1, j + 1)];
    const tl = Math.sqrt((q.x - p.x) ** 2 + (q.z - p.z) ** 2) || 1; // det-ok: test geometry
    const lane = p.hw * 0.45;
    const tx = p.x - ((q.z - p.z) / tl) * lane;
    const tz = p.z + ((q.x - p.x) / tl) * lane;
    const dx = tx - v.pos.x;
    const dz = tz - v.pos.z;
    const fwd = dx * v.fwd.x + dz * v.fwd.z;
    const right = dx * -v.fwd.z + dz * v.fwd.x;
    const ang = datan2(right, Math.max(0.1, fwd));
    // Target speed from the sharpest bend in the next 80 m.
    let kmax = 0;
    for (let k = idx; k + 8 < pts.length && S[k] - S[idx] < 80; k += 4) {
      const a0 = datan2(pts[k + 4].x - pts[k].x, pts[k + 4].z - pts[k].z);
      const a1 = datan2(pts[k + 8].x - pts[k + 4].x, pts[k + 8].z - pts[k + 4].z);
      let da = a1 - a0;
      while (da > Math.PI) da -= 2 * Math.PI;
      while (da < -Math.PI) da += 2 * Math.PI;
      const ds = S[k + 8] - S[k + 4] || 1;
      kmax = Math.max(kmax, Math.abs(da) / ds);
    }
    const vTarget = Math.min(27, Math.sqrt(5.5 / Math.max(kmax, 1e-4)));
    c.steer = Math.max(-1, Math.min(1, ang * 2.2));
    c.analogSteer = true;
    c.source = 'gamepad';
    const err = vTarget - speed;
    c.throttle = err > 0 ? Math.min(1, err * 0.35) : 0;
    c.brake = err < -1.5 ? Math.min(1, -err * 0.12) : 0;
    sim.step(c);
    if (!sim.onRoad()) offRoad++;
    if (!v.pos.isFinite()) throw new Error(`NaN at tick ${ticks}`);
  }
  const secs = ticks / 240;
  log(`route ${(route.length / 1000).toFixed(2)} km in ${secs.toFixed(0)} s (${((route.length / secs) * 3.6).toFixed(0)} km/h avg), off-road ${((offRoad / ticks) * 100).toFixed(1)}% of ticks, max deviation ${maxDev.toFixed(1)} m`);
  metric('gpsBotSeconds', secs);
  assert(arrived, `did not arrive (stopped ${((S[S.length - 1] - S[idx]) / 1000).toFixed(2)} km short)`);
  assertRange(offRoad / ticks, 0, 0.05, 'fraction of time off the road');
  assert(maxDev < 20, `strayed ${maxDev.toFixed(1)} m from the route`);
});
