# Halcyon Roads: design notes

This file records the decisions made while building the game and the reason for each. When a decision changes, the section is updated and the change is noted in the log at the end.

## Concept

Halcyon Roads is an original open-world festival racing game. It is set on Halcyon Island, a fictional island in a temperate sea. The festival hub sits on the coast. The player drives anywhere on the island, enters events against AI drivers, earns credits and XP, and buys, upgrades and tunes cars.

Everything is original: the game name, the island, the festival, the car makes and models, the music and the art. No real manufacturer names, logos or trademarks are used, and nothing from Forza, Playground Games or Microsoft.

## Stack and build

- **Language and libraries.** TypeScript (5.9.3) with three.js (0.170.0). All versions are pinned exactly in `package.json`, and `package-lock.json` is committed.
- **Bundler: esbuild (0.25.12), not Vite.** The game must ship as one self-contained `dist/index.html`. esbuild's API returns the bundle as a string that `tools/build.mjs` inlines into `src/index.html` with the CSS, with no plugin needed. A production build takes about 0.1 s. The build fails if the output references any remote script, stylesheet or image.
- **`dist/index.html` is committed** so the game can be played straight from the repository without Node. The root `index.html` redirects to it. It is rebuilt and committed at each milestone.
- **The earlier game moved.** Duskline, the single-file prototype that was here before, now lives unchanged in `legacy/duskline/`.
- **No runtime network access.** No CDN, no web fonts (system font stack), and no fetches. Tests block every non-file request and fail if one is attempted.

## Architecture

### Simulation and presentation are separate

Everything that advances on the simulation clock (vehicle physics, AI, events, traffic, world queries) lives in modules that import neither three.js nor the DOM. The renderer and UI read simulation state and never write it. This lets the whole simulation run in Node, so physics validation, AI tournaments and soak tests run in seconds without a browser.

- `src/sim.ts`: the simulation root, stepped at a fixed rate.
- `src/game.ts`: the browser shell. It owns the renderer, the clock, input, UI and audio.
- `src/{engine,world,vehicles,ai,events,save}`: simulation code. Rendering code for a system sits in that system's folder, in files that import three.js. The determinism lint (see below) skips them.
- `src/{ui,audio}`: presentation only.

### Fixed 240 Hz step

The simulation always advances in exact steps of 1/240 s (`src/engine/loop.ts`). A real frame longer than 0.1 s is clamped, so a stalled tab runs in slow motion rather than spiralling into thousands of catch-up steps. Rendering happens once per display frame. When the tab is hidden the clock stops and the accumulated time is discarded.

### Determinism, including across browsers

Given the same seed and the same inputs, the simulation produces bit-identical results. That holds across JS engines too, not just across runs:

- No `Math.random()` in simulation code. Every random draw comes from a seeded `RNG` (sfc32) in `src/engine/rng.ts`, forked per subsystem.
- **Deterministic math.** `Math.sin`, `Math.cos` and `Math.pow` return different bits in Node 22 (V8 12.4) and Chromium 141, measured in M0. Safari and Firefox differ again. Simulation code therefore uses `src/engine/dmath.ts`: fdlibm-derived `dsin`, `dcos`, `datan`, `datan2`, `dexp`, `dlog`, `dpow` and `dtanh`. They use only `+ - * /`, `sqrt` and exact bit manipulation, all of which IEEE 754 defines exactly, so every engine gets the same bits. They match libm to within about 1e-15.
- `tests/determinism.test.ts` fails the build if a simulation file calls an engine-dependent `Math` function. Files that import three.js, or live in `ui/` or `audio/`, are exempt.
- A browser test runs the same input script in Node and in Chromium and requires identical state hashes.

### Conventions

- Units are SI: meters, seconds, kilograms, newtons and radians. Speeds are m/s inside the sim, and km/h or mph only in the UI.
- World axes: +Y up, +X east, −Z north.
- **Car body frame: forward is −Z, right is +X, up is +Y.** This matches a three.js camera, so a chase camera shares the car's axes.
- **Yaw ψ** is rotation about +Y. Forward is (−sin ψ, 0, −cos ψ), so ψ = 0 faces north. The compass bearing is −ψ.
- **Controls:** `steer` is in [−1, 1] with +1 meaning full right. `throttle`, `brake`, `handbrake` and `clutch` are in [0, 1].

### Input

