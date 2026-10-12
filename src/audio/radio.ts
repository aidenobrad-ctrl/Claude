// Three generative radio stations. Each one composes endless songs from a
// seed (key, tempo, chord progression, arrangement, motifs) and renders them
// a bar at a time into stereo sample buffers, with synthesized drums, bass,
// chords and leads, a reverb and a limiter. The renderer is plain sample
// code (no Web Audio nodes) so it runs in a worker here and in C# in Unity.
//
//   Halcyon Pulse   synthwave: four on the floor, gated pads, arpeggios
//   Coastline FM    lo-fi: swung drums, electric piano sevenths, vinyl hiss
//   Redline Radio   rock: power chords through a driven amp, big drums
import { AudioRng, Biquad, OnePole, softClip } from './synth';

export type StationId = 'pulse' | 'coast' | 'redline';

export interface StationInfo {
  id: StationId;
  name: string;
  tagline: string;
}

export const STATIONS: readonly StationInfo[] = [
  { id: 'pulse', name: 'Halcyon Pulse', tagline: 'synthwave all night' },
  { id: 'coast', name: 'Coastline FM', tagline: 'low tides, lo-fi' },
  { id: 'redline', name: 'Redline Radio', tagline: 'loud and fast' },
];

const midiHz = (m: number): number => 440 * Math.pow(2, (m - 69) / 12);

const SCALES: Record<string, number[]> = {
  minor: [0, 2, 3, 5, 7, 8, 10],
  major: [0, 2, 4, 5, 7, 9, 11],
  dorian: [0, 2, 3, 5, 7, 9, 10],
  mixolydian: [0, 2, 4, 5, 7, 9, 10],
};

/** Scale degree (any integer, wraps by octaves) to a semitone offset. */
function degree(scale: number[], d: number): number {
  const n = scale.length;
  const o = Math.floor(d / n);
  return scale[((d % n) + n) % n] + 12 * o;
}

const TITLE_A = ['Neon', 'Coastal', 'Midnight', 'Golden', 'Harbour', 'Summit', 'Velvet', 'Static', 'Salt', 'Chrome', 'Hollow', 'Paper', 'Ember', 'Northern', 'Long', 'Quiet', 'Open', 'Silver', 'Last', 'Wild'];
const TITLE_B = ['Drift', 'Lights', 'Horizon', 'Overpass', 'Tide', 'Signal', 'Run', 'Heat', 'Coast', 'Machine', 'Hours', 'Weather', 'Line', 'Road', 'Summer', 'Sky', 'Engine', 'Shore', 'Echo', 'Miles'];
const ARTISTS: Record<StationId, string[]> = {
  pulse: ['Laser Harbour', 'NightCity 86', 'Vector Bloom', 'Polar Grid', 'Sunset Kinetic'],
  coast: ['tidepool', 'slow ferry', 'mono lagoon', 'kettle & reed', 'paper boats'],
  redline: ['The Torque Brothers', 'Gravel Saints', 'Red Mile', 'Overboost', 'Fourth Gear'],
};

// ---------------------------------------------------------------------------
// Instruments: each note is a small object that renders itself into a window
// of the output and keeps its own state across bars.

interface Note {
  /** Absolute start sample. */
  start: number;
  /** Render samples [from, to) (absolute) into l/r at offset `base`; false once finished. */
  render(l: Float32Array, r: Float32Array, base: number, from: number, to: number, fx: FxSends): boolean;
}

/** Reverb and delay send buffers for the current bar. */
interface FxSends {
  rev: Float32Array;
  dly: Float32Array;
}

function panGains(pan: number): [number, number] {
  const a = (pan + 1) * 0.25 * Math.PI;
  return [Math.cos(a), Math.sin(a)];
}

abstract class Voice implements Note {
  protected t = 0; // samples since start
  protected gl: number;
  protected gr: number;
  constructor(
    readonly start: number,
    protected sr: number,
    protected len: number,
    pan: number,
    protected rev: number,
    protected dly = 0,
  ) {
    [this.gl, this.gr] = panGains(pan);
  }
  protected abstract sample(t: number): number;
  render(l: Float32Array, r: Float32Array, base: number, from: number, to: number, fx: FxSends): boolean {
    const a = Math.max(from, this.start + this.t);
    for (let i = a; i < to; i++) {
      if (this.t >= this.len) return false;
      const s = this.sample(this.t++);
      const k = i - base;
      l[k] += s * this.gl;
      r[k] += s * this.gr;
      fx.rev[k] += s * this.rev;
      if (this.dly) fx.dly[k] += s * this.dly;
    }
    return this.t < this.len;
  }
}

