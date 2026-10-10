import { importSrc } from '../../tools/browser.mjs';

// M0: the shell boots, the debug API works, rendering is not blank, and the
// game survives resizes and tab switches.

async function canvasStats(page) {
  return page.evaluate(() => {
    window.__game.render();
    const c = document.getElementById('view');
    const tmp = document.createElement('canvas');
    tmp.width = 64;
    tmp.height = 36;
    const ctx = tmp.getContext('2d');
    ctx.drawImage(c, 0, 0, 64, 36);
    const d = ctx.getImageData(0, 0, 64, 36).data;
    let sum = 0;
    let sq = 0;
    for (let i = 0; i < d.length; i += 4) {
      const l = (d[i] + d[i + 1] + d[i + 2]) / 3;
      sum += l;
      sq += l * l;
    }
    const n = d.length / 4;
    const mean = sum / n;
    return { mean, sd: Math.sqrt(sq / n - mean * mean) };
  });
}

export const scenarios = [
  {
    name: 'boots on desktop with the debug API and no console errors',
    async run(t) {
      const g = await t.open({ viewport: 'desktop' });
      const api = await g.page.evaluate(() => Object.keys(window.__game));
      for (const k of ['step', 'setInput', 'teleport', 'getState', 'screenshot', 'render', 'perf']) t.assert(api.includes(k), `__game.${k} missing`);
      const s = await canvasStats(g.page);
      t.assert(s.sd > 4, `canvas looks blank (luma sd ${s.sd.toFixed(1)})`);
      await t.shot(g.page, 'm0-desktop');
    },
  },
  {
    name: 'scripted throttle moves the car; steps are deterministic',
    async run(t) {
      const g = await t.open({ viewport: 'desktop' });
      const r = await g.page.evaluate(() => {
        const api = window.__game;
        const drive = () => {
          api.reset();
          api.setInput({ throttle: 1, steer: 0.3 });
          api.step(240 * 3);
          api.setInput({ throttle: 0, brake: 1, steer: -0.2 });
          api.step(240);
          return { hash: api.hash(), state: api.getState() };
        };
        const a = drive();
        const b = drive();
        return { a, b };
      });
      t.assert(r.a.hash === r.b.hash, `non-deterministic: ${r.a.hash} vs ${r.b.hash}`);
      const [x, , z] = r.a.state.pos;
      t.assert(Math.hypot(x, z) > 5, `car did not move (${x}, ${z})`);
    },
  },
  {
    name: 'renders in phone portrait and landscape and survives resize',
    async run(t) {
      const g = await t.open({ viewport: 'portrait' });
      await t.shot(g.page, 'm0-portrait');
      await g.page.setViewportSize({ width: 844, height: 390 });
      await g.page.evaluate(() => window.dispatchEvent(new Event('resize')));
      const s = await canvasStats(g.page);
      t.assert(s.sd > 4, 'blank after resize');
      await t.shot(g.page, 'm0-landscape');
      const size = await g.page.evaluate(() => [document.getElementById('view').width, document.getElementById('view').height]);
      t.assert(size[0] > size[1], `canvas not landscape after resize: ${size}`);
    },
  },
  {
    name: 'keyboard maps to controls and window blur releases held keys',
    async run(t) {
      const g = await t.open({ viewport: 'desktop' });
      await g.page.focus('#view');
      await g.page.keyboard.down('KeyW');
      await g.page.keyboard.down('KeyD');
      const held = await g.page.evaluate(() => {
        window.__game.step(240);
        const c = window.__game.game.controls;
        return { throttle: c.throttle, steer: c.steer, speed: window.__game.getState().speed };
      });
      t.assert(held.throttle === 1 && held.steer === 1, `keyboard controls wrong: ${JSON.stringify(held)}`);
      t.assert(held.speed > 1, 'throttle key did not accelerate');
      const after = await g.page.evaluate(() => {
        window.dispatchEvent(new Event('blur'));
        window.__game.step(1);
        return window.__game.game.controls.throttle;
      });
      t.assert(after === 0, 'keys stayed held after blur');
      await g.page.keyboard.up('KeyW');
      await g.page.keyboard.up('KeyD');
    },
  },
  {
    name: 'perf counters report frame, render time and draw calls',
    async run(t) {
      const g = await t.open({ viewport: 'desktop' });
      const p = await g.page.evaluate(() => {
        for (let i = 0; i < 3; i++) window.__game.render();
        return window.__game.perf();
      });
      t.assert(p.counters.draws > 0, `draws not counted: ${JSON.stringify(p.counters)}`);
      t.assert(p.systems.render && p.systems.render.avg > 0, 'render timing missing');
    },
  },
  {
    name: 'portrait keeps a usable horizontal field of view',
    async run(t) {
      const g = await t.open({ viewport: 'portrait' });
      const fov = await g.page.evaluate(() => {
        const cam = window.__game.game.camera;
        return (2 * Math.atan(Math.tan((cam.fov * Math.PI) / 360) * cam.aspect) * 180) / Math.PI;
      });
      t.range(fov, 55, 75, 'horizontal fov in portrait (vertical capped at 100°)');
    },
  },
  {
    name: 'same inputs give the same bits in Node and in Chromium',
    async run(t) {
      const g = await t.open({ viewport: 'desktop' });
      const script = (sim, c) => {
        for (let i = 0; i < 240 * 20; i++) {
          c.throttle = i % 700 < 500 ? 1 : 0;
          c.brake = i % 700 >= 600 ? 1 : 0;
          c.steer = Math.sin(i * 0.01);
          sim.step(c);
        }
        return sim.hash();
      };
      const browserHash = await g.page.evaluate((src) => {
        const fn = new Function('return ' + src)();
        const api = window.__game;
        api.reset();
        const sim = api.game.sim;
        const c = { steer: 0, throttle: 0, brake: 0, handbrake: 0, clutch: 0, analogSteer: true, held: {}, pressed: {}, source: 'script' };
        return fn(sim, c);
      }, script.toString());
      const { Sim } = await importSrc('sim.ts');
      const { neutralControls } = await importSrc('engine/input.ts');
      const nodeHash = script(new Sim(1), neutralControls());
      t.assert(browserHash === nodeHash, `Node ${nodeHash} vs Chromium ${browserHash}`);
    },
  },
  {
    name: 'survives WebGL context loss and restore',
    async run(t) {
      const g = await t.open({ viewport: 'desktop' });
      const r = await g.page.evaluate(async () => {
        const gl = window.__game.game.renderer.getContext();
        const ext = gl.getExtension('WEBGL_lose_context');
        if (!ext) return { skipped: true };
        ext.loseContext();
        await new Promise((res) => setTimeout(res, 50));
        const lost = window.__game.game.contextLost;
        window.__game.render();
        ext.restoreContext();
        await new Promise((res) => setTimeout(res, 100));
        window.__game.render();
        return { lost, restored: !window.__game.game.contextLost };
      });
      t.assert(r.skipped || (r.lost && r.restored), `context loss not handled: ${JSON.stringify(r)}`);
    },
  },
  {
    name: 'teleport rejects non-finite coordinates',
    async run(t) {
      const g = await t.open({ viewport: 'desktop' });
      const r = await g.page.evaluate(() => {
        try {
          window.__game.teleport(NaN, 0);
          return 'accepted';
        } catch {
          return Number.isFinite(window.__game.getState().pos[0]) ? 'rejected' : 'corrupted';
        }
      });
      t.assert(r === 'rejected', `teleport(NaN) was ${r}`);
    },
  },
  {
    name: 'a DPR-3 phone stays inside the 720p pixel budget',
    async run(t) {
      const g = await t.open({ viewport: 'portrait', dpr: 3 });
      const px = await g.page.evaluate(() => {
        const c = document.getElementById('view');
        return { w: c.width, h: c.height, dpr: window.devicePixelRatio };
      });
      t.assert(px.dpr === 3, 'emulated DPR not applied');
      t.range(px.w * px.h, 0.5e6, 1280 * 720 * 1.01, `internal pixels (${px.w}x${px.h})`);
    },
  },
  {
    name: 'without WebGL 2 the page explains why instead of hanging',
    async run(t) {
      const g = await t.open({
        viewport: 'desktop',
        waitReady: false,
        initScript: () => {
          const orig = HTMLCanvasElement.prototype.getContext;
          HTMLCanvasElement.prototype.getContext = function (type, ...rest) {
            if (type === 'webgl2' || type === 'webgl' || type === 'experimental-webgl') return null;
            return orig.call(this, type, ...rest);
          };
        },
      });
      await g.page.waitForTimeout(200);
      const text = await g.page.evaluate(() => document.getElementById('boot')?.textContent ?? '');
      t.assert(/WebGL 2/.test(text), `no explanation shown: "${text}"`);
    },
  },
  {
    name: 'an exception in the frame loop stops it and shows a reload overlay',
    async run(t) {
      const g = await t.open({ viewport: 'desktop', query: '' });
      await g.page.evaluate(() => {
        window.__game.game.sim.step = () => {
          throw new Error('injected failure');
        };
      });
      await g.page.waitForTimeout(400);
      const r = await g.page.evaluate(() => ({
        overlay: document.querySelector('.fatal p')?.textContent ?? null,
        frames: window.__game.game.perf.frames,
      }));
      await g.page.waitForTimeout(300);
      const framesLater = await g.page.evaluate(() => window.__game.game.perf.frames);
      t.assert(r.overlay === 'injected failure', `overlay missing: ${r.overlay}`);
      t.assert(framesLater === r.frames, 'frame loop kept running after the error');
      t.assert(g.errors.some((e) => e.includes('stopped')), 'error was not logged');
      g.errors.length = 0;
    },
  },
  {
    name: 'tab switch pauses the clock',
    async run(t) {
      const g = await t.open({ viewport: 'desktop' });
      const hidden = await g.page.evaluate(() => {
        Object.defineProperty(document, 'hidden', { value: true, configurable: true });
        document.dispatchEvent(new Event('visibilitychange'));
        return window.__game.game.hidden;
      });
      t.assert(hidden === true, 'game did not notice the hidden tab');
      const back = await g.page.evaluate(() => {
        Object.defineProperty(document, 'hidden', { value: false, configurable: true });
        document.dispatchEvent(new Event('visibilitychange'));
        return window.__game.game.hidden;
      });
      t.assert(back === false, 'game did not resume');
    },
  },
];
