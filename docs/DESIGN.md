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

- **Pixel budget.** The internal resolution is capped by a pixel budget rather than a fixed device pixel ratio: 1280×720 on mobile and 1920×1080 on desktop by default (`pixelRatioForBudget`). A DPR-3 phone in portrait renders about 0.92 MP instead of 2.1 MP. The presets (Low 960×540, Medium 1280×720, High 1920×1080, Ultra 2560×1440, Auto picking from the device) set the budget and the rest of the quality (post-processing, shadows, grass, scenery and tree distances); the pause menu saves the choice, which applies on a reload. Dynamic resolution (M8) then scales the pixel ratio: below 60 frames a second it steps down by 10% (to 55% at most), with headroom it climbs back by 5%, never more often than every 1.5 s.
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
  - Loose ground (dirt, gravel, sand, grass, snow, mud) gives way under the tread instead of tearing it: grip peaks at 1.5× the slip (κ and tan α), and the sliding ratio moves halfway to 1 (a second curve per tire, `looseCurve`). Cars slide on it progressively. ABS, traction control and launch control aim at the loose peak; the steering assist keeps the paved window, which is steadier (M8).
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
  - Wheelspin holds the gear, so a launch does not upshift on spinning wheels, but only for half a second at the shift point (a leaky timer): in a long slide the next gear is what keeps the driven wheels turning. Before M8 an automatic car in a drift sat on the limiter in first.
  - Manual mode refuses downshifts that would over-rev.
  - Reverse: hold the brake at a standstill for 0.35 s. Pedals swap in reverse, and throttle returns to first.
- **Launch.** Like launch control or a torque converter's stall speed:
  - a throttle governor holds the launch rpm (45% of redline, clamped);
  - the slipping clutch passes the engine's torque at that rpm;
  - with traction control on, the clutch is also capped at what the driven tires can put down: their current load × peak μ_long, with the tire's load sensitivity and any standing water, × 0.98;
  - a trim on that cap steers the driven axles' slip to 80% of κ_peak. It corrects for what the cap cannot see (wheel and driveline inertia, load swings). It rises at 1/s while the slip is under target and falls at 8/s past it, between 0.5 and 1.06, and it only moves while the tires, not the engine, are the limit. An axle's slip is that of its less-slipping wheel: in a turn the loaded outer wheel does the work, while a limited-slip differential spins the light inner one faster than its patch of road.

  M1 found two failure modes of simpler designs. An rpm-proportional clutch bogged a turbo engine at 1180 rpm off boost, and TCS fighting the clutch caused a 5 Hz oscillation. M5 found four more (see PLAYTEST M5). A cap without load sensitivity let a squatting car's wheels spin up, so 6% more power made the Ironside slower. An open-loop cap left 5–8% of the grip unused. A trim that kept rising while the engine was the limit wound up and spun the tires on slicks. A trim that read the inner wheel's slip in a turn as wheelspin made the car crawl away with the wheel turned.
- **Aerodynamics.** Drag ½ρ·CdA·v² at the COM, scaled by a drafting factor (set by the race layer in M4). Downforce ½ρ·ClA·v² is split front and rear at the axles by aero balance.
- **Driver aids.**
  - ABS releases pressure per wheel when the lock slip exceeds κ_peak.
  - TCS cuts throttle when a driven wheel's slip exceeds κ_peak (during a launch the clutch cap does this instead).
  - ESC compares yaw rate with a bicycle-model target, v·δ/(L·(1 + 0.0022·v²)), clamped to 0.85·μ·g/v. It brakes the outer front wheel and cuts throttle on oversteer, and cuts throttle when sideslip passes 0.1 rad. The handbrake bypasses it so you can drift.
  - The handbrake declutches at any speed, as in a drift car (and in Forza): the engine cannot fight it, and an AWD car's rear wheels lock free of the front ones. Released with the engine revved, the clutch bites like a clutch kick. Before M8 it only declutched under 7 m/s, and an AWD car's rears barely slowed under it.
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
| Caster c (deg, stock 5.5) | Steering leans each steered wheel: its camber changes by (c − 5.5)·sin δ degrees, more negative on the outer wheel and less on the inner. Lateral grip then follows the camber formula wheel by wheel. At the stock 5.5° nothing changes, so every validated figure stands. |

