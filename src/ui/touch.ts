// On-screen touch controls: a slide-steering pad, gas and brake pedals, a
// handbrake and a few action buttons. Pointer events handle any number of
// fingers; each control owns the pointer that pressed it.
import type { Action, Input } from '../engine/input';

export interface TouchOptions {
  leftHanded: boolean;
  haptics: boolean;
}

export class TouchControls {
  readonly el: HTMLDivElement;
  private steerPointer = -1;
  private steerOriginX = 0;
  private knob: HTMLDivElement;
  private pad: HTMLDivElement;
  private held = new Map<number, string>();
  opts: TouchOptions = { leftHanded: false, haptics: true };
  visible = false;

  constructor(parent: HTMLElement, private input: Input, private onAction: (a: Action) => void) {
    this.el = document.createElement('div');
    this.el.className = 'touch';
    this.el.innerHTML = `
      <div class="t-pad"><div class="t-track"></div><div class="t-knob"></div><span class="t-hint">STEER</span></div>
      <div class="t-pedals">
        <button class="t-btn t-hand" data-k="handbrake" aria-label="Handbrake">HAND<br>BRAKE</button>
        <button class="t-btn t-brake" data-k="brake" aria-label="Brake">BRAKE</button>
        <button class="t-btn t-gas" data-k="throttle" aria-label="Gas">GAS</button>
      </div>
      <div class="t-actions">
        <button class="t-small" data-a="camera" aria-label="Camera">CAM</button>
        <button class="t-small" data-a="reset" aria-label="Reset to road">ROAD</button>
        <button class="t-small" data-a="map" aria-label="Map">MAP</button>
        <button class="t-small" data-a="pause" aria-label="Pause">II</button>
      </div>`;
    parent.appendChild(this.el);
    this.pad = this.el.querySelector('.t-pad') as HTMLDivElement;
    this.knob = this.el.querySelector('.t-knob') as HTMLDivElement;

    this.pad.addEventListener('pointerdown', (e) => {
      e.preventDefault();
      if (this.steerPointer >= 0) return;
      this.steerPointer = e.pointerId;
      this.pad.setPointerCapture(e.pointerId);
      this.steerOriginX = e.clientX;
      this.setSteer(0);
      this.input.touch.active = true;
    });
    this.pad.addEventListener('pointermove', (e) => {
      if (e.pointerId !== this.steerPointer) return;
      const range = Math.max(60, this.pad.clientWidth * 0.32);
      this.setSteer((e.clientX - this.steerOriginX) / range);
    });
    const endSteer = (e: PointerEvent): void => {
      if (e.pointerId !== this.steerPointer) return;
      this.steerPointer = -1;
      this.setSteer(0);
    };
    this.pad.addEventListener('pointerup', endSteer);
    this.pad.addEventListener('pointercancel', endSteer);

    for (const b of this.el.querySelectorAll<HTMLButtonElement>('[data-k]')) {
      const key = b.dataset.k as string;
      b.addEventListener('pointerdown', (e) => {
        e.preventDefault();
        b.setPointerCapture(e.pointerId);
        this.held.set(e.pointerId, key);
        b.classList.add('down');
        this.input.touch.active = true;
        this.apply();
        this.buzz(8);
      });
      const up = (e: PointerEvent): void => {
        if (this.held.get(e.pointerId) !== key) return;
        this.held.delete(e.pointerId);
        if (![...this.held.values()].includes(key)) b.classList.remove('down');
        this.apply();
      };
      b.addEventListener('pointerup', up);
      b.addEventListener('pointercancel', up);
      b.addEventListener('lostpointercapture', up);
    }
    for (const b of this.el.querySelectorAll<HTMLButtonElement>('[data-a]')) {
      b.addEventListener('pointerdown', (e) => {
        e.preventDefault();
        this.buzz(10);
        this.onAction(b.dataset.a as Action);
      });
    }
    this.el.addEventListener('contextmenu', (e) => e.preventDefault());
  }

  private setSteer(v: number): void {
    const s = Math.max(-1, Math.min(1, v));
    this.input.touch.steer = s;
    const range = this.pad.clientWidth * 0.32;
    this.knob.style.transform = `translate(calc(-50% + ${s * range}px), -50%)`;
  }

  private apply(): void {
    const keys = new Set(this.held.values());
    const t = this.input.touch;
    t.throttle = keys.has('throttle') ? 1 : 0;
    t.brake = keys.has('brake') ? 1 : 0;
    t.handbrake = keys.has('handbrake') ? 1 : 0;
  }

  buzz(ms: number): void {
    if (this.opts.haptics && typeof navigator !== 'undefined' && navigator.vibrate) {
      try {
        navigator.vibrate(ms);
      } catch {
        // Some browsers throw without a user gesture; haptics are optional.
      }
    }
  }

  setVisible(v: boolean): void {
    this.visible = v;
    this.el.style.display = v ? '' : 'none';
    if (!v) {
      this.held.clear();
      this.steerPointer = -1;
      this.input.touch.active = false;
      this.input.touch.steer = this.input.touch.throttle = this.input.touch.brake = this.input.touch.handbrake = 0;
    }
  }

  setLeftHanded(v: boolean): void {
    this.opts.leftHanded = v;
    this.el.classList.toggle('lefty', v);
  }
}
