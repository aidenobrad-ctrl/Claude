// Unified input: keyboard, gamepad and touch merge into one Controls snapshot
// per frame. Tests drive the game through `override`, which replaces every
// physical source so runs are deterministic.

import { dpow } from './dmath';

export type Action =
  | 'throttle'
  | 'brake'
  | 'steerLeft'
  | 'steerRight'
  | 'handbrake'
  | 'clutch'
  | 'shiftUp'
  | 'shiftDown'
  | 'camera'
  | 'lookBack'
  | 'reset'
  | 'rewind'
  | 'pause'
  | 'map'
  | 'photo'
  | 'horn'
  | 'lights'
  | 'radio'
  | 'confirm';

export const ACTIONS: readonly Action[] = [
  'throttle', 'brake', 'steerLeft', 'steerRight', 'handbrake', 'clutch', 'shiftUp', 'shiftDown',
  'camera', 'lookBack', 'reset', 'rewind', 'pause', 'map', 'photo', 'horn', 'lights', 'radio', 'confirm',
];

export const ACTION_LABELS: Record<Action, string> = {
  throttle: 'Throttle', brake: 'Brake / reverse', steerLeft: 'Steer left', steerRight: 'Steer right',
  handbrake: 'Handbrake', clutch: 'Clutch', shiftUp: 'Shift up', shiftDown: 'Shift down',
  camera: 'Change camera', lookBack: 'Look back', reset: 'Reset to road', rewind: 'Rewind',
  pause: 'Pause / back', map: 'Map', photo: 'Photo mode', horn: 'Horn', lights: 'Headlights',
  radio: 'Next radio station', confirm: 'Confirm / start event',
};

export type KeyBindings = Record<Action, string[]>;

export const DEFAULT_KEYS: KeyBindings = {
  throttle: ['KeyW', 'ArrowUp'],
  brake: ['KeyS', 'ArrowDown'],
  steerLeft: ['KeyA', 'ArrowLeft'],
  steerRight: ['KeyD', 'ArrowRight'],
  handbrake: ['Space'],
  clutch: ['ShiftRight'],
  shiftUp: ['KeyE'],
  shiftDown: ['KeyQ'],
  camera: ['KeyC'],
  lookBack: ['KeyB'],
  reset: ['KeyR'],
  rewind: ['KeyT'],
  pause: ['Escape', 'KeyP'],
  map: ['KeyM'],
  photo: ['KeyO'],
  horn: ['KeyH'],
  lights: ['KeyL'],
  radio: ['KeyN'],
  confirm: ['Enter'],
};

// Standard gamepad mapping (Xbox layout names).
export const PAD = {
  A: 0, B: 1, X: 2, Y: 3, LB: 4, RB: 5, LT: 6, RT: 7, VIEW: 8, MENU: 9, LS: 10, RS: 11,
  UP: 12, DOWN: 13, LEFT: 14, RIGHT: 15,
} as const;

export const DEFAULT_PAD: Partial<Record<Action, number>> = {
  handbrake: PAD.A,
  shiftUp: PAD.B,
  shiftDown: PAD.X,
  rewind: PAD.Y,
  camera: PAD.RB,
  lookBack: PAD.LB,
  pause: PAD.MENU,
  map: PAD.VIEW,
  horn: PAD.LS,
  lights: PAD.UP,
  radio: PAD.RIGHT,
  photo: PAD.DOWN,
  confirm: PAD.A,
};

export type InputSource = 'keyboard' | 'gamepad' | 'touch' | 'script';

export interface Controls {
  steer: number; // -1 full left .. +1 full right
  throttle: number; // 0..1
  brake: number; // 0..1
  handbrake: number; // 0..1
  clutch: number; // 0..1
  /** True when steering is analog (pad, touch, script); keyboard steering gets smoothed. */
  analogSteer: boolean;
  held: Record<Action, boolean>;
  /** Actions that went down this frame. */
  pressed: Record<Action, boolean>;
  source: InputSource;
}

