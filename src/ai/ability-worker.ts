// The rivals' cars measured off the main thread: CarAbility.measure drives
// each car round a virtual test track for a fraction of a second of CPU, and a
// grid of seven measured on the main thread froze the page for seconds on a
// phone. The game asks for one car at a time (see AbilityCache.measurer) and
// gets the measured numbers back; the same function runs on the main thread
// when workers are not available.
import { CARS } from '../vehicles/cars';
import type { SurfaceId } from '../world/surfaces';
import { CarAbility } from './racing-line';

export interface AbilityRequest {
  key: string;
  id: string;
  wetness: number;
  surface: SurfaceId;
}

export interface AbilityReply {
  key: string;
  /** CarAbility's own fields, as measured. */
  data: Record<string, unknown>;
}

export function measureAbility(req: AbilityRequest): AbilityReply {
  const spec = CARS.find((c) => c.id === req.id) ?? CARS[0];
  const a = CarAbility.measure(spec, req.wetness, req.surface);
  return { key: req.key, data: { ...a } };
}

/** A CarAbility from the numbers a measurement posted back. */
export function abilityFrom(data: Record<string, unknown>): CarAbility {
  return Object.assign(new CarAbility(), data);
}

// Running as a worker: answer messages. (Imported on the main thread, this does nothing.)
declare const WorkerGlobalScope: unknown;
if (typeof self !== 'undefined' && typeof WorkerGlobalScope !== 'undefined' && typeof window === 'undefined') {
  const scope = self as unknown as { onmessage: (e: MessageEvent<AbilityRequest>) => void; postMessage(m: unknown, t: ArrayBuffer[]): void };
  scope.onmessage = (e) => {
    const out = measureAbility(e.data);
    const accel = out.data.accel as Float64Array;
    scope.postMessage(out, [accel.buffer as ArrayBuffer]);
  };
}
