// Pause menu and keyboard help for the driving slice.
import type { Aids, SteeringAssist } from '../vehicles/vehicle';
import type { QualityName } from '../engine/render/quality';
import type { AudioSettings, RadioChoice } from '../audio/audio';
import { STATIONS } from '../audio/radio';

export type QualityChoice = 'auto' | QualityName;

export interface PauseHandlers {
  resume(): void;
  reset(): void;
  getAids(): Aids;
  setAids(a: Aids): void;
  getUnits(): 'kmh' | 'mph';
  setUnits(u: 'kmh' | 'mph'): void;
  getTouch(): boolean;
  setTouch(v: boolean): void;
  /** The cars to choose from (id, name, class line) and the one being driven. */
  getCars(): { id: string; name: string; info: string }[];
  getCar(): string;
  setCar(id: string): void;
  getQuality(): QualityChoice;
  /** Saves the preset; true when it needs a reload to take effect. */
  setQuality(q: QualityChoice): boolean;
  getAudio(): AudioSettings;
  setAudio(s: Partial<AudioSettings>): void;
  /** A click for the menu (optional). */
  click?(): void;
}

/** Set by the pause menu so every segment button clicks. */
let segClick: (() => void) | undefined;

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
      segClick?.();
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
    p.innerHTML = '<h2>Paused</h2><p class="sub">Halcyon Roads · car, assists, graphics and sound</p>';
    const onOff: [string, string][] = [['on', 'On'], ['off', 'Off']];
    // The car: a native picker (a long list reads best in the phone's own wheel).
    const carRow = document.createElement('div');
    carRow.className = 'row';
    const carLabel = document.createElement('span');
    carLabel.textContent = 'Car';
    const pick = document.createElement('select');
    pick.className = 'car-pick';
    for (const c of this.h.getCars()) {
      const o = document.createElement('option');
      o.value = c.id;
      o.textContent = `${c.name} · ${c.info}`;
      pick.appendChild(o);
    }
    pick.value = this.h.getCar();
    pick.onchange = () => {
      this.h.setCar(pick.value);
      this.h.click?.();
    };
    carRow.append(carLabel, pick);
    p.append(carRow);
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
    // Graphics and sound.
    const note = document.createElement('p');
    note.className = 'sub';
    note.style.display = 'none';
    const reload = document.createElement('button');
    reload.type = 'button';
    reload.textContent = 'Reload now';
    reload.onclick = () => location.reload();
    note.append('The new graphics preset applies after a reload. ', reload);
    const audio = this.h.getAudio();
    const steps: [string, string][] = [['0', 'Off'], ['0.25', '25'], ['0.5', '50'], ['0.75', '75'], ['1', '100']];
    const nearest = (x: number): string => steps.reduce((a, b) => (Math.abs(+b[0] - x) < Math.abs(+a[0] - x) ? b : a))[0];
    p.append(
      seg<QualityChoice>('Graphics', [['auto', 'Auto'], ['low', 'Low'], ['medium', 'Medium'], ['high', 'High'], ['ultra', 'Ultra']], this.h.getQuality(), (v) => {
        note.style.display = this.h.setQuality(v) ? '' : 'none';
      }),
      note,
      seg<RadioChoice>('Radio', [['off', 'Off'], ...STATIONS.map((st): [RadioChoice, string] => [st.id, st.name.split(' ')[0]])], audio.station, (v) => this.h.setAudio({ station: v })),
      seg('Master volume', steps, nearest(audio.master), (v) => this.h.setAudio({ master: +v })),
      seg('Effects volume', steps, nearest(audio.effects), (v) => this.h.setAudio({ effects: +v })),
      seg('Music volume', steps, nearest(audio.music), (v) => this.h.setAudio({ music: +v })),
    );
    segClick = this.h.click;
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
      ['N', 'Next radio station'],
      ['Esc / P', 'Pause, assists, sound and graphics'],
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
