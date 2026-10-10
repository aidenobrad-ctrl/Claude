# Prompt: open-world festival racing game for web, mobile and PC

Paste everything below the line into a new Claude Code session. Opus is recommended (`/model claude-opus-5-5`). Expect a long, many-turn project.

---

You are building a complete, original, open-world festival racing game for the web. It follows the structure of the Forza Horizon series: a huge drivable map, a big garage of detailed cars, varied race events against capable AI, deep tuning, and a progression loop that makes the player want one more event. It must run in desktop browsers (keyboard and gamepad) and on phones (touch).

Work autonomously. Do not stop to ask me questions. When a decision is needed, make it, write it in `docs/DESIGN.md`, and keep going. Do not open a pull request unless I ask. Commit after each milestone and push to the branch you were given.

## 0. Ground rules

- **Original IP only.** Use your own game name, art, music, map and fictional car makes and models. Do not use real manufacturer names or logos, and do not use Forza, Playground Games or Microsoft names, assets or trademarks. The game is inspired by the genre, not a copy.
- **Stack.** TypeScript or modern JavaScript with three.js, bundled with esbuild or Vite. `npm run build` must produce one self-contained `dist/index.html` with no runtime CDN dependency. Pin every dependency version.
- **Layout.** `src/{engine,world,vehicles,ai,events,ui,audio,save}`, `tools/` (test harness and bots), `tests/`, and `docs/` (`DESIGN.md`, `PLAYTEST.md`, `CARS.md`, `STATE.md`).
- **Priorities when budgets conflict, highest first.** Driving feel, AI quality, car visuals, map size, event variety, polish. Cut content before you cut stability.
- **Honesty.** Never claim something works unless you ran it. Separate "verified" from "not verified" in every report.

## 1. Product spec

### 1.1 World

- Map of at least 8 km by 8 km (stretch goal 12 km), seamless, with no loading screens. Stream it in chunks of about 256 m with LOD. Terrain comes from a deterministic seed plus authored regions.
- At least 6 biomes with distinct looks and driving surfaces: coast and beaches, farmland, forest and mountain pass with switchbacks, snow, desert or canyon, a city with a downtown grid and highways, harbor and industrial, a lake or river with bridges, and a dirt or rally region.
- A road network stored as a graph: highways, main roads, town streets, dirt tracks and mountain passes, with intersections, bridges, tunnels, ramps and jumps. The player may drive anywhere, including cross-country shortcuts.
- Towns with instanced buildings of varied facades and lit windows at night. Landmarks such as a lighthouse, castle, stadium, radio tower and wind farm. A festival hub with the garage, shop and campaign start.
- Day and night cycle (a full day in about 24 minutes) and weather: clear, rain with wet-road grip and puddle reflections, fog and storms. Cars have working headlights.
- Discoveries: speed traps, speed zones, drift zones, danger signs (long jumps), smashable billboards, barn-find cars and fast-travel boards, with a fog-of-war map.
- Full-screen map with filters, waypoints, and a GPS ribbon drawn on the road in 3D.
- **Performance budgets.** PC: 60 fps at 1080p on integrated-GPU-class hardware. Mobile: 30 fps at 720p internal resolution or lower, under 400 draw calls per frame. Provide Auto, Low, Medium, High and Ultra presets with dynamic resolution scaling. Use instancing and geometry merging.

### 1.2 Cars

- At least 30 cars in at least 6 classes (for example Compact, Hot Hatch, Classic Muscle, Sports, Super, Hyper, Rally and Off-road, Truck and SUV). Each car has real data: mass, weight distribution, wheelbase, track width, center-of-gravity height, a power and torque curve, gear ratios, final drive, drivetrain (FWD, RWD or AWD), tire compound, aero and brake size. A performance index (PI) is computed from these stats, never typed by hand.
- **Detailed models.** Build a parametric car generator (body profile curves lofted into bodies, wheel arches, glasshouse, bumpers, grille, lights, mirrors, spoilers, exhausts, and a cockpit visible from the driver camera). Body types: hatch, coupe, sedan, wagon, pickup, SUV, mid-engine supercar. Each car needs a unique silhouette plus hand-authored details.
- Optional Blender route: scripted `bpy` modeling exported to GLB hero models. Run a short spike and keep whichever route looks better within the budgets. Budgets: a PC hero car under 60k triangles, LOD1 at about 15k, LOD2 at about 4k. Mobile uses LOD1 at most.
- **Materials and animation.** Clear-coat paint with environment reflections, glass, chrome, rubber and carbon. Working brake, head, tail, reverse and indicator lights. Spinning wheels with detailed rims and brake discs and calipers, suspension travel, body roll and pitch, a moving steering wheel, and exhaust pops on deceleration.
- **Customization.** Paint color and finish, canvas-drawn liveries and decals, at least 3 wheel styles, bumper and spoiler variants, and a plate.
- **Cameras.** Cockpit, bonnet, chase, far chase and free photo mode with depth of field and a time-of-day slider.

