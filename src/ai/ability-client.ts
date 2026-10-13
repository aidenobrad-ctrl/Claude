// The main thread's side of the ability worker (ability-worker.ts): cars are
// measured one at a time, a race's own cars ahead of any measured in advance.
// Without workers (tests, tools, a page that forbids them) measuring happens
// here, a car per task, so the page still draws between cars.
import type { SurfaceId } from '../world/surfaces';
import { abilityFrom, measureAbility, type AbilityReply, type AbilityRequest } from './ability-worker';
import type { CarAbility } from './racing-line';

/** How long a measurement may take in the worker (its start included, on a slow phone) before the page does it. */
const PATIENCE_MS = 8000;

export class AbilityMeasurer {
  private worker: Worker | null = null;
  private queue: AbilityRequest[] = [];
  private current: AbilityRequest | null = null;
  private waiting = new Map<string, ((a: CarAbility) => void)[]>();
  private watchdog: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    try {
      if (typeof __AI_WORKER__ === 'string' && __AI_WORKER__ && typeof Worker !== 'undefined') {
        const url = URL.createObjectURL(new Blob([__AI_WORKER__], { type: 'text/javascript' }));
        this.worker = new Worker(url);
        this.worker.onmessage = (e: MessageEvent<AbilityReply>) => this.done(e.data);
        // The worker did not start: measure here instead, starting with the one it was given.
        this.worker.onerror = () => this.fallBack();
      }
    } catch {
      this.worker = null;
    }
  }

  /** Measure a car (urgent: ahead of the cars queued in advance). */
  measure(key: string, id: string, wetness: number, surface: SurfaceId, urgent = false): Promise<CarAbility> {
    return new Promise((resolve) => {
      const list = this.waiting.get(key);
      if (list) {
        list.push(resolve);
        // Already queued in advance: move it to the front.
        if (urgent) {
          const i = this.queue.findIndex((q) => q.key === key);
          if (i > 0) this.queue.unshift(...this.queue.splice(i, 1));
        }
        return;
      }
      this.waiting.set(key, [resolve]);
      const req: AbilityRequest = { key, id, wetness, surface };
      if (urgent) this.queue.unshift(req);
      else this.queue.push(req);
      this.next();
    });
  }

  private next(): void {
    if (this.current || this.queue.length === 0) return;
    const req = (this.current = this.queue.shift()!);
    if (this.worker) {
      this.worker.postMessage(req);
      // A page can block a worker without an error: one that never answers is replaced by the page.
      this.watchdog = setTimeout(() => this.fallBack(), PATIENCE_MS);
    } else setTimeout(() => this.done(measureAbility(req)), 0);
  }

  /** Measure on the page from now on, starting with the car the worker was given. */
  private fallBack(): void {
    if (this.watchdog) clearTimeout(this.watchdog);
    this.watchdog = null;
    this.worker?.terminate();
    this.worker = null;
    if (this.current) this.queue.unshift(this.current);
    this.current = null;
    this.next();
  }

  private done(r: AbilityReply): void {
    if (this.watchdog) clearTimeout(this.watchdog);
    this.watchdog = null;
    this.current = null;
    const a = abilityFrom(r.data);
    const list = this.waiting.get(r.key) ?? [];
    this.waiting.delete(r.key);
    for (const f of list) f(a);
    this.next();
  }
}
