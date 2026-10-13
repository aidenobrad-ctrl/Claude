// The game's sound, on Web Audio: the player's engine (synthesized loops,
// pitched and crossfaded by rpm and load), tires on every surface, road and
// wind noise, turbo and intake, impacts, gear shifts, UI blips and the radio.
// Rendering happens in a worker (see audio-worker.ts); this side only plays
// buffers and moves gains and playback rates each frame. Browsers start audio
// only after a user gesture, so nothing is created until the first key, click
// or touch. Everything here is a no-op when Web Audio is unavailable.
import type { Vehicle } from '../vehicles/vehicle';
import { SURFACES, SURFACE } from '../world/surfaces';
import { AudioRenderer, type AudioReply, type AudioRequest, type LoopData } from './audio-worker';
import { loopMix, type LoopMix } from './engine-synth';
import { STATIONS, type StationId } from './radio';
import type { UiId } from './beds';


export type RadioChoice = StationId | 'off';

export interface AudioSettings {
  /** 0..1 each. */
  master: number;
  effects: number;
  music: number;
  station: RadioChoice;
}

const SETTINGS_KEY = 'halcyon-audio';

function loadSettings(): AudioSettings {
  const d: AudioSettings = { master: 0.8, effects: 0.9, music: 0.55, station: 'pulse' };
  try {
    const raw = typeof localStorage !== 'undefined' ? localStorage.getItem(SETTINGS_KEY) : null;
    if (!raw) return d;
    const s = JSON.parse(raw) as Partial<AudioSettings>;
    const vol = (x: unknown, def: number): number => (typeof x === 'number' && Number.isFinite(x) ? Math.max(0, Math.min(1, x)) : def);
    const station = s.station === 'off' || STATIONS.some((t) => t.id === s.station) ? (s.station as RadioChoice) : d.station;
    return { master: vol(s.master, d.master), effects: vol(s.effects, d.effects), music: vol(s.music, d.music), station };
  } catch {
    return d;
  }
}

interface Layer {
  src: AudioBufferSourceNode;
  gain: GainNode;
  /** Last values sent, to skip redundant automation. */
  g: number;
  rate: number;
}

const smoothstep = (a: number, b: number, x: number): number => {
  const t = Math.max(0, Math.min(1, (x - a) / (b - a)));
  return t * t * (3 - 2 * t);
};

export interface FrameAudio {
  paused: boolean;
  /** Camera inside the car (bonnet or cockpit). */
  inside: boolean;
}

/** Another car, for its engine's voice. */
export interface RemoteCar {
  id: number;
  specId: string;
  vehicle: Vehicle;
}

/** Where the sound is heard from: the camera, its right-hand direction, and how it moves. */
export interface Listener {
  x: number;
  y: number;
  z: number;
  rx: number;
  rz: number;
  vx: number;
  vz: number;
}

/** Another car's engine loops (on the throttle), shared by every voice that plays that model. */
interface RemoteEngine {
  rpms: { rpm: number }[];
  bufs: AudioBuffer[];
  electric: boolean;
  idle: number;
  limiter: number;
}

/** As many other cars as Unity voices (GameSound.RemoteVoices), heard to the same range. */
const REMOTE_VOICES = 4;
const REMOTE_RANGE = 110;
/** Unity's rolloff for the other cars (GameSound.RemoteVoice): full near the car, nothing at the range. */
const ROLLOFF: [number, number][] = [
  [0, 1],
  [0.06, 1],
  [0.15, 0.45],
  [0.35, 0.15],
  [0.7, 0.04],
  [1, 0],
];

function rolloff(x: number): number {
  if (x >= 1) return 0;
  for (let i = 1; i < ROLLOFF.length; i++) {
    const [x1, y1] = ROLLOFF[i];
    if (x <= x1) {
      const [x0, y0] = ROLLOFF[i - 1];
      return y0 + ((y1 - y0) * (x - x0)) / (x1 - x0);
    }
  }
  return 0;
}

/**
 * One other car's engine: its loops crossfaded and pitched by its rpm (as the player's, on the throttle
 * only), through a distance rolloff, a low-pass for the air and a stereo pan, fading in and out over a
 * quarter second when it takes or lets go of a car (as in Unity).
 */
