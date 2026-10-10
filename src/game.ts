// Game shell: owns the renderer, the simulation clock, input and UI, and
// wires them together each frame.
import * as THREE from 'three';
import { FixedStepper, SIM_DT } from './engine/loop';
import { Input, neutralControls, type Controls } from './engine/input';
import { Perf } from './engine/perf';
import { Sim } from './sim';

/**
 * Vertical FOV that keeps at least `minHorizontal` degrees across the screen,
 * so tall phone screens in portrait still see the road around the car.
 */
export function fovForAspect(vertical: number, minHorizontal: number, aspect: number): number {
  const hFromV = (2 * Math.atan(Math.tan((vertical * Math.PI) / 360) * aspect) * 180) / Math.PI;
  if (hFromV >= minHorizontal) return vertical;
  const v = (2 * Math.atan(Math.tan((minHorizontal * Math.PI) / 360) / aspect) * 180) / Math.PI;
  return Math.min(v, 100);
}

export function isMobileDevice(): boolean {
  if (typeof navigator === 'undefined') return false;
  const ua = navigator.userAgent;
  const coarse = typeof matchMedia !== 'undefined' && matchMedia('(pointer: coarse)').matches;
  return /Android|iPhone|iPad|iPod|Mobile/i.test(ua) || (coarse && navigator.maxTouchPoints > 0);
}

/**
 * Device pixel ratio that keeps the internal resolution under a pixel budget
 * (mobile: 1280x720, desktop: 1920x1080 by default). Never below 0.5.
 */
export function pixelRatioForBudget(cssW: number, cssH: number, dpr: number, maxPixels: number): number {
  const fit = Math.sqrt(maxPixels / Math.max(1, cssW * cssH));
  return Math.max(0.5, Math.min(dpr, fit));
}

export interface GameOptions {
  canvas: HTMLCanvasElement;
  ui: HTMLElement;
  /** Manual stepping for headless tests: no requestAnimationFrame loop. */
  test: boolean;
  seed: number;
}

export class Game {
  readonly test: boolean;
  readonly canvas: HTMLCanvasElement;
  readonly uiRoot: HTMLElement;
  readonly renderer: THREE.WebGLRenderer;
  readonly scene = new THREE.Scene();
  readonly camera = new THREE.PerspectiveCamera(60, 1, 0.1, 4000);
  readonly sim: Sim;
  readonly input = new Input();
  readonly perf = new Perf();
  readonly stepper = new FixedStepper();
  controls: Controls = neutralControls();
  paused = false;
  hidden = false;
  contextLost = false;
  /** Maximum internal render pixels; quality presets change this. */
  pixelBudget = isMobileDevice() ? 1280 * 720 : 1920 * 1080;
  fatalError: Error | null = null;
  private raf = 0;
  private lastT = 0;
  private probeMesh: THREE.Mesh;
  private hud: HTMLDivElement;

  constructor(opts: GameOptions) {
    this.test = opts.test;
    this.canvas = opts.canvas;
    this.uiRoot = opts.ui;
    this.sim = new Sim(opts.seed);
    this.renderer = new THREE.WebGLRenderer({
      canvas: this.canvas,
      antialias: true,
      powerPreference: 'high-performance',
      // Tests read pixels back after rendering.
      preserveDrawingBuffer: opts.test,
    });
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;

    this.scene.background = new THREE.Color(0x9cc4e4);
    this.scene.fog = new THREE.Fog(0x9cc4e4, 200, 1500);
    const hemi = new THREE.HemisphereLight(0xdfefff, 0x4a5a3a, 1.2);
    const sun = new THREE.DirectionalLight(0xfff1dc, 2.2);
    sun.position.set(80, 120, 40);
    this.scene.add(hemi, sun);

    const grid = new THREE.GridHelper(2000, 200, 0x335533, 0x446644);
    const ground = new THREE.Mesh(
      new THREE.PlaneGeometry(2000, 2000).rotateX(-Math.PI / 2),
      new THREE.MeshLambertMaterial({ color: 0x5c7a45 }),
    );
    ground.position.y = -0.01;
    this.scene.add(ground, grid);
    this.probeMesh = new THREE.Mesh(
      new THREE.BoxGeometry(1.9, 1.3, 4.4),
      new THREE.MeshStandardMaterial({ color: 0xd8342c, roughness: 0.4, metalness: 0.2 }),
    );
    this.probeMesh.position.y = 0.65;
    this.scene.add(this.probeMesh);

    this.hud = document.createElement('div');
    this.hud.className = 'hud-debug';
    this.uiRoot.appendChild(this.hud);
    document.getElementById('boot')?.remove();

    this.input.attach(window);
    // Mobile browsers drop the GL context when backgrounded. Keep the page
    // alive and let three.js rebuild GPU resources when it comes back.
    this.canvas.addEventListener('webglcontextlost', (e) => {
      e.preventDefault();
      this.contextLost = true;
    });
    this.canvas.addEventListener('webglcontextrestored', () => {
      this.contextLost = false;
      this.resize();
    });
    window.addEventListener('resize', () => this.resize());
    document.addEventListener('visibilitychange', () => this.onVisibility());
    this.resize();
  }