### 1.3 Driving physics

- Fixed 240 Hz deterministic step. Four-wheel model with per-wheel load (longitudinal and lateral weight transfer plus aero), per-wheel slip with a Pacejka-style combined-slip curve, a friction circle and load sensitivity.
- Surfaces with their own grip, rolling resistance and drag: dry and wet asphalt, dirt, gravel, sand, grass, snow, ice and mud.
- Suspension with springs, dampers, anti-roll bars and ride height, with ground contact against the terrain, roads and props.
- Drivetrain with engine inertia, clutch, automatic and manual gearbox, an open or limited-slip differential with acceleration and deceleration settings, and an AWD split. Optional turbo lag.
- Toggleable ABS, traction control and stability control, handbrake, and car-to-car and car-to-world collision with impulse response.
- Assist presets from Beginner to Pro, including steering assist and a braking line.
- **Validation.** For every car, run automated 0-100 km/h, 0-200 km/h, 100-0 km/h, skidpad and top-speed tests. Record the results in `docs/CARS.md` and check them against plausible real-world numbers for that class (within 10%). Without slicks or aero, lateral grip must stay below about 1.3 g.

### 1.4 Tuning

- **Upgrades:** engine parts, tires, drivetrain, aero, weight reduction, brakes and chassis. Each has a measurable effect and a PI cost.
- **Tune sliders:** tire pressure, final drive and per-gear ratios, camber, toe, caster, anti-roll bars, springs, ride height, damping (bump and rebound), aero, brake balance and pressure, and differential settings.
- Every slider must change the physics through a documented formula. The tuning screen shows live graphs (power curve, gear and speed chart, balance bar).
- A tuning test track with telemetry: understeer gradient, lateral g, 0-100, top speed and braking distance.
- Export and import tunes as share codes. An auto-tune helper builds a sensible tune for a given event type.

### 1.5 Events and custom AI

- **Event types:** road race (point-to-point and multi-lap), dirt and cross-country race, street race with free route choice, drag, drift zone and drift trail, speed trap, time trial, rival duel and championship series.
- **Race rules:** class and PI limits, a grid with rolling or standing start, cut detection on road races, limited rewind, results, payouts and XP, and reward chests.
- **The AI is the core of the game and must be custom-built.** It must not rely on rubber-banding by default.
  - **Racing lines.** Generate per-route racing lines in the engine: centerline plus curvature, then iterative minimum-curvature or minimum-time optimization within track limits. Compute a speed profile from each car's own accel, brake and grip, so every AI drives its own car under the same physics limits as the player.
  - **Controller.** Pure-pursuit or model-predictive steering with a speed-dependent look-ahead, throttle and brake control that follows the speed profile, and slide recovery.
  - **Personalities.** Each driver has skill (line accuracy, brake-point error, reaction), aggression (defending, overtaking and contact tolerance), consistency (mistake rate) and risk. Difficulty presets map to distributions of these values. Optional dynamic difficulty stays inside a plus or minus 3% performance band.
  - **Racecraft.** Overtake planning with side choice and late-braking windows, defending lines, drafting, obstacle avoidance, contact that resolves exactly as it does for the player, and recovery from spins and off-track excursions (reverse and rejoin).
  - **Traffic.** Lane-following ambient cars on public roads with a cheap LOD simulation that only runs near the player.
  - **Budget.** Under 2 ms per frame for 12 racers on a mid-range phone.
- **AI validation.** Run automated tournaments: every AI against every other AI on every route, over many seeds. Drivers ranked by skill must finish in that order in at least 80% of runs. No AI may be stuck for more than 5 s. Track collision rates and lap-time spread.

### 1.6 Progression, audio and accessibility

- Credits, XP, levels, car shop (prices from PI and class), garage, paint shop, wheelspin and chests, settings, and saves in localStorage with autosave and file export and import.
- **Audio.** Everything is procedural with Web Audio. Engine sounds per car (cylinder count, boost, redline), tire squeal on each surface, wind, collisions, UI sounds, and 3 generative radio stations.
- **Accessibility.** Color-blind-safe HUD, remappable controls, assist toggles, reduced camera shake and text-size options.

### 1.7 Controls and UI

- **Inputs.** Keyboard. Gamepad with deadzones and analog triggers. Touch with an analog slide-steering pad, an optional tilt-steering mode, large gas and brake pedals, optional auto-throttle, left-handed layout and haptics.
- **HUD.** Speedometer, tachometer, gear, assist indicators, minimap with GPS, race position, lap and splits, gap to rival, and skill-chain XP popups.
- **Menus.** Pause (map, garage, upgrade and tune, events, settings). Layouts work from 360 by 640 up to 4K in both orientations, with tap targets of at least 44 px and safe-area insets respected.

