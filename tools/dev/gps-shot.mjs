// GPS check: set a waypoint at the city, drive a little, shoot the chase view and the full map.
import { launchBrowser, openGame, shot } from '../browser.mjs';
const b = await launchBrowser();
const g = await openGame(b, { viewport: 'desktop', query: 'test=1&q=high&time=golden' });
const r = await g.page.evaluate(() => {
  const api = window.__game;
  api.game.setWaypoint(3200, -160);
  api.setInput({ throttle: 0.4 });
  for (let i = 0; i < 20; i++) api.step(12);
  return { len: api.game.route?.length, pts: api.game.route?.points.length };
});
console.log('route', JSON.stringify(r));
await shot(g.page, 'gps-chase');
await g.page.evaluate(() => {
  window.__game.game.toggleMap(true);
});
await shot(g.page, 'gps-map');
console.log('errors', g.errors.slice(0, 3));
await b.close();