class RemoteVoice {
  car: RemoteCar | null = null;
  engine: RemoteEngine | null = null;
  releasing = false;
  fade = 0;
  private layers: Layer[] = [];
  private readonly out: GainNode;
  private readonly air: BiquadFilterNode;
  private readonly pan: StereoPannerNode;
  private readonly mix: LoopMix = { lo: 0, hi: 0, t: 0, rateLo: 1, rateHi: 1 };
  private lastD = -1;

  constructor(
    private ctx: AudioContext,
    dest: AudioNode,
    private makeLayer: (b: AudioBuffer, dest: AudioNode) => Layer,
  ) {
    this.out = ctx.createGain();
    this.out.gain.value = 0;
    this.air = ctx.createBiquadFilter();
    this.air.type = 'lowpass';
    this.air.frequency.value = 16000;
    this.pan = ctx.createStereoPanner();
    this.out.connect(this.air).connect(this.pan).connect(dest);
  }

  get busy(): boolean {
    return this.car !== null;
  }

  assign(car: RemoteCar, engine: RemoteEngine): void {
    this.car = car;
    this.releasing = false;
    this.fade = 0;
    this.lastD = -1;
    if (engine === this.engine) return;
    this.dispose();
    this.engine = engine;
    this.layers = engine.bufs.map((b) => this.makeLayer(b, this.out));
  }

  private dispose(): void {
    for (const l of this.layers) {
      try {
        l.src.stop();
      } catch {
        /* already stopped */
      }
      l.src.disconnect();
      l.gain.disconnect();
    }
    this.layers = [];
  }

  /** gone: the car has left the simulation (it fades out where it was). */
  update(dt: number, li: Listener, volume: number, paused: boolean, gone: boolean, t: number, set: (l: Layer, g: number, rate: number, t: number) => void): void {
    const car = this.car;
    const eng = this.engine;
    if (!car || !eng) return;
    if (gone) this.releasing = true;
    this.fade = Math.max(0, Math.min(1, this.fade + (this.releasing ? -dt * 4 : dt * 4)));
    if (this.releasing && this.fade <= 0) {
      // Silent now: stop its loops (a playing source costs the same at zero gain).
      this.out.gain.setTargetAtTime(0, t, 0.02);
      this.dispose();
      this.engine = null;
      this.car = null;
      this.releasing = false;
      return;
    }
    const v = car.vehicle;
    const dx = v.pos.x - li.x;
    const dy = v.pos.y - li.y;
    const dz = v.pos.z - li.z;
    const d = Math.sqrt(dx * dx + dy * dy + dz * dz);
    // Doppler at Unity's 0.6: the closing speed along the line between them.
    const closing = d > 0.5 ? ((li.vx - v.vel.x) * dx + (li.vz - v.vel.z) * dz) / d : 0;
    const doppler = Math.max(0.85, Math.min(1.15, 1 + (0.6 * closing) / 343));
    const rpm = Math.max(v.rpm, eng.electric ? 1 : eng.idle * 0.6);
    const thr = Math.max(0, Math.min(1, v.throttleEff));
    const level = paused ? 0 : this.fade * volume * (0.35 + 0.65 * thr) * (0.5 + 0.5 * Math.min(1, rpm / eng.limiter)) * rolloff(d / REMOTE_RANGE);
    loopMix(eng.rpms, rpm, this.mix);
    const m = this.mix;
    const a = Math.cos(m.t * Math.PI * 0.5);
    const b = Math.sin(m.t * Math.PI * 0.5);
    for (let i = 0; i < this.layers.length; i++) {
      const w = i === m.lo ? (m.lo === m.hi ? 1 : a) : i === m.hi ? b : 0;
      set(this.layers[i], w, (i === m.lo ? m.rateLo : m.rateHi) * doppler, t);
    }
    this.out.gain.setTargetAtTime(level, t, 0.03);
    // The air takes the top off with distance; left and right by the side of the camera it is on.
    if (Math.abs(d - this.lastD) > 0.5) {
      this.lastD = d;
      this.air.frequency.setTargetAtTime(16000 - 13000 * Math.min(1, d / REMOTE_RANGE), t, 0.05);
    }
    const side = d > 0.5 ? (dx * li.rx + dz * li.rz) / d : 0;
    this.pan.pan.setTargetAtTime(Math.max(-1, Math.min(1, side)) * 0.85, t, 0.03);
  }
}