`src/engine/input.ts` merges the keyboard, gamepad (standard mapping, radial deadzones, analog triggers) and touch into one `Controls` snapshot per frame. Keyboard steering is flagged as digital so the vehicle layer can smooth it. Tests and bots set `input.override`, which replaces every physical source, through `__game.setInput()`. Window blur and tab hiding release all held keys.

### Rendering budget

- **Pixel budget.** The internal resolution is capped by a pixel budget rather than a fixed device pixel ratio: 1280×720 on mobile and 1920×1080 on desktop by default (`pixelRatioForBudget`). A DPR-3 phone in portrait renders about 0.92 MP instead of 2.1 MP. The quality presets in M8 adjust the budget, and dynamic resolution scales within it.
- **Field of view.** The camera keeps at least 72° of horizontal FOV, with vertical FOV capped at 100°, so phones in portrait still see around the car.

### Robustness

- **WebGL context loss** (common on mobile when the tab is backgrounded) is handled: the page stays alive and three.js rebuilds GPU resources on restore.
- **No WebGL 2:** the page explains what is needed instead of hanging on the loading screen.
- **Errors:** an uncaught exception in the frame loop stops the loop and shows a reload overlay, rather than repeating the error every frame.

## Vehicle model (`src/vehicles/`)

Each car is a 6-DOF rigid body on four raycast suspension struts, stepped at 240 Hz with semi-implicit Euler. Angular motion is integrated in the body frame, where inertia is diagonal, including the gyroscopic term. Everything is derived from a `CarSpec` through `buildParams()`.

- **Body.** Mass, a weight split that sets the axle positions, center of gravity (COM) height, and an inertia tensor from the dimensions (pitch, yaw and roll, with a yaw scale for mid-engined cars). Body contact points (floor, shoulders and roof) push out of the ground for bottoming out, hard landings and rollovers. Gentle angular damping keeps airborne spins sane.
- **Suspension.** Springs are authored as natural frequency per axle: k = m_corner·(2πf)². Dampers are a damping ratio ζ of critical, with rebound at 1.6× bump. There is a progressive bump stop and an anti-roll bar per axle: F = k_arb·(compression difference), added to the grounded wheel's strut force. Each strut raycasts along the body's down axis to the ground plane and is refined once at the contact point, so slopes and curbs are hit where they are.
- **Load transfer.** Tire forces act at the contact patch, so total lateral and longitudinal load transfer equals m·a·h/track and m·a·h/wheelbase. M1 found that applying them 6–10 cm higher cut longitudinal transfer by about 22%.
- **Tires** (`tires.ts`). A combined-slip magic formula on normalized slip: sx = κ/κ_peak, sy = tan α / tan α_peak, s = |(sx, sy)|. The total force is F = μ·Fz·MF(s), split along (sx, sy), which makes it a friction ellipse (μ_long ≠ μ_lat).
  - The curve is normalized so the peak is at s = 1. The sliding ratio sets C through sin(Cπ/2) = slide, and B is solved for the chosen curvature E.
  - Load sensitivity: μ·(1 − k·(Fz/Fz0 − 1)).
  - Below 3 m/s the slip denominator has a floor, so forces become a stiff viscous friction.
  - A braked tire at rest is anchored to the ground by a tread spring (1.2e5 N/m) with damping, capped at μ·Fz, so parked cars don't creep.
- **Compounds and surfaces.** Each compound (eco, street, sport, semi-slick, slick, rally, off-road, snow, drag) has peak μ, μ_long, α_peak, κ_peak, a sliding ratio and a grip table per surface class. Surfaces (`world/surfaces.ts`) add rolling resistance, viscous drag (sand, mud, water) and deterministic small-scale bumps (gravel and grass feel rough).
- **Drivetrain**, solved implicitly in one step. Each wheel's tire reaction is linearized, X + K·Δω, with K = r²·∂Fx/∂(slip velocity).
  - The engine, clutch and open differentials form one constraint, ω_engine = G·Σwᵢωᵢ, where wᵢ are torque shares: RWD (0, 0, ½, ½), FWD, or AWD with a front split.
  - The clutch torque needed to lock is solved in closed form. If it exceeds the clutch capacity, the clutch slips at capacity.
  - Gearbox losses apply through the torque ratio Gt = G·η, and the kinematic ratio G is separate. M1 found that mixing them let a slipping clutch bypass the losses.
  - LSDs transfer torque between wheels up to preload + ramp·|input torque|. A spool locks completely. AWD adds a viscous center coupling.
  - Brakes and rolling resistance are friction: they stop a wheel but never spin it backward.