class Kick extends Voice {
  private ph = 0;
  constructor(start: number, sr: number, private vel: number, private punch = 1) {
    super(start, sr, Math.round(sr * 0.45), 0, 0.04);
  }
  protected sample(t: number): number {
    const s = t / this.sr;
    const f = 44 + 120 * this.punch * Math.exp(-s / 0.028);
    this.ph += f / this.sr;
    const body = Math.sin(2 * Math.PI * this.ph) * Math.exp(-s / 0.26);
    const click = s < 0.002 ? (1 - s / 0.002) * 0.5 : 0;
    return this.vel * 0.9 * softClip((body + click) * 1.4);
  }
}

class NoiseHit extends Voice {
  private rng: AudioRng;
  private f1: Biquad;
  private f2: Biquad | null;
  constructor(
    start: number,
    sr: number,
    private vel: number,
    private decay: number,
    hp: number,
    bp: number,
    pan: number,
    rev: number,
    private tone = 0,
    seed = 1,
  ) {
    super(start, sr, Math.round(sr * Math.min(2.5, decay * 7)), pan, rev);
    this.rng = new AudioRng(seed);
    this.f1 = Biquad.highpass(sr, hp, 0.7);
    this.f2 = bp > 0 ? Biquad.bandpass(sr, bp, 0.8) : null;
  }
  protected sample(t: number): number {
    const s = t / this.sr;
    let n = this.f1.process(this.rng.signed());
    if (this.f2) n = this.f2.process(n) * 1.6;
    let v = n * Math.exp(-s / this.decay);
    if (this.tone) v += Math.sin(2 * Math.PI * this.tone * s) * Math.exp(-s / 0.06) * 0.6;
    return v * this.vel;
  }
}

/** Band-limited saw (polyBLEP). */
function blepSaw(ph: number, dt: number): number {
  let v = 2 * ph - 1;
  if (ph < dt) {
    const x = ph / dt;
    v -= x + x - x * x - 1;
  } else if (ph > 1 - dt) {
    const x = (ph - 1) / dt;
    v -= x * x + x + x + 1;
  }
  return v;
}

function blepSquare(ph: number, dt: number): number {
  const half = ph < 0.5 ? 1 : -1;
  let v = half;
  const p2 = (ph + 0.5) % 1;
  // Rising edge at 0, falling edge at 0.5.
  if (ph < dt) {
    const x = ph / dt;
    v += x + x - x * x - 1;
  } else if (ph > 1 - dt) {
    const x = (ph - 1) / dt;
    v += x * x + x + x + 1;
  }
  if (p2 < dt) {
    const x = p2 / dt;
    v -= x + x - x * x - 1;
  } else if (p2 > 1 - dt) {
    const x = (p2 - 1) / dt;
    v -= x * x + x + x + 1;
  }
  return v;
}

/** An envelope: attack, then hold for `hold` samples, then release; or a pure decay. */
function adsr(t: number, sr: number, a: number, hold: number, rel: number): number {
  const s = t / sr;
  if (s < a) return s / a;
  if (t < hold) return 1;
  return Math.exp(-(t - hold) / (rel * sr));
}

