// Pause menu and keyboard help for the driving slice.
import type { Aids, SteeringAssist } from '../vehicles/vehicle';

export interface PauseHandlers {
  resume(): void;
  reset(): void;
  getAids(): Aids;
  setAids(a: Aids): void;
  getUnits(): 'kmh' | 'mph';
  setUnits(u: 'kmh' | 'mph'): void;
  getTouch(): boolean;
  setTouch(v: boolean): void;
}

function seg<T extends string>(label: string, options: [T, string][], current: T, onPick: (v: T) => void): HTMLDivElement {
  const row = document.createElement('div');
  row.className = 'row';
  const l = document.createElement('span');
  l.textContent = label;
  const s = document.createElement('div');
  s.className = 'seg';
  for (const [v, text] of options) {
    const b = document.createElement('button');
    b.type = 'button';
    b.textContent = text;
    if (v === current) b.classList.add('on');
    b.onclick = () => {
      for (const x of s.querySelectorAll('button')) x.classList.remove('on');
      b.classList.add('on');
      onPick(v);
    };
    s.appendChild(b);
  }
  row.append(l, s);
  return row;
}

export class PauseMenu {
  readonly el: HTMLDivElement;
  open = false;

  constructor(parent: HTMLElement, private h: PauseHandlers) {
    this.el = document.createElement('div');
    this.el.className = 'overlay';
    this.el.style.display = 'none';
    parent.appendChild(this.el);
    this.el.addEventListener('pointerdown', (e) => {
      if (e.target === this.el) this.h.resume();
    });
  }

  show(): void {
    this.open = true;
    const aids = this.h.getAids();
    const p = document.createElement('div');
    p.className = 'panel';
    p.innerHTML = '<h2>Paused</h2><p class="sub">Halcyon Proving Ground · driving setup</p>';
    const onOff: [string, string][] = [['on', 'On'], ['off', 'Off']];
    const set = (k: 'abs' | 'tcs' | 'esc') => (v: string) => this.h.setAids({ ...this.h.getAids(), [k]: v === 'on' });
    p.append(
      seg('ABS', onOff, aids.abs ? 'on' : 'off', set('abs')),
      seg('Traction control', onOff, aids.tcs ? 'on' : 'off', set('tcs')),
      seg('Stability control', onOff, aids.esc ? 'on' : 'off', set('esc')),
      seg<SteeringAssist>('Steering assist', [['off', 'Off'], ['standard', 'Standard'], ['assisted', 'Assisted']], aids.steering, (v) => this.h.setAids({ ...this.h.getAids(), steering: v })),
      seg<'auto' | 'manual'>('Gearbox', [['auto', 'Automatic'], ['manual', 'Manual']], aids.gearbox, (v) => this.h.setAids({ ...this.h.getAids(), gearbox: v })),
      seg<'kmh' | 'mph'>('Units', [['kmh', 'km/h'], ['mph', 'mph']], this.h.getUnits(), (v) => this.h.setUnits(v)),
      seg('Touch controls', onOff, this.h.getTouch() ? 'on' : 'off', (v) => this.h.setTouch(v === 'on')),
    );
    const actions = document.createElement('div');
    actions.className = 'actions';
    const resume = document.createElement('button');
    resume.className = 'primary';
    resume.textContent = 'Resume';
    resume.onclick = () => this.h.resume();
    const reset = document.createElement('button');
    reset.textContent = 'Back to the road';
    reset.onclick = () => {
      this.h.reset();
      this.h.resume();
    };
    actions.append(resume, reset);
    p.append(actions);
    this.el.replaceChildren(p);
    this.el.style.display = '';
  }

  hide(): void {
    this.open = false;
    this.el.style.display = 'none';
  }
}

export class HelpCard {
  readonly el: HTMLDivElement;
  constructor(parent: HTMLElement) {
    this.el = document.createElement('div');
    this.el.className = 'overlay help';
    this.el.style.background = 'transparent';
    this.el.style.pointerEvents = 'none';
    this.el.style.alignItems = 'flex-start';
    this.el.style.justifyContent = 'flex-start';
    const rows: [string, string][] = [
      ['W / ↑', 'Throttle'],
      ['S / ↓', 'Brake, hold at a stop to reverse'],
      ['A D / ← →', 'Steer'],
      ['Space', 'Handbrake'],
      ['E / Q', 'Shift up / down (manual)'],
      ['C', 'Change camera'],
      ['B', 'Look back'],
      ['R', 'Back to the road'],
      ['Esc / P', 'Pause and assists'],
      ['H', 'Hide this help'],
    ];
    this.el.innerHTML = `<div class="panel" style="width:auto;max-width:340px;margin-top:64px;padding:14px 16px;background:rgba(17,22,31,0.82)"><div class="keys">${rows
      .map(([k, v]) => `<kbd>${k}</kbd><span>${v}</span>`)
      .join('')}</div></div>`;
    parent.appendChild(this.el);
  }
  setVisible(v: boolean): void {
    this.el.style.display = v ? '' : 'none';
  }
}