  start(): void {
    if (this.test) {
      this.renderNow();
      return;
    }
    this.lastT = performance.now();
    this.raf = requestAnimationFrame(this.frame);
  }

  private frame = (t: number): void => {
    try {
      this.frameInner(t);
      this.raf = requestAnimationFrame(this.frame);
    } catch (e) {
      this.fatal(e);
    }
  };

  /** Stop the loop and show what went wrong instead of freezing silently. */
  fatal(e: unknown): void {
    const err = e instanceof Error ? e : new Error(String(e));
    if (this.fatalError) return;
    this.fatalError = err;
    cancelAnimationFrame(this.raf);
    console.error('Halcyon Roads stopped:', err);
    const box = document.createElement('div');
    box.className = 'fatal';
    box.innerHTML = '<h2>Something went wrong</h2><p></p><button type="button">Reload</button>';
    (box.querySelector('p') as HTMLElement).textContent = err.message;
    (box.querySelector('button') as HTMLButtonElement).onclick = () => location.reload();
    this.uiRoot.appendChild(box);
  }

  private frameInner(t: number): void {
    const dt = (t - this.lastT) / 1000;
    this.lastT = t;
    this.perf.frame();
    this.controls = this.input.poll();
    if (this.controls.pressed.pause) this.paused = !this.paused;
    if (!this.paused && !this.hidden) {
      this.perf.begin('sim');
      this.stepper.advance(dt, () => this.sim.step(this.controls));
      this.perf.end('sim');
    }
    this.render();
  }

  /** Advance exactly n fixed steps (test mode). Input edges apply on the first step. */
  stepTicks(n: number): void {
    this.controls = this.input.poll();
    for (let i = 0; i < n; i++) {
      this.sim.step(this.controls);
      if (i === 0) for (const k in this.controls.pressed) (this.controls.pressed as Record<string, boolean>)[k] = false;
    }
  }

  renderNow(): void {
    this.render();
  }

  private render(): void {
    if (this.contextLost) return;
    this.perf.begin('render');
    const p = this.sim.probe;
    this.probeMesh.position.set(p.pos.x, 0.65, p.pos.z);
    this.probeMesh.rotation.y = p.yaw;
    const fx = -Math.sin(p.yaw);
    const fz = -Math.cos(p.yaw);
    this.camera.position.set(p.pos.x - fx * 9, 3.2, p.pos.z - fz * 9);
    this.camera.lookAt(p.pos.x + fx * 4, 1.0, p.pos.z + fz * 4);
    this.renderer.render(this.scene, this.camera);
    const info = this.renderer.info.render;
    this.perf.counters.draws = info.calls;
    this.perf.counters.tris = info.triangles;
    this.perf.end('render');
    this.hud.textContent = `${(p.vel.len() * 3.6).toFixed(0)} km/h  ·  t=${this.sim.time.toFixed(2)} s`;
  }

  resize(): void {
    const w = this.canvas.clientWidth || window.innerWidth;
    const h = this.canvas.clientHeight || window.innerHeight;
    this.renderer.setPixelRatio(pixelRatioForBudget(w, h, window.devicePixelRatio || 1, this.pixelBudget));
    this.renderer.setSize(w, h, false);
    this.camera.aspect = w / Math.max(1, h);
    this.camera.fov = fovForAspect(60, 72, this.camera.aspect);
    this.camera.updateProjectionMatrix();
  }

  private onVisibility(): void {
    this.hidden = document.hidden;
    // Do not simulate the time spent in the background.
    this.lastT = performance.now();
    this.stepper.reset();
    this.perf.resetFrameClock();
    if (this.hidden) this.input.clearKeys();
  }

  get simDt(): number {
    return SIM_DT;
  }
}
