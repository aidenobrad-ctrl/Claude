# Halcyon Roads

An original open-world festival racing game for the web, built with TypeScript and three.js. It runs in desktop browsers (keyboard or gamepad) and on phones (touch).

> **Work in progress.** This is being built milestone by milestone. See [`docs/STATE.md`](docs/STATE.md) for what works today, and [`docs/PLAYTEST.md`](docs/PLAYTEST.md) for what was tested and fixed.
>
> **Today (M1):** one car on the Halcyon Proving Ground, with a full four-wheel driving model:
> - Pacejka tires, suspension and anti-roll bars, a clutch and gearbox, an LSD, launch control, and ABS, TCS and stability control;
> - chase, far chase and bonnet cameras;
> - the HUD, lap timing, and touch controls for phones.

## Controls

| Keyboard | Gamepad | Touch | Action |
| --- | --- | --- | --- |
| W / ↑ | RT | GAS | Throttle |
| S / ↓ | LT | BRAKE | Brake; hold at a stop to reverse |
| A D / ← → | Left stick | Slide on the steering pad | Steer |
| Space | A | HAND BRAKE | Handbrake |
| E / Q | B / X | | Shift up / down (manual gearbox) |
| C | RB | CAM | Change camera |
| B | LB | | Look back |
| R | | ROAD | Back to the road |
| Esc / P | Menu | II | Pause, assists and settings |

## Play

Open [`dist/index.html`](dist/index.html) in a browser. It is a single self-contained file with no network access. Or build and serve it yourself:

```sh
npm install
npm run build      # writes dist/index.html
npm run dev        # rebuild on change, serve on http://localhost:8080
```

## Develop

```sh
npm run verify     # typecheck, build, simulation tests (Node), browser tests (headless Chromium)
```

The simulation (physics, AI, events and world queries) has no dependency on three.js or the DOM, and runs bit-for-bit identically in Node and in the browser. See [`docs/DESIGN.md`](docs/DESIGN.md) for the architecture and the reasons behind it.

| Folder | Contents |
| --- | --- |
| `src/engine` | Fixed-step clock, seeded RNG, deterministic math, input, perf counters |
| `src/world`, `src/vehicles`, `src/ai`, `src/events` | Simulation, plus the matching three.js rendering |
| `src/ui`, `src/audio`, `src/save` | HUD and menus, procedural audio, saves |
| `tools/` | Build, test runners, Playwright helpers |
| `tests/` | Simulation tests (`*.test.ts`) and browser scenarios (`browser/*.mjs`) |
| `docs/` | Design notes, playtest log, car data, project state |

## Legacy

[`legacy/duskline/`](legacy/duskline/) holds Duskline, the earlier single-file prototype, unchanged.
