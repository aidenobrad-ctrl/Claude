// Engine sound, synthesized per car from its engine spec. Each firing of a
// cylinder is an exhaust pressure pulse; the pulses run through the exhaust
// (a few resonances per bank and a muffler) and are rendered into seamless
// loops at a ladder of engine speeds, on and off the throttle. At run time
// the player pitches the two loops either side of the current rpm and
// crossfades between them (and between the on- and off-load sets), the way
// racing games play recorded engines; here the recordings are synthesized.
//
// The firing pattern carries the character: an inline four's even buzz, a
// six's smooth wail, a cross-plane V8's uneven bank pattern (the burble),
// a flat-plane V8's scream. Electric motors are a stack of motor harmonics.
import type { CarSpec } from '../vehicles/spec';
import { AudioRng, Biquad, normalize, renderPeriodic, softClip } from './synth';

export interface Firing {
  /** Fraction of the 720-degree cycle. */
  at: number;
  /** Exhaust bank, 0 or 1. */
  bank: number;
  /** Relative pulse strength. */
  amp: number;
}

export interface EngineVoice {
  electric: boolean;
  firing: Firing[];
  /** Exhaust resonances, Hz, and how much each bank detunes them. */
  boom: number;
  honk: number;
  rasp: number;
  bankDetune: number;
  /** Muffler low-pass cutoff, Hz. */
  muffler: number;
  /** Rasp and combustion noise, 0..1 (sporty engines more). */
  edge: number;
  idle: number;
  limiter: number;
  turbo: boolean;
  /** Electric: motor pole pairs (sets the whine's pitch). */
  poles: number;
  seed: number;
}

export interface EngineLoop {
  /** Engine speed the loop was rendered at (after rounding to whole cycles). */
  rpm: number;
  onLoad: boolean;
  data: Float32Array;
}

export interface EngineSoundSet {
  voice: EngineVoice;
  sampleRate: number;
  /** Ascending rpm; the same ladder for the on- and off-load sets. */
  on: EngineLoop[];
  off: EngineLoop[];
}

function hashId(s: string): number {
  let h = 0x811c9dc5;
  for (let i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i);
    h = Math.imul(h, 0x01000193);
  }
  return h >>> 0;
}

/** The engine's voice from its spec: firing pattern, exhaust and character. */
export function engineVoice(spec: CarSpec): EngineVoice {
  const e = spec.engine;
  const rng = new AudioRng(hashId(spec.id) ^ 0x5eed);
  const n = Math.max(0, e.cylinders);
  const layout = e.layout.toUpperCase();
  const vee = layout.startsWith('V') || layout.startsWith('FLAT') || layout.startsWith('W');
  // A V8 revving past 7500 is taken to be flat-plane (even banks); otherwise cross-plane.
  const crossPlane = n === 8 && vee && e.redline < 7500;
  const firing: Firing[] = [];
  const crossBanks = [0, 1, 1, 0, 1, 0, 0, 1];
  for (let k = 0; k < n; k++) {
    const bank = !vee ? 0 : crossPlane ? crossBanks[k] : k % 2;
    // Cylinders are never quite equal; cross-plane banks differ more.
    const amp = 1 + 0.06 * rng.signed() + (crossPlane ? (bank ? 0.1 : -0.1) : 0);
    firing.push({ at: k / n, bank, amp });
  }
  const disp = Math.max(0.6, e.displacement);
  const sporty = { super: 1, hyper: 1, sports: 0.8, hothatch: 0.75, rally: 0.8, gt: 0.6, muscle: 0.55, classic: 0.45, compact: 0.25, offroad: 0.3, truck: 0.3, suv: 0.35 }[spec.category] ?? 0.5;
  return {
    electric: e.aspiration === 'electric' || n === 0,
    firing,
    boom: Math.max(55, Math.min(150, 150 - 13 * (disp - 1))),
    honk: Math.max(300, 660 - 38 * disp),
    rasp: 1500 + 1500 * sporty,
    bankDetune: vee ? 0.07 : 0,
    muffler: 900 + 3000 * sporty,
    edge: sporty,
    idle: e.idle,
    limiter: e.limiter,
    turbo: e.aspiration === 'turbo' || e.aspiration === 'twinturbo',
    poles: 4,
    seed: hashId(spec.id),
  };
}

