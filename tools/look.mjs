// Render review shots of the current build: node tools/look.mjs [quality] [time] [prefix] [which]
//   which: comma list of island,festival,show,vista,forest,city,village,chase,hero,race (default: the first nine)
import { launchBrowser, openGame, shot } from './browser.mjs';
const [q = 'ultra', time = 'golden', prefix = 'look', which = 'island,festival,vista,forest,city,village,chase,hero'] = process.argv.slice(2);
const want = new Set(which.split(','));
const b = await launchBrowser();
const g = await openGame(b, { viewport: 'desktop', query: `test=1&q=${q}&time=${time}` });
// The cars' Blender models load a moment after the game.
await g.page.evaluate(() => Promise.all([...window.__game.game.views.map((v) => v.ready), window.__game.game.festivalExtras.ready]));
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
if (want.has('festival')) {
  // The festival from above the tents, looking at the stage and the wheel.
  await g.page.evaluate(() => {
    const api = window.__game;
    const f = api.game.sim.world.festival;
    api.setInput({});
    api.setUiVisible(false);
    const cx = f.stage.x + f.stage.fx * 170 + f.stage.fz * 60;
    const cz = f.stage.z + f.stage.fz * 170 - f.stage.fx * 60;
    api.setCamera(cx, f.stage.y + 34, cz, (f.stage.x + f.wheel.x) / 2, f.stage.y + 12, (f.stage.z + f.wheel.z) / 2, 50);
  });
  await shot(g.page, `${prefix}-${q}-${time}-festival`);
  await g.page.evaluate(() => window.__game.setCamera());
}
if (want.has('show')) {
  // The cars on show, from the festival road side, with the stage's pit behind.
  await g.page.evaluate(() => {
    const api = window.__game;
    const f = api.game.sim.world.festival;
    const d = f.displays;
    const mid = d[Math.floor(d.length / 2)];
    api.setInput({});
    api.setUiVisible(false);
    // Stand back from the middle plinth, towards the road, a little above head height.
    api.setCamera(mid.x + mid.fx * 11 - mid.fz * 5, mid.y + 2.4, mid.z + mid.fz * 11 + mid.fx * 5, mid.x, mid.y + 1, mid.z, 50);
  });
  await shot(g.page, `${prefix}-${q}-${time}-show`);
  await g.page.evaluate(() => {
    const api = window.__game;
    const f = api.game.sim.world.festival;
    const c = f.crowds[0];
    // The pit from behind the crowd, looking at the stage.
    api.setCamera(c.x - c.fx * (c.d / 2 + 14), c.y + 5, c.z - c.fz * (c.d / 2 + 14), f.stage.x, f.stage.y + 6, f.stage.z, 55);
  });
  await shot(g.page, `${prefix}-${q}-${time}-pit`);
  await g.page.evaluate(() => {
    const api = window.__game;
    const f = api.game.sim.world.festival;
    const t = f.trucks[2];
    api.setCamera(t.x + t.fx * 26 + t.fz * 8, t.y + 4, t.z + t.fz * 26 - t.fx * 8, t.x, t.y + 1.5, t.z, 55);
  });
  await shot(g.page, `${prefix}-${q}-${time}-food`);
  await g.page.evaluate(() => {
    const api = window.__game;
    const f = api.game.sim.world.festival;
    const p = f.parked[Math.floor(f.parked.length / 2)];
    api.setCamera(p.x - p.fz * 14 + p.fx * 9, p.y + 5, p.z + p.fx * 14 + p.fz * 9, p.x, p.y + 0.6, p.z, 50);
  });
  await shot(g.page, `${prefix}-${q}-${time}-park`);
  await g.page.evaluate(() => window.__game.setCamera());
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
if (want.has('city')) {
  // Downtown from a street corner, chase cam heading north.
  await g.page.evaluate(() => {
    const api = window.__game;
    api.setUiVisible(true);
    api.setCamera();
    api.teleport(3100 + 2.5, -260, 0);
    api.game.sim.player.vehicle.setSpeed(12);
    api.setInput({ throttle: 0.2 });
    for (let i = 0; i < 12; i++) api.step(8);
  });
  await shot(g.page, `${prefix}-${q}-${time}-city`);
}
if (want.has('village')) {
  // A village street.
  await g.page.evaluate(() => {
    const api = window.__game;
    api.setUiVisible(true);
    api.setCamera();
    const w = api.game.sim.world;
    const n = w.nodes.find((x) => x.id === 'farmVillage');
    let best = null;
    for (const e of w.edges) {
      const r = e.road;
      for (let i = 0; i < r.n; i++) {
        const d = Math.hypot(r.x[i] - n.x, r.z[i] - n.z);
        if (d > 60 && d < 90) {
          best = { x: r.x[i], z: r.z[i], tx: r.tx[i], tz: r.tz[i], lane: e.info.halfWidth * 0.45 };
          break;
        }
      }
      if (best) break;
    }
    if (best) {
      api.teleport(best.x - best.tz * best.lane, best.z + best.tx * best.lane, Math.atan2(-best.tx, -best.tz));
      api.game.sim.player.vehicle.setSpeed(10);
      api.setInput({ throttle: 0.15 });
      for (let i = 0; i < 12; i++) api.step(8);
    }
  });
  await shot(g.page, `${prefix}-${q}-${time}-village`);
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
if (want.has('race')) {
  // The Proving Ground Grand Prix: the grid during the countdown, the pack after the start (the player driving
  // along the route), and the field from the trackside; then the Harbour Dash's grid at the festival.
  const drive = (seconds) =>
    g.page.evaluate((seconds) => {
      const api = window.__game;
      const race = api.game.race;
      const me = race.playerEntrant;
      const v = me.car.vehicle;
      const at = { x: 0, y: 0, z: 0, tx: 0, tz: 1 };
      for (let k = 0; k < Math.round(seconds * 30); k++) {
        race.event.route.at(me.s + 14 + v.speed * 0.6, at);
        const dx = at.x - v.pos.x;
        const dz = at.z - v.pos.z;
        const steer = Math.max(-1, Math.min(1, Math.atan2(dx * v.right.x + dz * v.right.z, dx * v.fwd.x + dz * v.fwd.z) * 2.2));
        api.setInput({ steer, throttle: race.clock < 0 ? 0 : 0.9 });
        api.step(8);
      }
    }, seconds);
  const start = async (id) => {
    await g.page.evaluate((id) => {
      const api = window.__game;
      api.setCamera();
      api.setUiVisible(true);
      api.setInput({});
      if (api.game.race) api.game.endRace();
      api.game.openEvents();
      api.game.startRace(id, 'pro');
    }, id);
    // The rivals' Blender models load a moment after the grid forms.
    await g.page.evaluate(() => Promise.all(window.__game.game.views.map((v) => v.ready)));
  };
  await start('proving-gp');
  await drive(1.2);
  await shot(g.page, `${prefix}-${q}-${time}-race-grid`);
  await drive(4.6);
  await shot(g.page, `${prefix}-${q}-${time}-race-pack`);
  await g.page.evaluate(() => {
    // Trackside, a few metres off the edge ahead of the pack, low, looking back at it.
    const api = window.__game;
    const race = api.game.race;
    const lead = race.standings[0];
    const r = race.event.route;
    const at = { x: 0, y: 0, z: 0, tx: 0, tz: 1 };
    r.at(lead.s + 30, at);
    const side = 9;
    api.setUiVisible(false);
    const me = race.playerEntrant.car.vehicle.pos;
    api.setCamera(at.x - at.tz * side, at.y + 1.6, at.z + at.tx * side, (lead.car.vehicle.pos.x + me.x) / 2, at.y + 0.8, (lead.car.vehicle.pos.z + me.z) / 2, 38);
  });
  await shot(g.page, `${prefix}-${q}-${time}-race-trackside`);
  await start('sprint-harbor');
  await drive(1.5);
  await shot(g.page, `${prefix}-${q}-${time}-race-sprint`);
  await g.page.evaluate(() => {
    const api = window.__game;
    api.setCamera();
    api.setUiVisible(true);
    api.game.endRace();
  });
}
const perf = await g.page.evaluate(() => window.__game.perf());
console.log(`${q}/${time}: ${((Date.now() - t0) / 1000).toFixed(1)} s`, g.errors.length ? g.errors.slice(0, 3) : 'no errors', JSON.stringify(perf).slice(0, 400));
await b.close();
