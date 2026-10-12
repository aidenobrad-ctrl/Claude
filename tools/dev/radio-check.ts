// Renders the three radio stations for a while and checks the output:
// finite, never clipping, a sensible loudness, songs that change. Writes a
// stereo WAV per station to the given folder for listening.
//   node tools/run-ts.mjs tools/dev/radio-check.ts [outDir] [seconds]
import fs from 'node:fs';
import path from 'node:path';
import { RadioStation, STATIONS } from '../../src/audio/radio';

const outDir = process.argv[2];
const seconds = Number(process.argv[3] ?? 75);
const SR = 44100;
let problems = 0;
for (const st of STATIONS) {
  const radio = new RadioStation(st.id, SR, 1234);
  const ls: Float32Array[] = [];
  const rs: Float32Array[] = [];
  let total = 0;
  let renderMs = 0;
  const songs: string[] = [];
  let bars = 0;
  while (total < seconds * SR) {
    const t0 = performance.now();
    const bar = radio.renderBar();
    renderMs += performance.now() - t0;
    if (bar.info.newSong && !bar.info.ident) songs.push(`${bar.info.artist} - ${bar.info.title}`);
    ls.push(bar.l);
    rs.push(bar.r);
    total += bar.l.length;
    bars++;
  }
  let sum = 0;
  let pk = 0;
  let bad = 0;
  for (const arr of [...ls, ...rs]) {
    for (let i = 0; i < arr.length; i++) {
      const v = arr[i];
      if (!Number.isFinite(v)) bad++;
      sum += v * v;
      pk = Math.max(pk, Math.abs(v));
    }
  }
  const rmsDb = 10 * Math.log10(sum / (2 * total) + 1e-12);
  const notes: string[] = [];
  if (bad) notes.push(`${bad} non-finite samples`);
  if (pk > 0.99) notes.push(`peak ${pk.toFixed(3)}`);
  if (rmsDb < -30 || rmsDb > -8) notes.push(`loudness ${rmsDb.toFixed(1)} dBFS`);
  problems += notes.length;
  const realtime = (total / SR) / (renderMs / 1000);
  console.log(`${st.name.padEnd(14)} ${bars} bars, ${(total / SR).toFixed(1)} s in ${renderMs.toFixed(0)} ms (${realtime.toFixed(0)}x real time); rms ${rmsDb.toFixed(1)} dBFS, peak ${pk.toFixed(2)}; songs: ${songs.join(' / ')}${notes.length ? ' PROBLEMS: ' + notes.join(', ') : ''}`);
  if (outDir) {
    fs.mkdirSync(outDir, { recursive: true });
    const n = total;
    const buf = Buffer.alloc(44 + n * 4);
    buf.write('RIFF', 0);
    buf.writeUInt32LE(36 + n * 4, 4);
    buf.write('WAVE', 8);
    buf.write('fmt ', 12);
    buf.writeUInt32LE(16, 16);
    buf.writeUInt16LE(1, 20);
    buf.writeUInt16LE(2, 22);
    buf.writeUInt32LE(SR, 24);
    buf.writeUInt32LE(SR * 4, 28);
    buf.writeUInt16LE(4, 32);
    buf.writeUInt16LE(16, 34);
    buf.write('data', 36);
    buf.writeUInt32LE(n * 4, 40);
    let o = 44;
    for (let b = 0; b < ls.length; b++) {
      for (let i = 0; i < ls[b].length; i++) {
        buf.writeInt16LE(Math.round(Math.max(-1, Math.min(1, ls[b][i])) * 32767), o);
        buf.writeInt16LE(Math.round(Math.max(-1, Math.min(1, rs[b][i])) * 32767), o + 2);
        o += 4;
      }
    }
    fs.writeFileSync(path.join(outDir, `radio-${st.id}.wav`), buf);
  }
}
console.log(`${problems} problems`);
process.exit(problems ? 1 : 0);
