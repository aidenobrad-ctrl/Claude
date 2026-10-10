// Render review shots of the current build: node tools/look.mjs [quality] [time] [prefix] [which]
//   which: comma list of island,vista,forest,chase,hero (default all)
import { launchBrowser, openGame, shot } from './browser.mjs';
const [q = 'ultra', time = 'golden', prefix = 'look', which = 'island,vista,forest,chase,hero'] = process.argv.slice(2);
const want = new Set(which.split(','));
const b = await launchBrowser();
const g = await openGame(b, { viewport: 'desktop', query: `test=1&q=${q}&time=${time}` });
const t0 = Date.now();
if (want.has('island')) {
  // 1. Chase cam on the island road leaving the festival.
  await g.page.evaluate(() => {
    const api = window.__game;
    const sp = api.game.sim.islandSpawn();
    api.teleport(sp.x, sp.z, sp.yaw);
    api.game.sim.player.vehicle.setSpeed(22);
    api.setInput({ throttle: 0.35 });
    for (let i = 0; i < 12; i++) api.step(8);
  });
  await shot(g.page, `${prefix}-${q}-${time}-island`);
}
if (want.has('vista')) {
  // 2. A wide view north across the lake to the mountains.
  await g.page.evaluate(() => {
    const api = window.__game;
    api.setInput({});
    api.teleport(150, 900, 0);
    api.step(60);
    api.setUiVisible(false);
    const y = api.game.sim.groundHeight(150, 900);
    api.setCamera(150, y + 60, 900, 300, 120, -2600, 55);
  });
  await shot(g.page, `${prefix}-${q}-${time}-vista`);
}
if (want.has('forest')) {
  // A road through the eastern forest, chase cam.
  await g.page.evaluate(() => {
    const api = window.__game;
    api.setUiVisible(true);
    api.setCamera();
    const w = api.game.sim.world;
    const s = { h: 0, region: 0, coast: 0, water: -Infinity, forest: 0, desert: 0 };
    let best = null;
    for (const e of w.edges) {
      const r = e.road;
      for (let i = 20; i < r.n - 20; i += 10) {
        w.island.sample(r.x[i], r.z[i], s);
        if (s.forest > 0.85 && !e.bridge[i] && e.info.halfWidth > 4) {
          best = { x: r.x[i], z: r.z[i], tx: r.tx[i], tz: r.tz[i], lane: e.info.halfWidth * 0.45 };
          break;
        }
      }
      if (best) break;
    }
    if (best) {
      api.teleport(best.x - best.tz * best.lane, best.z + best.tx * best.lane, Math.atan2(-best.tx, -best.tz));
      api.game.sim.player.vehicle.setSpeed(20);
      api.setInput({ throttle: 0.3 });
      for (let i = 0; i < 12; i++) api.step(8);
    }
  });
  await shot(g.page, `${prefix}-${q}-${time}-forest`);
}
if (want.has('chase')) {
  // 3. Chase cam on the circuit at speed.
  await g.page.evaluate(() => {
    const api = window.__game;
    api.setUiVisible(true);
    api.setCamera();
    const road = api.game.sim.track.road;
    const at = { x: 0, y: 0, z: 0, tx: 0, tz: 0, i: 0 };
    road.at(api.game.sim.track.startS + 900, at);
    api.teleport(at.x, at.z, Math.atan2(-at.tx, -at.tz));
    api.game.sim.player.vehicle.setSpeed(38);
    api.setInput({ throttle: 0.6 });
    for (let i = 0; i < 12; i++) api.step(8);
  });
  await shot(g.page, `${prefix}-${q}-${time}-chase`);
}
if (want.has('hero')) {
  // 4. Low hero angle of the car, parked by the skidpad.
  await g.page.evaluate(() => {
    const api = window.__game;
    api.setInput({});
    const sk = api.game.sim.track.skidpad;
    api.teleport(sk.x + 40, sk.z, 0.6);
    api.step(240);
    api.setUiVisible(false);
    const v = api.game.sim.player.vehicle;
    api.setCamera(v.pos.x + 5.2, v.pos.y + 0.4, v.pos.z - 3.2, v.pos.x, v.pos.y + 0.1, v.pos.z, 36);
  });
  await shot(g.page, `${prefix}-${q}-${time}-hero`);
}
const perf = await g.page.evaluate(() => window.__game.perf());
console.log(`${q}/${time}: ${((Date.now() - t0) / 1000).toFixed(1)} s`, g.errors.length ? g.errors.slice(0, 3) : 'no errors', JSON.stringify(perf).slice(0, 400));
await b.close();