## 2. Process: build, play, critique, fix

Work in these milestones. Do not start the next one until the current one passes its own playtest.

- **M0 Foundation and test harness.** Before any content: the bundler, a fixed-step loop, a seeded RNG, a debug API `window.__game` (step(n), setInput, teleport, getState, screenshot), a headless test runner, performance counters and one command, `npm run verify`.
- **M1 Driving feel slice.** One car on a flat test track with full physics, 3 cameras and the HUD. Hit the validation targets in 1.3.
- **M2 World v1.** Terrain, the road graph, 2 biomes, chunk streaming, GPS and minimap. A bot must drive from one end of the map to the other within budget.
- **M3 Cars v1.** 12 cars, the generator, hero details, a showroom with a turntable, and LODs. Review every car from 6 angles.
- **M4 AI v1 and first events.** Racing lines, controller, personalities, a circuit and a sprint, plus the AI tournament tests.
- **M5 Tuning and upgrades.** All sliders wired to physics, test-track telemetry, PI system, shop and saves.
- **M6 World v2.** All biomes, towns, landmarks, day and night, weather, discoveries, fast travel and traffic.
- **M7 Everything else.** All event types, championships, progression, photo mode, settings and accessibility.
- **M8 Polish and optimization.** Mobile profiling, presets, audio, a final art pass, a bug bash and documentation.

**Every milestone follows this loop:**

1. Build the smallest vertical slice.
2. **Play it yourself.** Do not assume it works. Run headless Chromium through Playwright and drive every new mechanic with scripted bots, not just one happy path. Capture screenshots at 1280 by 720, 390 by 844 (phone portrait) and 844 by 390 (phone landscape). Look at the images yourself and judge them as a player and an art director: is it readable, is it fun, and does it look good? Check for z-fighting, clipping, floating wheels, stretched textures, overlapping UI and unreadable text.
3. Write what you found in `docs/PLAYTEST.md`: what you tried, what broke, severity, root cause, fix and retest result. Aim for at least 10 real findings per milestone. If you find fewer, you are not testing hard enough. Try off-road, wrong-way, reverse, spin-outs, extreme tunes, full grids, 10-minute soaks, tab-switching, resizing and multi-finger touch.
4. Fix the findings, rerun the whole regression suite, and commit.
5. Measure, do not guess. Log frame-time proxies, draw calls, triangles, milliseconds per system, memory, bot lap times and AI gaps.
6. **Self-critique.** List the 5 weakest things against this spec and fix at least 3 before moving on.

**Stability soak.** A 15-minute automated free-roam with a random bot must show no NaNs, no console errors, no memory growth beyond a threshold you set, no tunneling through terrain, and no car falling out of the world.

## 3. Environment notes (if you are in a Claude Code-style cloud sandbox)

- You cannot watch the game live. Headless Chromium with software GL (SwiftShader) runs at about 3 fps, so the simulation must be steppable offline. Stub `requestAnimationFrame` and call `__game.step(dt)` so a 10-minute race simulates in seconds, and render only when you need a screenshot. Launch Chromium with `--use-gl=angle --use-angle=swiftshader --enable-unsafe-swiftshader`.
- Playwright is preinstalled (for example under `/opt/node-tools`, with Chromium under `/opt/pw-browsers`). Do not run `playwright install`.
- CDNs and Google Fonts are blocked in the sandbox, but the npm registry works. Install three.js and esbuild from npm and use a system font stack. Block external requests in tests so a missing network resource cannot hide a bug.
- Blender as a Python module works if you need it: `pip install --target ./.cache/bpylib bpy` takes about a minute and about 1 GB. Keep it out of git. CPU Cycles renders fine, but EEVEE needs a GPU and fails headless. Commit your generator scripts and exported GLBs, not the library.
- Add `node_modules` and the Blender library to `.gitignore`. Keep `docs/STATE.md` current (what is done, what is next, how to run the tests) so a fresh session can resume.

## 4. Definition of done

- `npm run verify` passes. This includes the physics validation, AI tournaments, the soak test and zero console errors.
- It runs on desktop with keyboard and gamepad, and on a phone with touch, in both orientations.
- The map, car count, event types, tuning and AI meet section 1.
- `docs/PLAYTEST.md` shows real iteration, and `docs/CARS.md` has the validation numbers.
- Your final report says what was verified and what was not, lists known issues, and includes screenshots.

If time remains, add online-style ghosts and leaderboards stored locally, a livery editor, and more cars and events.
