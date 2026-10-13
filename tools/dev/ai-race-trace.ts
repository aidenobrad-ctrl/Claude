// The TS side of `dotnet run -- airace EVERY UNTIL` (dotnet/Halcyon.AI.Tests): the reference race step by
// step, in the same format, to diff against the C# and find where the port drifts.
//   node tools/run-ts.mjs tools/dev/ai-race-trace.ts EVERY UNTIL
import { Sim } from '../../src/sim';
import { festivalEvents } from '../../src/ai/events';
import { AbilityCache, RaceSession, raceField } from '../../src/ai/race-session';
import { neutralControls } from '../../src/engine/input';

const every = +(process.argv[2] ?? 24);
const until = +(process.argv[3] ?? 2400);
// As C#'s "R" format: the shortest round-trip form.
const N = (v: number): string => {
  if (Number.isNaN(v)) return 'NaN';
  let s = String(v);
  if (s.includes('e')) s = s.replace('e+', 'E+').replace('e-', 'E-');
  return s;
};
const sim = new Sim(1);
const ev = festivalEvents(sim).find((e) => e.id === 'proving-gp')!;
ev.laps = 1;
ev.distance = ev.route.length;
const race = new RaceSession(sim, ev, raceField(sim.player.specId, 5, 'pro', 7), new AbilityCache(), 7, undefined, false);
const c = neutralControls();
const out: string[] = [];
for (let step = 0; step <= until; step++) {
  if (step % every === 0) {
    let line = `${step}`;
    for (const e of race.entrants) {
      const v = e.car.vehicle;
      line += ` | ${N(v.pos.x)} ${N(v.pos.z)} ${N(v.vel.x)} ${N(v.input.steer)} ${N(v.input.throttle)} ${N(v.input.brake)} ${N(e.s)} ${N(v.draftFactor)} ${N(v.pos.y)} ${N(v.vel.y)} ${N(v.vel.z)}`;
    }
    out.push(line);
    if (step >= until - 3) {
      const v2 = race.entrants[2].car.vehicle;
      for (const wh of v2.wheels) out.push(`wheel ${step} ${wh.grounded ? 1 : 0} ${wh.surface} ${N(wh.contact.x)} ${N(wh.contact.y)} ${N(wh.contact.z)} ${N(wh.normal.x)} ${N(wh.normal.y)} ${N(wh.normal.z)} ${N(wh.length)} ${N(wh.load)}`);
    }
  }
  const tick = sim.tick;
  race.step(c);
  if (step % every === 0) for (const e of sim.events) if (e.tick === tick) out.push(`event ${step} ${e.kind} car ${e.carId} ${N(e.x)} ${N(e.z)} ${N(e.value)}`);
  sim.events.length = 0;
}
console.log(out.join('\n'));
