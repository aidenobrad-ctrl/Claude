// Fixed-step simulation clock. The simulation always advances in exact
// 1/240 s steps; rendering interpolates between the last two states.

export const SIM_HZ = 240;
export const SIM_DT = 1 / SIM_HZ;

export class FixedStepper {
  accumulator = 0;
  /** Longest real frame we will simulate; slower frames run in slow motion. */
  maxFrame = 0.1;
  ticks = 0;

  /** Run `step` as many times as the elapsed time requires. Returns the count. */
  advance(frameDt: number, step: () => void): number {
    if (!(frameDt > 0)) return 0;
    this.accumulator += Math.min(frameDt, this.maxFrame);
    let n = 0;
    while (this.accumulator >= SIM_DT) {
      step();
      this.accumulator -= SIM_DT;
      this.ticks++;
      n++;
    }
    return n;
  }

  /** Interpolation factor between the previous and current state. */
  get alpha(): number {
    return this.accumulator / SIM_DT;
  }

  reset(): void {
    this.accumulator = 0;
  }
}
