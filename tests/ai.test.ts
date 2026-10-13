// The AI on the web (src/ai/, a port of the Unity version's C# AI): the
// festival's routes, racing lines and speed profiles against the C#'s own
// (dotnet/ref/ai/ai-ref.json, written by `dotnet run -- airef` in
// dotnet/Halcyon.AI.Tests), measured car abilities bit for bit, and races run
// to the finish on the island.
import fs from 'node:fs';
import { test, assert, assertRange, log, metric, QUICK } from './kit';
import { Sim } from '../src/sim';
import { CARS } from '../src/vehicles/cars';
import { festivalEvents } from '../src/ai/events';
import { RacingLine, SpeedProfile, CarAbility } from '../src/ai/racing-line';
import { Personality } from '../src/ai/ai-driver';
import { AbilityCache, RaceSession, raceField } from '../src/ai/race-session';
import { neutralControls } from '../src/engine/input';
import { SURFACE } from '../src/world/surfaces';

interface RefEvent {
  id: string;
  kind: string;
  n: number;
  length: number;
  startS: number;
  distance: number;
  closed: boolean;
  x: number[];
  z: number[];
  y: number[];
  wl: number[];
  wr: number[];
  offset: number[];
  curv: number[];
  v: number[];
}
interface RefAbility {
  id: string;
  wetness: number;
  surface: number;
  mu: number;
  brakeG: number;
  aeroK: number;
  dragK: number;
  topSpeed: number;
  rollG: number;
  accel: number[];
  stable: number[];
}

interface RefRace {
  event: string;
  laps: number;
  count: number;
  seed: number;
  steps: number;
  contacts: number;
  wallHits: number;
  cars: string[];
  times: number[];
  /** Every 12 steps through the start: step, then per car x, z, vx, steer, throttle, brake. */
  trace: number[][];
}

const ref = JSON.parse(fs.readFileSync('dotnet/ref/ai/ai-ref.json', 'utf8')) as { events: RefEvent[]; abilities: RefAbility[]; race: RefRace };
const sim = new Sim(1);
const events = festivalEvents(sim);

function maxDiff(a: ArrayLike<number>, b: ArrayLike<number>): number {
  let m = 0;
  for (let i = 0; i < Math.min(a.length, b.length); i++) m = Math.max(m, Math.abs(a[i] - b[i]));
  return m;
}

test('events: the same races as the Unity build, on the same routes', () => {
  for (const r of ref.events) {
    const e = events.find((x) => x.id === r.id);
    assert(!!e, `no event ${r.id} on the web`);
    if (!e) continue;
    const route = e.route;
    assert(route.n === r.n, `${r.id}: ${route.n} samples, the C# has ${r.n}`);
    const dl = Math.abs(route.length - r.length);
    const dx = maxDiff(route.x, r.x);
    const dz = maxDiff(route.z, r.z);
    const dw = Math.max(maxDiff(route.wLeft, r.wl), maxDiff(route.wRight, r.wr));
    log(`${r.id.padEnd(22)} ${route.n} samples, ${(route.length / 1000).toFixed(2)} km: length ${dl.toExponential(1)} m, points ${Math.max(dx, dz).toExponential(1)} m, widths ${dw.toExponential(1)} m`);
    // Bit for bit: the road data is read as the Unity build reads its export (32-bit floats).
    assert(dl === 0 && dx === 0 && dz === 0 && dw === 0, `${r.id}: the route differs from the C#'s (length ${dl}, x ${dx}, z ${dz}, widths ${dw})`);
    assert(e.distance === r.distance && e.startS === r.startS, `${r.id}: start or distance differ`);
  }
});

test('car abilities: measured exactly as the C# measures them', () => {
  for (const r of ref.abilities) {
    const spec = CARS.find((c) => c.id === r.id);
    assert(!!spec, `no car ${r.id}`);
    if (!spec) continue;
    const a = CarAbility.measure(spec, r.wetness, r.surface as never);
    const rel = (x: number, y: number): number => Math.abs(x - y) / Math.max(1e-9, Math.abs(y));
    const worst = Math.max(rel(a.mu, r.mu), rel(a.brakeG, r.brakeG), rel(a.aeroK, r.aeroK), rel(a.dragK, r.dragK), rel(a.topSpeed, r.topSpeed), maxDiff(a.accel, r.accel), maxDiff([a.stableBrake(0), a.stableBrake(0.5), a.stableBrake(1)], r.stable));
    log(`${r.id.padEnd(24)} wet ${r.wetness} surface ${r.surface}: mu ${a.mu.toFixed(3)} brake ${a.brakeG.toFixed(3)} g top ${(a.topSpeed * 3.6).toFixed(0)} km/h; worst difference ${worst.toExponential(1)}`);
    assert(a.accel.length === r.accel.length, `${r.id}: ${a.accel.length} acceleration bins, the C# has ${r.accel.length}`);
    assert(worst === 0, `${r.id}: abilities differ from the C#'s by ${worst}`);
  }
});