Tire width adds grip through load per mm of width: 1 + 0.18·(1 − (N/mm)/15), clamped to 0.9–1.12. M5 wires the sliders and upgrades through these formulas, and a test proves that every one of the 24 sliders and the gear ratios changes the car's path.

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
- **Terrain colour (M8 art pass):** meadows are tinted dry or lush by noise at three scales (1.4 km fields, 380 m patches, 95 m tufts), stretched to use its full range (the noise sits in a narrow band around 0.5); sun-facing slopes dry out first; lush patches carry a faint speckle of wildflowers up close. A view across the island reads as pasture and farmland instead of one green.

## Unity and Blender

The user asked for the game in Unity with Blender-made cars, so from M2 on Unity is the main target. The web version stays as the reference implementation.

- **Simulation in pure C#.** `unity/Assets/Halcyon/Core` is a line-by-line port of the TypeScript simulation (no UnityEngine). It uses doubles throughout, the same operation order, and fdlibm trig in `DMath`. The car physics is bit-exact with the TypeScript: `dotnet/Halcyon.Core.Tests` compares 339,299 numbers across 91 checks. The island loads from data exported by `tools/export-unity.ts`, and the world tests agree to about 1 cm.
- **Why port instead of rewriting.** The driving feel was tuned and validated in M1. A bit-exact port keeps every validated number and lets the web tests keep guarding the physics.
- **The core is thread-safe.** There are no mutable statics. Collision scratch is per thread, so the garage can measure cars on a worker thread and the AI tournaments run in parallel.
- **Testing outside Unity.** Unity can't run in the build container. Every Unity script is compile-checked against the UnityEngine reference assemblies (`dotnet/Halcyon.Unity`), and all behaviour that matters lives in Core, where .NET tests exercise it headless.

## Cars: the Blender generator (`blender/`)

- **Body.** Each car is a `CarShape`: dimensions, a side profile (nose, bonnet, cowl, windscreen, roof, rear slope, deck, tail), section shapes (tumblehome, crowns, tapers) and arches. These define a smooth signed distance field. Surface nets polygonize it, and the vertices are then projected back onto the exact field.
- **Crisp features.** Features such as glass, lights, grilles, intakes, vents, panel gaps and stripes are regions on the surface.
  - Each LOD cuts the body along the regions' exact contours (`meshops.cut`).
  - Holes get walls and an insert surface, offset by the feature's depth.
  - Patches become separate material groups.
  - Edges therefore stay sharp however far the body is decimated.
- **Arch skin.** Over each wheel arch the body's top surface is lifted, with a smooth max, so at least 8 cm of skin is left above the arch cut. Surface nets at 3 cm voxels need about two voxels of material to close a shell; thinner skin tore.
- **Normals and LODs.** Normals come from the field's gradient. `fix_winding` makes the faces agree with them after decimation. There are three LODs (about 24k, 6k and 1.6k body triangles).
- **Parts.** Wheels, interior, wings, spoilers, bumpers, roof racks, bull bars, snorkels and similar parts come from a registry, so every car is data in `blender/halcyon/cars.py`.
- **Export.** One FBX per car with `Wheel_XX` empties, plus a `<id>.style.json` with paint, rim and calliper colours. Unity remaps materials by name (`CarVisual`) and builds one LODGroup.
- **Review.** A six-view Cycles contact sheet per car goes to `blender/renders/`.

## AI (`unity/Assets/Halcyon/Core/AI/`)

The AI drives the same `Vehicle` and physics as the player. Only its inputs differ.

- **Routes.** `RaceRoute` resamples a route every 3 m with widths to each side.
  - The proving circuit uses its asphalt plus part of the kerbs.
  - Island sprints come from an A* search over the road graph, and use the paved width only, narrowed near guardrails and on bridges.
- **Racing line.** Minimum curvature: Gauss–Seidel sweeps of the biharmonic update, projected onto each sample's normal and clamped to the track, run coarse to fine. The line keeps 0.7 m from the edges plus the car's half-width. Its radius never drops below 7.5 m, otherwise hairpins bunch the line on the inside.
- **Car ability.** Each car (with its parts and tune) is measured on the test track:
  - skidpad grip, never more than its rollover limit: 85% of half its track over its CG height (a truck on slicks would grip harder than it can corner without tipping);
  - braking from 100 km/h;
  - a full-throttle acceleration curve;
  - drag and downforce;
  - a stable-braking limit.

  The stable-braking limit is the braking a car can do at a given lateral acceleration while its unloaded rear still has grip. It comes from the brake split, CG height, wheelbase and weight split.
