// Lightweight performance counters: milliseconds per system (smoothed and
// peak), frame time, and renderer counters filled in by the game.

interface Stat {
  last: number;
  avg: number;
  max: number;
  start: number;
}

export class Perf {
  readonly stats = new Map<string, Stat>();
  frameMs = 16.7;
  frameMsMax = 0;
  fps = 60;
  frames = 0;
  counters: Record<string, number> = {};
  private lastFrame = 0;
  private now: () => number;

  constructor() {
    this.now = typeof performance !== 'undefined' ? () => performance.now() : () => Date.now();
  }

  begin(name: string): void {
    let s = this.stats.get(name);
    if (!s) {
      s = { last: 0, avg: 0, max: 0, start: 0 };
      this.stats.set(name, s);
    }
    s.start = this.now();
  }

  end(name: string): void {
    const s = this.stats.get(name);
    if (!s) return;
    s.last = this.now() - s.start;
    s.avg += (s.last - s.avg) * 0.05;
    s.max = Math.max(s.max * 0.995, s.last);
  }

  /** Call once per rendered frame. */
  frame(): void {
    const t = this.now();
    if (this.lastFrame > 0) {
      const ms = t - this.lastFrame;
      this.frameMs += (ms - this.frameMs) * 0.05;
      this.frameMsMax = Math.max(this.frameMsMax * 0.99, ms);
      this.fps = 1000 / Math.max(1, this.frameMs);
    }
    this.lastFrame = t;
    this.frames++;
  }

  resetFrameClock(): void {
    this.lastFrame = 0;
  }

  report(): Record<string, unknown> {
    const systems: Record<string, { avg: number; max: number }> = {};
    for (const [k, s] of this.stats) systems[k] = { avg: +s.avg.toFixed(3), max: +s.max.toFixed(3) };
    return { fps: +this.fps.toFixed(1), frameMs: +this.frameMs.toFixed(2), frameMsMax: +this.frameMsMax.toFixed(2), systems, counters: { ...this.counters } };
  }
}
