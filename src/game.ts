// Game shell: owns the renderer, the simulation clock, input, views and UI,
// and wires them together each frame.
import * as THREE from 'three';
import { FixedStepper, SIM_DT } from './engine/loop';
import { Input, neutralControls, type Action, type Controls } from './engine/input';
import { Perf } from './engine/perf';
import { Sky } from './engine/render/sky';
import { CameraRig, CAM_LABELS } from './engine/render/camera-rig';
import { Puffs, Skidmarks } from './engine/render/effects';
import { buildTrackView } from './world/render/track-view';
import { SURFACES } from './world/surfaces';
import { newHit } from './world/ground';
import { CarView } from './vehicles/render/car-view';
import { CARS } from './vehicles/cars';
import { Hud } from './ui/hud';
import { TouchControls } from './ui/touch';
import { HelpCard, PauseMenu } from './ui/menus';
import { Sim } from './sim';

export { fovForAspect } from './engine/render/fov';

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
  /** Force touch controls on or off (default: on for touch devices). */
  touch?: boolean;
}

const tmpVel = new THREE.Vector3();

export class Game {
  readonly test: boolean;
  readonly canvas: HTMLCanvasElement;
  readonly uiRoot: HTMLElement;
  readonly renderer: THREE.WebGLRenderer;
  readonly scene = new THREE.Scene();
  readonly rig = new CameraRig();
  readonly sim: Sim;
  readonly input = new Input();
  readonly perf = new Perf();
  readonly stepper = new FixedStepper();
  readonly mobile = isMobileDevice();
  readonly sky: Sky;
  readonly hud: Hud;
  readonly touch: TouchControls;
  readonly pause: PauseMenu;
  readonly help: HelpCard;
  readonly skid = new Skidmarks(4000);
  readonly puffs = new Puffs(700);
  views: CarView[] = [];
  controls: Controls = neutralControls();
  paused = false;
  hidden = false;
  contextLost = false;
  units: 'kmh' | 'mph' = 'kmh';
  /** Maximum internal render pixels; quality presets change this. */
  pixelBudget = isMobileDevice() ? 1280 * 720 : 1920 * 1080;
  fatalError: Error | null = null;
  /** Fixed camera for reviews and photo tools: [x, y, z, targetX, targetY, targetZ, fov]. */
  cameraOverride: number[] | null = null;
  private raf = 0;
  private lastT = 0;
  private edgesUsed = false;
  private groundHit = newHit();
  private emitAcc = 0;

  constructor(opts: GameOptions) {
    this.test = opts.test;
    this.canvas = opts.canvas;
    this.uiRoot = opts.ui;
    this.sim = new Sim(opts.seed);
    this.renderer = new THREE.WebGLRenderer({
      canvas: this.canvas,
      antialias: !this.mobile,
      powerPreference: 'high-performance',
      // Tests read pixels back after rendering.
      preserveDrawingBuffer: opts.test,
    });
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.renderer.toneMappingExposure = 1.0;
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;

    this.scene.fog = new THREE.Fog(0xc4d6e6, 350, 2600);
    this.sky = new Sky(this.scene);
    const sun = this.sky.sun;
    sun.castShadow = true;
    const sm = this.mobile ? 1024 : 2048;
    sun.shadow.mapSize.set(sm, sm);
    const sc = sun.shadow.camera;
    sc.left = sc.bottom = -45;
    sc.right = sc.top = 45;
    sc.near = 10;
    sc.far = 320;
    sun.shadow.bias = -0.0004;
    sun.shadow.normalBias = 0.03;
    this.scene.environment = this.sky.buildEnvironment(this.renderer);

    this.scene.add(buildTrackView(this.sim.track));
    this.scene.add(this.skid.mesh, this.puffs.points);
    this.rebuildCarViews();

    this.hud = new Hud(this.uiRoot);
    this.touch = new TouchControls(this.uiRoot, this.input, (a) => this.onAction(a));
    this.pause = new PauseMenu(this.uiRoot, {
      resume: () => this.setPaused(false),
      reset: () => this.resetCar(),
      getAids: () => this.sim.player.vehicle.aids,
      setAids: (a) => (this.sim.player.vehicle.aids = a),
      getUnits: () => this.units,
      setUnits: (u) => (this.units = u),
      getTouch: () => this.touch.visible,
      setTouch: (v) => this.setTouchVisible(v),
    });
    this.help = new HelpCard(this.uiRoot);
    this.help.setVisible(!this.test && !this.mobile);
    this.setTouchVisible(opts.touch ?? this.mobile);
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
    this.rig.snap();
  }

