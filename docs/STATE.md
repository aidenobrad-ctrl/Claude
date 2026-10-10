# Project state

Keep this current so a fresh session can resume.

## Status

| Milestone | State |
|---|---|
| M0 Foundation and test harness | **Done** |
| M1 Driving feel slice | **Done** |
| M2 World v1 | Next |
| M3 Cars v1 | Not started |
| M4 AI v1 and first events | Not started |
| M5 Tuning and upgrades | Not started |
| M6 World v2 | Not started |
| M7 Everything else | Not started |
| M8 Polish and optimization | Not started |

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

## What exists (M1)

- **Vehicle model:** a four-wheel model with launch control, ABS, TCS, ESC and the steering assist; one car (Halden Aster GT).
- **World:** the Halcyon Proving Ground (flat circuit, skidpad and paddock) with lap timing.
- **Cameras and UI:**
  - chase, far chase and bonnet cameras;
  - HUD;
  - touch controls;
  - a pause menu with assist, units and touch toggles;
  - a keyboard help card.
- **Effects:** skid marks, smoke and dust.

## Next steps

M2: island terrain of at least 8×8 km from a seed plus authored regions, a road graph with splines, bridges and tunnels, two biomes, 256 m chunk streaming with LOD, GPS and a minimap. A bot must drive from one end of the map to the other within budget.
