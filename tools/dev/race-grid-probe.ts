// Grid order: the field as drawn against the field in qualifying order (fastest at the front), contacts in
// the opening 20 s and over a race, for six seeds.  node tools/run-ts.mjs tools/dev/race-grid-probe.ts [event] [difficulty]
import { Sim } from '../../src/sim';
import { festivalEvents } from '../../src/ai/events';
import { AbilityCache, RaceSession, raceField } from '../../src/ai/race-session';
import { RacingLine, SpeedProfile } from '../../src/ai/racing-line';
import { neutralControls } from '../../src/engine/input';

const cache = new AbilityCache();
const evId = process.argv[2] ?? 'proving-gp';
const diff = (process.argv[3] ?? 'pro') as 'pro';
for (const sorted of [false, true]) {
  let open = 0;
  let total = 0;
  let resets = 0;
  const lines: string[] = [];
  for (const seed of [1, 2, 3, 4, 5, 6]) {
    const sim = new Sim(1);
    const ev = festivalEvents(sim).find((e) => e.id === evId)!;
    if (ev.kind === 'circuit') {
      ev.laps = 1;
      ev.distance = ev.route.length;
    }
    const line = RacingLine.build(ev.route);
    let field = raceField(sim.player.specId, ev.gridSize - 1, diff, seed);
    if (sorted) {
      const est = (f: (typeof field)[number]): number => {
        const p = SpeedProfile.build(line.blend(f.driver.lineAccuracy), cache.get(f.carId, 0, ev.surface), f.driver.gripScale, f.driver.brakeScale, f.driver.accelScale);
        let t = 0;
        const step = ev.route.length / ev.route.n;
        for (let i = 0; i < ev.route.n; i++) t += step / Math.max(1, p.v[i]);
        return t;
      };
      const times = new Map(field.map((f) => [f, est(f)]));
      field = [...field].sort((a, b) => times.get(a)! - times.get(b)!);
    }
    const race = new RaceSession(sim, ev, field, cache, seed, line);
    const c = neutralControls();
    while (race.clock < 0) race.step(c);
    while (race.clock < 20) race.step(c);
    const o = race.contacts;
    let t = 0;
    while (!race.entrants.filter((e) => e.ai).every((e) => e.finished) && t < 900) {
      race.step(c);
      t += 1 / 240;
    }
    const r = race.entrants.reduce((n, e) => n + (e.ai?.resets ?? 0), 0);
    open += o;
    total += race.contacts;
    resets += r;
    lines.push(`seed ${seed}: opening ${o}, race ${race.contacts}, resets ${r}`);
  }
  console.log(`${sorted ? 'sorted by pace' : 'as drawn'}: opening contacts ${open}, race contacts ${total}, resets ${resets}  [${lines.join('; ')}]`);
}
