// window.__game: the debug and test API. Headless tests drive the game only
// through this surface, so keep it stable and documented in docs/STATE.md.
import type { Game } from './game';
import type { Controls } from './engine/input';
import { SIM_HZ } from './engine/loop';

export interface DebugApi {
  version: string;
  /** Advance n fixed 1/240 s steps. Returns the sim tick. */
  step(n?: number): number;
  /** Advance the given number of simulated seconds. */
  stepSeconds(s: number): number;
  /** Render one frame now (test mode does not render on its own). */
  render(): void;
  /** Replace the scripted controls (missing fields are neutral). Physical input is ignored until clearInput(). */
  setInput(c: Partial<Controls>): void;
  /** Merge into the current scripted controls. */
  patchInput(c: Partial<Controls>): void;
  clearInput(): void;
  teleport(x: number, z: number, yaw?: number, y?: number): void;
  getState(): unknown;
  /** PNG data URL of the canvas after a fresh render. */
  screenshot(): string;
  perf(): Record<string, unknown>;
  hash(): string;
  reset(): void;
  game: Game;
}

export function installDebugApi(game: Game): DebugApi {
  const api: DebugApi = {
    version: __BUILD_TIME__,
    step(n = 1) {
      game.stepTicks(Math.max(0, Math.floor(n)));
      return game.sim.tick;
    },
    stepSeconds(s) {
      return api.step(Math.round(s * SIM_HZ));
    },
    render() {
      game.renderNow();
    },
    setInput(c) {
      game.input.override = { ...c };
    },
    patchInput(c) {
      game.input.override = { ...(game.input.override ?? {}), ...c };
    },
    clearInput() {
      game.input.override = null;
    },
    teleport(x, z, yaw = 0, y) {
      game.sim.teleport(x, z, yaw, y);
    },
    getState() {
      return game.sim.getState();
    },
    screenshot() {
      game.renderNow();
      return game.renderer.domElement.toDataURL('image/png');
    },
    perf() {
      return game.perf.report();
    },
    hash() {
      return game.sim.hash();
    },
    reset() {
      game.input.override = null;
      game.sim.reset();
    },
    game,
  };
  (window as unknown as { __game: DebugApi }).__game = api;
  return api;
}