export class GameAudio {
  ctx: AudioContext | null = null;
  settings: AudioSettings = loadSettings();
  /** Called with a line to show when a new song starts. */
  onNowPlaying: ((text: string) => void) | null = null;
  private worker: Worker | null = null;
  private local: AudioRenderer | null = null;
  private masterGain!: GainNode;
  private fxGain!: GainNode;
  private musicGain!: GainNode;
  private uiGain!: GainNode;
  private muffle!: BiquadFilterNode;
  private engineOut!: GainNode;
  private engineOn: Layer[] = [];
  private engineOff: Layer[] = [];
  private engineRpms: { rpm: number }[] = [];
  private engineInfo: { electric: boolean; turbo: boolean; edge: number; limiter: number; idle: number } | null = null;
  private engineCar = '';
  private wantedCar = '';
  private beds = new Map<string, Layer>();
  private shots = new Map<string, AudioBuffer[]>();
  private uiBufs = new Map<string, AudioBuffer>();
  private mix: LoopMix = { lo: 0, hi: 0, t: 0, rateLo: 1, rateHi: 1 };
  private load = 0;
  private lastThrottle = 0;
  private liftTimer = 0;
  private lastGear = 1;
  private crackleClock = 0;
  private radioStation: RadioChoice = 'off';
  private radioNext = 0;
  private radioPending = false;
  private radioSources: AudioBufferSourceNode[] = [];
  private radioEpoch = 0;
  private unlockBound = (): void => this.unlock();
  private failed = false;
  // The other cars: their engines (by model) and the voices that play them.
  private remoteEngines = new Map<string, RemoteEngine>();
  private remotePending = new Set<string>();
  private voices: RemoteVoice[] = [];
  private nearCars: { d: number; car: RemoteCar }[] = [];

  constructor(private enabled = true) {
    if (!enabled || typeof window === 'undefined') return;
    for (const ev of ['pointerdown', 'keydown', 'touchstart']) window.addEventListener(ev, this.unlockBound, { passive: true });
  }

  /** Create (or resume) the audio context: call from a user gesture. */
  unlock(): void {
    if (!this.enabled || this.failed) return;
    if (this.ctx) {
      if (this.ctx.state === 'suspended') void this.ctx.resume();
      return;
    }
    const Ctor = (window as unknown as { AudioContext?: typeof AudioContext; webkitAudioContext?: typeof AudioContext }).AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
    if (!Ctor) {
      this.failed = true;
      return;
    }
    try {
      this.ctx = new Ctor({ latencyHint: 'interactive' });
    } catch {
      this.failed = true;
      return;
    }
    for (const ev of ['pointerdown', 'keydown', 'touchstart']) window.removeEventListener(ev, this.unlockBound);
    const ctx = this.ctx;
    this.masterGain = ctx.createGain();
    const limiter = ctx.createDynamicsCompressor();
    limiter.threshold.value = -6;
    limiter.knee.value = 6;
    limiter.ratio.value = 12;
    limiter.attack.value = 0.003;
    limiter.release.value = 0.2;
    this.masterGain.connect(limiter).connect(ctx.destination);
    this.fxGain = ctx.createGain();
    this.muffle = ctx.createBiquadFilter();
    this.muffle.type = 'lowpass';
    this.muffle.frequency.value = 18000;
    this.fxGain.connect(this.muffle).connect(this.masterGain);
    this.musicGain = ctx.createGain();
    this.musicGain.connect(this.masterGain);
    this.uiGain = ctx.createGain();
    this.uiGain.connect(this.masterGain);
    this.engineOut = ctx.createGain();
    this.engineOut.connect(this.fxGain);
    this.applyVolumes();
    try {
      if (typeof __AUDIO_WORKER__ === 'string' && __AUDIO_WORKER__ && typeof Worker !== 'undefined') {
        const url = URL.createObjectURL(new Blob([__AUDIO_WORKER__], { type: 'text/javascript' }));
        this.worker = new Worker(url);
        this.worker.onmessage = (e: MessageEvent<AudioReply>) => this.onReply(e.data);
        this.worker.onerror = () => {
          // The worker did not start (a page that forbids blob workers): render here
          // instead, and ask again for whatever was asked of it.
          this.worker = null;
          this.local = new AudioRenderer();
          this.retry();
        };
      }
    } catch {
      this.worker = null;
    }
    if (!this.worker) this.local = new AudioRenderer();
    this.send({ type: 'init', sr: ctx.sampleRate });
    if (this.wantedCar) this.setCar(this.wantedCar);
    this.setStation(this.settings.station);
  }