class Synth extends Voice {
  private ph: number[];
  private lp: Biquad;
  private hp: Biquad;
  private hold: number;
  private vibStart: number;
  constructor(
    start: number,
    sr: number,
    holdSec: number,
    private freqs: number[],
    private vel: number,
    private wave: 'saw' | 'square' | 'sine' | 'tri',
    private attack: number,
    private release: number,
    private cutoff: number,
    private cutoffEnv: number,
    private cutoffDecay: number,
    private detune: number,
    pan: number,
    rev: number,
    dly = 0,
    private vibrato = 0,
    private drive = 0,
  ) {
    super(start, sr, Math.round(sr * (holdSec + release * 6)), pan, rev, dly);
    this.ph = freqs.map((_, i) => (i * 0.37) % 1);
    this.lp = Biquad.lowpass(sr, cutoff, 0.9);
    this.hp = Biquad.highpass(sr, 60, 0.7);
    this.hold = Math.round(sr * holdSec);
    this.vibStart = sr * 0.18;
  }
  protected sample(t: number): number {
    const sr = this.sr;
    let s = 0;
    const vib = this.vibrato && t > this.vibStart ? 1 + this.vibrato * Math.sin((2 * Math.PI * 5.2 * t) / sr) * Math.min(1, (t - this.vibStart) / (sr * 0.3)) : 1;
    for (let i = 0; i < this.freqs.length; i++) {
      const f = this.freqs[i] * vib * (1 + this.detune * (i % 3 === 0 ? 0 : i % 3 === 1 ? 1 : -1));
      const dt = f / sr;
      this.ph[i] = (this.ph[i] + dt) % 1;
      const p = this.ph[i];
      s += this.wave === 'saw' ? blepSaw(p, dt) : this.wave === 'square' ? blepSquare(p, dt) : this.wave === 'tri' ? 1 - 4 * Math.abs(p - 0.5) : Math.sin(2 * Math.PI * p);
    }
    s /= Math.sqrt(this.freqs.length);
    if (this.drive) s = softClip(s * this.drive) * 0.7;
    if (t % 32 === 0) {
      const c = this.cutoff + this.cutoffEnv * Math.exp(-t / (this.cutoffDecay * sr));
      this.lp.setLowpass(sr, Math.min(sr * 0.45, c), 0.9);
    }
    s = this.lp.process(s);
    s = this.hp.process(s);
    return s * this.vel * adsr(t, sr, this.attack, this.hold, this.release);
  }
}

class EPiano extends Voice {
  private hold: number;
  constructor(
    start: number,
    sr: number,
    holdSec: number,
    private f: number,
    private vel: number,
    pan: number,
    rev: number,
  ) {
    super(start, sr, Math.round(sr * (holdSec + 1.6)), pan, rev);
    this.hold = Math.round(sr * holdSec);
  }
  protected sample(t: number): number {
    const s = t / this.sr;
    const idx = 1.6 * Math.exp(-s / 0.35) + 0.25;
    const mod = Math.sin(2 * Math.PI * this.f * s) * idx;
    const car = Math.sin(2 * Math.PI * this.f * s + mod);
    const bell = Math.sin(2 * Math.PI * this.f * 4.01 * s) * 0.12 * Math.exp(-s / 0.08);
    const env = Math.exp(-s / 1.4) * (t < this.hold ? 1 : Math.exp(-(t - this.hold) / (0.25 * this.sr)));
    const trem = 1 + 0.18 * Math.sin(2 * Math.PI * 4.6 * s);
    return (car + bell) * env * trem * this.vel * Math.min(1, s / 0.004);
  }
}

// ---------------------------------------------------------------------------
// Effects with state across bars.

class Comb {
  private buf: Float32Array;
  private i = 0;
  private lp = 0;
  constructor(
    len: number,
    private fb: number,
    private damp: number,
  ) {
    this.buf = new Float32Array(len);
  }
  process(x: number): number {
    const y = this.buf[this.i];
    this.lp = y * (1 - this.damp) + this.lp * this.damp;
    this.buf[this.i] = x + this.lp * this.fb;
    this.i = (this.i + 1) % this.buf.length;
    return y;
  }
}

class Allpass {
  private buf: Float32Array;
  private i = 0;
  constructor(len: number) {
    this.buf = new Float32Array(len);
  }
  process(x: number): number {
    const b = this.buf[this.i];
    const y = -x + b;
    this.buf[this.i] = x + b * 0.5;
    this.i = (this.i + 1) % this.buf.length;
    return y;
  }
}

