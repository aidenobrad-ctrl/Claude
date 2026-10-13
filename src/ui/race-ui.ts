// The festival's races on screen: the events menu (every race with its
// distance and kind, and the rivals' difficulty), the race HUD (position,
// lap, time, the gap to the car ahead), the countdown, and the results.
import type { Difficulty } from '../ai/ai-driver';
import type { Entrant, RaceEvent, RaceSession } from '../ai/race-session';
import { formatTime } from './hud';

export interface EventsHandlers {
  start(id: string, difficulty: Difficulty): void;
  close(): void;
  click?(): void;
}

const KIND: Record<string, string> = { circuit: 'Circuit', sprint: 'Sprint', drag: 'Drag' };
/** The race HUD's timing loops, m apart. */
const MARK = 5;
const DIFF: [Difficulty, string][] = [
  ['novice', 'Novice'],
  ['average', 'Average'],
  ['pro', 'Pro'],
  ['legend', 'Legend'],
];

/** The festival menu: pick a race and the rivals' difficulty. */
export class EventsMenu {
  readonly el: HTMLDivElement;
  open = false;

  constructor(
    parent: HTMLElement,
    private h: EventsHandlers,
  ) {
    this.el = document.createElement('div');
    this.el.className = 'overlay events';
    this.el.style.display = 'none';
    parent.appendChild(this.el);
    this.el.addEventListener('pointerdown', (e) => {
      if (e.target === this.el) this.h.close();
    });
  }

  show(events: RaceEvent[], difficulty: Difficulty): void {
    this.open = true;
    const p = document.createElement('div');
    p.className = 'panel';
    p.innerHTML = '<h2>Festival races</h2><p class="sub">Race the festival\'s drivers on the island. Your car, their pick of rivals.</p>';
    let diff = difficulty;
    const row = document.createElement('div');
    row.className = 'row';
    const label = document.createElement('span');
    label.textContent = 'Rivals';
    const seg = document.createElement('div');
    seg.className = 'seg';
    for (const [v, text] of DIFF) {
      const b = document.createElement('button');
      b.type = 'button';
      b.textContent = text;
      if (v === diff) b.classList.add('on');
      b.onclick = () => {
        for (const x of seg.querySelectorAll('button')) x.classList.remove('on');
        b.classList.add('on');
        diff = v;
        this.h.click?.();
      };
      seg.appendChild(b);
    }
    row.append(label, seg);
    p.append(row);
    const list = document.createElement('div');
    list.className = 'event-list';
    for (const ev of events) {
      const card = document.createElement('button');
      card.type = 'button';
      card.className = 'event-card';
      const km = (ev.distance / 1000).toFixed(1);
      const field = ev.rival ? `1 on 1 · ${ev.rival.name}` : ev.gridSize <= 2 ? 'Side by side' : `${ev.gridSize} cars`;
      card.innerHTML = `<b>${ev.name}</b><span class="meta">${KIND[ev.kind] ?? ev.kind} · ${km} km · ${field}${ev.surface !== 0 ? ' · dirt' : ''}</span><span class="desc">${ev.description}</span>`;
      card.onclick = () => {
        this.h.click?.();
        this.h.start(ev.id, diff);
      };
      list.append(card);
    }
    p.append(list);
    const actions = document.createElement('div');
    actions.className = 'actions';
    const back = document.createElement('button');
    back.textContent = 'Back to the road';
    back.onclick = () => this.h.close();
    actions.append(back);
    p.append(actions);
    this.el.replaceChildren(p);
    this.el.style.display = '';
  }

  /** A message in place of the list (building the race). */
  busy(text: string): void {
    this.el.replaceChildren();
    const p = document.createElement('div');
    p.className = 'panel';
    p.innerHTML = `<h2>${text}</h2>`;
    this.el.append(p);
    this.el.style.display = '';
  }

  hide(): void {
    this.open = false;
    this.el.style.display = 'none';
  }
}