  /** Ask the renderer again for everything not yet received (after the worker failed). */
  private retry(): void {
    if (!this.ctx) return;
    if (this.beds.size === 0) this.send({ type: 'init', sr: this.ctx.sampleRate });
    if (this.wantedCar && this.engineCar !== this.wantedCar) this.send({ type: 'engine', sr: this.ctx.sampleRate, carId: this.wantedCar });
    if (this.radioStation !== 'off') {
      this.radioPending = false;
      this.send({ type: 'radio', sr: this.ctx.sampleRate, station: this.radioStation, seed: (Math.random() * 1e9) | 0 });
    }
  }

  get active(): boolean {
    return !!this.ctx && this.ctx.state === 'running';
  }

  private send(req: AudioRequest): void {
    if (this.worker) {
      this.worker.postMessage(req);
      return;
    }
    if (!this.local) return;
    // Main-thread fallback: answer on a later task so callers see the same order as with a worker.
    setTimeout(() => {
      const out = this.local?.handle(req);
      if (out) this.onReply(out.reply);
    }, 0);
  }

  private buffer(data: Float32Array, right?: Float32Array): AudioBuffer {
    const ctx = this.ctx!;
    const b = ctx.createBuffer(right ? 2 : 1, data.length, ctx.sampleRate);
    b.copyToChannel(data as Float32Array<ArrayBuffer>, 0);
    if (right) b.copyToChannel(right as Float32Array<ArrayBuffer>, 1);
    return b;
  }

  private layer(data: Float32Array, dest: AudioNode): Layer {
    const ctx = this.ctx!;
    const src = ctx.createBufferSource();
    src.buffer = this.buffer(data);
    src.loop = true;
    const gain = ctx.createGain();
    gain.gain.value = 0;
    src.connect(gain).connect(dest);
    // Start each loop at a different point so identical beds do not phase.
    src.start(0, Math.random() * (src.buffer.duration * 0.9));
    return { src, gain, g: 0, rate: 1 };
  }

  private onReply(r: AudioReply): void {
    if (!this.ctx) return;
    switch (r.type) {
      case 'init':
        for (const [k, d] of Object.entries(r.beds)) this.beds.set(k, this.layer(d, this.fxGain));
        for (const [k, list] of Object.entries(r.shots)) this.shots.set(k, list.map((d) => this.buffer(d)));
        for (const [k, d] of Object.entries(r.ui)) this.uiBufs.set(k, this.buffer(d));
        break;
      case 'engine':
        if (r.remote) {
          this.remotePending.delete(r.carId);
          this.remoteEngines.set(r.carId, { rpms: r.on.map((l) => ({ rpm: l.rpm })), bufs: r.on.map((l) => this.buffer(l.data)), electric: r.electric, idle: r.idle, limiter: r.limiter });
          return;
        }
        if (r.carId !== this.wantedCar) return;
        this.disposeEngine();
        this.engineCar = r.carId;
        this.engineInfo = { electric: r.electric, turbo: r.turbo, edge: r.edge, limiter: r.limiter, idle: r.idle };
        this.engineRpms = r.on.map((l: LoopData) => ({ rpm: l.rpm }));
        this.engineOn = r.on.map((l) => this.layer(l.data, this.engineOut));
        this.engineOff = r.off.map((l) => this.layer(l.data, this.engineOut));
        break;
      case 'bar':
        this.onBar(r);
        break;
    }
  }

  private disposeEngine(): void {
    for (const l of [...this.engineOn, ...this.engineOff]) {
      try {
        l.src.stop();
      } catch {
        /* already stopped */
      }
      l.src.disconnect();
      l.gain.disconnect();
    }
    this.engineOn = [];
    this.engineOff = [];
    this.engineInfo = null;
  }

  /** The car whose engine to voice (renders its loops if needed). */
  setCar(carId: string): void {
    this.wantedCar = carId;
    if (!this.ctx || this.engineCar === carId) return;
    this.send({ type: 'engine', sr: this.ctx.sampleRate, carId });
  }