export interface TouchState {
  active: boolean;
  steer: number;
  throttle: number;
  brake: number;
  handbrake: number;
  buttons: Partial<Record<Action, boolean>>;
}

export interface PadSettings {
  deadzone: number;
  outerDeadzone: number;
  triggerDeadzone: number;
  steerLinearity: number; // 1 = linear, >1 = finer near center
}

function emptyActions(): Record<Action, boolean> {
  const r = {} as Record<Action, boolean>;
  for (const a of ACTIONS) r[a] = false;
  return r;
}

export function neutralControls(): Controls {
  return {
    steer: 0, throttle: 0, brake: 0, handbrake: 0, clutch: 0, analogSteer: true,
    held: emptyActions(), pressed: emptyActions(), source: 'script',
  };
}

/** Apply radial inner/outer deadzone and a response curve to a stick axis. */
export function shapeAxis(v: number, s: PadSettings): number {
  const a = Math.abs(v);
  if (a <= s.deadzone) return 0;
  const t = Math.min(1, (a - s.deadzone) / Math.max(1e-6, 1 - s.deadzone - s.outerDeadzone));
  return Math.sign(v) * dpow(t, s.steerLinearity);
}

export function shapeTrigger(v: number, s: PadSettings): number {
  if (v <= s.triggerDeadzone) return 0;
  return Math.min(1, (v - s.triggerDeadzone) / (1 - s.triggerDeadzone));
}

export class Input {
  bindings: KeyBindings = structuredCloneBindings(DEFAULT_KEYS);
  padBindings: Partial<Record<Action, number>> = { ...DEFAULT_PAD };
  pad: PadSettings = { deadzone: 0.1, outerDeadzone: 0.03, triggerDeadzone: 0.04, steerLinearity: 1.25 };
  touch: TouchState = { active: false, steer: 0, throttle: 0, brake: 0, handbrake: 0, buttons: {} };
  /** Scripted input for tests and bots. When set, physical sources are ignored. */
  override: Partial<Controls> | null = null;
  lastSource: InputSource = 'keyboard';
  /** Key capture hook for the remapping screen. Return true to swallow the key. */
  onKeyCapture: ((code: string) => boolean) | null = null;

  private keys = new Set<string>();
  private prevHeld = emptyActions();
  private padConnected = false;
  private overridePressed: Partial<Record<Action, boolean>> = {};