export interface RaceHudHandlers {
  /** Leave the results (or quit the race). */
  done(): void;
  click?(): void;
  /** The results are up (for a fanfare): the player's position. */
  finished?(position: number): void;
}

/** The race's own HUD: position, lap and time, the countdown, then the results. */
export class RaceHud {
  readonly el: HTMLDivElement;
  private top: HTMLDivElement;
  private big: HTMLDivElement;
  private results: HTMLDivElement;
  private shownResults = false;
  private lastCount = -1;
  /** When each car passed each MARK metres of its race (the race clock), for the gaps: a timing loop every 5 m. */
  private marks = new Map<Entrant, { t: Float64Array; k: number; p: number; c: number }>();
  private gapsAt = 0;

  /** The results are on screen (Enter or the pad's A continues). */
  get resultsOpen(): boolean {
    return this.shownResults && this.results.style.display !== 'none';
  }

  constructor(
    parent: HTMLElement,
    private h: RaceHudHandlers,
  ) {
    this.el = document.createElement('div');
    this.el.className = 'race-hud';
    this.el.innerHTML = '<div class="race-top"></div><div class="race-big"></div><div class="race-results overlay" style="display:none"></div>';
    parent.appendChild(this.el);
    this.top = this.el.querySelector('.race-top') as HTMLDivElement;
    this.big = this.el.querySelector('.race-big') as HTMLDivElement;
    this.results = this.el.querySelector('.race-results') as HTMLDivElement;
    this.el.style.display = 'none';
  }

  start(): void {
    this.shownResults = false;
    this.lastCount = -1;
    this.marks.clear();
    this.gapsAt = 0;
    // Nothing of the last race's (its gaps are carried from frame to frame).
    this.top.replaceChildren();
    this.results.style.display = 'none';
    this.el.style.display = '';
  }

  hide(): void {
    this.el.style.display = 'none';
    this.results.style.display = 'none';
  }

  /** Each frame: returns the countdown number when it changes (for the beep), 0 at GO, or -1. */
  update(s: RaceSession): number {
    const me = s.playerEntrant;
    let beep = -1;
    if (s.clock < 0) {
      const n = Math.ceil(-s.clock);
      this.big.textContent = String(n);
      this.big.className = 'race-big show';
      if (n !== this.lastCount) beep = n;
      this.lastCount = n;
    } else if (s.clock < 1.2) {
      this.big.textContent = 'GO';
      this.big.className = 'race-big show go';
      if (this.lastCount !== 0) beep = 0;
      this.lastCount = 0;
    } else this.big.className = 'race-big';
    if (me) {
      const ev = s.event;
      const pos = `<b>${me.position}</b><small>/${s.entrants.length}</small>`;
      const lap = ev.kind === 'circuit' ? `<span>Lap ${s.lap(me)}/${ev.laps}</span>` : `<span>${Math.max(0, (ev.distance - Math.max(0, me.progress)) / 1000).toFixed(1)} km to go</span>`;
      const t = formatTime(Math.max(0, me.finished ? me.finishTime : s.clock));
      const wrong = me.wrongWay && !me.finished ? '<span class="warn">Wrong way</span>' : '';
      this.time(s);
      // The gaps change slowly: four times a second is plenty, and keeps them readable.
      let gaps = s.clock < 0 ? '' : (this.top.querySelector('.race-gaps')?.outerHTML ?? '');
      if (s.clock >= 0 && s.clock >= this.gapsAt) {
        this.gapsAt = s.clock + 0.25;
        gaps = this.gaps(s, me);
      }
      this.top.innerHTML = `<div class="race-pos">${pos}</div><div class="race-info">${lap}<span>${t}</span>${wrong}${gaps}</div>`;
    }
    if (s.state === 'finished' && s.sinceFinish > 1.5 && !this.shownResults) this.showResults(s);
    return beep;
  }