  setSettings(s: Partial<AudioSettings>): void {
    this.settings = { ...this.settings, ...s };
    try {
      localStorage.setItem(SETTINGS_KEY, JSON.stringify(this.settings));
    } catch {
      /* private mode: settings last for this visit */
    }
    this.applyVolumes();
    if (s.station !== undefined) this.setStation(s.station);
  }

  private applyVolumes(): void {
    if (!this.ctx) return;
    const t = this.ctx.currentTime;
    const cube = (x: number): number => x * x; // perceptual-ish volume curve
    this.masterGain.gain.setTargetAtTime(cube(this.settings.master), t, 0.05);
    this.fxGain.gain.setTargetAtTime(cube(this.settings.effects), t, 0.05);
    this.musicGain.gain.setTargetAtTime(cube(this.settings.music) * 0.7, t, 0.05);
    this.uiGain.gain.setTargetAtTime(0.6, t, 0.05);
  }

  /** Next station (the radio key): off, then each station in turn. */
  cycleStation(): RadioChoice {
    const order: RadioChoice[] = ['off', ...STATIONS.map((s) => s.id)];
    const next = order[(order.indexOf(this.settings.station) + 1) % order.length];
    this.setSettings({ station: next });
    return next;
  }

  stationName(c: RadioChoice = this.settings.station): string {
    return c === 'off' ? 'Radio off' : STATIONS.find((s) => s.id === c)!.name;
  }

  private setStation(c: RadioChoice): void {
    if (!this.ctx || c === this.radioStation) return;
    this.radioStation = c;
    this.radioEpoch++;
    for (const s of this.radioSources) {
      try {
        s.stop();
      } catch {
        /* not started */
      }
    }
    this.radioSources = [];
    this.radioPending = false;
    this.radioNext = 0;
    if (c === 'off') return;
    this.playStatic();
    this.send({ type: 'radio', sr: this.ctx.sampleRate, station: c, seed: (Math.random() * 1e9) | 0 });
  }

  /** A short burst of tuning static between stations. */
  private playStatic(): void {
    const ctx = this.ctx!;
    const n = Math.round(ctx.sampleRate * 0.28);
    const d = new Float32Array(n);
    for (let i = 0; i < n; i++) d[i] = (Math.random() * 2 - 1) * 0.12 * Math.sin((Math.PI * i) / n);
    const src = ctx.createBufferSource();
    src.buffer = this.buffer(d);
    src.connect(this.musicGain);
    src.start();
  }

  private onBar(r: Extract<AudioReply, { type: 'bar' }>): void {
    this.radioPending = false;
    if (!this.ctx || r.station !== this.radioStation) return;
    const ctx = this.ctx;
    const buf = this.buffer(r.l, r.r);
    const src = ctx.createBufferSource();
    src.buffer = buf;
    src.connect(this.musicGain);
    const start = Math.max(this.radioNext, ctx.currentTime + 0.08);
    src.start(start);
    this.radioNext = start + buf.duration;
    this.radioSources.push(src);
    src.onended = () => {
      const i = this.radioSources.indexOf(src);
      if (i >= 0) this.radioSources.splice(i, 1);
    };
    if (r.newSong && !r.ident && this.onNowPlaying) {
      const epoch = this.radioEpoch;
      const delay = Math.max(0, (start - ctx.currentTime) * 1000);
      setTimeout(() => {
        if (epoch === this.radioEpoch) this.onNowPlaying?.(`${this.stationName()} · ${r.artist} – ${r.title}`);
      }, delay);
    }
  }

  private pumpRadio(): void {
    if (!this.ctx || this.radioStation === 'off' || this.radioPending) return;
    if (this.radioNext - this.ctx.currentTime < 3) {
      this.radioPending = true;
      this.send({ type: 'bar', station: this.radioStation });
    }
  }

  private analyser: AnalyserNode | null = null;

