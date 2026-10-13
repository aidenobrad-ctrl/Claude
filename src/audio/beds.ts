// The rest of the car's sound, synthesized once: looping beds (tire squeal,
// road roar, gravel crunch, grass swish, snow crunch, spray, wind) that the
// player pitches and fades with speed and slip, and one-shots (impacts,
// blow-off, overrun crackle, gear shift, UI blips).
import { AudioRng, Biquad, BrownNoise, OnePole, PinkNoise, crossfadeLoop, fadeEdges, normalize, softClip } from './synth';

export type BedId = 'squeal' | 'road' | 'gravel' | 'grass' | 'snow' | 'spray' | 'wind' | 'intake' | 'turbo';
export type ShotId = 'impactLight' | 'impactMedium' | 'impactHeavy' | 'blowoff' | 'crackle' | 'shift' | 'scrape';
export type UiId = 'move' | 'select' | 'back' | 'error' | 'count' | 'go' | 'reward' | 'photo' | 'checkpoint';

const LOOP = 2.0;
const FADE = 0.2;

function bedBuffer(sr: number, seed: number, gen: (rng: AudioRng, out: Float32Array) => void): Float32Array {
  const rng = new AudioRng(seed);
  const raw = new Float32Array(Math.round((LOOP + FADE) * sr));
  gen(rng, raw);
  return crossfadeLoop(raw, Math.round(FADE * sr));
}

/** A looping bed: unit-ish level, seamless. */
export function renderBed(id: BedId, sr: number): Float32Array {
  switch (id) {
    case 'squeal':
      // Tire squeal: a few narrow, wandering bands around a kilohertz over a little hiss.
      return normalize(
        bedBuffer(sr, 11, (rng, out) => {
          const bands = [
            [760, 30, 1],
            [1130, 34, 0.7],
            [1520, 40, 0.45],
            [2290, 30, 0.2],
          ].map(([f, q, g]) => ({ f, g, bq: Biquad.bandpass(sr, f, q), lfo: rng.next() * 6 }));
          const hiss = Biquad.bandpass(sr, 3000, 0.7);
          for (let i = 0; i < out.length; i++) {
            const t = i / sr;
            if (i % 64 === 0) for (const b of bands) b.bq.setBandpass(sr, b.f * (1 + 0.025 * Math.sin(t * 7.3 + b.lfo) + 0.012 * Math.sin(t * 23 + b.lfo * 2)), 32);
            const w = rng.signed();
            let s = 0;
            for (const b of bands) s += b.g * b.bq.process(w);
            out[i] = s * 6 + 0.08 * hiss.process(rng.signed()) * (0.8 + 0.2 * Math.sin(t * 13));
          }
        }),
        0.25,
      );
    case 'road':
      // Tire roar on asphalt: brown noise with a hump around 120 Hz and some tread whine.
      return normalize(
        bedBuffer(sr, 12, (rng, out) => {
          const br = new BrownNoise(rng);
          const hump = Biquad.peaking(sr, 120, 1, 9);
          const lp = Biquad.lowpass(sr, 900, 0.7);
          const tread = Biquad.bandpass(sr, 520, 6);
          for (let i = 0; i < out.length; i++) out[i] = lp.process(hump.process(br.next())) + 0.15 * tread.process(rng.signed());
        }),
        0.25,
      );
    case 'gravel':
      // Stones popping under the tread: dense random grains through a crunchy band.
      return normalize(
        bedBuffer(sr, 13, (rng, out) => {
          const bp = Biquad.bandpass(sr, 1800, 0.6);
          const lo = Biquad.lowpass(sr, 420, 0.7);
          let env = 0;
          for (let i = 0; i < out.length; i++) {
            if (rng.next() < 0.004) env = 0.4 + rng.next() * 0.9;
            env *= 0.993;
            const n = rng.signed();
            out[i] = bp.process(n * env) * 2.2 + lo.process(n) * 0.25;
          }
        }),
        0.25,
      );
    case 'grass':
      return normalize(
        bedBuffer(sr, 14, (rng, out) => {
          const pink = new PinkNoise(rng);
          const bp = Biquad.bandpass(sr, 1500, 0.5);
          for (let i = 0; i < out.length; i++) {
            const t = i / sr;
            out[i] = bp.process(pink.next()) * (0.7 + 0.3 * Math.sin(t * 9 + Math.sin(t * 2.3) * 2));
          }
        }),
        0.25,
      );
    case 'snow':
      return normalize(
        bedBuffer(sr, 15, (rng, out) => {
          const lp = Biquad.lowpass(sr, 1200, 0.9);
          let env = 0;
          for (let i = 0; i < out.length; i++) {
            if (rng.next() < 0.002) env = 0.6 + rng.next() * 0.6;
            env *= 0.997;
            out[i] = lp.process(rng.signed() * (0.3 + env));
          }
        }),
        0.25,
      );
    case 'spray':
      return normalize(
        bedBuffer(sr, 16, (rng, out) => {
          const hp = Biquad.highpass(sr, 2500, 0.7);
          const pink = new PinkNoise(rng);
          for (let i = 0; i < out.length; i++) out[i] = hp.process(pink.next()) * (0.85 + 0.15 * Math.sin((i / sr) * 17));
        }),
        0.25,
      );
    case 'wind':
      // Wind over the body: pink noise, low-passed, slowly gusting.
      return normalize(
        bedBuffer(sr, 17, (rng, out) => {
          const pink = new PinkNoise(rng);
          const lp = new OnePole(sr, 700);
          const bp = Biquad.bandpass(sr, 380, 0.9);
          for (let i = 0; i < out.length; i++) {
            const t = i / sr;
            const gust = 0.75 + 0.25 * Math.sin(t * 1.7) * Math.sin(t * 0.9 + 1);
            const n = pink.next();
            out[i] = (lp.process(n) * 0.8 + bp.process(n) * 0.6) * gust;
          }
        }),
        0.25,
      );
    case 'intake':
      // Air rushing into the engine: band-limited noise the player tunes with rpm.
      return normalize(
        bedBuffer(sr, 18, (rng, out) => {
          const bp = Biquad.bandpass(sr, 700, 0.9);
          const pink = new PinkNoise(rng);
          for (let i = 0; i < out.length; i++) out[i] = bp.process(pink.next());
        }),
        0.25,
      );
    case 'turbo': {
      // A turbo's whistle: a sine near 3.2 kHz with a little noise; pitched with boost.
      const f = 3200;
      const cycles = Math.round(LOOP * f);
      const n = Math.round((cycles * sr) / f);
      const out = new Float32Array(n);
      const rng = new AudioRng(19);
      const bp = Biquad.bandpass(sr, 4200, 2);
      for (let i = 0; i < n; i++) out[i] = Math.sin((2 * Math.PI * cycles * i) / n) * 0.6 + 0.4 * Math.sin((4 * Math.PI * cycles * i) / n) * 0.25 + 0.3 * bp.process(rng.signed());
      return normalize(out, 0.25);
    }
  }
}

