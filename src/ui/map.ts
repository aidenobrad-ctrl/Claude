// Island map: a painted base image (terrain, water, forests, roads, towns),
// the rotating minimap in the HUD and the full-screen map where a click or
// tap sets a GPS waypoint.
import type { World } from '../world/world';
import { WORLD_HALF, RIVER } from '../world/island';
import { BUILDING } from '../world/settlements';
import type { Route } from '../world/route';

const BASE = 1024;
const MPP = (WORLD_HALF * 2) / BASE; // metres per base-map pixel

const ROAD_STYLE: Record<string, [string, number]> = {
  highway: ['#f3c76b', 4.2],
  main: ['#f4efe4', 3],
  pass: ['#f4efe4', 2.6],
  street: ['#e6e2da', 2.2],
  dirt: ['#c9a77a', 1.8],
};

export interface Poi {
  name: string;
  x: number;
  z: number;
  kind: 'festival' | 'city' | 'village' | 'track' | 'harbor';
}

/** Points of interest shown on the full map. */
export function mapPois(world: World): Poi[] {
  const node = (id: string): { x: number; z: number } => world.nodes.find((n) => n.id === id) ?? { x: 0, z: 0 };
  const f = world.festival;
  return [
    { name: 'Halcyon Festival', x: f.stage.x, z: f.stage.z, kind: 'festival' },
    { name: 'Port Calder', x: 3200, z: -160, kind: 'city' },
    { name: 'Harbor', ...node('harbor'), kind: 'harbor' },
    { name: 'Proving Ground', x: world.provingOrigin.x - 100, z: world.provingOrigin.z + 200, kind: 'track' },
    { name: 'Ashby', ...node('farmVillage'), kind: 'village' },
    { name: 'Highcrest', ...node('northVillage'), kind: 'village' },
    { name: 'Redmesa', ...node('desertTown'), kind: 'village' },
    { name: 'Saltmarsh Bay', ...node('swBeach'), kind: 'village' },
    { name: 'Westhaven', ...node('westCoast'), kind: 'village' },
    { name: 'Rivermouth', ...node('riverEast'), kind: 'village' },
  ];
}

/**
 * Paint the base map: a terrain raster (n x n RGBA) scaled up, then vector
 * water, buildings, the proving circuit and roads on top. Repaint into the
 * same canvas when a finer raster arrives.
 */
export function paintBaseMap(world: World, raster: Uint8ClampedArray, n: number, circuit: { x: Float64Array; z: Float64Array; n: number } | null, into?: HTMLCanvasElement): HTMLCanvasElement {
  const small = document.createElement('canvas');
  small.width = small.height = n;
  (small.getContext('2d') as CanvasRenderingContext2D).putImageData(new ImageData(new Uint8ClampedArray(raster), n, n), 0, 0);
  const cv = into ?? document.createElement('canvas');
  cv.width = cv.height = BASE;
  const c = cv.getContext('2d') as CanvasRenderingContext2D;
  c.imageSmoothingEnabled = true;
  c.imageSmoothingQuality = 'high';
  c.drawImage(small, 0, 0, BASE, BASE);
  // Rivers and the lake as vectors (narrower than the raster cells).
  c.strokeStyle = '#4a96c0';
  c.lineJoin = c.lineCap = 'round';
  c.lineWidth = 52 / MPP;
  c.beginPath();
  RIVER.forEach((p, i) => {
    const [px, pz] = toMap(p.x, p.z);
    if (i === 0) c.moveTo(px, pz);
    else c.lineTo(px, pz);
  });
  c.stroke();
  // Buildings as small blocks.
  c.fillStyle = 'rgba(70, 66, 62, 0.85)';
  for (const bd of world.buildings.list) {
    const [px, pz] = toMap(bd.x, bd.z);
    const sz = Math.max(1.2, Math.min(4, Math.sqrt(bd.w * bd.d) / MPP));
    c.fillRect(px - sz / 2, pz - sz / 2, sz, sz);
    if (bd.kind === BUILDING.tower) c.fillRect(px - sz / 2, pz - sz / 2, sz, sz);
  }
  // The proving circuit.
  if (circuit) {
    for (const [col, w] of [
      ['rgba(30, 30, 30, 0.8)', 4.2],
      ['#f4efe4', 2.6],
    ] as [string, number][]) {
      c.strokeStyle = col;
      c.lineWidth = w;
      c.beginPath();
      for (let i = 0; i <= circuit.n; i += 2) {
        const [px, pz] = toMap(circuit.x[i % circuit.n], circuit.z[i % circuit.n]);
        if (i === 0) c.moveTo(px, pz);
        else c.lineTo(px, pz);
      }
      c.closePath();
      c.stroke();
    }
  }
  // Roads: dark casing then the fill, highways last.
  for (const pass of [0, 1]) {
    for (const cls of ['dirt', 'street', 'pass', 'main', 'highway']) {
      const [col, w] = ROAD_STYLE[cls];
      c.strokeStyle = pass === 0 ? 'rgba(40, 36, 30, 0.75)' : col;
      c.lineWidth = pass === 0 ? w + 1.6 : w;
      c.lineJoin = c.lineCap = 'round';
      for (const e of world.edges) {
        if (e.cls !== cls) continue;
        const r = e.road;
        c.beginPath();
        for (let i = 0; i < r.n; i += 3) {
          const [px, pz] = toMap(r.x[i], r.z[i]);
          if (i === 0) c.moveTo(px, pz);
          else c.lineTo(px, pz);
        }
        const [ex, ez] = toMap(r.x[r.n - 1], r.z[r.n - 1]);
        c.lineTo(ex, ez);
        c.stroke();
      }
    }
  }
  return cv;
}