/** A small Freeverb-style stereo reverb. */
class Reverb {
  private cl: Comb[];
  private cr: Comb[];
  private al: Allpass[];
  private ar: Allpass[];
  constructor(sr: number, size: number, damp: number) {
    const k = sr / 44100;
    const combs = [1116, 1188, 1277, 1356];
    this.cl = combs.map((c) => new Comb(Math.round(c * k), size, damp));
    this.cr = combs.map((c) => new Comb(Math.round((c + 23) * k), size, damp));
    this.al = [556, 441].map((a) => new Allpass(Math.round(a * k)));
    this.ar = [579, 464].map((a) => new Allpass(Math.round(a * k)));
  }
  process(x: number, out: [number, number]): void {
    let l = 0;
    let r = 0;
    for (const c of this.cl) l += c.process(x);
    for (const c of this.cr) r += c.process(x);
    for (const a of this.al) l = a.process(l);
    for (const a of this.ar) r = a.process(r);
    out[0] = l * 0.25;
    out[1] = r * 0.25;
  }
}

/** Ping-pong delay. */
class Delay {
  private bl: Float32Array;
  private br: Float32Array;
  private i = 0;
  constructor(
    len: number,
    private fb: number,
  ) {
    this.bl = new Float32Array(len);
    this.br = new Float32Array(len);
  }
  process(x: number, out: [number, number]): void {
    const l = this.bl[this.i];
    const r = this.br[this.i];
    this.bl[this.i] = x + r * this.fb;
    this.br[this.i] = l * this.fb;
    this.i = (this.i + 1) % this.bl.length;
    out[0] = l;
    out[1] = r;
  }
}

// ---------------------------------------------------------------------------
// Composition.

interface Section {
  name: string;
  bars: number;
  /** 0 = sparse ... 3 = everything. */
  energy: number;
}

interface Song {
  title: string;
  artist: string;
  bpm: number;
  root: number;
  scale: number[];
  prog: number[];
  sections: Section[];
  /** A two-bar motif: [16th step, length in 16ths, scale degree offset]. */
  motif: [number, number, number][];
  /** The chorus's own motif (the hook). */
  hook: [number, number, number][];
  bassPattern: number[];
  seed: number;
}

/** A motif of 4 to 7 notes over two bars, on chord-friendly degrees. */
function makeMotif(r: AudioRng): [number, number, number][] {
  const motif: [number, number, number][] = [];
  let step = r.int(3) * 2;
  const count = 4 + r.int(4);
  let deg = r.pick([0, 2, 4]);
  for (let i = 0; i < count && step < 30; i++) {
    const len = r.pick([2, 2, 3, 4, 4, 6]);
    motif.push([step, len, deg]);
    step += len + (r.next() < 0.3 ? 2 : 0);
    deg += r.pick([-2, -1, 1, 1, 2, 3, -3]);
    deg = Math.max(-2, Math.min(7, deg));
  }
  return motif;
}

export interface BarInfo {
  station: StationId;
  title: string;
  artist: string;
  /** True on the first bar of a song (and on the station ident). */
  newSong: boolean;
  ident: boolean;
}

export class RadioStation {
  readonly info: StationInfo;
  private rng: AudioRng;
  private song!: Song;
  private bar = 0;
  private sectionIndex = 0;
  private sectionBar = 0;
  private notes: Note[] = [];
  /** Absolute sample of the next bar's start. */
  private clock = 0;
  private reverb: Reverb;
  private delay: Delay;
  private songCount = 0;
  private identPending = true;
  private crackle: AudioRng;
  private crackleLp: OnePole;
  private hissHp: Biquad;
  private lim = 1;
  private master = [0, 0] as [number, number];
  private tmp: [number, number] = [0, 0];

  constructor(
    readonly id: StationId,
    readonly sr: number,
    seed: number,
  ) {
    this.info = STATIONS.find((s) => s.id === id)!;
    this.rng = new AudioRng(seed ^ (id === 'pulse' ? 0x9a1 : id === 'coast' ? 0xc0a : 0x7ed));
    const size = id === 'coast' ? 0.8 : id === 'pulse' ? 0.84 : 0.72;
    this.reverb = new Reverb(sr, size, 0.35);
    this.delay = new Delay(Math.round(sr * 0.3), 0.38);
    this.crackle = new AudioRng(seed + 77);
    this.crackleLp = new OnePole(sr, 3000);
    this.hissHp = Biquad.highpass(sr, 4000, 0.7);
    this.newSong();
  }