/** A one-shot effect. */
export function renderShot(id: ShotId, sr: number, variant = 0): Float32Array {
  const rng = new AudioRng(100 + variant * 31 + id.length * 7);
  const len = (s: number): Float32Array => new Float32Array(Math.round(s * sr));
  switch (id) {
    case 'impactLight':
    case 'impactMedium':
    case 'impactHeavy': {
      const heavy = id === 'impactHeavy' ? 1 : id === 'impactMedium' ? 0.55 : 0.2;
      const out = len(0.35 + 0.9 * heavy);
      const thump = 55 + 35 * (1 - heavy) + variant * 6;
      const metal = [Biquad.bandpass(sr, 900 + variant * 140, 6), Biquad.bandpass(sr, 1730 + variant * 90, 9), Biquad.bandpass(sr, 2950, 12)];
      const crunch = Biquad.bandpass(sr, 2400, 0.6);
      let grit = 0;
      for (let i = 0; i < out.length; i++) {
        const t = i / sr;
        const body = Math.sin(2 * Math.PI * thump * t * (1 + 0.6 * Math.exp(-t / 0.02))) * Math.exp(-t / (0.06 + 0.08 * heavy));
        if (rng.next() < 0.02 * heavy) grit = 1;
        grit *= 0.996;
        const n = rng.signed();
        const ring = (metal[0].process(n) + metal[1].process(n) * 0.7 + metal[2].process(n) * 0.4) * Math.exp(-t / (0.12 + 0.35 * heavy)) * 3;
        const cr = crunch.process(n) * (Math.exp(-t / (0.05 + 0.15 * heavy)) + grit * 0.5 * heavy);
        out[i] = softClip(body * 1.4 + ring * (0.4 + heavy) + cr * (0.5 + heavy));
      }
      return normalize(fadeEdges(out, sr, 0.5, 30), 0.22, 0.95);
    }
    case 'blowoff': {
      // The blow-off valve on a lift: a falling chirp then a hiss.
      const out = len(0.45);
      const hp = Biquad.highpass(sr, 1800, 0.7);
      const bp = Biquad.bandpass(sr, 3500, 1.5);
      for (let i = 0; i < out.length; i++) {
        const t = i / sr;
        bp.setBandpass(sr, 3800 - 1800 * Math.min(1, t / 0.3), 1.5);
        const n = rng.signed();
        out[i] = (bp.process(n) * 1.5 + hp.process(n) * 0.3) * Math.min(1, t / 0.01) * Math.exp(-t / 0.12);
      }
      return normalize(fadeEdges(out, sr), 0.18);
    }
    case 'crackle': {
      // Overrun: two or three sharp pops in the exhaust.
      const out = len(0.3);
      const bp = Biquad.bandpass(sr, 600 + variant * 120, 0.9);
      const pops = 2 + rng.int(2);
      for (let k = 0; k < pops; k++) {
        const at = Math.round((0.02 + rng.next() * 0.2) * sr);
        for (let j = 0; j < sr * 0.03 && at + j < out.length; j++) out[at + j] += rng.signed() * Math.exp(-j / (sr * 0.006)) * (0.6 + rng.next() * 0.4);
      }
      for (let i = 0; i < out.length; i++) out[i] = softClip(bp.process(out[i]) * 3 + out[i] * 0.2);
      return normalize(fadeEdges(out, sr), 0.2);
    }
    case 'shift': {
      const out = len(0.12);
      const bp = Biquad.bandpass(sr, 420, 3);
      for (let i = 0; i < out.length; i++) {
        const t = i / sr;
        out[i] = bp.process(rng.signed()) * Math.exp(-t / 0.025) + Math.sin(2 * Math.PI * 140 * t) * Math.exp(-t / 0.02) * 0.4;
      }
      return normalize(fadeEdges(out, sr), 0.15);
    }
    case 'scrape': {
      // Bodywork dragging along a wall or rail: a gritty band, looped by the player.
      const out = len(1.2);
      const bp = Biquad.bandpass(sr, 1400, 1.2);
      const bp2 = Biquad.bandpass(sr, 3100, 4);
      let env = 0.5;
      for (let i = 0; i < out.length; i++) {
        if (rng.next() < 0.003) env = 0.4 + rng.next() * 0.6;
        const n = rng.signed();
        out[i] = (bp.process(n) + bp2.process(n) * 0.6) * env;
      }
      return normalize(crossfadeLoop(out, Math.round(0.15 * sr)), 0.2);
    }
  }
}

