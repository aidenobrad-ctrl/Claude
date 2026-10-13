// The festival's races on the web: the menu, the grid and countdown, AI
// rivals racing, the race HUD, leaving a race, and a drag race run to its
// results (the AI itself is tested against the C# in tests/ai.test.ts).

/** Drive the player along the race route (pure pursuit on the route, throttle flat out) for `seconds`. */
async function drive(page, seconds) {
  return page.evaluate((seconds) => {
    const api = window.__game;
    const race = api.game.race;
    const route = race.event.route;
    const me = race.playerEntrant;
    const v = me.car.vehicle;
    const at = { x: 0, y: 0, z: 0, tx: 0, tz: 1 };
    const steps = Math.round(seconds * 30);
    for (let k = 0; k < steps; k++) {
      route.at(me.s + 14 + v.speed * 0.6, at);
      const dx = at.x - v.pos.x;
      const dz = at.z - v.pos.z;
      const side = dx * v.right.x + dz * v.right.z;
      const along = dx * v.fwd.x + dz * v.fwd.z;
      const finished = race.state === 'finished';
      api.setInput({ steer: Math.max(-1, Math.min(1, Math.atan2(side, along) * 2.2)), throttle: finished ? 0 : 1, brake: finished ? 0.6 : 0 });
      api.step(8);
      if (race.state === 'finished' && race.sinceFinish > 2) break;
    }
    return { state: race.state, position: me.position, finishTime: me.finishTime, speed: v.speed };
  }, seconds);
}

const visible = (sel) => {
  const el = document.querySelector(sel);
  return !!el && getComputedStyle(el).display !== 'none';
};

