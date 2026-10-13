// Review shots of the lattice-routed roads: the switchbacks on the forest climb to the city
// and the canyon road, from above and from the driver's seat.
//   node tools/dev/switchback-shot.mjs [quality]
import { launchBrowser, openGame, shot } from '../browser.mjs';
const [q = 'high'] = process.argv.slice(2);
const b = await launchBrowser();
const g = await openGame(b, { viewport: 'desktop', query: `test=1&q=${q}&time=golden` });
const spots = [
  { name: 'forest-hairpins', edge: ['forestEast', 'cityNorth'], x: 2290, z: -1250, up: 95, back: 120 },
  { name: 'canyon-road', edge: ['canyonNorth', 'x0'], x: -3400, z: -600, up: 70, back: 110 },
];
for (const s of spots) {
  const at = await g.page.evaluate((s) => {
    const api = window.__game;
    const w = api.game.sim.world;
    const e = w.edges.find((e) => w.nodes[e.a].id === s.edge[0] && w.nodes[e.b].id === s.edge[1]) ?? w.edges.find((e) => w.nodes[e.a].id === s.edge[0]);
    const r = e.road;
    let best = 0;
    let bd = Infinity;
    for (let i = 0; i < r.n; i++) {
      const d = Math.hypot(r.x[i] - s.x, r.z[i] - s.z);
      if (d < bd) {
        bd = d;
        best = i;
      }
    }
    const i = best;
    // The car on the road there, heading along it, the camera above and behind.
    api.teleport(r.x[i], r.z[i], Math.atan2(-r.tx[i], -r.tz[i]));
    api.setInput({});
    api.step(60);
    api.setUiVisible(false);
    api.setCamera(r.x[i] - r.tx[i] * s.back + r.tz[i] * 40, r.y[i] + s.up, r.z[i] - r.tz[i] * s.back - r.tx[i] * 40, r.x[i] + r.tx[i] * 40, r.y[i], r.z[i] + r.tz[i] * 40, 55);
    return { x: r.x[i], z: r.z[i], y: r.y[i], edge: `${w.nodes[e.a].id}->${w.nodes[e.b].id}` };
  }, s);
  console.log(s.name, JSON.stringify(at));
  await shot(g.page, `m7-${s.name}-above`);
  await g.page.evaluate(() => {
    const api = window.__game;
    api.setCamera();
    api.setUiVisible(true);
    api.game.sim.player.vehicle.setSpeed(14);
    api.setInput({ throttle: 0.3 });
    api.step(30);
  });
  await shot(g.page, `m7-${s.name}-chase`);
}
await b.close();