  get camera(): THREE.PerspectiveCamera {
    return this.rig.camera;
  }

  rebuildCarViews(): void {
    for (const v of this.views) this.scene.remove(v.root);
    this.views = this.sim.cars.map((c) => {
      const spec = CARS.find((s) => s.id === c.specId) ?? CARS[0];
      const view = new CarView(c.vehicle, spec);
      this.scene.add(view.root);
      return view;
    });
  }

  setTouchVisible(v: boolean): void {
    this.touch.setVisible(v);
    document.body.classList.toggle('touch-mode', v);
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
    const dt = Math.min(0.1, Math.max(0, (t - this.lastT) / 1000));
    this.lastT = t;
    this.perf.frame();
    this.controls = this.input.poll();
    this.handleFrameActions(this.controls);
    if (!this.paused && !this.hidden) {
      this.perf.begin('sim');
      this.edgesUsed = false;
      this.stepper.advance(dt, () => this.tick());
      this.perf.end('sim');
    }
    this.present(dt, this.stepper.alpha);
  }

  /** One fixed step. Input edges (shifts) apply to the first step of a frame only. */
  private tick(): void {
    for (const v of this.views) v.snapshot();
    if (this.edgesUsed) {
      this.controls.pressed.shiftUp = false;
      this.controls.pressed.shiftDown = false;
    }
    this.sim.step(this.controls);
    this.edgesUsed = true;
  }

  /** Advance exactly n fixed steps (test mode). */
  stepTicks(n: number): void {
    this.controls = this.input.poll();
    this.handleFrameActions(this.controls);
    this.edgesUsed = false;
    for (let i = 0; i < n; i++) this.tick();
    this.present(n * SIM_DT, 1, false);
  }

  renderNow(): void {
    this.present(0, 1);
  }

  private onAction(a: Action): void {
    switch (a) {
      case 'camera':
        this.hud.toast(CAM_LABELS[this.rig.cycle()]);
        break;
      case 'reset':
        this.resetCar();
        break;
      case 'pause':
        this.setPaused(!this.paused);
        break;
      default:
        this.input.tap(a);
    }
  }

  private handleFrameActions(c: Controls): void {
    if (c.pressed.pause) this.setPaused(!this.paused);
    if (this.paused) return;
    if (c.pressed.camera) this.onAction('camera');
    if (c.pressed.reset) this.resetCar();
    if (c.pressed.lights) for (const v of this.views) v.headlightsOn = !v.headlightsOn;
    if (c.pressed.horn) this.help.setVisible(this.help.el.style.display === 'none');
    this.rig.lookBack = c.held.lookBack;
  }

  resetCar(): void {
    this.sim.resetToRoad();
    for (const v of this.views) v.snapshot();
    this.skid.clear();
    this.rig.snap();
  }

  setPaused(p: boolean): void {
    this.paused = p;
    if (p) this.pause.show();
    else this.pause.hide();
    this.lastT = performance.now();
    this.stepper.reset();
  }

  private present(dt: number, alpha: number, render = true): void {
    if (this.contextLost) return;
    this.perf.begin('present');
    for (const v of this.views) v.update(alpha);
    this.drainEvents();
    this.updateEffects(dt);
    const pv = this.views[0];
    const veh = this.sim.player.vehicle;
    const p = veh.params;
    if (this.cameraOverride) {
      const [x, y, z, tx, ty, tz, fov] = this.cameraOverride;
      this.rig.camera.position.set(x, y, z);
      this.rig.camera.lookAt(tx, ty, tz);
      this.rig.camera.fov = fov ?? 40;
      this.rig.camera.updateProjectionMatrix();
    } else this.rig.update(
      dt > 0 ? dt : 1 / 60,
      {
        pos: pv.root.position,
        quat: pv.root.quaternion,
        vel: tmpVel.set(veh.vel.x, veh.vel.y, veh.vel.z),
        bonnetY: p.spec.height * 0.86 - p.cgHeight,
        bonnetZ: -p.a * 0.45,
      },
      (x, z) => (this.sim.ground.sample(x, z, 100, this.groundHit) ? this.groundHit.y : 0),
    );
    this.sky.follow(pv.root.position, this.rig.camera);
    if (render) {
      this.perf.begin('render');
      this.renderer.render(this.scene, this.rig.camera);
      this.perf.end('render');
      const info = this.renderer.info.render;
      this.perf.counters.draws = info.calls;
      this.perf.counters.tris = info.triangles;
    }
    this.updateHud(dt);
    this.perf.end('present');
  }

