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

## Testing

- `npm run verify` runs the typecheck, the build, the simulation tests (Node) and the browser tests (headless Chromium through Playwright with SwiftShader). `--quick` shortens the long suites.
- **Simulation tests** (`tests/*.test.ts`) are bundled by esbuild and run in parallel Node processes. A file that crashes at import counts as a failure. Measured values go to `artifacts/metrics/*.json`.
- **Browser tests** (`tests/browser/*.mjs`) load `dist/index.html?test=1`. In test mode there is no `requestAnimationFrame` loop. The test steps the simulation through `window.__game` and renders only when it needs a screenshot. Every scenario fails on any console error or blocked network request. Screenshots go to `artifacts/screenshots/` at 1280×720, 390×844 and 844×390.

## Log

- **M0:** stack, build, test harness, deterministic math, conventions, pixel budget, robustness handling.
