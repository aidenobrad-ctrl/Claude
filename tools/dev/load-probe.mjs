// Measure startup streaming time and steady-state frame cost in normal play mode.
import { launchBrowser, openGame } from '../browser.mjs';
const q = process.argv[2] ?? 'high';
const b = await launchBrowser();
const t0 = Date.now();
const g = await openGame(b, { viewport: 'desktop', query: `q=${q}`, waitReady: false });
await g.page.waitForFunction(() => window.__game, null, { timeout: 120000 });
const tBoot = Date.now() - t0;
let last = '';
for (;;) {
  const st = await g.page.evaluate(() => ({ ready: window.__game.ready, text: document.getElementById('boot')?.textContent ?? '', tiles: window.__game.game.terrain.tileCount, inflight: window.__game.game.tiles.inFlight, cells: window.__game.game.vegetation.cellCount }));
  if (st.text !== last) {
    console.log(`${((Date.now() - t0) / 1000).toFixed(1)} s`, JSON.stringify(st));
    last = st.text;
  }
  if (st.ready) break;
  await g.page.waitForTimeout(250);
}
console.log(`boot ${tBoot} ms, ready after ${Date.now() - t0} ms`);
await g.page.waitForTimeout(3000);
console.log(JSON.stringify(await g.page.evaluate(() => window.__game.perf())));
console.log('errors', g.errors.slice(0, 3));
await b.close();
