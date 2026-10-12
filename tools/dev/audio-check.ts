// Renders every car's engine loops and checks them: finite, levels in range,
// seamless seams, and a pitch that follows the firing frequency. Writes a
// WAV sweep per car (idle to the limiter and back, on and off the throttle)
// to the given folder for listening.
//   node tools/run-ts.mjs tools/dev/audio-check.ts [outDir]
import fs from 'node:fs';
import path from 'node:path';
import { CARS } from '../../src/vehicles/cars';
import { loopMix, renderEngineSet, type LoopMix } from '../../src/audio/engine-synth';
import { peak, rms } from '../../src/audio/synth';

const outDir = process.argv[2];
const SR = 44100;

function wav(file: string, data: Float32Array, sr: number): void {
  const buf = Buffer.alloc(44 + data.length * 2);
  buf.write('RIFF', 0);
  buf.writeUInt32LE(36 + data.length * 2, 4);
  buf.write('WAVE', 8);
  buf.write('fmt ', 12);
  buf.writeUInt32LE(16, 16);
  buf.writeUInt16LE(1, 20);
  buf.writeUInt16LE(1, 22);
  buf.writeUInt32LE(sr, 24);
  buf.writeUInt32LE(sr * 2, 28);
  buf.writeUInt16LE(2, 32);
  buf.writeUInt16LE(16, 34);
  buf.write('data', 36);
  buf.writeUInt32LE(data.length * 2, 40);
  for (let i = 0; i < data.length; i++) buf.writeInt16LE(Math.max(-32767, Math.min(32767, Math.round(data[i] * 32767))), 44 + i * 2);
  fs.writeFileSync(file, buf);
}

/** Dominant period by autocorrelation within [minLag, maxLag], in samples. */
function dominantLag(x: Float32Array, minLag: number, maxLag: number): number {
  let best = minLag;
  let bestV = -Infinity;
  const n = Math.min(x.length - maxLag, 8000);
  for (let lag = minLag; lag <= maxLag; lag++) {
    let s = 0;
    for (let i = 0; i < n; i++) s += x[i] * x[i + lag];
    if (s > bestV) {
      bestV = s;
      best = lag;
    }
  }
  return best;
}

let problems = 0;
const t0 = Date.now();
for (const spec of CARS) {
  const tc = Date.now();
  const set = renderEngineSet(spec, SR);
  const ms = Date.now() - tc;
  const notes: string[] = [];
  for (const loop of [...set.on, ...set.off]) {
    const d = loop.data;
    if (!d.every(Number.isFinite)) notes.push(`non-finite at ${loop.rpm.toFixed(0)}`);
    const r = rms(d);
    const p = peak(d);
    if (r < 0.1 || p > 0.99) notes.push(`level rms ${r.toFixed(2)} peak ${p.toFixed(2)} at ${loop.rpm.toFixed(0)}`);
    // The seam: the jump from the last sample to the first is no bigger than the
    // biggest step inside the loop (a firing can land on the seam; a click cannot).
    let step = 0;
    for (let i = 1; i < d.length; i++) step = Math.max(step, Math.abs(d[i] - d[i - 1]));
    const seam = Math.abs(d[0] - d[d.length - 1]);
    if (seam > step * 1.02) notes.push(`seam ${seam.toFixed(3)} vs largest step ${step.toFixed(3)} at ${loop.rpm.toFixed(0)}`);
  }
  // Pitch: the firing period (or the motor's) should be the strongest repetition near it.
  if (!set.voice.electric) {
    const mid = set.on[Math.floor(set.on.length / 2)];
    const fire = (mid.rpm / 120) * spec.engine.cylinders;
    const lag = dominantLag(mid.data, Math.floor((SR / fire) * 0.6), Math.ceil((SR / fire) * 2.2));
    const f = SR / lag;
    const ratio = f / fire;
    // The cycle (rpm/120) or a firing multiple; within 3% of firing/k for k in 1..cyl.
    let ok = false;
    for (let k = 1; k <= spec.engine.cylinders; k++) if (Math.abs(ratio * k - Math.round(ratio * k)) < 0.03 * k && Math.round(ratio * k) >= 1) ok = true;
    notes.push(`mid ${mid.rpm.toFixed(0)} rpm: firing ${fire.toFixed(1)} Hz, strongest repetition ${f.toFixed(1)} Hz${ok ? '' : ' (UNRELATED)'}`);
    if (!ok) problems++;
  }
  const bad = notes.filter((n) => !n.startsWith('mid'));
  problems += bad.length;
  console.log(`${spec.id.padEnd(24)} ${spec.engine.layout.padEnd(10)} ${set.on.length} loops x2 in ${ms} ms; ${notes.join('; ')}`);
  if (outDir) {
    fs.mkdirSync(outDir, { recursive: true });
    // A sweep: 2 s up on the throttle, 1.5 s down off it.
    const len = Math.round(3.5 * SR);
    const out = new Float32Array(len);
    const mix: LoopMix = { lo: 0, hi: 0, t: 0, rateLo: 1, rateHi: 1 };
    const phaseOn = new Float64Array(set.on.length);
    const phaseOff = new Float64Array(set.off.length);
    const lo = set.voice.idle;
    const hi = set.voice.limiter;
    for (let i = 0; i < len; i++) {
      const t = i / SR;
      const up = t < 2;
      const rpm = up ? lo + (hi - lo) * Math.min(1, t / 2) : hi - (hi - lo) * Math.min(1, (t - 2) / 1.5);
      const load = up ? 1 : 0;
      let s = 0;
      for (const [loops, phase, g] of [[set.on, phaseOn, load], [set.off, phaseOff, 1 - load]] as const) {
        loopMix(loops, rpm, mix);
        const a = Math.cos(mix.t * Math.PI * 0.5);
        const b = Math.sin(mix.t * Math.PI * 0.5);
        for (let k = 0; k < loops.length; k++) {
          const rate = rpm / loops[k].rpm;
          phase[k] = (phase[k] + rate) % loops[k].data.length;
          const w = k === mix.lo ? a : k === mix.hi ? b : 0;
          if (w > 0 && g > 0) {
            const j = Math.floor(phase[k]);
            const fr = phase[k] - j;
            const d = loops[k].data;
            s += g * w * (d[j] * (1 - fr) + d[(j + 1) % d.length] * fr);
          }
        }
      }
      out[i] = s * (0.5 + 0.5 * rpm / hi);
    }
    wav(path.join(outDir, `${spec.id}.wav`), out, SR);
  }
}
console.log(`${CARS.length} cars in ${((Date.now() - t0) / 1000).toFixed(1)} s; ${problems} problems`);
process.exit(problems ? 1 : 0);