test('racing lines and speed profiles: bit for bit the C#\'s', () => {
  const car = CarAbility.measure(CARS.find((c) => c.id === 'halden-aster-gt')!, 0);
  const dirtCar = CarAbility.measure(CARS.find((c) => c.id === 'halden-aster-gt')!, 0, SURFACE.dirt);
  const p = new Personality({ skill: 0.8, risk: 0.5 });
  for (const r of ref.events) {
    const e = events.find((x) => x.id === r.id);
    if (!e) continue;
    const line = RacingLine.build(e.route);
    const prof = SpeedProfile.build(line.blend(p.lineAccuracy), e.surface === SURFACE.dirt ? dirtCar : car, p.gripScale, p.brakeScale, p.accelScale);
    const dOff = maxDiff(line.offset, r.offset);
    const dV = maxDiff(prof.v, r.v);
    log(`${r.id.padEnd(22)} line ${dOff.toExponential(1)} m, profile ${dV.toExponential(1)} m/s`);
    assert(dOff === 0, `${r.id}: the line differs by ${dOff} m`);
    assert(dV === 0, `${r.id}: the speed profile differs by ${dV} m/s`);
  }
});

test('a Grand Prix on the proving ground: every car finishes, no resets, the field spread sensibly', () => {
  const s = new Sim(1);
  const ev = festivalEvents(s).find((e) => e.id === 'proving-gp')!;
  ev.laps = QUICK ? 1 : 2;
  ev.distance = ev.laps * ev.route.length;
  const field = raceField(s.player.specId, 5, 'pro', 7);
  // AI only: the player's car leaves the island for the race.
  const race = new RaceSession(s, ev, field, new AbilityCache(), 7, undefined, false);
  const c = neutralControls();
  let t = 0;
  while (!race.allFinished && t < 600) {
    race.step(c);
    t += 1 / 240;
  }
  const ai = race.entrants.filter((e) => e.ai);
  const resets = ai.reduce((n, e) => n + (e.ai?.resets ?? 0), 0);
  const times = ai.map((e) => e.finishTime).sort((a, b) => a - b);
  log(`${ai.length} AI cars, ${ev.laps} laps: winner ${times[0].toFixed(1)} s, last ${times[times.length - 1].toFixed(1)} s, ${resets} resets, ${race.contacts} contacts, ${race.wallHits} wall hits`);
  metric('ai-gp', { winner: times[0], last: times[times.length - 1], resets, contacts: race.contacts });
  assert(ai.every((e) => e.finished), 'a car did not finish');
  assert(resets === 0, `${resets} resets`);
  assertRange(times[times.length - 1] / times[0], 1, 1.25, 'last over winner');
});

test('a whole race as the C# runs it: the start to the last bit, then the finishing times within 1%', () => {
  const r = ref.race;
  const s = new Sim(1);
  const ev = festivalEvents(s).find((e) => e.id === r.event)!;
  ev.laps = r.laps;
  ev.distance = r.laps * ev.route.length;
  const field = raceField(s.player.specId, r.count, 'pro', r.seed);
  assert(field.map((f) => f.carId).join() === r.cars.join(), `the field differs: ${field.map((f) => f.carId).join()} against the C#'s ${r.cars.join()}`);
  const race = new RaceSession(s, ev, field, new AbilityCache(), r.seed, undefined, false);
  const c = neutralControls();
  let steps = 0;
  let k = 0;
  let firstDiff = -1;
  while (!race.allFinished && race.clock < 600) {
    const row = r.trace[k];
    if (row && row[0] === steps) {
      k++;
      let j = 1;
      for (const e of race.entrants) {
        const v = e.car.vehicle;
        for (const x of [v.pos.x, v.pos.z, v.vel.x, v.input.steer, v.input.throttle, v.input.brake]) if (x !== row[j++] && firstDiff < 0) firstDiff = steps;
      }
    }
    race.step(c);
    steps++;
  }
  const times = race.entrants.map((e) => e.finishTime);
  const worst = Math.max(...times.map((t, i) => Math.abs(t - r.times[i]) / r.times[i]));
  const last = r.trace[r.trace.length - 1][0];
  log(`${r.count} AI, ${r.laps} lap: identical to the C# through step ${firstDiff < 0 ? last : firstDiff - 12} (the countdown is 720); finishing times within ${(worst * 100).toFixed(2)}%; contacts ${race.contacts} (C# ${r.contacts})`);
  assert(firstDiff < 0, `the race left the C#'s at step ${firstDiff} (the countdown is 720 steps): the port has drifted`);
  assert(race.allFinished && worst < 0.01, `finishing times differ by up to ${(worst * 100).toFixed(2)}%: ${times.map((t) => t.toFixed(3)).join(' ')} against ${r.times.map((t) => t.toFixed(3)).join(' ')}`);
  assert(Math.abs(race.contacts - r.contacts) <= 2, `${race.contacts} contacts against the C#'s ${r.contacts}`);
});
