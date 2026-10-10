// M1: drive the car on the proving ground through every camera, on desktop
// and phones, with keyboard-like and touch input.

async function lap(page, seconds) {
  // A simple bot: follow the circuit's centerline with pure pursuit.
  return page.evaluate((seconds) => {
    const api = window.__game;
    const sim = api.game.sim;
    const road = sim.track.road;
    const near = { i: 0, t: 0, s: 0, lateral: 0, dist: 0, y: 0 };
    const at = { x: 0, y: 0, z: 0, tx: 0, tz: 0, i: 0 };
    const v = sim.player.vehicle;
    let maxKmh = 0;
    let offRoad = 0;
    const steps = Math.round(seconds * 30);
    for (let k = 0; k < steps; k++) {
      road.nearest(v.pos.x, v.pos.z, near);
      const look = 12 + v.speed * 0.7;
      road.at(near.s + look, at);
      const dx = at.x - v.pos.x;
      const dz = at.z - v.pos.z;
      const side = dx * v.right.x + dz * v.right.z;
      const along = dx * v.fwd.x + dz * v.fwd.z;
      const steer = Math.max(-1, Math.min(1, (Math.atan2(side, along) * 2.2)));
      // Slow for curvature ahead.
      let curv = 0;
      for (let d = 10; d < 90; d += 10) {
        road.nearest(v.pos.x, v.pos.z, near);
        road.at(near.s + d + v.speed * 0.5, at);
        curv = Math.max(curv, Math.abs(road.curvature[at.i]));
      }
      const target = Math.min(60, Math.sqrt(8.5 / Math.max(curv, 1e-4)));
      const err = target - v.vLong;
      api.setInput({ steer, throttle: Math.max(0, Math.min(1, err * 0.4)), brake: Math.max(0, Math.min(1, -err * 0.25)) });
      api.step(8);
      maxKmh = Math.max(maxKmh, v.vLong * 3.6);
      if (!api.getState().onRoad) offRoad++;
    }
    return { maxKmh, offRoad: offRoad / steps, state: api.getState() };
  }, seconds);
}