- **Speed profile.** Per driver and line: the cornering limit, then a forward pass with acceleration, then a backward pass with braking. Both passes share a friction circle and respect stable braking.
- **Controller.**
  - Steering is pure pursuit on the line (look-ahead 7.5–42 m with speed), plus yaw damping towards the arc being steered and countersteer in slides.
  - The steering target keeps 0.25 m from the road edges, plus 0.025 m for every m/s over 20 m/s: a car tracking a target that hugs the edge at speed runs past it.
  - Lane changes (passing, defending, mistakes) ease in, never faster than 2.5 m/s sideways.
  - Off the road above 80 km/h the driver brakes, harder unless sliding, and aims 2.5 times its lateral error ahead, so it rejoins at about 22° instead of turning back at full lock.
  - Pedals use feedforward from the profile's slope plus feedback, converted to throttle and brake through the car's measured curves.
  - The driver eases the pedals when the yaw runs ahead of the arc. The stability control is off, with ABS and TCS on.
- **Skill.** Skill is what separates drivers. From 0.3 to 1.0 it spans about 15–20% of pace, about 3% per 0.14 step. It sets:
  - line accuracy: the line blended towards the centreline, 0.30 + 0.70·skill;
  - grip used: 0.72 + 0.20·skill + 0.03·risk;
  - braking used: 0.64 + 0.28·skill + 0.03·risk;
  - throttle commitment: 0.80 + 0.20·skill;
  - reaction: braking up to 0.35·(1 − skill) s early and the throttle twice that late;
  - wander around the line.
- **Other traits.**
  - Consistency sets the mistake rate (late braking, lifting, running wide); a late-braking mistake never exceeds the car's real grip.
  - Aggression sets following distance, when to pass and whether to defend.
  - Risk adds a little grip and braking.
  - Difficulty presets (Novice 0.35, Average 0.58, Pro 0.78, Legend 0.93) centre a spread of skills.
  - There is no rubber-banding.
- **Racecraft.**
  - A driver follows on the line until close.
  - Passing is a state machine on one car. It starts within about half a second, or from the slipstream on a straight, and takes the inside of the next corner when there is room. It holds beside that car until it is behind, or gives up if it never gets alongside. It backs out rather than be squeezed off the road, and slows for a tighter inside line.
  - A car alongside sets a hard limit on where the steering target may go.
  - A car ahead that is spinning or rolling backwards counts as stopped: its speed is never followed below zero.
  - Aggressive drivers defend the inside.
- **Recovery.** One reverse attempt when stuck; otherwise a reset to the route (flipped, far off the road, stuck, or facing the wrong way). Cars that have finished park.
- **Races.** `RaceSession` runs a race on the fixed step, the same in Unity and in the tests. It handles:
  - a two-column grid and a countdown;
  - laps or sprint distance, positions and lap times;
  - drafting (up to 32% less drag 3–30 m behind);
  - contacts counted as episodes, with wall hits kept apart.

  The events are a 3-lap Proving Ground Grand Prix and four island sprints.
- **Tournament.** The spec says "every AI against every other AI on every route, over many seeds", so the check is head to head:
  - 6 skills, 15 pairs, 5 routes and 4 seeds make 300 races;
  - the stronger driver starts from behind in half of them;
  - it must win at least 80%, and no AI may be stuck for 5 s.

  Six-car grids and mixed fields add crowd statistics.

## Career (`unity/Assets/Halcyon/Core/Career/`)

M5 is pure C# in Core, like the AI, so it is tested headless (`dotnet/Halcyon.Career.Tests`).

- **Performance index (PI).** It is measured, never typed in. The inputs are the four figures in `docs/CARS.md`, with downforce added to the grip:
  - 0–100 km/h on a log scale: 11 s scores 0 and 1.9 s scores 1;
  - top speed: 150 km/h scores 0 and 430 km/h scores 1;
  - grip at 180 km/h (skidpad grip plus downforce at 50 m/s): 0.62 g scores 0 and 1.30 g scores 1;
  - 100–0 km/h: 48 m scores 0 and 25 m scores 1.

  The grip counts only up to the car's rollover limit (85% of half its track over its CG height), so a truck on slicks is rated on the grip it can use. S = 0.42·accel + 0.20·top + 0.26·grip + 0.12·braking, and PI = 100 + 899·S^0.62. The exponent spreads the crowded middle. Classes are D ≤ 500, C ≤ 600, B ≤ 700, A ≤ 800, S1 ≤ 900, S2 ≤ 998 and X. The stock roster runs from the Ridgeback (D 353) through the Aster GT (A 703) to the Vanta (S2 928). One measurement takes 70–100 ms, so menus measure on a worker thread.
