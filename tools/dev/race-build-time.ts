// How long the game takes to build a race (racing line, rivals measured, grid): the menu's "Building the grid…".
import { Sim } from '../../src/sim';
import { festivalEvents } from '../../src/ai/events';
import { AbilityCache, RaceSession, qualifyingOrder, raceField } from '../../src/ai/race-session';
import { RacingLine } from '../../src/ai/racing-line';

const sim = new Sim(1);
let t0 = performance.now();
const events = festivalEvents(sim);
console.log(`events: ${(performance.now() - t0).toFixed(0)} ms`);
const cache = new AbilityCache();
for (const ev of events) {
  t0 = performance.now();
  const line = RacingLine.build(ev.route);
  const t1 = performance.now();
  const field = qualifyingOrder(ev, line, raceField(sim.player.specId, ev.gridSize - 1, 'pro', 1), cache);
  const t2 = performance.now();
  const race = new RaceSession(sim, ev, field, cache, 1, line, true, Math.min(Math.floor(ev.gridSize / 2), field.length));
  const t3 = performance.now();
  race.end();
  console.log(`${ev.id.padEnd(20)} line ${(t1 - t0).toFixed(0)} ms, rivals ${(t2 - t1).toFixed(0)} ms, grid ${(t3 - t2).toFixed(0)} ms`);
}
