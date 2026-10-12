// M8: the sound. With audio switched on in test mode, the worker renders the
// player's engine, the beds and the radio; driving makes the engine and the
// road play (the output is not silent), the radio queues bars ahead, the
// station key cycles stations, and switching cars renders the new engine.

const waitFor = async (page, fn, arg, timeout = 30000) => {
  const t0 = Date.now();
  for (;;) {
    const v = await page.evaluate(fn, arg);
    if (v) return v;
    if (Date.now() - t0 > timeout) return null;
    await page.waitForTimeout(150);
  }
};

export const scenarios = [
  {
    name: 'Audio: engine, beds and radio load in the worker and play while driving',
    async run(t) {
      // A small window: SwiftShader renders it quickly, and the audio only needs frames to update.
      const g = await t.open({ viewport: { width: 480, height: 270, isMobile: false, hasTouch: false }, query: 'test=1&q=low&audio=1' });
      const { page } = g;
      await page.evaluate(() => {
        const game = window.__game.game;
        game.audio.setSettings({ master: 0.8, effects: 0.9, music: 0.5, station: 'pulse' });
        game.audio.unlock();
      });
      const ready = await waitFor(page, () => {
        const s = window.__game.game.audio.debugState();
        return s.state === 'running' && s.engineLoops > 0 && s.beds >= 9 && s.ui >= 9 ? s : null;
      });
      t.assert(ready, 'the audio did not come up (context running, engine loops, beds, UI sounds)');
      t.assert(ready.worker, 'the audio worker did not start');
      // Drive: full throttle for two seconds, rendering frames so the audio follows.
      // (Waits happen on the Node side: timers inside a headless page get throttled.)
      await page.evaluate(() => window.__game.setInput({ throttle: 1 }));
      for (let i = 0; i < 20; i++) {
        // Stepping presents a frame without rendering it, which is all the audio needs
        // (SwiftShader takes seconds to draw a frame).
        await page.evaluate(() => window.__game.step(24));
        await page.waitForTimeout(30);
      }
      const driving = await page.evaluate(() => {
        const game = window.__game.game;
        return { rpm: game.sim.player.vehicle.rpm, speed: game.sim.player.vehicle.speed, audio: game.audio.debugState() };
      });
      t.range(driving.speed, 5, 80, 'speed after two seconds of throttle (m/s)');
      t.assert(driving.audio.engineOnGain > 0.1, `the engine's on-throttle loops are silent (gain ${driving.audio.engineOnGain})`);
      t.assert(driving.audio.bedGains.road > 0 || driving.audio.bedGains.gravel > 0, 'no road or gravel noise while driving');
      // The output is not silent (sampled over half a second of real time).
      let loud = 0;
      for (let i = 0; i < 6; i++) {
        await page.waitForTimeout(100);
        loud = Math.max(loud, await page.evaluate(() => {
          window.__game.step(4);
          return window.__game.game.audio.debugState().rms;
        }));
      }
      t.assert(loud > 0.005, `the mix is silent (rms ${loud})`);
      // The radio keeps bars queued ahead.
      const radio = await waitFor(page, () => {
        window.__game.step(4);
        const s = window.__game.game.audio.debugState();
        return s.radioQueued >= 1 && s.radioAhead > 1 ? s : null;
      });
      t.assert(radio, 'the radio did not queue any bars');
      // The radio key cycles stations (pulse -> coast).
      const next = await page.evaluate(() => window.__game.game.audio.cycleStation());
      t.assert(next === 'coast', `the next station was ${next}`);
      const coast = await waitFor(page, () => {
        window.__game.step(4);
        const s = window.__game.game.audio.debugState();
        return s.station === 'coast' && s.radioQueued >= 1 ? s : null;
      });
      t.assert(coast, 'Coastline FM did not start');
      // A different car: its engine is rendered and replaces the first.
      const swapped = await page.evaluate(() => {
        const game = window.__game.game;
        game.audio.setCar('stallworth-ironside-454');
        return true;
      });
      t.assert(swapped, 'could not switch the engine');
      const v8 = await waitFor(page, () => {
        const s = window.__game.game.audio.debugState();
        return s.engineCar === 'stallworth-ironside-454' && s.engineLoops > 0 ? s : null;
      });
      t.assert(v8, 'the V8 engine did not load');
      console.log(`     engine loops ${ready.engineLoops} (${ready.engineCar}), beds ${ready.beds}, rms ${loud.toFixed(3)} at ${driving.rpm.toFixed(0)} rpm; radio ${radio.radioQueued} bars queued, ${radio.radioAhead} s ahead`);
      await page.evaluate(() => window.__game.game.audio.setSettings({ station: 'off' }));
    },
  },
];