export const scenarios = [
  {
    name: 'Races: the menu lists the festival races; a Grand Prix grid counts down and the rivals race',
    async run(t) {
      const g = await t.open({ viewport: 'desktop', query: 'test=1&q=low' });
      await g.page.keyboard.press('KeyF');
      await g.page.evaluate(() => window.__game.step(1));
      const menu = await g.page.evaluate((v) => {
        const isVisible = new Function(`return (${v})`)();
        return { open: window.__game.game.eventsOpen === true && isVisible('.overlay.events'), cards: [...document.querySelectorAll('.event-card b')].map((b) => b.textContent) };
      }, visible.toString());
      t.assert(menu.open, 'F did not open the festival races menu');
      t.assert(menu.cards.length >= 8, `only ${menu.cards.length} races: ${menu.cards.join(', ')}`);
      for (const name of ['Proving Ground Grand Prix', 'Highway Quarter Mile', 'Redmesa Rally', 'Showdown: Mika Varga']) t.assert(menu.cards.includes(name), `no "${name}" in the menu`);
      // The world waits while the menu is open.
      const tick0 = await g.page.evaluate(() => window.__game.game.sim.tick);
      await g.page.waitForTimeout(300);
      t.assert((await g.page.evaluate(() => window.__game.game.sim.tick)) === tick0, 'the world ran on behind the menu');
      await t.shot(g.page, 'm9-races-menu');

      await g.page.getByRole('button', { name: /Proving Ground Grand Prix/ }).click();
      const grid = await g.page.evaluate(() => {
        const api = window.__game;
        const game = api.game;
        api.step(1);
        const race = game.race;
        if (!race) return null;
        return {
          cars: game.sim.cars.length,
          views: game.views.length,
          entrants: race.entrants.length,
          grid: race.event.gridSize,
          playerFirst: game.sim.cars[0] === game.sim.player,
          paints: new Set(game.sim.cars.slice(1).map((c) => c.paint)).size,
          slot: race.playerEntrant.grid,
          pos: document.querySelector('.race-pos').textContent,
          big: document.querySelector('.race-big').textContent,
          hud: getComputedStyle(document.querySelector('.race-hud')).display !== 'none',
          menuGone: getComputedStyle(document.querySelector('.overlay.events')).display === 'none',
          start: game.sim.cars.map((c) => [c.vehicle.pos.x, c.vehicle.pos.z]),
        };
      });
      t.assert(grid, 'the race did not start');
      t.assert(grid.cars === grid.grid && grid.entrants === grid.grid && grid.views === grid.grid, `grid of ${grid.grid}: ${grid.cars} cars, ${grid.entrants} entrants, ${grid.views} views`);
      t.assert(grid.playerFirst, 'the player is no longer the first car (the views assume it)');
      t.assert(grid.paints === grid.grid - 1, `${grid.grid - 1} rivals share ${grid.paints} paints`);
      t.assert(grid.slot === grid.grid / 2 && grid.pos === `${grid.grid / 2 + 1}/${grid.grid}`, `the player starts in slot ${grid.slot} (HUD "${grid.pos}"), not mid-grid`);
      t.assert(grid.hud && grid.menuGone, 'the race HUD is not up, or the menu is still open');
      t.assert(grid.big === '3', `the countdown shows "${grid.big}"`);
      await t.shot(g.page, 'm9-races-grid');

      // The countdown: nobody moves until GO.
      const held = await g.page.evaluate(() => {
        const api = window.__game;
        api.setInput({ throttle: 1 });
        api.stepSeconds(2.5);
        const game = api.game;
        return { moved: Math.max(...game.sim.cars.map((c) => c.vehicle.speed)), clock: game.race.clock };
      });
      t.assert(held.moved < 0.5, `a car moved during the countdown (${held.moved.toFixed(2)} m/s)`);

      const go = await g.page.evaluate(() => {
        const api = window.__game;
        api.setInput({});
        api.stepSeconds(0.6);
        return document.querySelector('.race-big').textContent;
      });
      t.assert(go === 'GO', `the countdown ended on "${go}"`);
      await g.page.evaluate(() => window.__game.stepSeconds(2.5));
      await t.shot(g.page, 'm9-races-start');
      const run = await g.page.evaluate(() => {
        const api = window.__game;
        api.stepSeconds(9);
        const game = api.game;
        const race = game.race;
        const ai = race.entrants.filter((e) => e.ai);
        return {
          finite: game.sim.cars.every((c) => Number.isFinite(c.vehicle.pos.x) && Number.isFinite(c.vehicle.pos.z)),
          minProgress: Math.min(...ai.map((e) => e.progress)),
          resets: ai.reduce((n, e) => n + e.ai.resets, 0),
          me: race.playerEntrant.position,
          pos: document.querySelector('.race-pos').textContent,
          lap: document.querySelector('.race-info span').textContent,
          gaps: document.querySelector('.race-gaps')?.textContent ?? '',
        };
      });
      t.assert(run.finite, 'NaN in a car position');
      t.assert(run.minProgress > 150, `the slowest rival made ${run.minProgress.toFixed(0)} m in 12 s`);
      t.assert(run.resets === 0, `${run.resets} AI resets on the opening lap`);
      t.assert(run.me === grid.grid && run.pos === `${grid.grid}/${grid.grid}`, `standing still, the player is ${run.me} (HUD "${run.pos}")`);
      t.assert(/^Lap 1\/\d+$/.test(run.lap), `the HUD lap reads "${run.lap}"`);
      t.assert(/^▲ \S+ .+ \d+\.\d$/.test(run.gaps), `the gap to the car ahead reads "${run.gaps}"`);

      // Leave from the pause menu: the rivals leave the island.
      await g.page.evaluate(() => window.__game.clearInput());
      await g.page.keyboard.press('Escape');
      await g.page.evaluate(() => window.__game.step(1));
      await g.page.getByRole('button', { name: 'Leave race' }).click();
      const after = await g.page.evaluate(() => {
        const game = window.__game.game;
        return { race: game.race !== null, cars: game.sim.cars.length, views: game.views.length, hud: getComputedStyle(document.querySelector('.race-hud')).display, paused: game.paused };
      });
      t.assert(!after.race && after.cars === 1 && after.views === 1, `after leaving: race ${after.race}, ${after.cars} cars, ${after.views} views`);
      t.assert(after.hud === 'none' && !after.paused, 'the race HUD stayed up, or the game stayed paused');
      // The next race starts clean: no gaps carried over from the last one during its countdown.
      const next = await g.page.evaluate(() => {
        const game = window.__game.game;
        game.openEvents();
        game.startRace('sprint-harbor', 'pro');
        window.__game.step(1);
        return { gaps: document.querySelector('.race-gaps')?.textContent ?? '', info: document.querySelector('.race-info')?.textContent ?? '' };
      });
      t.assert(next.gaps === '' && /km to go/.test(next.info), `the next race's HUD during its countdown: "${next.info}"`);
    },
  },
  {
    name: 'Races: a drag race run to the finish shows the results; Enter goes back to free roam',
    async run(t) {
      const g = await t.open({ viewport: 'desktop', query: 'test=1&q=low' });
      const started = await g.page.evaluate(() => {
        const game = window.__game.game;
        game.openEvents();
        const card = [...document.querySelectorAll('.event-card')].find((c) => c.textContent.includes('Highway Quarter Mile'));
        card.click();
        return { race: !!game.race, route: !!game.route, cars: game.sim.cars.length };
      });
      t.assert(started.race && started.cars === 2, `drag race: ${JSON.stringify(started)}`);
      t.assert(started.route, 'the drag strip is not on the GPS');
      await g.page.evaluate(() => window.__game.stepSeconds(3.05));
      const r = await drive(g.page, 45);
      t.assert(r.state === 'finished', `the player did not finish the quarter mile (state ${r.state})`);
      t.range(r.finishTime, 9, 20, 'quarter mile time (s)');
      const res = await g.page.evaluate(() => {
        const game = window.__game.game;
        const rows = [...document.querySelectorAll('.res-row')].map((x) => x.textContent);
        return { open: game.raceHud.resultsOpen, rows, head: document.querySelector('.race-results h2')?.textContent ?? '' };
      });
      t.assert(res.open, 'no results after the finish');
      t.assert(res.rows.length === 2 && res.rows.some((x) => x.includes('You')) && res.rows.every((x) => /km\/h|racing/.test(x)), `results rows: ${res.rows.join(' | ')}`);
      t.assert(/You won!|You finished 2nd/.test(res.head), `results heading "${res.head}"`);
      await t.shot(g.page, 'm9-races-results');
      await g.page.evaluate(() => window.__game.clearInput());
      await g.page.keyboard.press('Enter');
      await g.page.evaluate(() => window.__game.step(1));
      const after = await g.page.evaluate(() => ({ race: window.__game.game.race !== null, cars: window.__game.game.sim.cars.length, route: window.__game.game.route !== null }));
      t.assert(!after.race && after.cars === 1 && !after.route, `after the results: ${JSON.stringify(after)}`);
    },
  },
  {
    name: 'Races: phone landscape has a RACE button that opens the menu',
    async run(t) {
      const g = await t.open({ viewport: 'landscape', query: 'test=1&q=low' });
      const btn = g.page.locator('button[data-a="events"]');
      t.assert((await btn.count()) === 1, 'no RACE button on the touch controls');
      await btn.dispatchEvent('pointerdown');
      await btn.dispatchEvent('pointerup');
      await g.page.evaluate(() => window.__game.step(1));
      const open = await g.page.evaluate(() => window.__game.game.eventsOpen);
      t.assert(open, 'the RACE button did not open the menu');
      await t.shot(g.page, 'm9-races-menu-phone');
      await g.page.getByRole('button', { name: 'Back to the road' }).click();
      t.assert(!(await g.page.evaluate(() => window.__game.game.eventsOpen)), 'Back did not close the menu');
    },
  },
  {
    name: "Races: the rivals' cars are measured in the worker, to the same numbers as on the page",
    async run(t) {
      const g = await t.open({ viewport: { width: 480, height: 270, isMobile: false, hasTouch: false }, query: 'test=1&q=low' });
      const r = await g.page.evaluate(async () => {
        const game = window.__game.game;
        const t0 = performance.now();
        const ahead = game.measureAhead();
        // The page keeps going while the worker measures.
        const during = performance.now() - t0;
        await ahead;
        const ms = performance.now() - t0;
        const cache = game.abilities;
        const fresh = new cache.constructor();
        const ids = [...cache.map.keys()].map((k) => k.split('|')[0]);
        let worst = 0;
        for (const id of ids) {
          const a = cache.get(id);
          const b = fresh.get(id);
          for (const k of ['mu', 'brakeG', 'aeroK', 'dragK', 'topSpeed', 'rollG', 'wheelbase', 'halfWidth', 'length', 'maxSteer', 'rearBrake', 'rearWeight', 'hOverL', 'rearMuX', 'rearMuY']) if (a[k] !== b[k]) worst = Math.max(worst, Math.abs(a[k] - b[k]) || 1);
          if (a.accel.length !== b.accel.length) worst = Infinity;
          for (let i = 0; i < a.accel.length; i++) if (a.accel[i] !== b.accel[i]) worst = Math.max(worst, Math.abs(a.accel[i] - b.accel[i]));
        }
        return { worker: !!cache.measurer.worker, cars: ids.length, during, ms, worst };
      });
      console.log(`     ${r.cars} cars measured in the worker in ${(r.ms / 1000).toFixed(1)} s (the page blocked ${r.during.toFixed(0)} ms to ask)`);
      t.assert(r.worker, 'the measuring worker did not start');
      t.assert(r.cars >= 3, `only ${r.cars} cars measured`);
      t.assert(r.during < 200, `asking blocked the page for ${r.during.toFixed(0)} ms`);
      t.assert(r.worst === 0, `the worker's numbers differ from the page's by ${r.worst}`);
    },
  },
  {
    name: 'Races: a page that blocks workers without an error still gets its grid (measured on the page)',
    async run(t) {
      const g = await t.open({ viewport: { width: 480, height: 270, isMobile: false, hasTouch: false }, query: 'test=1&q=low' });
      const r = await g.page.evaluate(async () => {
        // A worker that is created but never answers.
        window.Worker = class {
          postMessage() {}
          terminate() {}
        };
        const game = window.__game.game;
        const t0 = performance.now();
        await game.measureAhead();
        return { ms: performance.now() - t0, worker: !!game.abilities.measurer.worker, cars: game.abilities.map.size };
      });
      console.log(`     ${r.cars} cars measured on the page after the worker's silence, in ${(r.ms / 1000).toFixed(1)} s`);
      t.assert(!r.worker && r.cars >= 3, `the page did not take over: worker ${r.worker}, ${r.cars} cars`);
    },
  },
  {
    name: "Races: the rivals' engines are heard: voices take the nearest cars and fade in",
    async run(t) {
      const g = await t.open({ viewport: { width: 480, height: 270, isMobile: false, hasTouch: false }, query: 'test=1&q=low&audio=1' });
      const { page } = g;
      await page.evaluate(() => {
        const game = window.__game.game;
        game.audio.setSettings({ station: 'off' });
        game.audio.unlock();
        game.openEvents();
        game.startRace('proving-gp', 'pro');
      });
      const t0 = Date.now();
      let loaded = null;
      while (!loaded && Date.now() - t0 < 40000) {
        loaded = await page.evaluate(() => {
          const game = window.__game.game;
          const s = game.audio.debugState();
          const need = new Set(game.sim.cars.filter((c) => !c.player).map((c) => c.specId));
          return s.state === 'running' && [...need].every((id) => s.remoteEngines.includes(id)) ? { need: [...need], s } : null;
        });
        if (!loaded) await page.waitForTimeout(200);
      }
      t.assert(loaded, 'the rivals\' engines did not render');
      // Through the countdown and two seconds of racing, a frame at a time (the voices fade in over a quarter second).
      for (let i = 0; i < 30; i++) {
        await page.evaluate(() => window.__game.step(40));
        await page.waitForTimeout(20);
      }
      const heard = await page.evaluate(() => {
        const game = window.__game.game;
        const s = game.audio.debugState();
        const cam = game.rig.camera.position;
        const near = game.sim.cars
          .filter((c) => !c.player)
          .map((c) => ({ id: c.id, d: Math.hypot(c.vehicle.pos.x - cam.x, c.vehicle.pos.z - cam.z) }))
          .sort((a, b) => a.d - b.d);
        return { voices: s.voices, near: near.slice(0, 4), rms: s.rms };
      });
      t.assert(heard.voices.length === 4, `${heard.voices.length} voices busy, 4 expected with seven rivals close by`);
      t.assert(heard.voices.every((v) => v.fade > 0.9), `voices not faded in: ${JSON.stringify(heard.voices)}`);
      const nearest = heard.near.map((n) => n.id).sort().join();
      const voiced = heard.voices.map((v) => v.car).sort().join();
      t.assert(nearest === voiced, `voices on cars ${voiced}, the nearest four are ${nearest}`);
      console.log(`     ${loaded.need.length} engines rendered for ${heard.voices.length} voices; nearest rival ${heard.near[0].d.toFixed(1)} m`);
      // Leaving the race: the rivals go, and their voices fade out.
      await page.evaluate(() => {
        window.__game.game.endRace();
        for (let i = 0; i < 10; i++) window.__game.step(24);
      });
      const after = await page.evaluate(() => window.__game.game.audio.debugState().voices.length);
      t.assert(after === 0, `${after} voices still busy after the race`);
    },
  },
];