  /** For tests and the debug API: what is loaded and playing, and the output level. */
  debugState(): Record<string, unknown> {
    const ctx = this.ctx;
    if (!ctx) return { state: 'none', failed: this.failed, enabled: this.enabled };
    if (!this.analyser) {
      this.analyser = ctx.createAnalyser();
      this.analyser.fftSize = 2048;
      this.masterGain.connect(this.analyser);
    }
    const buf = new Float32Array(this.analyser.fftSize);
    this.analyser.getFloatTimeDomainData(buf);
    let sum = 0;
    for (let i = 0; i < buf.length; i++) sum += buf[i] * buf[i];
    const gains = (ls: Layer[]): number => ls.reduce((m, l) => Math.max(m, l.g), 0);
    return {
      state: ctx.state,
      sampleRate: ctx.sampleRate,
      worker: !!this.worker,
      engineCar: this.engineCar,
      engineLoops: this.engineOn.length + this.engineOff.length,
      engineOnGain: gains(this.engineOn),
      engineOffGain: gains(this.engineOff),
      beds: this.beds.size,
      bedGains: Object.fromEntries([...this.beds].map(([k, l]) => [k, +l.g.toFixed(3)])),
      shots: this.shots.size,
      ui: this.uiBufs.size,
      station: this.radioStation,
      remoteEngines: [...this.remoteEngines.keys()],
      voices: this.voices.filter((v) => v.busy).map((v) => ({ car: v.car?.id, fade: +v.fade.toFixed(2) })),
      radioQueued: this.radioSources.length,
      radioAhead: +(this.radioNext - ctx.currentTime).toFixed(2),
      rms: Math.sqrt(sum / buf.length),
    };
  }

  /** Render the engines of these models for other cars' voices (a race's field), ahead of hearing them. */
  prepareVoices(carIds: readonly string[]): void {
    if (!this.ctx) return;
    for (const id of new Set(carIds)) {
      if (this.remoteEngines.has(id) || this.remotePending.has(id)) continue;
      this.remotePending.add(id);
      this.send({ type: 'engine', sr: this.ctx.sampleRate, carId: id, remote: true });
    }
  }

  /**
   * Once per rendered frame: the other cars' engines. Each voice keeps its car while it is within 1.1× the
   * range and among the nearest six; the nearest cars without one take a free voice, or the farthest voice
   * lets go (fades out) for them, as in Unity.
   */
  updateRemote(dt: number, cars: readonly RemoteCar[], li: Listener, paused: boolean): void {
    const ctx = this.ctx;
    if (!ctx || ctx.state !== 'running') return;
    if (this.voices.length === 0) {
      if (cars.length === 0) return;
      for (let i = 0; i < REMOTE_VOICES; i++) this.voices.push(new RemoteVoice(ctx, this.fxGain, (b, dest) => this.loopLayer(b, dest)));
    }
    const t = ctx.currentTime;
    const keep = REMOTE_RANGE * 1.1;
    const near = this.nearCars;
    near.length = 0;
    for (const c of cars) {
      const p = c.vehicle.pos;
      const d = (p.x - li.x) * (p.x - li.x) + (p.y - li.y) * (p.y - li.y) + (p.z - li.z) * (p.z - li.z);
      if (d < keep * keep) near.push({ d, car: c });
    }
    near.sort((a, b) => a.d - b.d);
    const rank = (car: RemoteCar | null): number => {
      const i = near.findIndex((n) => n.car === car);
      return i < 0 ? 1e9 : i;
    };
    for (const v of this.voices) if (v.busy && !v.releasing && rank(v.car) >= 6) v.releasing = true;
    for (let i = 0; i < near.length && i < REMOTE_VOICES; i++) {
      const { d, car } = near[i];
      if (d > REMOTE_RANGE * REMOTE_RANGE) break;
      const has = this.voices.find((v) => v.busy && v.car === car);
      if (has) {
        has.releasing = false;
        continue;
      }
      const eng = this.remoteEngines.get(car.specId);
      if (!eng) {
        this.prepareVoices([car.specId]);
        continue;
      }
      const free = this.voices.find((v) => !v.busy);
      if (free) {
        free.assign(car, eng);
        continue;
      }
      let farthest: RemoteVoice | null = null;
      let far = REMOTE_VOICES - 1;
      for (const v of this.voices) {
        const r = rank(v.car);
        if (r > far) {
          far = r;
          farthest = v;
        }
      }
      if (farthest) farthest.releasing = true;
    }
    const volume = 0.55;
    const set = (l: Layer, g: number, rate: number, tt: number): void => this.set(l, g, rate, tt);
    for (const v of this.voices) v.update(dt, li, volume, paused, !!v.car && !cars.includes(v.car), t, set);
  }