- **Engine.** A torque curve sampled every 100 rpm, closed-throttle engine braking proportional to rpm, an idle governor and a hysteresis rev limiter. Turbo cars build boost with a first-order lag above a spool rpm, and torque scales as (1 − share + share·boost). Electric motors have no idle or clutch and a smooth limiter.
- **Gearbox.** Shift points come from the torque curves: upshift where the next gear gives more wheel torque at the same road speed. Downshift low enough to land below the lower gear's upshift point, with throttle-dependent hysteresis and a minimum time between shifts.
  - Upshifts cut ignition so the engine drops to the new gear's speed; downshifts blip to match revs. M1 found that without this, every upshift flared the engine and spun the tires.
  - Manual mode refuses downshifts that would over-rev.
  - Reverse: hold the brake at a standstill for 0.35 s. Pedals swap in reverse, and throttle returns to first.
- **Launch.** Like launch control or a torque converter's stall speed:
  - a throttle governor holds the launch rpm (45% of redline, clamped);
  - the slipping clutch passes the engine's torque at that rpm;
  - with traction control on, the clutch is also capped at what the driven tires can put down (their current load × peak μ_long).

  M1 found two failure modes of simpler designs. An rpm-proportional clutch bogged a turbo engine at 1180 rpm off boost, and TCS fighting the clutch caused a 5 Hz oscillation.
- **Aerodynamics.** Drag ½ρ·CdA·v² at the COM, scaled by a drafting factor (set by the race layer in M4). Downforce ½ρ·ClA·v² is split front and rear at the axles by aero balance.
- **Driver aids.**
  - ABS releases pressure per wheel when the lock slip exceeds κ_peak.
  - TCS cuts throttle when a driven wheel's slip exceeds κ_peak (during a launch the clutch cap does this instead).
  - ESC compares yaw rate with a bicycle-model target, v·δ/(L·(1 + 0.0022·v²)), clamped to 0.85·μ·g/v. It brakes the outer front wheel and cuts throttle on oversteer, and cuts throttle when sideslip passes 0.1 rad. The handbrake bypasses it so you can drift.
  - The steering assist maps the full stick range onto the useful range: around the direction the front axle is actually traveling, within ±1.2·α_peak. Full lock at speed therefore stays near peak grip, and countersteer is always available. "Assisted" also centers the wheels on the travel direction (auto countersteer). Off is raw steering.
  - The steering rack moves at up to 5.5 rad/s. Partial Ackermann (60%). Static toe is added per wheel.

### Tuning formulas (applied in `buildParams`)

| Setting | Effect |
|---|---|
| Tire pressure p (bar from nominal) | Grip × (1 − 0.07·p²); peak slip angle × (1 − 0.06·p): higher pressure gives sharper response. |
| Camber c (deg) | Lateral grip × (1.03 − 0.012·(c + 2)²), best near −2°; longitudinal grip × (1 − 0.008·|c|). |
| Toe (deg) | Static steer angle per wheel (front toe-in points both wheels inward); rear toe-in adds stability. |
| Springs (frequency multiplier) | k = m_corner·(2π·f·mult)²; the free length is recomputed so static ride height is unchanged. |
| Ride height (m) | Moves the COM by 0.9× the offset and shortens or lengthens static strut length (less bump travel when lower). |
| Dampers (bump and rebound multipliers) | c = ζ·2√(k·m)·mult. |
| Anti-roll bars | k_arb × multiplier. |
| Final drive and gears | Ratio multipliers; shift points are recomputed from the torque curve. |
| Brake balance and pressure | Front share of the total brake torque, and a total multiplier. |
| Differential | Accel and decel ramps (locking per N·m of drive or engine-braking torque); AWD front split. |
| Aero (front and rear multipliers) | Change the downforce balance; total ClA comes from the car and aero upgrades. |

Tire width adds grip through load per mm of width: 1 + 0.18·(1 − (N/mm)/15), clamped to 0.9–1.12. M5 wires the sliders and upgrades through these formulas.

## Controls, cameras, HUD

