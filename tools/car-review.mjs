// Render each car from six angles for an art review:
//   node tools/car-review.mjs [carId]  -> artifacts/cars/<id>-<angle>.png
import fs from 'node:fs';
import path from 'node:path';
import { launchBrowser, openGame, root } from './browser.mjs';

const only = process.argv[2];
const out = path.join(root, 'artifacts/cars');
fs.mkdirSync(out, { recursive: true });
const browser = await launchBrowser();
const g = await openGame(browser, { viewport: { width: 960, height: 600, isMobile: false, hasTouch: false } });
const ids = await g.page.evaluate(() => window.__game.game.sim.cars.map((c) => c.specId));
const angles = [
  ['front', 0, 0.75],
  ['rear', 180, 0.75],
  ['side', 90, 0.6],
  ['front34', 35, 0.9],
  ['rear34', 145, 0.9],
  ['top', 60, 4.5],
];
for (const id of ids) {
  if (only && id !== only) continue;
  for (const [name, deg, h] of angles) {
    await g.page.evaluate(([deg, h]) => {
      const api = window.__game;
      api.teleport(-150, 160, 0);
      api.step(240);
      api.setUiVisible(false);
      const v = api.game.sim.player.vehicle;
      const a = (deg * Math.PI) / 180;
      const d = h > 3 ? 6 : 7.5;
      // Angle 0 = looking at the front (camera ahead of the car, which faces -Z).
      const x = v.pos.x + Math.sin(a) * d;
      const z = v.pos.z - Math.cos(a) * d;
      api.setCamera(x, v.pos.y + h, z, v.pos.x, v.pos.y - 0.05, v.pos.z, 32);
      api.render();
    }, [deg, h]);
    await g.page.screenshot({ path: path.join(out, `${id}-${name}.png`) });
  }
  // Contact sheet: the six angles in a 3x2 grid, cropped to the car.
  const sheet = await browser.newPage({ viewport: { width: 1200, height: 520 } });
  const imgs = angles.map(([name]) => 'data:image/png;base64,' + fs.readFileSync(path.join(out, `${id}-${name}.png`)).toString('base64'));
  await sheet.setContent(`<body style="margin:0;background:#222;display:grid;grid-template-columns:repeat(3,400px);gap:0">${imgs
    .map((src, i) => `<div style="width:400px;height:260px;overflow:hidden;position:relative"><img src="${src}" style="position:absolute;left:-40px;top:-55px;width:480px"><span style="position:absolute;left:6px;top:4px;color:#fff;font:600 12px sans-serif;text-shadow:0 1px 2px #000">${angles[i][0]}</span></div>`)
    .join('')}</body>`);
  await sheet.screenshot({ path: path.join(out, `${id}-sheet.png`) });
  await sheet.close();
  console.log(`reviewed ${id}`);
}
if (g.errors.length) console.log('console errors:', g.errors.slice(0, 5));
await browser.close();
