// M8: graphics and sound settings in the pause menu, and dynamic resolution.
// The menu offers the presets (Auto to Ultra), the radio and three volumes;
// picking a preset saves it and says when it needs a reload. In normal play
// (not test mode) a slow frame rate steps the internal resolution down.

export const scenarios = [
  {
    name: 'Pause menu: graphics preset, radio and volumes are there and are saved',
    async run(t) {
      const g = await t.open({ viewport: { width: 960, height: 600, isMobile: false, hasTouch: false }, query: 'test=1&q=low' });
      const { page } = g;
      const rows = await page.evaluate(() => {
        const game = window.__game.game;
        game.setPaused(true);
        return [...document.querySelectorAll('.overlay .row > span')].map((e) => e.textContent);
      });
      for (const want of ['Car', 'Graphics', 'Radio', 'Master volume', 'Effects volume', 'Music volume']) t.assert(rows.includes(want), `the pause menu has no "${want}" row (rows: ${rows.join(', ')})`);
      // The car picker puts you in another car where you stand.
      const car = await page.evaluate(() => {
        const game = window.__game.game;
        const before = { x: game.sim.player.vehicle.pos.x, z: game.sim.player.vehicle.pos.z };
        const sel = document.querySelector('select.car-pick');
        const options = sel.options.length;
        sel.value = 'stallworth-ironside-454';
        sel.dispatchEvent(new Event('change'));
        const v = game.sim.player.vehicle;
        return { options, spec: game.sim.player.specId, moved: Math.hypot(v.pos.x - before.x, v.pos.z - before.z), views: game.views.length, model: v.params.spec.id };
      });
      t.assert(car.options === 12, `the car picker lists ${car.options} cars`);
      t.assert(car.spec === 'stallworth-ironside-454' && car.model === 'stallworth-ironside-454', `the car did not change (${car.spec})`);
      t.assert(car.moved < 0.5, `the new car is ${car.moved.toFixed(1)} m from where the old one stood`);
      t.assert(car.views === 1, `${car.views} car views after the change`);
      await t.shot(page, 'm8-pause-menu');
      const picked = await page.evaluate(() => {
        const row = [...document.querySelectorAll('.overlay .row')].find((r) => r.querySelector('span')?.textContent === 'Graphics');
        const button = (label) => [...row.querySelectorAll('button')].find((b) => b.textContent === label);
        const note = () => [...document.querySelectorAll('.overlay p.sub')].find((p) => p.textContent.includes('reload'));
        button('Low').click();
        const lowNote = note()?.style.display !== 'none';
        button('Ultra').click();
        const ultraNote = note()?.style.display !== 'none';
        const radio = [...document.querySelectorAll('.overlay .row')].find((r) => r.querySelector('span')?.textContent === 'Radio');
        [...radio.querySelectorAll('button')].find((b) => b.textContent === 'Redline').click();
        return { lowNote, ultraNote, saved: localStorage.getItem('halcyon-quality'), audio: JSON.parse(localStorage.getItem('halcyon-audio') ?? '{}') };
      });
      t.assert(!picked.lowNote, 'choosing the preset already running asked for a reload');
      t.assert(picked.ultraNote, 'choosing another preset did not say it applies after a reload');
      t.assert(picked.saved === 'ultra', `the preset was not saved (${picked.saved})`);
      t.assert(picked.audio.station === 'redline', `the radio station was not saved (${JSON.stringify(picked.audio)})`);
      await page.evaluate(() => {
        localStorage.removeItem('halcyon-quality');
        localStorage.removeItem('halcyon-audio');
      });
    },
  },
  {
    name: 'Dynamic resolution: slow frames step the internal resolution down',
    slow: true,
    async run(t) {
      // Normal play (no test mode) runs the frame loop; SwiftShader is far below 60 frames a second.
      const g = await t.open({ viewport: { width: 640, height: 360, isMobile: false, hasTouch: false }, query: 'q=low' });
      const { page } = g;
      const t0 = Date.now();
      let r = null;
      while (Date.now() - t0 < 90000) {
        r = await page.evaluate(() => ({ scale: window.__game.game.resScale, ratio: window.__game.game.renderer.getPixelRatio(), loading: window.__game.game.loading }));
        if (!r.loading && r.scale < 0.95) break;
        await page.waitForTimeout(1000);
      }
      t.assert(r && r.scale < 0.95, `the resolution scale stayed at ${r?.scale} with slow frames`);
      t.range(r.scale, 0.55, 0.95, 'resolution scale');
      t.range(r.ratio, 0.5, 0.95, 'pixel ratio (1 at full resolution here)');
      console.log(`     resolution scale ${r.scale.toFixed(2)}, pixel ratio ${r.ratio.toFixed(2)} after ${((Date.now() - t0) / 1000).toFixed(0)} s`);
    },
  },
];
