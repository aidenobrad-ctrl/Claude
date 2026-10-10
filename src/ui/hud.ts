// Driving HUD: tachometer and speed gauge, gear, driver-aid lights, lap
// timer and short toasts. The gauge is a canvas redrawn each frame.

export interface HudData {
  kmh: number;
  rpm: number;
  redline: number;
  limiter: number;
  gear: number;
  shiftLight: boolean;
  abs: boolean;
  tcs: boolean;
  esc: boolean;
  lap?: { lap: number; current: number; last: number; best: number } | null;
  units: 'kmh' | 'mph';
}

export function formatTime(t: number): string {
  if (!Number.isFinite(t)) return '–:––.––';
  const m = Math.floor(t / 60);
  const s = t - m * 60;
  return `${m}:${s.toFixed(2).padStart(5, '0')}`;
}

export class Hud {
  readonly el: HTMLDivElement;
  private gauge: HTMLCanvasElement;
  private g: CanvasRenderingContext2D;
  private aids: Record<'abs' | 'tcs' | 'esc', HTMLSpanElement>;
  private lapEl: HTMLDivElement;
  private toastEl: HTMLDivElement;
  private toastTimer = 0;
  private size = 0;
  private dpr = 1;

  constructor(parent: HTMLElement) {
    this.el = document.createElement('div');
    this.el.className = 'hud';
    this.el.innerHTML = `
      <div class="hud-lap"></div>
      <div class="hud-gauge-wrap"><canvas class="hud-gauge"></canvas>
        <div class="hud-aids"><span data-a="abs">ABS</span><span data-a="tcs">TCS</span><span data-a="esc">ESC</span></div>
      </div>
      <div class="hud-toast"></div>`;
    parent.appendChild(this.el);
    this.gauge = this.el.querySelector('.hud-gauge') as HTMLCanvasElement;
    this.g = this.gauge.getContext('2d') as CanvasRenderingContext2D;
    const q = (a: string): HTMLSpanElement => this.el.querySelector(`[data-a="${a}"]`) as HTMLSpanElement;
    this.aids = { abs: q('abs'), tcs: q('tcs'), esc: q('esc') };
    this.lapEl = this.el.querySelector('.hud-lap') as HTMLDivElement;
    this.toastEl = this.el.querySelector('.hud-toast') as HTMLDivElement;
  }

  toast(text: string, seconds = 1.6): void {
    this.toastEl.textContent = text;
    this.toastEl.classList.add('show');
    this.toastTimer = seconds;
  }

  setVisible(v: boolean): void {
    this.el.style.display = v ? '' : 'none';
  }

  update(d: HudData, dt: number): void {
    if (this.toastTimer > 0) {
      this.toastTimer -= dt;
      if (this.toastTimer <= 0) this.toastEl.classList.remove('show');
    }
    this.aids.abs.classList.toggle('on', d.abs);
    this.aids.tcs.classList.toggle('on', d.tcs);
    this.aids.esc.classList.toggle('on', d.esc);
    if (d.lap) {
      this.lapEl.style.display = '';
      this.lapEl.innerHTML = `<b>LAP ${d.lap.lap}</b><span class="big">${formatTime(d.lap.current)}</span><span>LAST ${formatTime(d.lap.last)}</span><span>BEST ${formatTime(d.lap.best)}</span>`;
    } else this.lapEl.style.display = 'none';
    this.drawGauge(d);
  }

  private drawGauge(d: HudData): void {
    const css = this.gauge.clientWidth || 180;
    const dpr = Math.min(2, window.devicePixelRatio || 1);
    if (css !== this.size || dpr !== this.dpr) {
      this.size = css;
      this.dpr = dpr;
      this.gauge.width = Math.round(css * dpr);
      this.gauge.height = Math.round(css * dpr);
    }
    const g = this.g;
    const S = this.gauge.width;
    g.setTransform(1, 0, 0, 1, 0, 0);
    g.clearRect(0, 0, S, S);
    g.scale(S / 200, S / 200);
    const cx = 100;
    const cy = 100;
    const a0 = Math.PI * 0.75;
    const sweep = Math.PI * 1.5;
    const maxRpm = Math.ceil((d.limiter + 300) / 1000) * 1000;
    const ang = (rpm: number): number => a0 + sweep * Math.min(1, Math.max(0, rpm / maxRpm));
    // Backplate.
    g.fillStyle = 'rgba(8,11,17,0.62)';
    g.beginPath();
    g.arc(cx, cy, 96, 0, Math.PI * 2);
    g.fill();
    // Track and redline band.
    g.lineCap = 'butt';
    g.lineWidth = 9;
    g.strokeStyle = 'rgba(255,255,255,0.12)';
    g.beginPath();
    g.arc(cx, cy, 82, a0, a0 + sweep);
    g.stroke();
    g.strokeStyle = 'rgba(232,58,48,0.85)';
    g.beginPath();
    g.arc(cx, cy, 82, ang(d.redline), a0 + sweep);
    g.stroke();
    // Filled rpm arc.
    const frac = Math.min(1, d.rpm / maxRpm);
    const hot = d.rpm >= d.redline * 0.97;
    g.strokeStyle = hot ? '#ff4d3d' : '#ffb547';
    g.lineWidth = 9;
    g.beginPath();
    g.arc(cx, cy, 82, a0, a0 + sweep * frac);
    g.stroke();
    // Ticks and numbers every 1000 rpm.
    g.fillStyle = 'rgba(255,255,255,0.85)';
    g.font = '600 12px system-ui, sans-serif';
    g.textAlign = 'center';
    g.textBaseline = 'middle';
    for (let r = 0; r <= maxRpm; r += 1000) {
      const a = ang(r);
      const c = Math.cos(a);
      const s = Math.sin(a);
      g.strokeStyle = r >= d.redline ? '#ff6a5c' : 'rgba(255,255,255,0.7)';
      g.lineWidth = 2;
      g.beginPath();
      g.moveTo(cx + c * 70, cy + s * 70);
      g.lineTo(cx + c * 76, cy + s * 76);
      g.stroke();
      g.fillText(String(r / 1000), cx + c * 59, cy + s * 59);
    }
    // Speed.
    const speed = d.units === 'mph' ? Math.abs(d.kmh) / 1.609344 : Math.abs(d.kmh);
    g.fillStyle = '#fff';
    g.font = '800 46px system-ui, sans-serif';
    g.fillText(String(Math.round(speed)), cx, cy - 4);
    g.font = '600 11px system-ui, sans-serif';
    g.fillStyle = 'rgba(255,255,255,0.65)';
    g.fillText(d.units === 'mph' ? 'MPH' : 'KM/H', cx, cy + 22);
    // Gear box.
    const gear = d.gear === -1 ? 'R' : d.gear === 0 ? 'N' : String(d.gear);
    g.fillStyle = d.shiftLight ? '#ff4d3d' : 'rgba(255,255,255,0.12)';
    roundRect(g, cx - 17, cy + 36, 34, 30, 7);
    g.fill();
    g.fillStyle = '#fff';
    g.font = '800 22px system-ui, sans-serif';
    g.fillText(gear, cx, cy + 52);
  }
}

function roundRect(g: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, r: number): void {
  g.beginPath();
  g.moveTo(x + r, y);
  g.arcTo(x + w, y, x + w, y + h, r);
  g.arcTo(x + w, y + h, x, y + h, r);
  g.arcTo(x, y + h, x, y, r);
  g.arcTo(x, y, x + w, y, r);
  g.closePath();
}