- **Parts.** Nine categories, each with levels. Every part folds into the physics' `Upgrades` layer (power, mass, tyre compound and width, brake torque, aero kit), so its effect goes through the same formulas as everything else.

  | Category | Levels |
  |---|---|
  | Engine | +6%, +13%, +22% power |
  | Weight | −3%, −6%, −10% mass |
  | Tyres | one or two steps up the road ladder (eco, street, sport, semi-slick, slick), slicks, rally, off-road |
  | Width | +15 mm, +30 mm |
  | Brakes | +20%, +45% torque |
  | Aero | sport kit (+0.25 m² ClA), race kit (+0.6 m²) |
  | Transmission, differential, suspension | no physics change of their own; they unlock tuning |

  As in Forza, parts unlock sliders:
  - tyre pressures are always open;
  - a sport transmission unlocks the final drive, and a race transmission every ratio;
  - sport suspension unlocks springs, ride height and anti-roll bars, and race suspension adds dampers and alignment;
  - race brakes unlock balance and pressure;
  - aero kits unlock front and rear downforce;
  - a sport differential unlocks locking and the AWD split.

  A part costs a share of the car's price, with a minimum, rounded to 100 credits.
- **Economy.**
  - A car's price is exp(10.1 + 0.006·(PI − 450) + 0.000273·max(0, PI − 850)²), rounded to 500 credits. The Ridgeback costs 13,500, the Aster GT 111,000, the Saetta 300,000 and the Vanta 2,255,500. Selling returns 60%.
  - A career starts with 50,000 credits and the Aster GT.
  - A race pays 2500·√max(0.5, distance / 5 km), rounded to 50 credits, times three factors:
    - finishing position: 1, 0.7, 0.5, 0.35 and 0.25 for the top five, then 0.15;
    - class: from 1.0 for D up to 2.6 for X;
    - rivals' difficulty: Novice 0.7, Average 1.0, Pro 1.3, Legend 1.7.
  - XP is 100 per km plus 300, 200 or 100 for a podium. Level n needs 1000·n^1.3 XP.
- **Rivals by PI.** Every car has the same upgrade ladder of nine steps, from stock to every part, each step a superset of the last; the career tests check that the PI never falls along it. For a player at PI p, a rival's car is the highest step whose measured PI is at most p + 15, and never above the event's class. Cars within 40 PI of the player make the pool. A seeded shuffle of the pool fills the grid, so a model repeats only when the pool is smaller than the field. If no car comes within 40, the closest one that fits the class races alone, because a one-make race is fairer than a mismatched one. PIs are measured in parallel and cached per car and step (108 measurements take 2.6 s on 4 cores).
- **Events and classes.** The Proving Ground GP is for class A and below, the Harbour Dash for S1, the Ashby Sprint for B and the Saltmarsh Bay Run for D. The Port Calder run is open. The starter car (A 703) can enter three events. A D car (from 13,500 credits) opens Saltmarsh, and a B car opens Ashby.
- **Saves.** The profile holds credits, XP, races, wins, and the garage: each car with its parts, tune and paint. It is versioned JSON, written by a small writer and read with MiniJson. A save that names a car the game no longer has still loads, without that car.
- **Sliders and share codes.** `TuneSpec` lists the 24 sliders with range, step, unit, the parts that unlock them and the formula the menu shows. A share code is `HT1.` plus base64url of the car, one byte per slider (its step index), one byte per gear ratio and a checksum. Every slider has at most 255 steps, so a snapped tune survives the round trip exactly. Damaged codes are refused.
- **Test track.** The understeer gradient is the extra steering per g on a 60 m circle, measured at 0.3 and 0.6 of the car's grip. On a constant radius the kinematic steer angle cancels. The stock roster runs from +0.29 deg/g (Vanta) to +0.98 deg/g (Kiri RS).
- **Auto-tune by measurement.** Rules of thumb measured worse than stock, so the auto-tune measures instead.
  - It runs a coordinate search over the unlocked sliders: steps of 6, then 3, then 1 slider step, within 48 measurements.
  - A change is kept only if it improves a score measured on the event's surface:
    - circuit: 0.25·accel + 0.5·grip + 0.25·braking;
    - road: 0.4/0.35/0.25;
    - dirt, on gravel: 0.3/0.45/0.25;
    - drag: acceleration plus the quarter mile.
  - Except for drag, the car must also keep an understeer gradient of at least max(0.25, min(stock − 0.15, 0.6)) deg/g. The steady tests reward rotation, so an unconstrained search found tail-happy setups.
  - Rear toe-out is never used. Dampers are left alone, because the steady tests cannot judge them.