/** World point to base-map pixel. */
export function toMap(x: number, z: number): [number, number] {
  return [(x + WORLD_HALF) / MPP, (z + WORLD_HALF) / MPP];
}

function drawRoute(c: CanvasRenderingContext2D, route: Route | null, from: number, width: number): void {
  if (!route || route.points.length < 2) return;
  c.strokeStyle = '#38d6ff';
  c.lineWidth = width;
  c.lineJoin = c.lineCap = 'round';
  c.beginPath();
  for (let i = Math.max(0, from); i < route.points.length; i += 2) {
    const [px, pz] = toMap(route.points[i].x, route.points[i].z);
    if (i <= from + 1) c.moveTo(px, pz);
    else c.lineTo(px, pz);
  }
  const last = route.points[route.points.length - 1];
  const [lx, lz] = toMap(last.x, last.z);
  c.lineTo(lx, lz);
  c.stroke();
}

function drawArrow(c: CanvasRenderingContext2D, size: number): void {
  c.fillStyle = '#ffffff';
  c.strokeStyle = '#0d1018';
  c.lineWidth = Math.max(1.5, size * 0.18);
  c.beginPath();
  c.moveTo(0, -size);
  c.lineTo(size * 0.72, size * 0.8);
  c.lineTo(0, size * 0.38);
  c.lineTo(-size * 0.72, size * 0.8);
  c.closePath();
  c.stroke();
  c.fill();
}

/** Round, heading-up minimap drawn into the HUD each frame. */
export class Minimap {
  readonly el: HTMLDivElement;
  private canvas: HTMLCanvasElement;
  private c: CanvasRenderingContext2D;
  private zoom = 1;

  constructor(
    parent: HTMLElement,
    private base: HTMLCanvasElement,
  ) {
    this.el = document.createElement('div');
    this.el.className = 'minimap';
    this.el.innerHTML = '<canvas></canvas><span class="mm-n">N</span><span class="mm-dist"></span>';
    parent.appendChild(this.el);
    this.canvas = this.el.querySelector('canvas') as HTMLCanvasElement;
    this.c = this.canvas.getContext('2d') as CanvasRenderingContext2D;
  }

  draw(x: number, z: number, yaw: number, speed: number, route: Route | null, routeFrom: number, distLeft: number): void {
    const css = this.el.clientWidth || 170;
    const dpr = Math.min(2, window.devicePixelRatio || 1);
    const px = Math.round(css * dpr);
    if (this.canvas.width !== px) {
      this.canvas.width = this.canvas.height = px;
    }
    const c = this.c;
    // Zoom out with speed: ~300 m across when slow, ~900 m at speed.
    const target = 300 + Math.min(600, speed * 9);
    this.zoom += (target - this.zoom) * 0.05;
    const span = this.zoom;
    const scale = px / (span / MPP);
    c.save();
    c.clearRect(0, 0, px, px);
    c.beginPath();
    c.arc(px / 2, px / 2, px / 2, 0, Math.PI * 2);
    c.clip();
    c.fillStyle = '#225a96';
    c.fillRect(0, 0, px, px);
    c.translate(px / 2, px / 2);
    // Heading up: forward (-sin yaw, -cos yaw) maps to screen up.
    c.rotate(yaw);
    c.scale(scale, scale);
    const [mx, mz] = toMap(x, z);
    c.translate(-mx, -mz);
    c.imageSmoothingEnabled = true;
    c.drawImage(this.base, 0, 0);
    drawRoute(c, route, routeFrom, 5 / scale + 2);
    c.restore();
    // Player arrow at the centre, always pointing up.
    c.save();
    c.translate(px / 2, px / 2);
    drawArrow(c, px * 0.06);
    c.restore();
    // North marker on the rim.
    const n = this.el.querySelector('.mm-n') as HTMLSpanElement;
    const r = css / 2 - 9;
    n.style.transform = `translate(${Math.sin(-yaw) * -r}px, ${-Math.cos(-yaw) * r}px)`;
    const d = this.el.querySelector('.mm-dist') as HTMLSpanElement;
    d.textContent = route ? `${(distLeft / 1000).toFixed(1)} km` : '';
  }

