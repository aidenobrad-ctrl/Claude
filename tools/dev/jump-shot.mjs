// A review shot of a yump (a danger sign's crest): the car taken over it at 150 km/h, caught in the air.
//   node tools/dev/jump-shot.mjs [quality]
import { launchBrowser, openGame, shot } from '../browser.mjs';
const [q = 'high'] = process.argv.slice(2);
const b = await launchBrowser();
const g = await openGame(b, { viewport: 'desktop', query: `test=1&q=${q}&time=golden` });
const info = await g.page.evaluate(() => {
  const api = window.__game;
  const w = api.game.sim.world;
  const y = w.yumps.find((q) => w.nodes[w.edges[q.edge].a].id === 'farmVillage') ?? w.yumps[0];
  const e = w.edges[y.edge];
  const r = e.road;
  let i = 0;
  while (i < r.n - 1 && r.s[i + 1] <= y.s - 160) i++;
  api.teleport(r.x[i], r.z[i], Math.atan2(-r.tx[i], -r.tz[i]));
  const v = api.game.sim.player.vehicle;
  v.setSpeed(150 / 3.6);
  // Pure pursuit along the road at 150 km/h until all four wheels leave it, then a little more.
  let j = i;
  let t = 0;
  let minGrounded = 4;
  let speedAtCrest = 0;
  for (; t < 240 * 8; t++) {
    let best = Infinity;
    for (let q = Math.max(0, j - 4); q < Math.min(r.n, j + 12); q++) {
      const d = (r.x[q] - v.pos.x) ** 2 + (r.z[q] - v.pos.z) ** 2;
      if (d < best) {
        best = d;
        j = q;
      }
    }
    const k = Math.min(r.n - 1, j + 6);
    const dx = r.x[k] - v.pos.x;
    const dz = r.z[k] - v.pos.z;
    const side = dx * v.right.x + dz * v.right.z;
    const along = dx * v.fwd.x + dz * v.fwd.z;
    const err = 150 / 3.6 - v.vLong;
    api.setInput({ steer: Math.max(-1, Math.min(1, Math.atan2(side, along) * 1.6)), throttle: Math.max(0, Math.min(1, err * 0.5)), brake: Math.max(0, Math.min(1, -err * 0.2)) });
    api.step(1);
    if (Math.abs(r.s[j] - y.s) < 60) {
      minGrounded = Math.min(minGrounded, v.groundedCount);
      speedAtCrest = Math.max(speedAtCrest, v.speed * 3.6);
    }
    if (v.groundedCount === 0 && Math.abs(r.s[j] - y.s) < 40) break;
  }
  api.setInput({ throttle: 0.3 });
  api.step(110);
  // A camera beside the road, level with the car in the air.
  api.setUiVisible(false);
  const c = v.pos;
  api.setCamera(c.x + v.right.x * 13 - v.fwd.x * 4, c.y - 0.6, c.z + v.right.z * 13 - v.fwd.z * 4, c.x, c.y - 0.3, c.z, 48);
  return { airborne: v.groundedCount === 0, above: v.pos.y - api.game.sim.groundHeight(v.pos.x, v.pos.z), minGrounded, speedAtCrest, crestY: r.y[r.s.findIndex((q) => q >= y.s)], edge: `${w.nodes[e.a].id}->${w.nodes[e.b].id}`, s: y.s, airborneAfter: t / 240, height: v.pos.y - r.y[i], speed: v.speed * 3.6 };
});
console.log(JSON.stringify(info));
await shot(g.page, 'm7-yump-air');
await b.close();