  private loopLayer(b: AudioBuffer, dest: AudioNode): Layer {
    const ctx = this.ctx!;
    const src = ctx.createBufferSource();
    src.buffer = b;
    src.loop = true;
    const gain = ctx.createGain();
    gain.gain.value = 0;
    src.connect(gain).connect(dest);
    src.start(0, Math.random() * (b.duration * 0.9));
    return { src, gain, g: 0, rate: 1 };
  }

  /** A menu or HUD sound. */
  ui(id: UiId): void {
    const b = this.uiBufs.get(id);
    if (!this.ctx || !b) return;
    const src = this.ctx.createBufferSource();
    src.buffer = b;
    src.connect(this.uiGain);
    src.start();
  }

  private shot(id: string, gain: number, rate = 1): void {
    const list = this.shots.get(id);
    if (!this.ctx || !list || !list.length) return;
    const src = this.ctx.createBufferSource();
    src.buffer = list[(Math.random() * list.length) | 0];
    src.playbackRate.value = rate;
    const g = this.ctx.createGain();
    g.gain.value = gain;
    src.connect(g).connect(this.fxGain);
    src.start();
  }

  /** A collision: `value` is the impact impulse the simulation reports (about 12000 for a hard hit). */
  impact(value: number): void {
    const s = Math.min(1, value / 12000);
    if (s < 0.03) return;
    const id = s > 0.6 ? 'impactHeavy' : s > 0.25 ? 'impactMedium' : 'impactLight';
    this.shot(id, 0.35 + 0.65 * s, 0.92 + Math.random() * 0.16);
  }

  setHidden(hidden: boolean): void {
    if (!this.ctx) return;
    if (hidden) void this.ctx.suspend();
    else void this.ctx.resume();
  }

  private set(l: Layer | undefined, g: number, rate: number, t: number): void {
    if (!l) return;
    if (Math.abs(g - l.g) > 0.002 || (g === 0 && l.g !== 0)) {
      l.gain.gain.setTargetAtTime(g, t, 0.03);
      l.g = g;
    }
    if (g > 0 && Math.abs(rate - l.rate) > 0.002) {
      l.src.playbackRate.setTargetAtTime(rate, t, 0.03);
      l.rate = rate;
    }
  }