  setVisible(v: boolean): void {
    this.el.style.display = v ? '' : 'none';
  }
}

/** Full-screen map: pan, zoom, points of interest, click to set a waypoint. */
export class FullMap {
  readonly el: HTMLDivElement;
  private canvas: HTMLCanvasElement;
  private c: CanvasRenderingContext2D;
  private cx = BASE / 2;
  private cz = BASE / 2;
  private zoom = 1;
  private drag: { x: number; y: number; cx: number; cz: number; moved: boolean } | null = null;
  private pinch: { d: number; zoom: number } | null = null;
  private pointers = new Map<number, { x: number; y: number }>();
  visible = false;
  private player = { x: 0, z: 0, yaw: 0 };
  private route: Route | null = null;
  private waypoint: { x: number; z: number } | null = null;

  constructor(
    parent: HTMLElement,
    private base: HTMLCanvasElement,
    private pois: Poi[],
    private onWaypoint: (x: number, z: number) => void,
    private onClose: () => void,
  ) {
    this.el = document.createElement('div');
    this.el.className = 'fullmap';
    this.el.innerHTML = `<canvas></canvas>
      <div class="fm-top"><b>HALCYON ISLAND</b><span>Tap or click to set a waypoint · drag to pan · scroll or pinch to zoom</span><button type="button" class="fm-close">Close (M)</button></div>
      <div class="fm-legend"><span><i style="background:#f3c76b"></i>Highway</span><span><i style="background:#f4efe4"></i>Road</span><span><i style="background:#c9a77a"></i>Dirt track</span><span><i style="background:#38d6ff"></i>Your route</span></div>`;
    parent.appendChild(this.el);
    this.el.style.display = 'none';
    this.canvas = this.el.querySelector('canvas') as HTMLCanvasElement;
    this.c = this.canvas.getContext('2d') as CanvasRenderingContext2D;
    (this.el.querySelector('.fm-close') as HTMLButtonElement).onclick = () => this.onClose();
    const cv = this.canvas;
    cv.addEventListener('pointerdown', (e) => {
      cv.setPointerCapture(e.pointerId);
      this.pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
      if (this.pointers.size === 1) this.drag = { x: e.clientX, y: e.clientY, cx: this.cx, cz: this.cz, moved: false };
      else if (this.pointers.size === 2) {
        const [a, b] = [...this.pointers.values()];
        this.pinch = { d: Math.hypot(a.x - b.x, a.y - b.y), zoom: this.zoom };
        this.drag = null;
      }
    });
    cv.addEventListener('pointermove', (e) => {
      if (!this.pointers.has(e.pointerId)) return;
      this.pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
      if (this.pinch && this.pointers.size === 2) {
        const [a, b] = [...this.pointers.values()];
        this.zoom = Math.max(1, Math.min(8, this.pinch.zoom * (Math.hypot(a.x - b.x, a.y - b.y) / this.pinch.d)));
        this.draw();
      } else if (this.drag) {
        const k = this.pixelsPerBase();
        const dx = e.clientX - this.drag.x;
        const dy = e.clientY - this.drag.y;
        if (Math.abs(dx) + Math.abs(dy) > 6) this.drag.moved = true;
        this.cx = this.drag.cx - dx / k;
        this.cz = this.drag.cz - dy / k;
        this.draw();
      }
    });
    const up = (e: PointerEvent): void => {
      const wasDrag = this.drag;
      this.pointers.delete(e.pointerId);
      if (this.pointers.size < 2) this.pinch = null;
      if (wasDrag && !wasDrag.moved && this.pointers.size === 0) {
        // A tap: set the waypoint there.
        const r = cv.getBoundingClientRect();
        const [x, z] = this.screenToWorld(e.clientX - r.left, e.clientY - r.top);
        this.waypoint = { x, z };
        this.onWaypoint(x, z);
        this.draw();
      }
      if (this.pointers.size === 0) this.drag = null;
    };
    cv.addEventListener('pointerup', up);
    cv.addEventListener('pointercancel', up);
    cv.addEventListener(
      'wheel',
      (e) => {
        e.preventDefault();
        this.zoom = Math.max(1, Math.min(8, this.zoom * (e.deltaY < 0 ? 1.15 : 1 / 1.15)));
        this.draw();
      },
      { passive: false },
    );
  }