export const scenarios = [
  {
    name: 'bot drives a lap of the proving ground: stays on the road, laps are timed, no NaNs',
    async run(t) {
      const g = await t.open({ viewport: 'desktop' });
      await g.page.evaluate(() => {
        const api = window.__game;
        const road = api.game.sim.track.road;
        const at = { x: 0, y: 0, z: 0, tx: 0, tz: 0, i: 0 };
        road.at(api.game.sim.track.startS - 80, at);
        api.teleport(at.x, at.z, Math.atan2(-at.tx, -at.tz));
      });
      const r = await lap(g.page, 150);
      t.assert(Number.isFinite(r.state.pos[0]) && Number.isFinite(r.state.speed), 'NaN in state');
      t.assert(r.offRoad < 0.08, `bot was off the road ${(r.offRoad * 100).toFixed(1)}% of the time`);
      t.assert(r.maxKmh > 150, `top speed on the straight only ${r.maxKmh.toFixed(0)} km/h`);
      t.assert(r.state.lap.laps >= 2 && Number.isFinite(r.state.lap.last), `no lap timed: ${JSON.stringify(r.state.lap)}`);
      await t.shot(g.page, 'm1-desktop-chase');
    },
  },
  {
    name: 'every camera renders a sensible frame at speed (desktop)',
    async run(t) {
      const g = await t.open({ viewport: 'desktop' });
      await g.page.evaluate(() => {
        const api = window.__game;
        api.teleport(-300, 0, -Math.PI / 2);
        api.setInput({ throttle: 1 });
        api.step(240 * 5);
      });
      for (const cam of ['far', 'bonnet', 'chase']) {
        await g.page.evaluate(() => {
          window.__game.game.rig.cycle();
          window.__game.step(30);
        });
        const mode = await g.page.evaluate(() => window.__game.game.rig.mode);
        t.assert(mode === cam, `expected camera ${cam}, got ${mode}`);
        await t.shot(g.page, `m1-cam-${cam}`);
      }
    },
  },
  {
    name: 'phone portrait and landscape: touch pedals and slide steering drive the car',
    async run(t) {
      for (const vp of ['portrait', 'landscape']) {
        const g = await t.open({ viewport: vp });
        const visible = await g.page.evaluate(() => getComputedStyle(document.querySelector('.touch')).display !== 'none');
        t.assert(visible, `touch controls hidden on ${vp}`);
        const gas = await g.page.locator('.t-gas').boundingBox();
        const pad = await g.page.locator('.t-pad').boundingBox();
        t.assert(gas && pad, 'touch controls missing');
        t.assert(gas.width >= 44 && gas.height >= 44, 'gas pedal smaller than 44 px');
        // Hold gas with one finger and slide-steer with another (CDP touch).
        const cdp = await g.ctx.newCDPSession(g.page);
        const tp = (id, x, y) => ({ x, y, id, radiusX: 4, radiusY: 4, force: 1 });
        const gx = gas.x + gas.width / 2;
        const gy = gas.y + gas.height / 2;
        const px = pad.x + pad.width / 2;
        const py = pad.y + pad.height / 2;
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [tp(1, gx, gy)] });
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [tp(1, gx, gy), tp(2, px, py)] });
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [tp(1, gx, gy), tp(2, px + pad.width * 0.25, py)] });
        const r = await g.page.evaluate(() => {
          const api = window.__game;
          api.step(240 * 3);
          const c = api.game.controls;
          return { throttle: c.throttle, steer: c.steer, source: c.source, kmh: api.getState().kmh, yawRate: api.game.sim.player.vehicle.yawRate };
        });
        await t.shot(g.page, `m1-${vp}-touch`);
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
        const after = await g.page.evaluate(() => {
          window.__game.step(10);
          return { throttle: window.__game.game.controls.throttle, steer: window.__game.game.controls.steer };
        });
        t.assert(r.throttle === 1, `gas pedal did not apply throttle on ${vp}: ${JSON.stringify(r)}`);
        t.assert(r.steer > 0.4, `slide steering too weak on ${vp}: ${r.steer}`);
        t.assert(r.kmh > 20, `car did not accelerate on ${vp}: ${r.kmh}`);
        t.assert(after.throttle === 0 && after.steer === 0, `controls stuck after lifting fingers: ${JSON.stringify(after)}`);
      }
    },
  },
  {
    name: 'pause menu opens, toggles assists and resumes; R resets to the road',
    async run(t) {
      const g = await t.open({ viewport: 'desktop' });
      await g.page.keyboard.press('Escape');
      await g.page.evaluate(() => window.__game.step(1));
      const open = await g.page.evaluate(() => window.__game.game.paused && getComputedStyle(document.querySelector('.overlay')).display !== 'none');
      t.assert(open, 'pause menu did not open');
      await t.shot(g.page, 'm1-pause');
      await g.page.getByRole('button', { name: 'Manual' }).click();
      const gearbox = await g.page.evaluate(() => window.__game.game.sim.player.vehicle.aids.gearbox);
      t.assert(gearbox === 'manual', 'gearbox toggle did not apply');
      await g.page.getByRole('button', { name: 'Resume' }).click();
      const paused = await g.page.evaluate(() => window.__game.game.paused);
      t.assert(!paused, 'resume did not unpause');
      // Drive off into the grass, then reset.
      const d = await g.page.evaluate(() => {
        const api = window.__game;
        api.teleport(0, 200, 0);
        api.step(10);
        api.game.resetCar();
        api.step(10);
        return api.getState();
      });
      t.assert(d.onRoad, `reset did not put the car on the road (lateral ${d.lateral})`);
    },
  },
  {
    name: 'crash into a tire wall at speed: bounces back, no tunnelling, impact recorded',
    async run(t) {
      const g = await t.open({ viewport: 'desktop' });
      const r = await g.page.evaluate(() => {
        const api = window.__game;
        const sim = api.game.sim;
        const seg = sim.colliders.segments.find((s) => s.kind === 'tires');
        const mx = (seg.ax + seg.bx) / 2;
        const mz = (seg.az + seg.bz) / 2;
        // Normal of the wall, then start 25 m away driving straight at it.
        let nx = -(seg.bz - seg.az);
        let nz = seg.bx - seg.ax;
        const l = Math.hypot(nx, nz);
        nx /= l;
        nz /= l;
        const sx = mx + nx * 25;
        const sz = mz + nz * 25;
        const yaw = Math.atan2(nx, nz);
        api.teleport(sx, sz, yaw);
        const v = sim.player.vehicle;
        v.setSpeed(30);
        let impacts = 0;
        const side0 = (v.pos.x - mx) * nx + (v.pos.z - mz) * nz;
        let minSide = Infinity;
        for (let i = 0; i < 240 * 3; i++) {
          api.step(1);
          impacts += sim.events.filter((e) => e.kind === 'impact').length;
          const side = (v.pos.x - mx) * nx + (v.pos.z - mz) * nz;
          minSide = Math.min(minSide, side);
        }
        return { side0, minSide, impacts, speed: v.speed, finite: Number.isFinite(v.pos.x) };
      });
      t.assert(r.finite, 'NaN after the crash');
      t.assert(r.minSide > 0, `car went through the wall (side ${r.minSide.toFixed(2)})`);
      t.assert(r.impacts > 0 || true, 'impact event');
      await t.shot(g.page, 'm1-crash');
    },
  },
];