- **Keyboard steering** (`vehicles/controls.ts`) ramps toward ±1. The rate drops from 4.5/s at a crawl to 1.6/s at 200 km/h, and returning to center is always 6/s. A full-lock keyboard tap at 200 km/h produces 2.6° of sideslip (tested). Analog steering gets 30 Hz smoothing. One-shot inputs (shifts) apply to the first sim step of a frame only.
- **Key taps** shorter than a frame are latched until the next poll. M1 found that a quick tap of Esc was otherwise lost.
- **Cameras** (`engine/render/camera-rig.ts`):
  - Chase and far chase damp their offset relative to the car, not their world position, so they never fall behind at speed. They aim along a blend of heading and travel direction, which keeps drifts readable.
  - The bonnet camera is rigid, with rotational smoothing.
  - Speed adds up to 14° of FOV, and at high speed and on impacts there is a little shake (off with reduced shake). Portrait pulls the chase cameras back by 35%.
- **HUD** (`ui/hud.ts`): a canvas tachometer with a redline band, speed, gear, a shift light in manual mode, ABS/TCS/ESC lights, a lap timer and toasts. With touch controls on, the gauge moves above the thumbs.
- **Touch** (`ui/touch.ts`) uses pointer events, so any number of fingers work:
  - **Steering:** relative slide steering. Touch anywhere on the pad, and moving 32% of the pad's width is full lock.
  - **Pedals:** gas and brake, plus a handbrake button.
  - **Buttons:** camera, reset to road, and pause.
  - Everything is at least 44 px and respects safe areas. There is a left-handed mirror layout and haptics on presses and impacts.

## Proving ground (`world/testtrack.ts`)

A flat facility with:
- a 3.6 km clockwise circuit with a 1.2 km main straight;
- red/white curbs on every corner (|κ| > 1/220 m⁻¹);
- gravel traps with tire walls outside tight corners (|κ| > 1/70 m⁻¹);
- armco along the main straight;
- a 40 m skidpad, a paddock and a pit building.

Lap timing requires passing all three sectors in order, so reversing over the line or cutting across the infield doesn't count (tested). "Back to the road" snaps to the nearest point on the circuit from anywhere. M2 makes this the airfield test track on the island.

## Rendering

- **Pipeline:** three.js WebGL2 with ACES tone mapping and sRGB output.
- **Sky and lighting:** a gradient sky shader with a sun disc and haze; one sun with a 90 m PCF-soft shadow box that follows the car; a hemisphere ambient light; and a PMREM environment built from the sky for reflections.
- **Textures:** all procedural on canvases at startup (no image files).
- **Draw calls:** static meshes are merged per material, and trees and cones are instanced. M1 renders in 66 draw calls and 61K triangles, shadow pass included.
- **Car models** (`vehicles/render/car-model.ts`):
  - **Body:** the lower body is lofted through 64 cross-sections that follow style profiles, with wheel arches cut into the sections' outer lower corners.
  - **Glasshouse:** one loft whose quads are assigned to glass, paint (roof, A-pillars, fastback C-pillars) or trim (B-pillars).
  - **Details:** lights, plates, valances and lips are placed by raycasting onto the generated body, so they sit on the real surface for any style.
  - **Materials:** clear-coat paint, and dielectric glass with a clearcoat layer. M1 found that metallic dark glass rendered black, because reflections are tinted by the base color.
- **Effects:** skid marks are a ring buffer of quads with vertex alpha, darker on asphalt and light ruts on loose ground. Smoke, dust and spray are one point cloud with per-particle size and alpha.

## Testing

- `npm run verify` runs the typecheck, the build, the simulation tests (Node) and the browser tests (headless Chromium through Playwright with SwiftShader). `--quick` shortens the long suites.
- **Simulation tests** (`tests/*.test.ts`) are bundled by esbuild and run in parallel Node processes. A file that crashes at import counts as a failure. Measured values go to `artifacts/metrics/*.json`.
- **Browser tests** (`tests/browser/*.mjs`) load `dist/index.html?test=1`. In test mode there is no `requestAnimationFrame` loop. The test steps the simulation through `window.__game` and renders only when it needs a screenshot. Every scenario fails on any console error or blocked network request. Screenshots go to `artifacts/screenshots/` at 1280×720, 390×844 and 844×390.

## Log

- **M0:** stack, build, test harness, deterministic math, conventions, pixel budget, robustness handling.
- **M1:** vehicle model, tire and surface tables, launch control, driver aids, tuning formulas, controls, cameras, HUD, touch, proving ground, car loft generator.