## The living island (M6: `unity/Assets/Halcyon/Core/World/`, `Core/AI/Traffic.cs`)

Like the AI and the career, all of M6's logic is pure C# in Core, stepped by the simulation and tested headless (`dotnet/Halcyon.Sim.Tests`). Unity only draws it.

- **Clock.** A festival day lasts 24 real minutes (one game hour a minute) at latitude 35° in midsummer (declination 15°): sunrise 5:17, sunset 18:43, the sun 70° up at noon. The moon is full, opposite the sun. During a race the clock runs at a fifth, so a night race stays a night race.
- **Weather.** A seeded Markov chain over Clear, Cloudy, Rain, Storm and Fog, each spell a few minutes long. Cloud, rain, fog and wind ease towards each state's values, and rain is capped by the cloud. Fog is likelier at dawn. Lightning is a Poisson process (about every 10 s) once a storm's rain has built.
  - **Road wetness** (0 to 1) soaks towards the rain with a time constant of 30 s divided by the rain's strength, and dries with daylight and wind (3 to 12 minutes).
  - The physics reads it per car: the paved grip blends from asphalt to wet asphalt, and standing water adds drag, in the TypeScript and the C# together (bit-exact).
  - A race holds the weather: the sky may change, but the road stays as wet as it was, rounded to a tenth. The AI measures its abilities at that wetness (cached per tenth).
  - Events can carry conditions (an hour and a sky): the Proving Ground by Night, and the Storm Run to Port Calder.
- **Traffic.** About 26 cars around the player, driving on the right. They appear out of sight, 500–1100 m away, and vanish beyond 1.4 km.
  - Beyond about 220 m they are ghosts, posed along their lane with no physics. Within it they become physics cars, so they can be hit, and they go back to ghosts beyond 280 m.
  - A physics car follows its lane line by pure pursuit, with the steering assist off. Its speed comes from the road class and the bends ahead.
  - It keeps a gap (11 m at a standstill) to anything ahead along its own path, sampled every 4 m for 80 m. It yields at junctions and holds on the handbrake when stopped.
  - On dual carriageways it pulls out to pass when the other lane is clear: the gaps it needs grow with the closing speed.
  - It goes round a stopped car only when the sideways move fits the gap, and otherwise backs up first.
  - A car stuck for 5 s is put back on its lane once the player is 60 m away.
  - Races close the roads: the race suspends the traffic and resumes it afterwards.
- **The Highcrest pass.** The road baker routes it with a heading-aware A* on a 48 m lattice (at most 90° per step, never back on itself), then rounds every corner with a 20 m fillet. The pass is one continuous road, and its height profile is box-filtered (30 m, twice) after the grade limit. The AI's speed plan also reads crest curvature from the route's heights: cornering speed satisfies v² ≤ μg/(k + μc), and over a crest the speed is at most 0.95·√(g/c).
- **Landmarks.** Five, placed from the world data the same way every time: a radio mast on the highest summit a road reaches, a lighthouse on the eastern cape, a wind farm of six turbines on the hills above Westhaven, a stone arch in the Redmesa desert, and an observatory on the high ground above the lake. Each stands on dry land, 30–600 m from the nearest road over the whole network, far from the others, and is found from that road. Their structures are solid to the cars.
- **Discoveries.** Places (the festival, the city, the villages, the harbour and the proving ground) and landmarks are found by driving within their radius, and pay XP (landmarks credits too). Every 4 m road sample driven is kept in a bitset, with a reward for each tenth of the network. The fog of war is a 128 × 128 grid, cleared within 280 m of wherever the player has been. Fast travel goes to any place found, onto its nearest road (the proving ground: its paddock). All of it is in the profile as compact base64, and older saves load with an undiscovered island.
- **GPS.** A destination is a place, a landmark, or any point clicked on the map. The route is the shortest over the road network (A* by length), from the car's nearest road to the destination's.
  - The GPS follows the car along it and plans again 1.5 s after the car strays more than 45 m from it (every 5 s when it is far from any road).
  - It calls out junctions of three or more ways where the route turns (35° or more), and forks where it takes a gentler branch than the straightest road on.
  - It says "turn around" after 2 s facing back along the route, and "arrived" within 30 m of the route's end, which is the road nearest the destination.