  /** Once per rendered frame, from the player's car. */
  update(dt: number, v: Vehicle, f: FrameAudio): void {
    if (!this.ctx || this.ctx.state !== 'running') return;
    this.pumpRadio();
    const t = this.ctx.currentTime;
    this.muffle.frequency.setTargetAtTime(f.inside ? 5200 : 18000, t, 0.1);
    const on = !f.paused;
    // --- Engine ------------------------------------------------------------
    const info = this.engineInfo;
    const rpm = Math.max(0, v.rpm);
    const thr = v.throttleEff;
    this.load += ((thr > 0.05 ? Math.min(1, 0.25 + thr) : 0) - this.load) * Math.min(1, dt / 0.06);
    if (info && this.engineOn.length) {
      const limiter = info.limiter;
      const level = on ? (info.electric ? Math.min(1, 0.15 + Math.abs(thr) * 0.5 + v.speed / 60) : 0.4 + 0.6 * Math.pow(Math.min(1.05, rpm / limiter), 0.8)) : 0;
      const onG = Math.sqrt(this.load);
      const offG = Math.sqrt(1 - this.load);
      loopMix(this.engineRpms, Math.max(rpm, info.electric ? 1 : info.idle * 0.6), this.mix);
      const m = this.mix;
      const a = Math.cos(m.t * Math.PI * 0.5);
      const b = Math.sin(m.t * Math.PI * 0.5);
      const both = (layers: Layer[], g: number): void => {
        for (let i = 0; i < layers.length; i++) {
          const w = i === m.lo ? (m.lo === m.hi ? 1 : a) : i === m.hi ? b : 0;
          const rate = i === m.lo ? m.rateLo : i === m.hi ? m.rateHi : 1;
          this.set(layers[i], w * g * level, rate, t);
        }
      };
      if (info.electric) both(this.engineOn, 1);
      else {
        both(this.engineOn, onG);
        both(this.engineOff, offG * 0.8);
      }
      // Turbo whistle and the blow-off valve on a lift; overrun crackle on sporty engines.
      // (Set every frame, to zero for a car without one: a whistle must not outlive a car change.)
      this.set(this.beds.get('turbo'), info.turbo && on ? 0.05 * v.boost * (0.3 + 0.7 * thr) : 0, 0.5 + 0.7 * v.boost, t);
      if (info.turbo) {
        if (this.lastThrottle > 0.6 && thr < 0.15 && v.boost > 0.5 && on) {
          this.liftTimer = 0.4;
          this.shot('blowoff', 0.4 * v.boost);
        }
      }
      if (on && !info.electric && thr < 0.05 && rpm > limiter * 0.55 && info.edge > 0.5) {
        this.crackleClock -= dt;
        if (this.crackleClock <= 0) {
          this.crackleClock = 0.25 + Math.random() * 0.9;
          if (Math.random() < 0.55) this.shot('crackle', 0.22 + 0.25 * info.edge, 0.9 + Math.random() * 0.2);
        }
      }
      this.set(this.beds.get('intake'), on && !info.electric ? 0.2 * thr * Math.min(1, rpm / limiter) : 0, 0.6 + 1.1 * Math.min(1, rpm / limiter), t);
    } else {
      // Between cars (the next engine still rendering): no turbo or intake left hanging.
      this.set(this.beds.get('turbo'), 0, 1, t);
      this.set(this.beds.get('intake'), 0, 1, t);
    }
    this.lastThrottle = thr;
    this.liftTimer -= dt;
    if (on && v.gear !== this.lastGear && v.gear > 0 && this.lastGear > 0) this.shot('shift', 0.3);
    this.lastGear = v.gear;

    // --- Tires, road and wind ---------------------------------------------
    const speed = v.speed;
    let squeal = 0;
    let slideRate = 0;
    let road = 0;
    let gravel = 0;
    let grass = 0;
    let snow = 0;
    let spray = 0;
    for (const w of v.wheels) {
      if (!w.grounded) continue;
      const s = SURFACES[w.surface];
      const roll = Math.min(1, speed / 30);
      const slipK = smoothstep(0.95, 2.2, w.slip) * smoothstep(2, 6, w.slideSpeed);
      if (w.surface === SURFACE.water || w.water > 0.05) spray += 0.25 * Math.min(1, speed / 12);
      if (s.skid) {
        squeal += 0.25 * slipK;
        slideRate += 0.25 * Math.min(1, w.slideSpeed / 18);
        road += 0.25 * roll;
      } else if (w.surface === SURFACE.grass) grass += 0.25 * Math.min(1, roll + slipK);
      else if (w.surface === SURFACE.snow || w.surface === SURFACE.ice) snow += 0.25 * Math.min(1, roll + slipK);
      else gravel += 0.25 * Math.min(1, roll * 0.9 + slipK * 0.6);
      if (s.grip === 'wet' || v.wetness > 0.2) spray += 0.25 * Math.min(1, speed / 25) * (s.grip === 'wet' ? 1 : v.wetness);
    }
    const k = on ? 1 : 0;
    this.set(this.beds.get('squeal'), k * 0.5 * Math.min(1, squeal), 0.88 + 0.2 * slideRate, t);
    this.set(this.beds.get('road'), k * 0.32 * Math.pow(road, 1.2), 0.55 + Math.min(1.2, speed / 45), t);
    this.set(this.beds.get('gravel'), k * 0.42 * gravel, 0.7 + Math.min(1, speed / 40), t);
    this.set(this.beds.get('grass'), k * 0.3 * grass, 0.8 + Math.min(0.6, speed / 50), t);
    this.set(this.beds.get('snow'), k * 0.35 * snow, 0.8 + Math.min(0.6, speed / 50), t);
    this.set(this.beds.get('spray'), k * 0.3 * Math.min(1, spray), 0.8 + Math.min(0.5, speed / 60), t);
    const wind = Math.min(1, Math.pow(speed / 70, 2));
    this.set(this.beds.get('wind'), k * 0.45 * wind * (f.inside ? 0.6 : 1), 0.6 + Math.min(1, speed / 80), t);
  }
}