  attach(target: Window): void {
    target.addEventListener('keydown', (e) => {
      if (this.onKeyCapture && this.onKeyCapture(e.code)) {
        e.preventDefault();
        return;
      }
      const t = e.target as HTMLElement | null;
      if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.tagName === 'SELECT')) return;
      this.keys.add(e.code);
      this.lastSource = 'keyboard';
      if (e.code.startsWith('Arrow') || e.code === 'Space' || e.code === 'Tab') e.preventDefault();
    });
    target.addEventListener('keyup', (e) => this.keys.delete(e.code));
    target.addEventListener('blur', () => this.keys.clear());
    target.addEventListener('gamepadconnected', () => (this.padConnected = true));
  }

  /** Simulate an action press from UI buttons (edge on next poll). */
  tap(action: Action): void {
    this.overridePressed[action] = true;
  }

  clearKeys(): void {
    this.keys.clear();
  }

  poll(): Controls {
    const c = neutralControls();
    if (this.override) {
      Object.assign(c, this.override);
      c.held = { ...emptyActions(), ...(this.override.held ?? {}) };
      c.source = 'script';
    } else {
      this.readKeyboard(c);
      this.readGamepad(c);
      this.readTouch(c);
    }
    for (const a of ACTIONS) {
      c.pressed[a] = (c.held[a] && !this.prevHeld[a]) || !!this.overridePressed[a];
      this.prevHeld[a] = c.held[a];
    }
    if (this.override?.pressed) for (const a of ACTIONS) if (this.override.pressed[a]) c.pressed[a] = true;
    this.overridePressed = {};
    return c;
  }

  private readKeyboard(c: Controls): void {
    let any = false;
    for (const a of ACTIONS) {
      const codes = this.bindings[a];
      for (let i = 0; i < codes.length; i++) {
        if (this.keys.has(codes[i])) {
          c.held[a] = true;
          any = true;
          break;
        }
      }
    }
    if (!any) return;
    c.source = 'keyboard';
    c.throttle = c.held.throttle ? 1 : 0;
    c.brake = c.held.brake ? 1 : 0;
    c.handbrake = c.held.handbrake ? 1 : 0;
    c.clutch = c.held.clutch ? 1 : 0;
    c.steer = (c.held.steerRight ? 1 : 0) - (c.held.steerLeft ? 1 : 0);
    c.analogSteer = false;
  }

  private readGamepad(c: Controls): void {
    if (typeof navigator === 'undefined' || !navigator.getGamepads) return;
    if (!this.padConnected && !navigator.getGamepads().some((p) => p)) return;
    const pads = navigator.getGamepads();
    for (const p of pads) {
      if (!p || !p.connected) continue;
      const btn = (i: number): number => (p.buttons[i] ? p.buttons[i].value || (p.buttons[i].pressed ? 1 : 0) : 0);
      const steer = shapeAxis(p.axes[0] ?? 0, this.pad);
      const rt = shapeTrigger(btn(PAD.RT), this.pad);
      const lt = shapeTrigger(btn(PAD.LT), this.pad);
      let used = Math.abs(steer) > 0 || rt > 0 || lt > 0;
      for (const a of ACTIONS) {
        const b = this.padBindings[a];
        if (b !== undefined && btn(b) > 0.5) {
          c.held[a] = true;
          used = true;
        }
      }
      if (!used) continue;
      this.lastSource = 'gamepad';
      c.source = 'gamepad';
      if (Math.abs(steer) > Math.abs(c.steer) || c.analogSteer) {
        if (Math.abs(steer) > 0 || c.steer === 0) {
          c.steer = steer;
          c.analogSteer = true;
        }
      }
      c.throttle = Math.max(c.throttle, rt);
      c.brake = Math.max(c.brake, lt);
      if (c.held.handbrake) c.handbrake = 1;
      break;
    }
  }

  private readTouch(c: Controls): void {
    const t = this.touch;
    if (!t.active) return;
    this.lastSource = 'touch';
    c.source = 'touch';
    if (Math.abs(t.steer) > Math.abs(c.steer)) {
      c.steer = t.steer;
      c.analogSteer = true;
    }
    c.throttle = Math.max(c.throttle, t.throttle);
    c.brake = Math.max(c.brake, t.brake);
    c.handbrake = Math.max(c.handbrake, t.handbrake);
    for (const k in t.buttons) {
      const a = k as Action;
      if (t.buttons[a]) c.held[a] = true;
    }
  }
}

function structuredCloneBindings(b: KeyBindings): KeyBindings {
  const r = {} as KeyBindings;
  for (const a of ACTIONS) r[a] = [...b[a]];
  return r;
}

export function keyLabel(code: string): string {
  if (code.startsWith('Key')) return code.slice(3);
  if (code.startsWith('Digit')) return code.slice(5);
  const map: Record<string, string> = {
    ArrowUp: '↑', ArrowDown: '↓', ArrowLeft: '←', ArrowRight: '→', Space: 'Space', Escape: 'Esc',
    ShiftLeft: 'L-Shift', ShiftRight: 'R-Shift', ControlLeft: 'L-Ctrl', ControlRight: 'R-Ctrl',
    AltLeft: 'L-Alt', AltRight: 'R-Alt', Enter: 'Enter', Backspace: 'Backspace', Tab: 'Tab',
  };
  return map[code] ?? code;
}