  private newSong(): void {
    const r = this.rng;
    const id = this.id;
    const bpm = id === 'pulse' ? 104 + r.int(16) : id === 'coast' ? 74 + r.int(14) : 132 + r.int(22);
    const scale = id === 'pulse' ? SCALES.minor : id === 'coast' ? r.pick([SCALES.dorian, SCALES.major]) : r.pick([SCALES.minor, SCALES.mixolydian]);
    const progs =
      id === 'pulse'
        ? [[0, 5, 2, 6], [0, 5, 3, 4], [0, 3, 5, 4], [5, 6, 0, 0], [0, 6, 5, 6]]
        : id === 'coast'
          ? [[1, 4, 0, 0], [3, 2, 1, 0], [0, 5, 1, 4], [5, 3, 4, 0], [1, 4, 2, 5]]
          : [[0, 6, 5, 6], [0, 3, 4, 3], [0, 5, 6, 0], [0, 2, 3, 4], [0, 0, 3, 5]];
    const root = (id === 'redline' ? 40 : 45) + r.int(7); // E2..B2 / A2..D#3
    const sections: Section[] =
      id === 'coast'
        ? [
            { name: 'intro', bars: 4, energy: 1 },
            { name: 'a', bars: 8, energy: 2 },
            { name: 'b', bars: 8, energy: 3 },
            { name: 'a', bars: 8, energy: 2 },
            { name: 'b', bars: 8, energy: 3 },
            { name: 'outro', bars: 4, energy: 1 },
          ]
        : [
            { name: 'intro', bars: 4, energy: 1 },
            { name: 'verse', bars: 8, energy: 2 },
            { name: 'chorus', bars: 8, energy: 3 },
            { name: 'verse', bars: 8, energy: 2 },
            { name: 'chorus', bars: 8, energy: 3 },
            { name: 'break', bars: 4, energy: 1 },
            { name: 'chorus', bars: 8, energy: 3 },
            { name: 'outro', bars: 4, energy: 1 },
          ];
    const motif = makeMotif(r);
    const bassPatterns = id === 'pulse' ? [[0, 2, 4, 6, 8, 10, 12, 14], [0, 3, 6, 8, 11, 14]] : id === 'coast' ? [[0, 6, 10], [0, 7, 10, 14]] : [[0, 2, 4, 6, 8, 10, 12, 14], [0, 4, 6, 8, 12, 14]];
    this.song = {
      title: `${r.pick(TITLE_A)} ${r.pick(TITLE_B)}`,
      artist: r.pick(ARTISTS[id]),
      bpm,
      root,
      scale,
      prog: r.pick(progs),
      sections,
      motif,
      bassPattern: r.pick(bassPatterns),
      seed: r.int(1 << 30),
      hook: [],
    };
    // The chorus gets a hook of its own, so a song goes somewhere.
    this.song.hook = makeMotif(r);
    this.sectionIndex = 0;
    this.sectionBar = 0;
    this.bar = 0;
    this.songCount++;
  }

  /** The song playing (for the HUD). */
  get nowPlaying(): { title: string; artist: string } {
    return { title: this.song.title, artist: this.song.artist };
  }

  /** Render the next bar: stereo samples and what it is. */
  renderBar(): { l: Float32Array; r: Float32Array; info: BarInfo } {
    const ident = this.identPending;
    this.identPending = false;
    const s = this.song;
    const bpm = ident ? 120 : s.bpm;
    const barLen = Math.round(this.sr * (60 / bpm) * 4);
    const base = this.clock;
    const newSong = ident || (this.sectionIndex === 0 && this.sectionBar === 0);
    if (ident) this.scheduleIdent(base, barLen);
    else this.scheduleBar(base, barLen);
    const l = new Float32Array(barLen);
    const r = new Float32Array(barLen);
    const fx: FxSends = { rev: new Float32Array(barLen), dly: new Float32Array(barLen) };
    const end = base + barLen;
    this.notes = this.notes.filter((n) => n.render(l, r, base, base, end, fx));
    // Lo-fi: vinyl crackle and hiss under everything.
    if (this.id === 'coast') {
      for (let i = 0; i < barLen; i++) {
        const c = this.crackle.next() < 0.0009 ? this.crackle.signed() * 0.5 : 0;
        const hiss = this.hissHp.process(this.crackle.signed()) * 0.012;
        const v = this.crackleLp.process(c) + hiss;
        l[i] += v;
        r[i] += v;
      }
    }
    const t = this.tmp;
    const wet = this.id === 'redline' ? 0.6 : 0.9;
    for (let i = 0; i < barLen; i++) {
      this.reverb.process(fx.rev[i], t);
      let L = l[i] + t[0] * wet;
      let R = r[i] + t[1] * wet;
      this.delay.process(fx.dly[i], t);
      L += t[0] * 0.5;
      R += t[1] * 0.5;
      // A slow limiter: pull the gain down fast on peaks, let it recover over half a second.
      const p = Math.max(Math.abs(L), Math.abs(R)) * this.lim;
      if (p > 0.85) this.lim *= 0.85 / p;
      else this.lim += (1 - this.lim) * (1 / (0.5 * this.sr));
      l[i] = softClip(L * this.lim * 1.1) * 0.82;
      r[i] = softClip(R * this.lim * 1.1) * 0.82;
    }
    this.master[0] = l[barLen - 1];
    this.master[1] = r[barLen - 1];
    this.clock = end;
    const info: BarInfo = { station: this.id, title: s.title, artist: s.artist, newSong, ident };
    if (!ident) this.advance();
    return { l, r, info };
  }