/** The ladder of loop speeds: idle, then even steps to the limiter (about 900 rpm apart). */
export function rpmLadder(v: EngineVoice): number[] {
  const lo = v.idle;
  const hi = v.limiter * 1.02;
  const steps = Math.max(3, Math.round((hi - lo) / 900));
  const out: number[] = [];
  for (let i = 0; i <= steps; i++) out.push(lo + ((hi - lo) * i) / steps);
  return out;
}

/** One combustion engine loop at `rpm`, on or off the throttle. */
export function renderEngineLoop(v: EngineVoice, rpm: number, onLoad: boolean, sr: number, seconds = 0.7): EngineLoop {
  const cycleHz = rpm / 120; // four-stroke: one cycle per two revolutions
  const cycles = Math.max(3, Math.round(seconds * cycleHz));
  const period = Math.round((cycles / cycleHz) * sr);
  const effRpm = ((cycles * sr) / period) * 120;
  const cyc = period / cycles;
  const rng = new AudioRng(v.seed ^ Math.round(rpm) ^ (onLoad ? 0xa11 : 0x0ff));
  const load = onLoad ? 1 : 0.3;
  const rev = Math.min(1, rpm / v.limiter);
  // Pulse rise time: sharper on load and at speed.
  const rise = ((onLoad ? 0.32 : 0.75) / 1000) * sr * (1.15 - 0.35 * rev);
  const burstDecay = (2.2 / 1000) * sr;
  const burstAmp = (0.15 + 0.5 * v.edge) * (onLoad ? 1 : 0.35);
  // The events of one period; the excitation repeats them exactly each period.
  interface Ev {
    t: number;
    bank: number;
    amp: number;
    noiseSeed: number;
  }
  const events: Ev[] = [];
  const idleWobble = 1 - rev;
  for (let c = 0; c < cycles; c++) {
    for (const f of v.firing) {
      const jitter = rng.gauss() * (0.004 + 0.01 * idleWobble) * (cyc / v.firing.length);
      const amp = f.amp * load * (1 + rng.gauss() * (0.03 + 0.07 * idleWobble));
      events.push({ t: (c + f.at) * cyc + jitter, bank: f.bank, amp, noiseSeed: rng.int(1 << 30) });
    }
  }
  const banks = v.firing.some((f) => f.bank === 1) ? 2 : 1;
  const exc = [new Float32Array(period * 3), new Float32Array(period * 3)];
  const pulseLen = Math.ceil(rise * 10);
  const burstLen = Math.ceil(burstDecay * 6);
  for (let pass = 0; pass < 3; pass++) {
    for (const ev of events) {
      const buf = exc[ev.bank];
      const t0 = ev.t + pass * period;
      const i0 = Math.floor(t0);
      const nrng = new AudioRng(ev.noiseSeed);
      for (let j = 0; j < Math.max(pulseLen, burstLen); j++) {
        const i = i0 + j;
        if (i < 0 || i >= buf.length) continue;
        const tau = i - t0;
        if (tau < 0) continue;
        let s = 0;
        if (j < pulseLen) {
          const x = tau / rise;
          s += x * Math.exp(1 - x);
        }
        if (j < burstLen) s += burstAmp * nrng.signed() * Math.exp(-tau / burstDecay);
        buf[i] += ev.amp * s;
      }
    }
  }
  const data = renderPeriodic(period, (_p, out) => {
    for (let b = 0; b < banks; b++) {
      const d = b === 0 ? 1 - v.bankDetune * 0.5 : 1 + v.bankDetune * 0.5;
      const boom = Biquad.bandpass(sr, v.boom * d, 1.3);
      const honk = Biquad.bandpass(sr, v.honk * d, 2.4);
      const rasp = Biquad.bandpass(sr, v.rasp * d, 1.1);
      const muff = Biquad.lowpass(sr, v.muffler * (onLoad ? 1 : 0.6), 0.8);
      const raspGain = (0.12 + 0.55 * v.edge) * (onLoad ? 1 : 0.4);
      const e = exc[b];
      for (let i = 0; i < out.length; i++) {
        const x = e[i];
        out[i] += 1.0 * boom.process(x) + 0.55 * honk.process(x) + raspGain * rasp.process(x) + 0.35 * muff.process(x);
      }
    }
    // Remove DC and rumble below hearing, then saturate a little: loud engines are not clean.
    const dc = Biquad.highpass(sr, 28, 0.7);
    const drive = onLoad ? 1.6 + 1.2 * v.edge : 1.1;
    let level = 0;
    for (let i = 0; i < out.length; i++) level += out[i] * out[i];
    level = Math.sqrt(level / out.length) || 1;
    for (let i = 0; i < out.length; i++) out[i] = softClip((dc.process(out[i]) / level) * 0.35 * drive) / drive;
  });
  normalize(data, 0.25);
  return { rpm: effRpm, onLoad, data };
}