/** Menu and HUD sounds: short tuned blips. */
export function renderUi(id: UiId, sr: number): Float32Array {
  const tone = (out: Float32Array, at: number, f: number, dur: number, gain: number, bright = 0.3): void => {
    const a = Math.round(at * sr);
    const n = Math.round(dur * sr);
    for (let i = 0; i < n && a + i < out.length; i++) {
      const t = i / sr;
      const env = Math.min(1, t / 0.003) * Math.exp(-t / (dur * 0.35));
      out[a + i] += gain * env * (Math.sin(2 * Math.PI * f * t) + bright * Math.sin(4 * Math.PI * f * t) * Math.exp(-t / 0.03));
    }
  };
  const out = new Float32Array(Math.round(0.7 * sr));
  switch (id) {
    case 'move':
      tone(out, 0, 1760, 0.05, 0.5, 0.2);
      break;
    case 'select':
      tone(out, 0, 1320, 0.08, 0.5);
      tone(out, 0.06, 1980, 0.12, 0.5);
      break;
    case 'back':
      tone(out, 0, 1320, 0.08, 0.5);
      tone(out, 0.06, 990, 0.12, 0.45);
      break;
    case 'error':
      tone(out, 0, 330, 0.12, 0.6, 0.6);
      tone(out, 0.1, 311, 0.18, 0.6, 0.6);
      break;
    case 'count':
      tone(out, 0, 880, 0.25, 0.6, 0.1);
      break;
    case 'go':
      tone(out, 0, 1760, 0.5, 0.6, 0.1);
      break;
    case 'checkpoint':
      tone(out, 0, 1568, 0.12, 0.45);
      tone(out, 0.07, 2093, 0.2, 0.45);
      break;
    case 'reward':
      [0, 0.08, 0.16, 0.24].forEach((at, i) => tone(out, at, [1047, 1319, 1568, 2093][i], 0.35, 0.4));
      break;
    case 'photo': {
      const rng = new AudioRng(5);
      const hp = Biquad.highpass(sr, 2000, 0.7);
      for (let i = 0; i < 0.06 * sr; i++) out[i] += hp.process(rng.signed()) * Math.exp(-i / (0.008 * sr)) * 0.8;
      for (let i = 0; i < 0.05 * sr; i++) out[Math.round(0.07 * sr) + i] += hp.process(rng.signed()) * Math.exp(-i / (0.006 * sr)) * 0.6;
      break;
    }
  }
  return normalize(fadeEdges(out, sr, 0.5, 20), 0.1, 0.8);
}