  private advance(): void {
    const s = this.song;
    this.bar++;
    this.sectionBar++;
    if (this.sectionBar >= s.sections[this.sectionIndex].bars) {
      this.sectionBar = 0;
      this.sectionIndex++;
      if (this.sectionIndex >= s.sections.length) {
        this.newSong();
        // Every second song, the station says its name.
        if (this.songCount % 2 === 0) this.identPending = true;
      }
    }
  }

  /** The station's ident: a rising sweep and three chimes spelling its sound. */
  private scheduleIdent(base: number, barLen: number): void {
    const sr = this.sr;
    const beat = barLen / 4;
    const chord = this.id === 'pulse' ? [69, 72, 76] : this.id === 'coast' ? [67, 71, 74] : [64, 67, 71];
    this.notes.push(new NoiseHit(base, sr, 0.18, 0.9, 1500, 0, 0, 0.5, 0, 9));
    chord.forEach((m, i) => {
      this.notes.push(new Synth(base + Math.round(beat * (1 + i * 0.5)), sr, 0.25, [midiHz(m + 12)], 0.22, 'sine', 0.003, 0.5, 9000, 0, 0.1, 0, i - 1, 0.5, 0.3));
    });
  }

  private scheduleBar(base: number, barLen: number): void {
    const s = this.song;
    const sr = this.sr;
    const sec = s.sections[this.sectionIndex];
    const e = sec.energy;
    const r = new AudioRng(s.seed + this.bar * 7919);
    const chordDeg = s.prog[this.bar % s.prog.length];
    const sixteenth = barLen / 16;
    const swing = this.id === 'coast' ? 0.16 : 0;
    const at = (step: number): number => base + Math.round(sixteenth * (step + (step % 2 === 1 ? swing * 2 : 0)));
    const lastBarOfSection = this.sectionBar === sec.bars - 1;
    const firstBarOfSection = this.sectionBar === 0;
    const chordNote = (k: number, octave: number): number => s.root + 12 * octave + degree(s.scale, chordDeg + k);
    const hold = (steps: number): number => (steps * sixteenth) / sr;

    // --- Drums -------------------------------------------------------------
    if (this.id === 'pulse') {
      if (e >= 2 || (e === 1 && this.sectionBar >= 2)) for (let b = 0; b < 4; b++) this.notes.push(new Kick(at(b * 4), sr, 0.9));
      if (e >= 2) {
        for (const b of [4, 12]) this.notes.push(new NoiseHit(at(b), sr, 0.42, 0.14, 900, 2200, 0, 0.35, 185, r.int(1e9)));
        for (let st = 2; st < 16; st += 4) this.notes.push(new NoiseHit(at(st), sr, 0.16, 0.05, 7000, 0, 0.3, 0.05, 0, r.int(1e9)));
      }
      if (e >= 3) for (let st = 0; st < 16; st += 2) if (st % 4 !== 2) this.notes.push(new NoiseHit(at(st), sr, 0.07, 0.025, 8000, 0, -0.3, 0.02, 0, r.int(1e9)));
      // A snare roll into the next section, and a crash where the chorus lands.
      if (lastBarOfSection && e >= 2) for (const st of [12, 13, 14, 15]) this.notes.push(new NoiseHit(at(st), sr, 0.22 + 0.06 * (st - 12), 0.1, 900, 2200, 0, 0.35, 185, r.int(1e9)));
      if (firstBarOfSection && e >= 3) this.notes.push(new NoiseHit(at(0), sr, 0.22, 1.1, 5000, 0, 0.2, 0.45, 0, r.int(1e9)));
    } else if (this.id === 'coast') {
      if (e >= 2) {
        const kicks = r.next() < 0.5 ? [0, 7, 10] : [0, 6, 10, 14];
        for (const st of kicks) this.notes.push(new Kick(at(st), sr, 0.65, 0.6));
        for (const st of [4, 12]) this.notes.push(new NoiseHit(at(st), sr, 0.3, 0.11, 700, 1800, 0.05, 0.4, 170, r.int(1e9)));
      }
      if (e >= 1) for (let st = 0; st < 16; st += 2) this.notes.push(new NoiseHit(at(st), sr, st % 4 === 0 ? 0.08 : 0.05, 0.03, 7500, 0, 0.25, 0.08, 0, r.int(1e9)));
      // A soft drag on the snare at the end of a section.
      if (lastBarOfSection && e >= 2) for (const st of [14, 15]) this.notes.push(new NoiseHit(at(st), sr, 0.18, 0.08, 700, 1800, 0.05, 0.4, 170, r.int(1e9)));
    } else {
      if (e >= 2 || (e === 1 && this.sectionBar % 2 === 1)) {
        const kicks = e >= 3 ? [0, 3, 8, 10] : [0, 8, 11];
        for (const st of kicks) this.notes.push(new Kick(at(st), sr, 0.95, 1.2));
        for (const st of [4, 12]) this.notes.push(new NoiseHit(at(st), sr, 0.6, 0.16, 600, 2500, 0, 0.45, 200, r.int(1e9)));
        for (let st = 0; st < 16; st += 2) this.notes.push(new NoiseHit(at(st), sr, 0.09, e >= 3 ? 0.09 : 0.04, 6500, 0, 0.3, 0.05, 0, r.int(1e9)));
      }
      if (firstBarOfSection && e >= 2) this.notes.push(new NoiseHit(at(0), sr, 0.28, 1.0, 4500, 0, -0.2, 0.4, 0, r.int(1e9)));
      if (lastBarOfSection && e >= 2) for (const st of [12, 13, 14, 15]) this.notes.push(new NoiseHit(at(st), sr, 0.45, 0.12, 600, 2400, (st - 13.5) * 0.3, 0.4, 210, r.int(1e9)));
    }

    // --- Bass --------------------------------------------------------------
    if (e >= 2 || (this.id !== 'redline' && e >= 1 && this.sectionBar >= 2)) {
      const f0 = midiHz(chordNote(0, -1));
      // In a chorus the last note of the bar walks to the next chord's root.
      const next = midiHz(s.root - 12 + degree(s.scale, s.prog[(this.bar + 1) % s.prog.length]));
      for (const st of s.bassPattern) {
        const f = e >= 3 && st === 14 ? next : f0;
        const oct = this.id === 'pulse' && st % 4 === 2 ? 2 : 1;
        const len = this.id === 'coast' ? 3 : 2;
        if (this.id === 'coast') this.notes.push(new Synth(at(st), sr, hold(len), [f], 0.55, 'sine', 0.005, 0.12, 900, 0, 0.1, 0, 0, 0.02));
        else if (this.id === 'pulse') this.notes.push(new Synth(at(st), sr, hold(len) * 0.8, [f * oct], 0.32, 'saw', 0.002, 0.06, 260, 1400, 0.09, 0.002, 0, 0.03));
        else this.notes.push(new Synth(at(st), sr, hold(len) * 0.9, [f], 0.36, 'saw', 0.002, 0.05, 500, 900, 0.07, 0, 0, 0.02, 0, 0, 1.5));
      }
    }

    // --- Chords ------------------------------------------------------------
    if (this.id === 'pulse') {
      const fs = [0, 2, 4].map((k) => midiHz(chordNote(k, 1)));
      if (e >= 3) {
        // Gated pad: eighth-note chops.
        for (let st = 0; st < 16; st += 2) this.notes.push(new Synth(at(st), sr, hold(1.6), fs, 0.11, 'saw', 0.004, 0.05, 1600, 800, 0.1, 0.004, 0, 0.4));
      } else {
        this.notes.push(new Synth(base, sr, hold(15), fs, 0.12, 'saw', 0.35, 0.6, 1300, 0, 0.1, 0.005, 0, 0.6));
      }
      // Arpeggio.
      if (e >= 2) {
        const arp = [0, 2, 4, 7, 4, 2];
        for (let st = 0; st < 16; st++) {
          const k = arp[st % arp.length];
          this.notes.push(new Synth(at(st), sr, hold(0.7), [midiHz(chordNote(k, 2))], 0.07, 'square', 0.001, 0.08, 1200, 3500, 0.06, 0, (st % 2) * 0.6 - 0.3, 0.2, 0.25));
        }
      }
    } else if (this.id === 'coast') {
      // Seventh chords on the electric piano, sometimes pushed off the beat.
      const push = r.next() < 0.4 ? 2 : 0;
      const voicing = [0, 2, 4, 6];
      voicing.forEach((k, i) => {
        this.notes.push(new EPiano(at(push) + i * Math.round(sr * 0.012), sr, hold(12), midiHz(chordNote(k, 1)), 0.14, -0.3 + i * 0.2, 0.45));
      });
      if (e >= 3 && r.next() < 0.5) voicing.forEach((k, i) => this.notes.push(new EPiano(at(10) + i * Math.round(sr * 0.01), sr, hold(5), midiHz(chordNote(k, 1)), 0.09, -0.3 + i * 0.2, 0.45)));
    } else {
      // Power chords: root, fifth, octave through a driven amp.
      if (e >= 2) {
        const rootM = chordNote(0, 0);
        const fs = [midiHz(rootM), midiHz(rootM + 7), midiHz(rootM + 12)];
        const pattern = e >= 3 ? [[0, 3], [3, 3], [6, 2], [8, 3], [11, 3], [14, 2]] : [[0, 6], [8, 6]];
        for (const [st, len] of pattern) {
          this.notes.push(new Synth(at(st), sr, hold(len) * 0.92, fs, 0.2, 'saw', 0.002, 0.05, 2600, 0, 0.1, 0.004, -0.45, 0.12, 0, 0, 7));
          this.notes.push(new Synth(at(st), sr, hold(len) * 0.92, fs, 0.2, 'saw', 0.003, 0.05, 2500, 0, 0.1, 0.006, 0.45, 0.12, 0, 0, 7));
        }
      } else if (this.sectionBar % 2 === 0) {
        const rootM = chordNote(0, 0);
        this.notes.push(new Synth(at(0), sr, hold(14), [midiHz(rootM), midiHz(rootM + 7)], 0.16, 'saw', 0.01, 0.8, 1800, 0, 0.1, 0.004, 0, 0.3, 0, 0, 4));
      }
    }

    // --- Lead: the motif, on the chord ------------------------------------
    if (e >= 3 || (this.id === 'coast' && e >= 2 && this.sectionBar % 4 >= 2)) {
      const half = this.bar % 2; // which bar of the two-bar motif
      const tune = sec.name === 'chorus' || sec.name === 'b' ? s.hook : s.motif;
      for (const [st, len, d] of tune) {
        const local = st - half * 16;
        if (local < 0 || local >= 16) continue;
        const m = s.root + 24 + degree(s.scale, chordDeg + d);
        const f = midiHz(m);
        if (this.id === 'pulse') this.notes.push(new Synth(at(local), sr, hold(len) * 0.9, [f, f * 1.003], 0.11, 'saw', 0.01, 0.15, 2400, 1500, 0.2, 0.002, 0.15, 0.3, 0.35, 0.006));
        else if (this.id === 'coast') this.notes.push(new Synth(at(local), sr, hold(len) * 0.85, [f * 2], 0.09, 'tri', 0.02, 0.3, 3000, 0, 0.1, 0, 0.2, 0.5, 0.15, 0.008));
        else this.notes.push(new Synth(at(local), sr, hold(len) * 0.95, [f * 2], 0.12, 'saw', 0.004, 0.12, 2800, 800, 0.15, 0, 0.1, 0.25, 0.15, 0.012, 5));
      }
    }
  }
}
