// Reference renders of the procedural audio for the C# port's checks
// (dotnet/Halcyon.Core.Tests, AudioChecks.cs): engine loops of five cars,
// every bed, one-shot and UI sound, and the first bars of each radio station.
// The C# renders the same and must match within a small tolerance (the two
// runtimes' sin/exp can differ in the last bit; the samples are float32).
//   node tools/run-ts.mjs tools/dev/audio-ref.ts
import fs from 'node:fs';
import path from 'node:path';
import { CARS } from '../../src/vehicles/cars';
import { renderEngineSet } from '../../src/audio/engine-synth';
import { renderBed, renderShot, renderUi, type BedId, type ShotId, type UiId } from '../../src/audio/beds';
import { RadioStation, STATIONS } from '../../src/audio/radio';
import { rms } from '../../src/audio/synth';

const SR = 44100;
const HEAD = 3000;
const head = (d: Float32Array, n = HEAD): number[] => Array.from(d.subarray(0, Math.min(n, d.length)));
const out = path.resolve('dotnet/ref/audio');
fs.mkdirSync(out, { recursive: true });

const engines = ['halden-aster-gt', 'aurelle-pico', 'stallworth-ironside-454', 'orsini-saetta', 'arcline-vanta'].map((id) => {
  const set = renderEngineSet(CARS.find((c) => c.id === id)!, SR);
  const pick = [0, Math.floor(set.on.length / 2), set.on.length - 1];
  const loops = (ls: typeof set.on) => ls.map((l, i) => ({ rpm: l.rpm, length: l.data.length, rms: rms(l.data), head: pick.includes(i) ? head(l.data) : [] }));
  return { car: id, electric: set.voice.electric, on: loops(set.on), off: loops(set.off) };
});
const beds = (['squeal', 'road', 'gravel', 'grass', 'snow', 'spray', 'wind', 'intake', 'turbo'] as BedId[]).map((id) => {
  const d = renderBed(id, SR);
  return { id, length: d.length, rms: rms(d), head: head(d) };
});
const shots = ([['impactLight', 0], ['impactMedium', 1], ['impactHeavy', 2], ['blowoff', 0], ['crackle', 1], ['shift', 0], ['scrape', 0]] as [ShotId, number][]).map(([id, v]) => {
  const d = renderShot(id, SR, v);
  return { id, variant: v, length: d.length, rms: rms(d), head: head(d) };
});
const ui = (['move', 'select', 'back', 'error', 'count', 'go', 'reward', 'photo', 'checkpoint'] as UiId[]).map((id) => {
  const d = renderUi(id, SR);
  return { id, length: d.length, rms: rms(d), head: head(d, 2000) };
});
const radio = STATIONS.map((st) => {
  const r = new RadioStation(st.id, SR, 1234);
  const bars = [];
  // 24 bars: the ident, the intro and its fill, a verse, and into the chorus (its hook,
  // the crash, the bass walking to the next chord). Samples of a few bars, levels of all.
  const keep = new Set([2, 4, 13, 20]);
  for (let b = 0; b < 24; b++) {
    const bar = r.renderBar();
    bars.push({ length: bar.l.length, title: bar.info.title, artist: bar.info.artist, newSong: bar.info.newSong, ident: bar.info.ident, rmsL: rms(bar.l), rmsR: rms(bar.r), headL: keep.has(b) ? head(bar.l, 1500) : [], headR: keep.has(b) ? head(bar.r, 1500) : [] });
  }
  return { station: st.id, seed: 1234, bars };
});
fs.writeFileSync(path.join(out, 'audio-ref.json'), JSON.stringify({ sampleRate: SR, engines, beds, shots, ui, radio }));
console.log(`wrote ${path.join(out, 'audio-ref.json')} (${(fs.statSync(path.join(out, 'audio-ref.json')).size / 1e6).toFixed(2)} MB)`);
