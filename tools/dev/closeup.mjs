// Close-up review of one building kind: node tools/dev/closeup.mjs <kind> [q] [prefix]
import { launchBrowser, openGame, shot } from '../browser.mjs';
const kind = Number(process.argv[2] ?? 4);
const q = process.argv[3] ?? 'high';
const prefix = process.argv[4] ?? 'closeup';
const b = await launchBrowser();
const g = await openGame(b, { viewport: 'desktop', query: `test=1&q=${q}&time=golden` });
const info = await g.page.evaluate((kind) => {
  const api = window.__game;
  const list = api.game.sim.world.buildings.list.filter((x) => x.kind === kind);
  const bd = list[Math.min(list.length - 1, 3)];
  api.teleport(bd.x + bd.fx * 40, bd.z + bd.fz * 40, 0);
  api.step(30);
  api.setUiVisible(false);
  const y = bd.floor + 2;
  api.setCamera(bd.x + bd.fx * (bd.d / 2 + 14) - bd.fz * 6, y + 1.5, bd.z + bd.fz * (bd.d / 2 + 14) + bd.fx * 6, bd.x, y + 2, bd.z, 50);
  return { n: list.length, bd };
}, kind);
console.log(JSON.stringify(info));
await shot(g.page, `${prefix}-k${kind}`);
await b.close();
