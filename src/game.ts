// Game shell: owns the renderer, the simulation clock, input, views and UI,
// and wires them together each frame.
import * as THREE from 'three';
import { FixedStepper, SIM_DT } from './engine/loop';
import { Input, neutralControls, type Action, type Controls } from './engine/input';
import { Perf } from './engine/perf';
import { Atmosphere, type TimeOfDay } from './engine/render/atmosphere';
import { Pipeline } from './engine/render/pipeline';
import { QUALITY, autoQuality, gpuName, type Quality, type QualityName } from './engine/render/quality';
import { patchTree } from './engine/render/materials';
import { CarReflections } from './vehicles/render/reflections';
import { CameraRig, CAM_LABELS } from './engine/render/camera-rig';
import { Puffs, Skidmarks } from './engine/render/effects';
import { buildTrackView } from './world/render/track-view';
import { TerrainView } from './world/render/terrain-view';
import { TileSource } from './world/render/tile-source';
import { Vegetation } from './world/render/vegetation';
import { Grass } from './world/render/grass';
import { buildBuildingsView, type BuildingsView } from './world/render/buildings-view';
import { FestivalView } from './world/render/festival-view';
import { FestivalExtras } from './world/render/festival-extras';
import { EventsMenu, RaceHud } from './ui/race-ui';
import { festivalEvents } from './ai/events';
import { AbilityCache, RaceSession, fieldPool, qualifyingOrder, raceField, type RaceEvent } from './ai/race-session';
import { AbilityMeasurer } from './ai/ability-client';
import { RacingLine } from './ai/racing-line';
import { DIFFICULTIES, type Difficulty } from './ai/ai-driver';
import { RNG } from './engine/rng';
import { festivalBare } from './world/festival';
import { GpsView } from './world/render/gps-view';
import { findRoute, type Route } from './world/route';
import { paintBaseMap, mapPois, Minimap, FullMap } from './ui/map';
import { terrainRaster as terrainRasterQuick } from './world/terrain-data';
import { buildRoadView, type RoadView } from './world/render/road-view';
import { SURFACES } from './world/surfaces';
import { newHit } from './world/ground';
import { CarView } from './vehicles/render/car-view';
import { CARS } from './vehicles/cars';
import { Hud } from './ui/hud';
import { TouchControls } from './ui/touch';
import { HelpCard, PauseMenu, type QualityChoice } from './ui/menus';
import { Sim, type SimCar } from './sim';
import { GameAudio, type Listener } from './audio/audio';

export { fovForAspect } from './engine/render/fov';

const QUALITY_KEY = 'halcyon-quality';
const CAR_KEY = 'halcyon-car';

/** The graphics preset chosen in the pause menu, if any ('auto' when left to the device). */
const DIFFICULTY_KEY = 'halcyon-difficulty';
/** The rivals' paints: strong festival colours and a few everyday ones. */
const RIVAL_PAINTS = [0xe0337a, 0x14b8a6, 0xffc23d, 0x3b82f6, 0xff6b2c, 0x7c4dff, 0x2e7d32, 0xf2f2f0, 0x18191b, 0xa7abb0, 0x9a2d22, 0x1f3b6b];

function savedDifficulty(): Difficulty {
  try {
    const d = localStorage.getItem(DIFFICULTY_KEY);
    if (d && (DIFFICULTIES as readonly string[]).includes(d)) return d as Difficulty;
  } catch {
    /* private mode */
  }
  return 'pro';
}

export function savedQuality(): QualityChoice | null {
  try {
    const q = typeof localStorage !== 'undefined' ? localStorage.getItem(QUALITY_KEY) : null;
    return q === 'auto' || (q !== null && q in QUALITY) ? (q as QualityChoice) : null;
  } catch {
    return null;
  }
}