  /**
   * Record, for each car, the race clock at each 5 m of its progress it has passed. A frame can cover several
   * marks (or, when the tab was away, hundreds): each gets its time from the car's progress over the frame.
   */
  private time(s: RaceSession): void {
    if (s.clock < 0) return;
    const n = Math.ceil(s.event.distance / MARK) + 2;
    for (const e of s.entrants) {
      let m = this.marks.get(e);
      if (!m) this.marks.set(e, (m = { t: new Float64Array(n).fill(NaN), k: -1, p: 0, c: 0 }));
      // Still behind the start line: it crosses it after now.
      if (e.progress < 0) {
        m.c = s.clock;
        continue;
      }
      const p = e.progress;
      const k = Math.min(n - 1, Math.floor(p / MARK));
      while (m.k < k) {
        const j = ++m.k;
        const f = p > m.p ? Math.max(0, Math.min(1, (j * MARK - m.p) / (p - m.p))) : 1;
        m.t[j] = m.c + (s.clock - m.c) * f;
      }
      m.p = p;
      m.c = s.clock;
    }
  }

  /** The time to the car ahead (since it passed where I am) and to the car behind (since I passed where it is). */
  private gaps(s: RaceSession, me: Entrant): string {
    if (s.clock < 1 || me.finished) return '';
    const order = s.standings;
    const i = order.indexOf(me);
    const at = (e: Entrant, progress: number): number => {
      const m = this.marks.get(e);
      const k = Math.floor(Math.max(0, progress) / MARK);
      return m && k <= m.k ? m.t[k] : NaN;
    };
    const short = (e: Entrant): string => e.name.replace(/^(\S)\S*\.? /, '$1. ');
    const parts: string[] = [];
    const ahead = i > 0 ? order[i - 1] : null;
    const behind = i < order.length - 1 ? order[i + 1] : null;
    if (ahead && !ahead.finished) {
      const g = s.clock - at(ahead, me.progress);
      if (Number.isFinite(g)) parts.push(`<span class="gap up">▲ ${short(ahead)} ${g.toFixed(1)}</span>`);
    }
    if (behind) {
      const g = s.clock - at(me, behind.progress);
      if (Number.isFinite(g)) parts.push(`<span class="gap down">▼ ${short(behind)} ${g.toFixed(1)}</span>`);
    }
    return parts.length ? `<span class="race-gaps">${parts.join('')}</span>` : '';
  }

  private showResults(s: RaceSession): void {
    this.shownResults = true;
    const p = document.createElement('div');
    p.className = 'panel';
    const me = s.playerEntrant;
    const head = me && me.position === 1 ? 'You won!' : me ? `You finished ${ordinal(me.position)}` : 'Results';
    p.innerHTML = `<h2>${head}</h2><p class="sub">${s.event.name}</p>`;
    const table = document.createElement('div');
    table.className = 'results';
    const winner = s.standings[0];
    for (const e of s.standings) {
      const r = document.createElement('div');
      r.className = `res-row${e.player ? ' me' : ''}`;
      const time = e.finished ? (e === winner ? formatTime(e.finishTime) : `+${(e.finishTime - winner.finishTime).toFixed(2)}`) : 'racing';
      const extra = s.event.kind === 'drag' && e.finished ? ` · ${(e.finishSpeed * 3.6).toFixed(0)} km/h` : s.event.kind === 'circuit' && Number.isFinite(e.bestLap) ? ` · best ${formatTime(e.bestLap)}` : '';
      r.innerHTML = `<span class="p">${e.position}</span><span class="n">${e.name}</span><span class="t">${time}${extra}</span>`;
      table.append(r);
    }
    p.append(table);
    const actions = document.createElement('div');
    actions.className = 'actions';
    const ok = document.createElement('button');
    ok.className = 'primary';
    ok.textContent = 'Continue';
    ok.onclick = () => {
      this.h.click?.();
      this.h.done();
    };
    actions.append(ok);
    p.append(actions);
    this.results.replaceChildren(p);
    this.results.style.display = '';
    if (me) this.h.finished?.(me.position);
  }
}

function ordinal(n: number): string {
  const s = ['th', 'st', 'nd', 'rd'];
  const v = n % 100;
  return n + (s[(v - 20) % 10] || s[v] || s[0]);
}