## Events, stunts and progression (M7: `Core/AI/RaceSession.cs`, `Core/AI/Ghost.cs`, `Core/World/Stunts.cs`, `Core/Career/`)

- **The roads, rebuilt.** The road baker routes highways, main roads and dirt tracks with the heading-aware A* the pass got in M6, on lattices of 36, 24 and 24 m that turn at most 22.5°, 67.5° and 90° a step, with a cost per turn. The heading carries on through each place in a chain, so a road runs through a town instead of kinking there. Corners are rounded with arcs of up to 200 m (highways), 40 m (main roads) and 22 m (dirt).
  - **Junctions.** Where two roads come within their half widths plus 2 m without crossing, they get a junction. Every road stays within 1% of level for 25 m from a junction, and on for as far as its pavement overlaps another road's there, so roads meet at one height. A road too short to climb between its two junctions that way keeps the plain grade limit.
  - **Yumps.** On the longest flat, straight stretch of a dirt track or country main road, the profile rises over a raised-cosine crest: 1.3 m over 52 m on a main road, 2.2 m over 64 m on dirt, within the class's grade limit. Four roads have one.
- **Race kinds.** A `RaceEvent` is a circuit, a sprint or a drag race, on asphalt or dirt, and can name a rival or a ghost.
  - *Drag:* two cars side by side on the longest straight, each in its own lane line, over a quarter mile, with the trap speed at the line.
  - *Rally:* the AI measures its grip, braking and acceleration on the event's surface, and plans with them.
  - *Head to head:* the festival's rival, Mika Varga (skill 0.95, aggression 0.75), in the car the PI matching chose.
  - *Time trial:* the rival's run is recorded headless in a simulation of its own, 60 samples a second (pose, progress), on a worker thread while the grid lines up. The gap to it at any point is the time the ghost reached that point, minus now.