function saveQuality(q: QualityChoice): void {
  try {
    localStorage.setItem(QUALITY_KEY, q);
  } catch {
    /* private mode: this visit only */
  }
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
  /** Force touch controls on or off (default: on for touch devices). */
  touch?: boolean;
  quality?: QualityName;
  time?: TimeOfDay;
  /** Sound in test mode (tests of the audio itself). */
  audio?: boolean;
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
  readonly atmo: Atmosphere;
  readonly pipeline: Pipeline;
  readonly reflections: CarReflections;
  quality: Quality;
  readonly hud: Hud;
  readonly touch: TouchControls;
  readonly pause: PauseMenu;
  readonly help: HelpCard;
  readonly audio: GameAudio;
  readonly terrain: TerrainView;
  readonly tiles: TileSource;
  readonly vegetation: Vegetation;
  readonly grass: Grass | null = null;
  readonly buildings: BuildingsView;
  readonly festival: FestivalView;
  /** The festival's crowds, cars on show, car park, food trucks and towers. */
  readonly festivalExtras: FestivalExtras;
  /** The race under way, if any (the festival's races: src/ai/). */
  race: RaceSession | null = null;
  private raceEvents: RaceEvent[] | null = null;
  private readonly abilities = new AbilityCache();
  /** Racing lines by event (built the first time each is raced). */
  private readonly raceLines = new Map<string, RacingLine>();
  private eventsMenu!: EventsMenu;
  private raceHud!: RaceHud;
  /** The festival menu is open (the world waits). */
  private eventsOpen = false;
  /** Bumped by each race asked for: a grid still being measured is dropped if the menu closed meanwhile. */
  private raceToken = 0;
  private difficulty: Difficulty = savedDifficulty();
  private raceSeed = 1;
  readonly gps = new GpsView();
  readonly minimap: Minimap;
  readonly fullmap: FullMap;
  route: Route | null = null;
  waypoint: { x: number; z: number } | null = null;
  private routeIndex = 0;
  private routeS: Float64Array = new Float64Array(0);
  private rerouteTimer = 0;
  readonly roads: RoadView;
  readonly skid = new Skidmarks(4000);
  readonly puffs = new Puffs(700);
  views: CarView[] = [];
  controls: Controls = neutralControls();
  paused = false;
  hidden = false;
  contextLost = false;
  units: 'kmh' | 'mph' = 'kmh';
  /** Maximum internal render pixels; quality presets change this. */
  pixelBudget = 1280 * 720;
  /** Dynamic resolution: a linear scale on the pixel budget, moved by the frame time. */
  resScale = 1;
  private resTimer = 0;
  fatalError: Error | null = null;
  /** True until the island around the player has streamed in (normal play). */
  loading = true;
  private loadStart = 0;
  private loadPct = 0;
  private boot: HTMLElement | null = null;
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
    const nav = typeof navigator !== 'undefined' ? (navigator as Navigator & { deviceMemory?: number }) : undefined;
    this.quality = QUALITY[opts.quality ?? autoQuality(this.mobile, nav?.hardwareConcurrency ?? 4, nav?.deviceMemory, gpuName())];
    this.pixelBudget = this.quality.pixelBudget;
    this.renderer = new THREE.WebGLRenderer({
      canvas: this.canvas,
      // MSAA happens in the HDR target when post-processing is on.
      antialias: !this.quality.post && !this.mobile,
      powerPreference: 'high-performance',
      // Tests read pixels back after rendering.
      preserveDrawingBuffer: opts.test,
    });
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    this.renderer.info.autoReset = false;
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;

    // Fog presence enables the patched atmospheric fog in every material.
    this.scene.fog = new THREE.FogExp2(0xc4d6e6, 0);
    this.atmo = new Atmosphere(this.scene, this.quality.clouds);
    this.atmo.setTime(opts.time ?? 'golden');
    this.pipeline = new Pipeline(this.renderer, this.scene, this.rig.camera, this.quality);
    this.pipeline.setupShadows(this.atmo.sunDir, this.atmo.sun.intensity, this.atmo.sun.color);
    const sun = this.atmo.sun;
    if (this.pipeline.csm) {
      // The cascades carry the direct sunlight.
      sun.intensity = 0;
    } else {
      sun.castShadow = true;
      const sm = this.quality.shadowSize;
      sun.shadow.mapSize.set(sm, sm);
      const sc = sun.shadow.camera;
      sc.left = sc.bottom = -60;
      sc.right = sc.top = 60;
      sc.near = 10;
      sc.far = 420;
      sun.shadow.bias = -0.0004;
      sun.shadow.normalBias = 0.03;
    }
    this.scene.environment = this.atmo.buildEnvironment(this.renderer);
    this.reflections = new CarReflections(this.renderer);

    // Tests build tiles synchronously so every frame is deterministic.
    const cores = typeof navigator !== 'undefined' ? navigator.hardwareConcurrency || 4 : 4;
    this.tiles = new TileSource(this.sim.world, !opts.test, Math.max(1, Math.min(3, cores - 2)));
    this.terrain = new TerrainView(this.sim.world, this.tiles, this.quality.terrain);
    this.scene.add(this.terrain.group);
    this.roads = buildRoadView(this.sim.world);
    this.scene.add(this.roads.group);
    this.buildings = buildBuildingsView(this.sim.world);
    this.buildings.uniforms.uNight.value = opts.time === 'night' ? 1 : opts.time === 'sunset' ? 0.35 : 0;
    this.scene.add(this.buildings.group);
    this.festival = new FestivalView(this.sim.world.festival);
    this.scene.add(this.festival.group);
    this.festivalExtras = new FestivalExtras(this.sim.world.festival, { parked: this.quality.name !== 'low', crowd: this.quality.name === 'low' ? 0.4 : this.quality.name === 'medium' ? 0.7 : 1 });
    this.scene.add(this.festivalExtras.group);
    const q = this.quality;
    this.vegetation = new Vegetation(this.renderer, this.tiles, { near: q.treeNear, far: q.treeFar, thinFrom: q.treeThin, thinKeep: q.treeKeep, shadows: q.treeShadows });
    this.scene.add(this.vegetation.group);
    if (q.grass > 0) {
      this.grass = new Grass(this.sim.ground, q.grass);
      const fest = this.sim.world.festival;
      this.grass.exclude = (x, z) => festivalBare(fest, x, z);
      this.scene.add(this.grass.mesh);
    }
    this.scene.add(buildTrackView(this.sim.track));
    this.scene.add(this.skid.mesh, this.puffs.points);
    this.rebuildCarViews();
    patchTree(this.scene);

    this.hud = new Hud(this.uiRoot);
    // Sound starts on the first key, click or touch (browsers require a gesture); tests stay silent.
    this.audio = new GameAudio(!opts.test || !!opts.audio);
    this.audio.onNowPlaying = (text) => this.hud.toast(text, 4);
    this.audio.setCar(this.sim.player.specId);
    if (!opts.test) {
      let saved: string | null = null;
      try {
        saved = localStorage.getItem(CAR_KEY);
      } catch {
        /* private mode */
      }
      if (saved && CARS.some((c) => c.id === saved) && saved !== this.sim.player.specId) {
        this.sim.changePlayerCar(saved);
        this.rebuildCarViews();
      }
    }
    // A quick coarse map now; a finer one from the worker when it is ready.
    const circuit = this.sim.track.road;
    const mapBase = paintBaseMap(this.sim.world, terrainRasterQuick(this.sim.world, opts.test ? 160 : 96), opts.test ? 160 : 96, circuit);
    if (!opts.test) this.tiles.mapRaster(384, (px, n) => paintBaseMap(this.sim.world, px, n, circuit, mapBase));
    this.minimap = new Minimap(this.hud.el, mapBase);
    this.fullmap = new FullMap(this.uiRoot, mapBase, mapPois(this.sim.world), (x, z) => this.setWaypoint(x, z), () => this.toggleMap(false));
    this.scene.add(this.gps.mesh);
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
      getCars: () => CARS.map((c) => ({ id: c.id, name: `${c.make} ${c.model}`, info: `${c.drivetrain} · ${Math.round(Math.max(...c.engine.torque.map(([rpm, nm]) => (rpm * nm) / 9549)))} kW` })),
      getCar: () => this.sim.player.specId,
      setCar: (id) => this.changeCar(id),
      getQuality: () => savedQuality() ?? 'auto',
      setQuality: (q) => {
        saveQuality(q);
        const want = q === 'auto' ? autoQuality(this.mobile, navigator.hardwareConcurrency ?? 4, (navigator as Navigator & { deviceMemory?: number }).deviceMemory, gpuName()) : q;
        return want !== this.quality.name;
      },
      getAudio: () => this.audio.settings,
      setAudio: (a) => this.audio.setSettings(a),
      click: () => this.audio.ui('move'),
      races: () => this.openEvents(),
      racing: () => this.race !== null,
      leaveRace: () => this.endRace(),
    });
    this.eventsMenu = new EventsMenu(this.uiRoot, {
      start: (id, d) => this.startRace(id, d),
      close: () => this.closeEvents(),
      click: () => this.audio.ui('move'),
    });
    this.raceHud = new RaceHud(this.uiRoot, {
      done: () => this.endRace(),
      click: () => this.audio.ui('select'),
      finished: (pos) => this.audio.ui(pos <= 3 ? 'reward' : 'checkpoint'),
    });
    this.help = new HelpCard(this.uiRoot);
    this.help.setVisible(!this.test && !this.mobile);
    this.setTouchVisible(opts.touch ?? this.mobile);
    this.boot = document.getElementById('boot');
    if (opts.test) this.boot?.remove();
    else this.boot?.classList.add('boot-loading');

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
    const p0 = this.sim.player.vehicle.pos;
    if (opts.test) {
      // Tests build everything up front, synchronously and deterministically.
      this.terrain.prime(new THREE.Vector3(p0.x, p0.y + 2, p0.z));
      this.vegetation.update(new THREE.Vector3(p0.x, p0.y + 2, p0.z), true);
      this.loading = false;
    } else {
      this.loadStart = performance.now();
      this.terrain.maxInFlight = 12;
    }
  }

  get camera(): THREE.PerspectiveCamera {
    return this.rig.camera;
  }

  rebuildCarViews(): void {
    for (const v of this.views) this.scene.remove(v.root);
    this.views = this.sim.cars.map((c) => {
      const spec = CARS.find((s) => s.id === c.specId) ?? CARS[0];
      const view = new CarView(c.vehicle, spec, { paint: c.paint });
      this.scene.add(view.root, view.contactShadow);
      patchTree(view.root);
      // The Blender model arrives a moment later: its materials get the fog and the live reflections too.
      void view.ready.then((swapped) => {
        if (!swapped || !this.views.includes(view)) return;
        patchTree(view.root);
        if (view === this.views[0]) this.reflections?.track(view.root);
      });
      return view;
    });
    if (this.views[0] && this.reflections) this.reflections.track(this.views[0].root);
    this.audio?.setCar(this.sim.player.specId);
  }

  /** Drive another car where you stand (the pause menu's car picker); remembered for next time. */
  changeCar(id: string): void {
    if (id === this.sim.player.specId) return;
    this.sim.changePlayerCar(id);
    this.rebuildCarViews();
    this.rig.snap();
    this.stepper.reset();
    try {
      if (!this.test) localStorage.setItem(CAR_KEY, id);
    } catch {
      /* private mode */
    }
    this.hud.toast(CARS.find((c) => c.id === id)?.model ?? id);
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
    if (this.loading) this.updateLoading(t);
    if (!this.paused && !this.hidden && !this.loading && !this.mapPaused && !this.eventsOpen) {
      this.perf.begin('sim');
      this.edgesUsed = false;
      this.stepper.advance(dt, () => this.tick());
      this.perf.end('sim');
    }
    this.present(dt, this.stepper.alpha);
  }

  /** Hold the car while terrain and trees around it stream in, with progress on the boot screen. */
  private updateLoading(t: number): void {
    const cam = this.rig.camera.position;
    const tReady = this.terrain.settled && this.terrain.tileCount > 0;
    const vReady = this.vegetation.nearReady(cam);
    const waited = t - this.loadStart;
    if ((tReady && vReady) || waited > 15000) {
      this.loading = false;
      this.terrain.maxInFlight = 6;
      this.lastT = t;
      if (this.boot) {
        this.boot.classList.add('boot-done');
        const b = this.boot;
        setTimeout(() => b.remove(), 700);
      }
      return;
    }
    if (this.boot) {
      // Progress never runs backwards, even as the tile set refines.
      const pct = Math.round(this.terrain.progress * 60 + this.vegetation.progress(cam, this.quality.treeNear + 256) * 40);
      this.loadPct = Math.max(this.loadPct, Math.min(99, pct));
      this.boot.textContent = `BUILDING THE ISLAND  ${this.loadPct}%`;
    }
  }

  /** One fixed step. Input edges (shifts) apply to the first step of a frame only. */
  private tick(): void {
    for (const v of this.views) v.snapshot();
    if (this.edgesUsed) {
      this.controls.pressed.shiftUp = false;
      this.controls.pressed.shiftDown = false;
    }
    if (this.race) this.race.step(this.controls);
    else this.sim.step(this.controls);
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
      case 'map':
        this.toggleMap();
        break;
      case 'radio':
        this.hud.toast(this.audio.stationName(this.audio.cycleStation()));
        break;
      case 'events':
        if (this.eventsOpen) this.closeEvents();
        else if (!this.paused) this.openEvents();
        break;
      default:
        this.input.tap(a);
    }
  }

  private handleFrameActions(c: Controls): void {
    if (this.eventsOpen) {
      if (c.pressed.pause || c.pressed.events) this.closeEvents();
      return;
    }
    if (this.race && this.raceHud.resultsOpen && c.pressed.confirm && !this.paused) {
      this.audio.ui('select');
      this.endRace();
      return;
    }
    if (c.pressed.events && !this.paused) this.openEvents();
    if (c.pressed.map) this.toggleMap();
    if (this.fullmap.visible) {
      if (c.pressed.pause) this.toggleMap(false);
      return;
    }
    if (c.pressed.pause) this.setPaused(!this.paused);
    if (this.paused) return;
    if (c.pressed.camera) this.onAction('camera');
    if (c.pressed.reset) this.resetCar();
    if (c.pressed.lights) for (const v of this.views) v.headlightsOn = !v.headlightsOn;
    if (c.pressed.horn) this.help.setVisible(this.help.el.style.display === 'none');
    if (c.pressed.radio) this.hud.toast(this.audio.stationName(this.audio.cycleStation()));
    this.rig.lookBack = c.held.lookBack;
  }

  /** Open or close the full map; the world pauses while it is open. */
  toggleMap(open = !this.fullmap.visible): void {
    if (open === this.fullmap.visible) return;
    this.audio.ui(open ? 'select' : 'back');
    if (open) {
      const v = this.sim.player.vehicle;
      this.fullmap.open({ x: v.pos.x, z: v.pos.z, yaw: Math.atan2(-v.fwd.x, -v.fwd.z) }, this.route, this.waypoint);
    } else this.fullmap.close();
    this.mapPaused = open;
    this.lastT = performance.now();
    this.stepper.reset();
  }

  private mapPaused = false;

  private readonly others: SimCar[] = [];
  private readonly listener: Listener = { x: 0, y: 0, z: 0, rx: 1, rz: 0, vx: 0, vz: 0 };
  private readonly camRight = new THREE.Vector3();

  /** The other cars' engines, heard from the camera. */
  private updateVoices(dt: number, paused: boolean): void {
    const others = this.others;
    others.length = 0;
    for (const c of this.sim.cars) if (!c.player) others.push(c);
    const cam = this.rig.camera;
    const r = this.camRight.setFromMatrixColumn(cam.matrixWorld, 0);
    const li = this.listener;
    li.x = cam.position.x;
    li.y = cam.position.y;
    li.z = cam.position.z;
    const n = Math.hypot(r.x, r.z) || 1;
    li.rx = r.x / n;
    li.rz = r.z / n;
    const pv = this.sim.player.vehicle.vel;
    li.vx = pv.x;
    li.vz = pv.z;
    this.audio.updateRemote(dt, others, li, paused);
  }

  /** The race rivals' positions, for the minimap. */
  private rivalDots(): { x: number; z: number }[] {
    const out: { x: number; z: number }[] = [];
    for (const c of this.sim.cars) if (!c.player) out.push(c.vehicle.pos);
    return out;
  }

  /** Route from the car to (x, z) over the roads; shown on the maps and as the GPS line. */
  setWaypoint(x: number, z: number): void {
    if (this.race) {
      this.hud.toast('Leave the race to set a waypoint');
      return;
    }
    this.waypoint = { x, z };
    this.audio.ui('checkpoint');
    this.computeRoute();
    this.fullmap.setRoute(this.route);
    if (this.route) this.hud.toast(`Route set · ${(this.route.length / 1000).toFixed(1)} km`);
  }

  clearWaypoint(): void {
    this.waypoint = null;
    this.route = null;
    this.gps.setRoute(null);
  }

  private computeRoute(): void {
    const v = this.sim.player.vehicle;
    if (!this.waypoint) return;
    this.route = findRoute(this.sim.world, v.pos.x, v.pos.z, this.waypoint.x, this.waypoint.z);
    this.routeIndex = 0;
    const pts = this.route?.points ?? [];
    this.routeS = new Float64Array(pts.length);
    for (let i = 1; i < pts.length; i++) this.routeS[i] = this.routeS[i - 1] + Math.hypot(pts[i].x - pts[i - 1].x, pts[i].z - pts[i - 1].z);
    this.gps.setRoute(this.route);
  }

  /** Follow the car along the route: progress, rerouting when it strays, arrival. */
  private updateRoute(dt: number): void {
    if (!this.route) return;
    const pts = this.route.points;
    const v = this.sim.player.vehicle;
    let best = Infinity;
    let bi = this.routeIndex;
    for (let i = Math.max(0, this.routeIndex - 20); i < Math.min(pts.length, this.routeIndex + 120); i++) {
      const d = (pts[i].x - v.pos.x) ** 2 + (pts[i].z - v.pos.z) ** 2;
      if (d < best) {
        best = d;
        bi = i;
      }
    }
    this.routeIndex = bi;
    if (this.race) {
      this.gps.update(this.routeS[bi], dt);
      return;
    }
    this.rerouteTimer -= dt;
    if (Math.sqrt(best) > 45 && this.rerouteTimer <= 0) {
      this.rerouteTimer = 1.5;
      this.computeRoute();
      return;
    }
    // Arrived at the route's end: the road nearest the waypoint, which may lie well off the road.
    const left = this.routeS[this.routeS.length - 1] - this.routeS[bi];
    if (left < 25 && Math.sqrt(best) <= 45) {
      this.hud.toast('You have arrived');
      this.clearWaypoint();
      return;
    }
    this.gps.update(this.routeS[bi], dt);
  }

  /** The festival menu: every race, built the first time it opens. */
  openEvents(): void {
    if (this.race || this.eventsOpen) return;
    if (this.fullmap.visible) this.toggleMap(false);
    this.raceEvents ??= festivalEvents(this.sim);
    if (!this.test) void this.measureAhead();
    this.eventsOpen = true;
    this.audio.ui('select');
    this.eventsMenu.show(this.raceEvents, this.difficulty);
  }

  /** The rivals' cars measured in the background while the menu is read: the whole pool the grid is drawn from. */
  measureAhead(): Promise<void> {
    this.abilities.measurer ??= new AbilityMeasurer();
    return this.abilities.prefetch(fieldPool(this.sim.player.specId));
  }

  closeEvents(): void {
    if (!this.eventsOpen) return;
    this.raceToken++;
    this.eventsOpen = false;
    this.eventsMenu.hide();
    this.audio.ui('back');
    this.lastT = performance.now();
    this.stepper.reset();
  }

  /** Build and start a race: the rivals' cars are measured (a moment), placed on the grid, and the countdown runs. */
  startRace(id: string, difficulty: Difficulty): void {
    const ev = this.raceEvents?.find((e) => e.id === id);
    if (!ev) return;
    this.difficulty = difficulty;
    try {
      if (!this.test) localStorage.setItem(DIFFICULTY_KEY, difficulty);
    } catch {
      /* private mode */
    }
    const seed = this.raceSeed++;
    const count = Math.max(0, ev.gridSize - 1);
    let field = raceField(this.sim.player.specId, count, difficulty, seed);
    // A duel: the festival's rival, in the player's own car (a fair fight).
    if (ev.rival) field = [{ carId: this.sim.player.specId, driver: ev.rival }];
    const build = (): void => {
      const rng = new RNG(seed * 977 + 13);
      let line = this.raceLines.get(ev.id);
      if (!line) this.raceLines.set(ev.id, (line = RacingLine.build(ev.route)));
      // Qualifying order, fastest at the front; the player starts mid-grid (as on PC), alone in a duel's other lane.
      field = qualifyingOrder(ev, line, field, this.abilities);
      const paints = [...RIVAL_PAINTS];
      const opponents = field.map((f) => ({ ...f, paint: paints.length ? paints.splice(rng.int(0, paints.length), 1)[0] : undefined }));
      this.clearWaypoint();
      this.race = new RaceSession(this.sim, ev, opponents, this.abilities, seed, line, true, Math.min(Math.floor(ev.gridSize / 2), opponents.length));
      // The rivals' engines render while the countdown runs.
      this.audio.prepareVoices(opponents.map((o) => o.carId));
      this.rebuildCarViews();
      this.skid.clear();
      for (const v of this.views) v.snapshot();
      this.rig.snap();
      this.stepper.reset();
      // A sprint shows its road on the minimap and as the GPS line.
      if (ev.kind !== 'circuit') this.setRaceRoute(ev);
      this.eventsOpen = false;
      this.eventsMenu.hide();
      this.raceHud.start();
      this.lastT = performance.now();
    };
    if (this.test) {
      build();
      return;
    }
    // The grid's cars measured off the main thread (most already are, from opening the menu); the page
    // keeps drawing meanwhile. Closing the menu drops the race.
    const token = ++this.raceToken;
    this.eventsMenu.busy('Building the grid…');
    void this.abilities.prefetch(field.map((f) => f.carId), ev.surface, true).then(() => {
      if (token !== this.raceToken || !this.eventsOpen || this.race) return;
      build();
    });
  }

  /** Leave the race (from its results, or the pause menu): the rivals leave the island. */
  endRace(): void {
    if (!this.race) return;
    this.race.end();
    this.race = null;
    this.raceHud.hide();
    this.clearWaypoint();
    this.rebuildCarViews();
    this.sim.filter.reset();
    this.lastT = performance.now();
    this.stepper.reset();
  }

  private setRaceRoute(ev: RaceEvent): void {
    const r = ev.route;
    const points = [];
    for (let i = 0; i < r.n; i++) points.push({ x: r.x[i], y: r.y[i], z: r.z[i], hw: Math.min(r.wLeft[i], r.wRight[i]) });
    this.route = { points, length: r.length };
    this.routeIndex = 0;
    this.routeS = new Float64Array(r.s);
    this.gps.setRoute(this.route);
  }

  resetCar(): void {
    if (this.race) this.race.resetPlayer();
    else this.sim.resetToRoad();
    for (const v of this.views) v.snapshot();
    this.skid.clear();
    this.rig.snap();
  }

  setPaused(p: boolean): void {
    if (p !== this.paused) this.audio.ui(p ? 'select' : 'back');
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
    const audioPaused = this.paused || this.mapPaused || this.loading || this.hidden || this.eventsOpen;
    this.audio.update(dt, this.sim.player.vehicle, { paused: audioPaused, inside: this.rig.mode === 'bonnet' });
    this.updateVoices(dt, audioPaused);
    this.updateResolution(dt);
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
    this.perf.begin('terrain');
    this.terrain.update(this.rig.camera.position, this.test);
    this.vegetation.update(this.rig.camera.position, this.test);
    this.grass?.update(this.rig.camera.position);
    this.festival.update(dt);
    this.updateRoute(dt);
    this.perf.counters.trees = this.vegetation.nearCount;
    this.perf.counters.impostors = this.vegetation.farCount;
    this.perf.end('terrain');
    this.perf.counters.tiles = this.terrain.tileCount;
    this.atmo.update(dt, this.rig.camera, pv.root.position, 420);
    if (this.pipeline.csm) this.pipeline.setSun(this.atmo.sunDir, this.atmo.preset.sunIntensity, this.atmo.sun.color);
    // Speed sensations: a touch of radial blur and fringing near top speed.
    const kmh = veh.speed * 3.6;
    this.pipeline.speedBlur = this.rig.mode === 'bonnet' ? 0 : Math.max(0, Math.min(1, (kmh - 120) / 180)) * 0.045;
    this.pipeline.aberration = Math.max(0, Math.min(1, (kmh - 150) / 200)) * 0.0025;
    this.pipeline.exposure = this.atmo.exposure;
    if (render) {
      this.perf.begin('render');
      // Count every draw of the frame: reflections, shadow cascades, scene and post.
      this.renderer.info.reset();
      this.reflections.update(this.renderer, this.scene, pv.root, this.quality.carReflections);
      this.pipeline.render(dt > 0 ? dt : 1 / 60);
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
        this.audio.impact(e.value);
        if (this.touch.visible) this.touch.buzz(Math.round(20 + s * 60));
      } else if (e.kind === 'lap' && !this.race) {
        this.hud.toast(`Lap ${(e.value / 60) | 0}:${(e.value % 60).toFixed(2).padStart(5, '0')}`, 2.5);
        this.audio.ui('checkpoint');
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
        lap: lap.laps > 0 && !this.race ? { lap: lap.laps, current: this.sim.time - lap.start, last: lap.last, best: lap.best } : null,
        units: this.units,
      },
      dt,
    );
    if (this.race) {
      const beep = this.raceHud.update(this.race);
      if (beep > 0) this.audio.ui('count');
      else if (beep === 0) this.audio.ui('go');
    }
    // The minimap redraws at half the frame rate.
    this.minimapFrame = (this.minimapFrame + 1) % 2;
    if (this.minimapFrame === 0 || this.test) {
      const left = this.route ? this.routeS[this.routeS.length - 1] - this.routeS[this.routeIndex] : 0;
      this.minimap.draw(v.pos.x, v.pos.z, Math.atan2(-v.fwd.x, -v.fwd.z), v.speed, this.route, this.routeIndex, left, this.race ? this.rivalDots() : []);
    }
  }

  private minimapFrame = 0;

  resize(): void {
    const w = this.canvas.clientWidth || window.innerWidth;
    const h = this.canvas.clientHeight || window.innerHeight;
    // The preset's pixel budget, then the dynamic resolution's scale on top (never under half a pixel per CSS pixel).
    const pr = Math.max(0.5, pixelRatioForBudget(w, h, window.devicePixelRatio || 1, this.pixelBudget) * this.resScale);
    this.renderer.setPixelRatio(pr);
    this.renderer.setSize(w, h, false);
    this.pipeline?.setSize(w, h, pr);
    this.rig.setAspect(w / Math.max(1, h));
    this.rig.camera.updateProjectionMatrix();
    this.puffs.setViewportHeight(h * this.renderer.getPixelRatio());
    document.body.classList.toggle('portrait', h > w);
  }

  /**
   * Dynamic resolution: below 60 frames a second the internal resolution
   * steps down (to 55% of the preset's at most, in each direction); with
   * headroom it climbs back. Changes are at least 1.5 s apart so the picture
   * does not pump.
   */
  private updateResolution(dt: number): void {
    if (this.test || this.loading || this.paused || this.hidden || dt <= 0) return;
    this.resTimer += dt;
    if (this.resTimer < 1.5) return;
    const ms = this.perf.frameMs;
    const target = 1000 / 60;
    let next = this.resScale;
    if (ms > target * 1.12) next = Math.max(0.55, this.resScale * 0.9);
    else if (ms < target * 0.8) next = Math.min(1, this.resScale * 1.05);
    if (Math.abs(next - this.resScale) > 0.005) {
      this.resScale = next;
      this.resTimer = 0;
      this.resize();
    } else this.resTimer = 1;
  }

  private onVisibility(): void {
    this.hidden = document.hidden;
    this.audio.setHidden(this.hidden);
    // Do not simulate the time spent in the background.
    this.lastT = performance.now();
    this.stepper.reset();
    this.perf.resetFrameClock();
    if (this.hidden) this.input.clearKeys();
  }
}
