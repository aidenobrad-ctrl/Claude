# Project state

Keep this current so a fresh session can resume.

## Status

Unity (with Blender-made cars) is the main target since M2. The web version is the reference for physics and world.

| Milestone | State |
|---|---|
| M0 Foundation and test harness | **Done** |
| M1 Driving feel slice | **Done** |
| M2 World v1 | **Done** in the web version: the island, roads, GPS and minimap, and a bot that drives from the festival to the city. Exported to Unity, which builds the island at Play (compile-checked). No M2 section in `PLAYTEST.md` yet. |
| M3 Cars v1 | **Done** headless: 12 cars validated within 10%, Blender models with 3 LODs and six-view review sheets, Unity car visuals and the garage (compile-checked). |
| M4 AI v1 and first events | **Done** headless: racing lines, controller, personalities, racecraft, five events, tournament 98% (`dotnet/Halcyon.AI.Tests`). The Unity race menu and HUD are compile-checked only. |
| M5 Tuning and upgrades | **In progress.** The core is done headless: the measured PI and classes, parts, prices and payouts, saves, share codes, the understeer test and the measured auto-tune (`dotnet/Halcyon.Career.Tests`). Caster now acts on the physics. The Unity screens are next. |
| M6 World v2 | Not started |
| M7 Everything else | Not started |
| M8 Polish and optimization | Not started |

**Not verified:** nothing has run in a real Unity Editor yet: the island build, the garage, races, rendering and performance. Unity can't be downloaded in the build container. `docs/UNITY-PC-BRIEF.md` is the brief for a session on the user's PC.

## How to run

```sh
npm install            # Node 22; Playwright uses the preinstalled Chromium in /opt/pw-browsers
npm run build          # writes dist/index.html
npm run dev            # rebuild on change, serve on http://localhost:8080
npm run verify         # typecheck + build + sim tests + browser tests
npm run verify -- --quick
node tools/run-sim-tests.mjs physics --grep "0-100"   # one file, matching cases
node tools/browser-tests.mjs m1                       # browser scenarios in files matching "m1"
```

Unity side (.NET 8; see `docs/UNITY.md`):

```sh
cd dotnet/Halcyon.Core.Tests && dotnet run -c Release          # physics, bit-exact with the TypeScript
cd dotnet/Halcyon.World.Tests && dotnet run -c Release         # island equivalence
cd dotnet/Halcyon.AI.Tests && dotnet run -c Release            # AI: lines, solo laps, sprints, tournament, grids, fields (~4 min)
cd dotnet/Halcyon.AI.Tests && dotnet run -c Release -- tournament   # just the head-to-head tournament (~2 min on 4 cores)
cd dotnet/Halcyon.Career.Tests && dotnet run -c Release      # career: PI, parts, prices, saves, share codes, auto-tune
cd dotnet/Halcyon.Unity && dotnet build                        # compile every Unity script
blender -b -P blender/build_car.py -- --car all --sheet 'blender/renders/{car}.png'   # all 12 cars + sheets; with pip bpy: python blender/build_car.py -- ...
```

Screenshots land in `artifacts/screenshots/` and metrics in `artifacts/metrics/`. Both folders are gitignored.

## Test API: `window.__game`

Load `dist/index.html?test=1` for manual stepping, with no animation loop.

| Call | Effect |
|---|---|
| `step(n)` / `stepSeconds(s)` | Advance n steps of 1/240 s, or s seconds. |
| `render()` | Render one frame now. |
| `setInput(c)` / `patchInput(c)` / `clearInput()` | Replace or merge the scripted controls, or return to physical input. |
| `teleport(x, z, yaw?, y?)` | Move the player. |
| `getState()` / `hash()` | Simulation state, and a bit-exact state hash. |
| `screenshot()` | PNG data URL of the canvas. |
| `perf()` | Frame time, milliseconds per system, draw calls and triangles. |
| `reset()` | Reset the simulation and clear scripted input. |
| `setCamera(x, y, z, tx, ty, tz, fov)` / `setCamera()` | Fix the camera for review shots, or release it. |
| `setUiVisible(v)` | Hide the HUD and touch controls for clean shots. |
| `game` | The `Game` instance, for anything else. |

## Tools

- `node tools/car-review.mjs [carId]` renders each car from six angles, plus a contact sheet, into `artifacts/cars/`.

## What exists

- **Vehicle model:** a four-wheel model with launch control, ABS, TCS, ESC and the steering assist. 12 cars in six classes, each validated against a real reference car (`docs/CARS.md`).
- **World (web, exported to Unity):**
  - the Halcyon Island terrain, roads, bridges, water, forests, settlements and the festival;
  - the proving ground;
  - GPS and the minimap;
  - guardrails.
- **Unity:**
  - the island builders, the car rig and chase camera, input and HUD;
  - Blender car models with LODs;
  - a garage on a turntable (G or LB);
  - festival events with AI rivals (T or D-pad up), with a countdown, positions, laps, gaps, wrong-way warning and results.
- **AI (C# Core):**
  - per-route racing lines and per-car abilities with stable braking;
  - per-driver speed profiles, a pure-pursuit controller and personalities with four difficulty presets;
  - racecraft (follow, pass, defend, give room), recovery and drafting;
  - five events: the Proving Ground GP and four island sprints.
- **Career (C# Core):**
  - a performance index measured on the test track, with classes D to X;
  - nine part categories that change the physics and unlock tuning sliders;
  - car prices from PI, payouts, XP and levels;
  - JSON saves, tune share codes, the understeer gradient and an auto-tune that measures.
- **Blender:** the car generator v2 (`blender/`) for 12 cars, with review sheets in `blender/renders/`. All 12 models are in `unity/Assets/Halcyon/Art/Resources/Cars/`.

## Next steps

- **PC session** (`docs/UNITY-PC-BRIEF.md`): open the project in Unity 6. Then fix what only the Editor reports, play the garage and a race, and capture screenshots.
- **Physics:** brake-force distribution (EBD) with ESC off, in TypeScript and C# together with regenerated references (playtest M4, finding 12).
- **M5 in Unity:** upgrade and tuning screens with live PI and graphs, the test track, the shop, saves, payouts and XP after races, and class-limited events with fields chosen by PI.
- **M6 world v2:** fix the Highcrest switchbacks, then add traffic, day and night, and weather.
- **Art:** the close-range items from the M3 review (saw-toothed arch lips, the Vanta's stepped side window), number plates, terrain textures, a water shader, Blender trees, and audio.
