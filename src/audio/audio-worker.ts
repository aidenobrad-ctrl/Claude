// The audio worker: renders engine loops, the beds and one-shots, and radio
// bars off the main thread, and posts the samples back (transferred, not
// copied). The same functions run on the main thread when workers are not
// available (see audio.ts).
import { CARS } from '../vehicles/cars';
import { renderEngineSet } from './engine-synth';
import { renderBed, renderShot, renderUi, type BedId, type ShotId, type UiId } from './beds';
import { RadioStation, type StationId } from './radio';

export type AudioRequest =
  | { type: 'init'; sr: number }
  | { type: 'engine'; sr: number; carId: string; /** For another car's voice (its on-load loops only). */ remote?: boolean }
  | { type: 'radio'; sr: number; station: StationId; seed: number }
  | { type: 'bar'; station: StationId };

export interface LoopData {
  rpm: number;
  data: Float32Array;
}

export type AudioReply =
  | { type: 'init'; beds: Record<string, Float32Array>; shots: Record<string, Float32Array[]>; ui: Record<string, Float32Array> }
  | { type: 'engine'; carId: string; remote: boolean; electric: boolean; turbo: boolean; edge: number; limiter: number; idle: number; on: LoopData[]; off: LoopData[] }
  | { type: 'bar'; station: StationId; l: Float32Array; r: Float32Array; title: string; artist: string; newSong: boolean; ident: boolean };

export const BEDS: BedId[] = ['squeal', 'road', 'gravel', 'grass', 'snow', 'spray', 'wind', 'intake', 'turbo'];
export const SHOTS: ShotId[] = ['impactLight', 'impactMedium', 'impactHeavy', 'blowoff', 'crackle', 'shift', 'scrape'];
export const UIS: UiId[] = ['move', 'select', 'back', 'error', 'count', 'go', 'reward', 'photo', 'checkpoint'];

/** Handles one request; returns the reply and the buffers to transfer. */
export class AudioRenderer {
  private stations = new Map<StationId, RadioStation>();

  handle(req: AudioRequest): { reply: AudioReply; transfer: ArrayBuffer[] } | null {
    switch (req.type) {
      case 'init': {
        const beds: Record<string, Float32Array> = {};
        const shots: Record<string, Float32Array[]> = {};
        const ui: Record<string, Float32Array> = {};
        const transfer: ArrayBuffer[] = [];
        for (const b of BEDS) transfer.push((beds[b] = renderBed(b, req.sr)).buffer as ArrayBuffer);
        for (const s of SHOTS) {
          const variants = s === 'crackle' || s.startsWith('impact') ? 3 : 1;
          shots[s] = [];
          for (let v = 0; v < variants; v++) {
            const d = renderShot(s, req.sr, v);
            shots[s].push(d);
            transfer.push(d.buffer as ArrayBuffer);
          }
        }
        for (const u of UIS) transfer.push((ui[u] = renderUi(u, req.sr)).buffer as ArrayBuffer);
        return { reply: { type: 'init', beds, shots, ui }, transfer };
      }
      case 'engine': {
        const spec = CARS.find((c) => c.id === req.carId) ?? CARS[0];
        const set = renderEngineSet(spec, req.sr);
        const pack = (loops: { rpm: number; data: Float32Array }[]): LoopData[] => loops.map((l) => ({ rpm: l.rpm, data: l.data }));
        const on = pack(set.on);
        const off = set.voice.electric || req.remote ? [] : pack(set.off);
        const transfer = [...on, ...off].map((l) => l.data.buffer as ArrayBuffer);
        const v = set.voice;
        return { reply: { type: 'engine', carId: spec.id, remote: !!req.remote, electric: v.electric, turbo: v.turbo, edge: v.edge, limiter: v.limiter, idle: v.idle, on, off }, transfer };
      }
      case 'radio': {
        this.stations.set(req.station, new RadioStation(req.station, req.sr, req.seed));
        return null;
      }
      case 'bar': {
        const st = this.stations.get(req.station);
        if (!st) return null;
        const bar = st.renderBar();
        return {
          reply: { type: 'bar', station: req.station, l: bar.l, r: bar.r, title: bar.info.title, artist: bar.info.artist, newSong: bar.info.newSong, ident: bar.info.ident },
          transfer: [bar.l.buffer as ArrayBuffer, bar.r.buffer as ArrayBuffer],
        };
      }
    }
  }
}

// Running as a worker: answer messages. (Imported on the main thread, this does nothing.)
declare const WorkerGlobalScope: unknown;
if (typeof self !== 'undefined' && typeof WorkerGlobalScope !== 'undefined' && typeof window === 'undefined') {
  const renderer = new AudioRenderer();
  const scope = self as unknown as { onmessage: (e: MessageEvent<AudioRequest>) => void; postMessage(m: unknown, t: ArrayBuffer[]): void };
  scope.onmessage = (e) => {
    const out = renderer.handle(e.data);
    if (out) scope.postMessage(out.reply, out.transfer);
  };
}