  private pixelsPerBase(): number {
    const w = this.canvas.clientWidth || 800;
    const h = this.canvas.clientHeight || 600;
    return (Math.min(w, h) / BASE) * this.zoom;
  }

  private screenToWorld(sx: number, sy: number): [number, number] {
    const k = this.pixelsPerBase();
    const w = this.canvas.clientWidth;
    const h = this.canvas.clientHeight;
    const bx = this.cx + (sx - w / 2) / k;
    const bz = this.cz + (sy - h / 2) / k;
    return [bx * MPP - WORLD_HALF, bz * MPP - WORLD_HALF];
  }

  open(player: { x: number; z: number; yaw: number }, route: Route | null, waypoint: { x: number; z: number } | null): void {
    this.visible = true;
    this.el.style.display = '';
    this.player = player;
    this.route = route;
    this.waypoint = waypoint;
    const [mx, mz] = toMap(player.x, player.z);
    this.cx = mx;
    this.cz = mz;
    this.zoom = 2.2;
    this.draw();
  }

  close(): void {
    this.visible = false;
    this.el.style.display = 'none';
  }

  setRoute(route: Route | null): void {
    this.route = route;
    if (this.visible) this.draw();
  }

  draw(): void {
    const dpr = Math.min(2, window.devicePixelRatio || 1);
    const w = this.canvas.clientWidth || 800;
    const h = this.canvas.clientHeight || 600;
    if (this.canvas.width !== Math.round(w * dpr) || this.canvas.height !== Math.round(h * dpr)) {
      this.canvas.width = Math.round(w * dpr);
      this.canvas.height = Math.round(h * dpr);
    }
    const c = this.c;
    c.setTransform(dpr, 0, 0, dpr, 0, 0);
    c.fillStyle = '#225a96';
    c.fillRect(0, 0, w, h);
    const k = this.pixelsPerBase();
    c.save();
    c.translate(w / 2, h / 2);
    c.scale(k, k);
    c.translate(-this.cx, -this.cz);
    c.imageSmoothingEnabled = true;
    c.drawImage(this.base, 0, 0);
    drawRoute(c, this.route, 0, 3.5 / Math.sqrt(k) + 1.5);
    c.restore();
    const toScreen = (x: number, z: number): [number, number] => {
      const [bx, bz] = toMap(x, z);
      return [(bx - this.cx) * k + w / 2, (bz - this.cz) * k + h / 2];
    };
    // Points of interest.
    c.font = '600 12px system-ui, sans-serif';
    c.textAlign = 'center';
    for (const p of this.pois) {
      const [sx, sy] = toScreen(p.x, p.z);
      c.fillStyle = p.kind === 'festival' ? '#ff6b2c' : p.kind === 'track' ? '#14b8a6' : '#f5f0e6';
      c.beginPath();
      c.arc(sx, sy, p.kind === 'festival' ? 7 : 5, 0, Math.PI * 2);
      c.fill();
      c.strokeStyle = '#0d1018';
      c.lineWidth = 2;
      c.stroke();
      c.fillStyle = '#ffffff';
      c.strokeStyle = 'rgba(10, 14, 22, 0.85)';
      c.lineWidth = 3;
      c.strokeText(p.name, sx, sy - 11);
      c.fillText(p.name, sx, sy - 11);
    }
    if (this.waypoint) {
      const [sx, sy] = toScreen(this.waypoint.x, this.waypoint.z);
      c.fillStyle = '#38d6ff';
      c.strokeStyle = '#0d1018';
      c.lineWidth = 2;
      c.beginPath();
      c.moveTo(sx, sy);
      c.lineTo(sx - 8, sy - 16);
      c.arc(sx, sy - 18, 8, Math.PI * 0.85, Math.PI * 2.15);
      c.closePath();
      c.fill();
      c.stroke();
    }
    const [px, py] = toScreen(this.player.x, this.player.z);
    c.save();
    c.translate(px, py);
    c.rotate(-this.player.yaw);
    drawArrow(c, 9);
    c.restore();
  }
}