/** An electric motor loop at `rpm`: motor harmonics, a gear whine and a little hiss. */
export function renderElectricLoop(v: EngineVoice, rpm: number, sr: number, seconds = 0.5): EngineLoop {
  const f1 = (rpm / 60) * v.poles;
  const k = Math.max(1, Math.round(seconds * f1));
  const period = Math.round((k * sr) / f1);
  const fEff = (k * sr) / period;
  const effRpm = (fEff * 60) / v.poles;
  const rng = new AudioRng(v.seed ^ 0xe1ec);
  const partials = [
    [1, 1],
    [2, 0.45],
    [3, 0.22],
    [6, 0.12],
    [5, 0.18], // the reduction gear
  ];
  const hiss = Biquad.bandpass(sr, 5000, 0.8);
  const data = new Float32Array(period);
  for (let i = 0; i < period; i++) {
    let s = 0;
    for (const [h, a] of partials) s += a * Math.sin((2 * Math.PI * h * k * i) / period);
    data[i] = s + 0.05 * hiss.process(rng.signed());
  }
  normalize(data, 0.2);
  return { rpm: effRpm, onLoad: true, data };
}

/** Every loop a car needs: the rpm ladder on and off the throttle (or one motor loop). */
export function renderEngineSet(spec: CarSpec, sr: number): EngineSoundSet {
  const voice = engineVoice(spec);
  if (voice.electric) {
    // Rendered at 9000 rpm: played from a crawl to 22000 rpm, its rate stays inside what Unity can pitch (3x).
    const loop = renderElectricLoop(voice, 9000, sr);
    return { voice, sampleRate: sr, on: [loop], off: [loop] };
  }
  const ladder = rpmLadder(voice);
  return {
    voice,
    sampleRate: sr,
    on: ladder.map((r) => renderEngineLoop(voice, r, true, sr)),
    off: ladder.map((r) => renderEngineLoop(voice, r, false, sr)),
  };
}

export interface LoopMix {
  /** Index of the lower loop, the blend toward the upper one (0..1), and both playback rates. */
  lo: number;
  hi: number;
  t: number;
  rateLo: number;
  rateHi: number;
}

/** Which two loops play at `rpm`, how they blend and how fast each plays. */
export function loopMix(loops: readonly { rpm: number }[], rpm: number, out: LoopMix): LoopMix {
  const n = loops.length;
  if (n === 1 || rpm <= loops[0].rpm) {
    out.lo = out.hi = 0;
    out.t = 0;
  } else if (rpm >= loops[n - 1].rpm) {
    out.lo = out.hi = n - 1;
    out.t = 0;
  } else {
    let i = 0;
    while (i < n - 2 && loops[i + 1].rpm <= rpm) i++;
    out.lo = i;
    out.hi = i + 1;
    out.t = (rpm - loops[i].rpm) / (loops[i + 1].rpm - loops[i].rpm);
  }
  out.rateLo = Math.max(0.05, rpm / loops[out.lo].rpm);
  out.rateHi = Math.max(0.05, rpm / loops[out.hi].rpm);
  return out;
}

/** A one-pole smoother that runs at the audio update rate (for gains and rates). */
export class Smooth {
  value: number;
  constructor(v = 0) {
    this.value = v;
  }
  step(target: number, dt: number, tau: number): number {
    this.value += (target - this.value) * Math.min(1, dt / Math.max(1e-4, tau));
    return this.value;
  }
}