  private drainEvents(): void {
    for (const e of this.sim.events) {
      if (e.kind === 'impact' && e.carId === this.sim.player.id) {
        const s = Math.min(1, e.value / 12000);
        this.rig.impact(s);
        if (this.touch.visible) this.touch.buzz(Math.round(20 + s * 60));
      } else if (e.kind === 'lap') {
        this.hud.toast(`Lap ${(e.value / 60) | 0}:${(e.value % 60).toFixed(2).padStart(5, '0')}`, 2.5);
      }
    }
    this.sim.events.length = 0;
  }

  private updateEffects(dt: number): void {
    if (dt <= 0) return;
    this.puffs.update(dt);
    this.emitAcc += dt;
    const emitNow = this.emitAcc >= 1 / 45;
    if (emitNow) this.emitAcc = 0;
    for (let c = 0; c < this.sim.cars.length; c++) {
      const v = this.sim.cars[c].vehicle;
      for (let i = 0; i < 4; i++) {
        const w = v.wheels[i];
        const key = c * 4 + i;
        if (!w.grounded) {
          this.skid.add(key, 0, 0, 0, 0, 0, 0, 0);
          continue;
        }
        const surf = SURFACES[w.surface];
        const slide = w.slideSpeed;
        const width = (i < 2 ? v.params.spec.tires.widthF : v.params.spec.tires.widthR) / 1000;
        if (surf.skid) {
          const k = Math.max(0, Math.min(1, (slide - 3) / 9));
          this.skid.add(key, w.contact.x, w.contact.y, w.contact.z, w.fwdDir.x, w.fwdDir.z, width, k, 0.04);
          if (emitNow && slide > 5 && v.speed > 2) {
            const a = Math.min(0.6, 0.12 + (slide - 5) / 14);
            this.puffs.emit(w.contact.x, w.contact.y + 0.3, w.contact.z, v.vel.x * 0.2, 0.9, v.vel.z * 0.2, 1.3, 4.2, 2.2, a, 0.88, 0.88, 0.9);
          }
        } else {
          // Loose ground: lighter ruts and dust behind the car.
          const k = surf.particles === 'none' ? 0 : Math.max(0, Math.min(0.6, (slide - 2) / 10 + v.speed / 80));
          this.skid.add(key, w.contact.x, w.contact.y, w.contact.z, w.fwdDir.x, w.fwdDir.z, width, k * 0.5, 0.18);
          if (emitNow && surf.particles !== 'none' && (v.speed > 4 || slide > 3)) {
            const dust = surf.particles === 'gravel' ? [0.62, 0.58, 0.5] : surf.particles === 'snow' ? [0.95, 0.96, 1] : [0.58, 0.47, 0.34];
            const a = Math.min(0.42, v.speed / 70 + slide / 30);
            this.puffs.emit(w.contact.x, w.contact.y + 0.2, w.contact.z, v.vel.x * 0.15, 0.6, v.vel.z * 0.15, 1.1, 3.6, 2.2, a, dust[0], dust[1], dust[2]);
          }
        }
      }
    }
  }

  private updateHud(dt: number): void {
    const v = this.sim.player.vehicle;
    const p = v.params;
    const lap = this.sim.lap;
    this.hud.update(
      {
        kmh: v.vLong * 3.6,
        rpm: v.rpm,
        redline: p.engine.redline,
        limiter: p.engine.limiter,
        gear: v.gear,
        shiftLight: v.aids.gearbox === 'manual' && v.gear >= 1 && v.rpm > p.gearbox.upshiftRpm[v.gear] - 150,
        abs: v.absActive,
        tcs: v.tcsActive,
        esc: v.escActive,
        lap: lap.laps > 0 ? { lap: lap.laps, current: this.sim.time - lap.start, last: lap.last, best: lap.best } : null,
        units: this.units,
      },
      dt,
    );
  }

  resize(): void {
    const w = this.canvas.clientWidth || window.innerWidth;
    const h = this.canvas.clientHeight || window.innerHeight;
    this.renderer.setPixelRatio(pixelRatioForBudget(w, h, window.devicePixelRatio || 1, this.pixelBudget));
    this.renderer.setSize(w, h, false);
    this.rig.setAspect(w / Math.max(1, h));
    this.rig.camera.updateProjectionMatrix();
    this.puffs.setViewportHeight(h * this.renderer.getPixelRatio());
    document.body.classList.toggle('portrait', h > w);
  }

  private onVisibility(): void {
    this.hidden = document.hidden;
    // Do not simulate the time spent in the background.
    this.lastT = performance.now();
    this.stepper.reset();
    this.perf.resetFrameClock();
    if (this.hidden) this.input.clearKeys();
  }
}