- **Stunts.** Placed from the road data, the same every time, at least 450 m apart.
  - *Speed traps:* the middle of a straight of 350 m or more; targets from the speed reachable from the slower way in (0.7, 0.85 and 1.0 of it).
  - *Speed zones:* 500–800 m with no bend under 100 m and some turning; targets 0.65, 0.8 and 0.95 of a speed profile (1.05 g in bends, 4.5 m/s² accelerating, less towards 250 km/h, 8 m/s² braking).
  - *Drift zones:* 350–450 m of main road or highway turning 120° or more in bends of 30–150 m. Points per second: speed × min(1, (angle − 10°)/30°) × 10 × the chain (1 to 3 after 8 s of sliding), between 10° and 90° above 30 km/h. A hit loses the chain; points bank after 1.2 s without sliding. Targets 1.5, 4 and 8 points a metre.
  - *Danger signs:* a crest with a straight run-up and run-out. The targets are measured: a stock Aster GT driven over it at 80, 90 and 100% of the run-up speed, from takeoff (within the crest's last 40 m) to landing, 3% short and rounded down to half metres. A crest with a flight before that is not a sign.
  - Each new star pays once: 150 XP and 1,000 credits for the first star, double for the second, triple for the third.
- **Championships.** Three: the Grassroots Trophy (class D), the Coastal Cup (class A, level 3) and Mountain Masters (class A, level 6), of three or four festival events. A series is raced in one car against the same rivals (one seed, one difficulty), scored 25-18-15-12-10-8-6-4, and pays 40,000, 20,000 or 10,000 credits (times the class factor) and 3,000, 1,800 or 1,000 XP for the top three. The series under way is in the profile.
- **Progression.** Festival levels need 1000 n^1.3 XP each. Events open at levels 1 to 6. 26 accolades (wins, podiums, each kind of event, championships, the garage, exploration, stunts) pay their XP once, from counters kept in the profile.
- **Settings** are kept apart from the career, in their own JSON (a shared career code should not carry someone's settings). Values read back are clamped to their ranges.
- **Accessibility in Unity:** HUD size 75–150%, palettes that keep good and bad apart with red-green or blue-yellow colour blindness (the HUD, the gaps and the racing line), camera shake from none to full, motion blur off, field of view 50–80°, lightning at a quarter brightness, the minimap and key hints off, and mph and miles.
- **The racing line** in Unity is the AI's line for the route, coloured from the player's own car: its speed profile (with a little more margin than the AI's) against the speed now, and the braking each point asks for.

## Sound (M8: `src/audio/`, `unity/Assets/Halcyon/Core/Audio/`, `Scripts/Audio/`)

Everything is synthesized; there are no recordings. The synthesis is sample code (no Web Audio graph inside it), written once in TypeScript and ported to C#, where it renders the same samples (checked: engine loops and beds bit-exact, one-shots within one float ulp, the radio within 1e-12; `AudioChecks.cs`).

- **Engines** (`engine-synth`). Each firing of a cylinder is an exhaust pulse (a smooth bump plus a burst of combustion noise), timed by the firing order with a little jitter per cycle. The pulses go through each bank's exhaust: a boom (55–150 Hz, lower for bigger engines), a honk (300–620 Hz), a rasp (1.5–3 kHz, louder on sporty cars) and a muffler, then a little saturation. The firing pattern carries the character: an inline four's even buzz, a six's wail, a cross-plane V8's uneven banks (L R R L R L L R: the burble; V8s that rev past 7500 are flat-plane), a V6's two banks. Loops are rendered at a ladder of speeds about 900 rpm apart, on and off the throttle, each a whole number of engine cycles and rendered three periods over so the filters settle and the seam is clean. At run time the two loops either side of the rpm play, pitched to it and crossfaded with equal power, and the on- and off-load sets blend with the throttle (the rev limiter's cut gives the "brap"). Electric motors are a stack of motor harmonics and a gear whine.
- **Beds and one-shots** (`beds`). Looping beds pitched and faded each frame: tire squeal (narrow, wandering bands near 1 kHz) from the slip on paved surfaces, road roar with speed, gravel crunch, grass swish, snow crunch and spray on loose or wet ground, wind with speed squared, intake noise with throttle and rpm, and a turbo whistle with boost. One-shots: three weights of impact (a thump, ringing metal and crunch), the blow-off valve on a lift at boost, overrun crackle on sporty engines, the gear shift, and UI blips (move, select, back, countdown, go, checkpoint, reward, photo).
- **The radio** (`radio`). Three stations compose endless songs from a seed: a key, a tempo, a progression from a station's set, an arrangement of sections with an energy each, a two-bar motif fitted to each chord, bass and drum patterns. Halcyon Pulse is synthwave (four on the floor, gated saw pads, arpeggios), Coastline FM is lo-fi (swung drums, an FM electric piano's seventh chords, vinyl crackle), Redline Radio is rock (power chords through a driven amp, crashes and fills). Bars are rendered with a small reverb, a ping-pong delay and a limiter, about 25 times faster than real time; every other song the station says its name with a jingle.
- **Web.** A worker renders the loops, beds and bars (the main thread only plays buffers and moves gains and playback rates). Nothing starts until the first key, click or touch (browsers require it). Settings: master, effects and music volume, the station (N, the FM touch button or the pause menu); saved in the browser.
- **Unity.** Loops and beds render on worker threads and become clips on the main thread. The player's car is 2D; the four nearest other cars (racers and traffic) get 3D engine voices. The radio renders bars on a worker thread into a queue that `OnAudioFilterRead` plays. The bonnet camera muffles the car. Settings add engine, effects, music and station to the master and weather volumes.

## Testing

- `npm run verify` runs the typecheck, the build, the simulation tests (Node) and the browser tests (headless Chromium through Playwright with SwiftShader). `--quick` shortens the long suites.
- **Simulation tests** (`tests/*.test.ts`) are bundled by esbuild and run in parallel Node processes. A file that crashes at import counts as a failure. Measured values go to `artifacts/metrics/*.json`.
- **Browser tests** (`tests/browser/*.mjs`) load `dist/index.html?test=1`. In test mode there is no `requestAnimationFrame` loop. The test steps the simulation through `window.__game` and renders only when it needs a screenshot. Every scenario fails on any console error or blocked network request. Screenshots go to `artifacts/screenshots/` at 1280×720, 390×844 and 844×390.

## Log

- **M0:** stack, build, test harness, deterministic math, conventions, pixel budget, robustness handling.
- **M1:** vehicle model, tire and surface tables, launch control, driver aids, tuning formulas, controls, cameras, HUD, touch, proving ground, car loft generator.
- **M2 (web):** the streamed island (terrain, roads, water, forests, settlements, the festival), minimap and GPS, and guardrails on retaining walls.
- **Unity port:** the user asked for Unity and Blender. The island is exported as data, the simulation is ported to bit-exact C#, and Unity builds the island at Play.
- **M3:** 12 cars, validated within 10% of real references. The Blender generator v2 has crisp feature edges, three LODs, all body styles and six-view review sheets. Unity has car visuals and a garage with a turntable.
- **M4:** racing lines, car abilities, speed profiles, the driver controller, personalities and difficulty, racecraft, recovery, races, five events, a race menu and HUD in Unity, and the head-to-head tournament (98% skill order).
- **M4 decisions:**
  - The AI drives with ESC off and plans with each car's stable braking.
  - Skill is worth about 3% of pace per step.
  - Guardrails are skipped on the inside of bends tighter than their offset.
  - The Highcrest pass is not an event until world v2 fixes its switchbacks.
- **M5 (core):** the measured performance index and classes, the parts catalogue, prices, payouts and XP, profile saves, tune share codes, the understeer test and the measured auto-tune. Caster now acts on the physics.
- **M5 (rivals and robustness):** rivals matched to the player's PI with parts, class limits on events, the slip-steered launch control (TypeScript and C#), rollover-aware AI and PI, and AI lane changes, edge margins and off-road recovery that hold up at 250+ km/h.
- **M5 decisions:**
  - PI comes only from measurement, so a part or a tune changes it exactly as much as it changes the car.
  - The auto-tune measures instead of following rules, and it may not make a car more tail-happy than its stock understeer allows.
- **M6:** wet roads in the shared physics, the clock and the weather, the sky, night, rain and lightning in Unity, events with their own conditions, traffic in two levels of detail, the rebuilt Highcrest pass and its hill climb, crest-aware AI, landmarks, discoveries, the map with fog of war and fast travel, and the GPS in Unity.
- **M6 decisions:**
  - Weather and traffic live in Core, so they are deterministic and tested headless like everything else.
  - A race holds the weather, so the AI's plan stays true from start to finish.
  - Traffic is a ghost when far and a physics car when near, so the island can be busy without simulating dozens of cars.
  - The GPS arrives at the road nearest a destination, since places off the road are found from there.
- **M7:** stunts (speed traps, speed zones, drift zones and danger signs on yumps), drag races, a rally on dirt, a head-to-head and a ghost time trial, championships, festival levels and accolades, the road network rerouted with headings and level junctions, the AI braking in time for much slower cars, settings and accessibility, photo mode and the racing line in Unity.
- **M7 decisions:**
  - Stunt targets are measured or modelled from the road, never guessed: jumps by driving them, zones from a speed profile.
  - Yumps are built into the road profile: real crests were too few and too messy to rely on.
  - Roads that share pavement must share a height: the baker makes junctions where roads touch, and the profiles stay level where pavements overlap.
  - Settings live outside the career save.
- **M8:** drifting (the handbrake declutches, the automatic box upshifts in a long slide, loose ground grips like loose ground), procedural sound in both versions (engines from firing pulses, tires, wind, impacts, UI and three generative radio stations), graphics presets in the web pause menu and dynamic resolution, an art pass on the meadows, a bug bash of the never-run Unity code, and the FM button on the touch controls.
- **M8 decisions:**
  - Drift feel came from the drivetrain and the controls, not the tire curve: the post-peak curve was already gentle (10% down at four times the peak slip). A probe drove every car with three kinds of driver (`tools/dev/drift-probe.ts`).
  - The sound is synthesized sample code, the same in TypeScript and C#, so the web and Unity sound alike and the port is tested like the physics.
  - Engines are played like recorded loops (pitch and crossfade between rpm points), not synthesized live: a few native sources per car instead of DSP in a script, on the web and in Unity.
  - No heavy mobile work: the player's phone is a current flagship; presets, the pixel budget and dynamic resolution cover the rest.
