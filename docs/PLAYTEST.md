# Playtest log

Each milestone is played through scripted bots in headless Chromium and in Node, then judged from screenshots. Every finding records what was tried, what broke, its severity, the root cause, the fix, and the retest result.

Severity: **S1** crash, data loss or blocks play · **S2** wrong behavior a player notices · **S3** minor or cosmetic · **S4** test or tooling only.

## M0: foundation and test harness

**Tried:** booting at 1280×720, 390×844 and 844×390. Scripted throttle and steering through `__game`. Running the same input script twice, and in Node and Chromium. Real keyboard events and window blur. A DPR-3 phone. Resizing. Tab switching. WebGL context loss and restore. A browser without WebGL 2. An exception injected into the frame loop. NaN passed to the debug API. A test file that crashes on import. Checking the build for remote references and the three.js license.

| # | Finding | Sev | Root cause | Fix | Retest |
|---|---|---|---|---|---|
| 1 | Two identical scripted runs gave different state hashes in the browser. | S4 | `__game.setInput()` merged into the previous scripted input, so the brake from run one stayed held in run two. | `setInput` now replaces the scripted controls, `patchInput` merges, and `reset()` clears scripted input. | Pass |
| 2 | The same input script gave different bits in Node 22 and Chromium 141. | S2 | `Math.sin`, `Math.cos` and `Math.pow` differ between V8 12.4 and V8 14. A probe of 18 `Math` functions found 3 that disagree. | `src/engine/dmath.ts` provides deterministic fdlibm-derived functions (max error 1e-15), and a lint test bans engine-dependent `Math` calls in simulation code. | Pass: identical hashes |
| 3 | `datan2(-0, -1)` returned +π where `Math.atan2` gives −π. | S3 | The quadrant test `y >= 0` treated −0 as positive. | Signed zeros are handled explicitly. | Pass |
| 4 | `wrapPi(3π)` returned −π, outside the documented (−π, π]. | S3 | The modulo formulation put the boundary on the wrong side. | Rewritten with explicit range checks. | Pass |
| 5 | In phone portrait, the box car filled most of the screen. | S2 | A fixed 60° vertical FOV gives about 30° horizontal at a 0.46 aspect ratio. | `fovForAspect` keeps at least 72° horizontal, with vertical capped at 100° (57° horizontal at 390×844). The M1 chase camera also pulls back in portrait. | Pass |
| 6 | A DPR-3 phone rendered 978×2118 (2.1 MP), more than twice the 720p mobile budget. | S2 | The pixel ratio was capped at 2 with no notion of a pixel budget. The emulated phone also kept a desktop user agent, so mobile detection failed. | `pixelRatioForBudget` caps internal pixels (mobile 1280×720, desktop 1920×1080). Mobile test viewports now send a phone user agent. | Pass: 0.92 MP |
| 7 | WebGL context loss left the renderer drawing into a dead context. | S2 | No `webglcontextlost` handler, so the default action discarded the context permanently. | The handler calls `preventDefault()`, rendering pauses, and it resumes on restore. | Pass |
| 8 | Without WebGL 2 the page hung on "LOADING" with an uncaught exception. | S2 | The renderer was constructed unconditionally. | WebGL 2 is checked first, and the page explains what is needed. | Pass |
| 9 | An exception thrown in the frame loop would repeat every frame. | S2 | `requestAnimationFrame` was scheduled before the frame's work ran. | The frame is wrapped: on error the loop stops and a reload overlay shows the message. | Pass |
| 10 | `__game.teleport(NaN, 0)` silently corrupted the car state. | S3 | No argument validation. | Non-finite arguments throw and leave the state untouched. | Pass |
| 11 | The test expected 70–75° horizontal FOV in portrait, which the 100° vertical cap makes impossible. | S4 | The expectation was wrong, not the game. | The test checks 55–75° and documents the cap. | Pass |

**Verified, no bug found:** the build output has no remote references, and the three.js MIT license notice is kept. A test file that throws at import is reported as a failure, not silently skipped. Long frames clamp to 0.1 s of simulation. Keyboard arrows and Space do not scroll the page. Blur releases held keys.

**Weakest five things at the end of M0:**

1. There is no real vehicle yet, only a point-mass placeholder (M1).
2. There is no HUD beyond debug text (M1).
3. The quality presets and dynamic resolution are not built, only the pixel budget (M8).
4. Gamepad and touch input have unit coverage only, with no browser test (M1 touch, M7 gamepad).
5. Screenshots are judged by eye, with no automated image checks beyond "not blank".

## M1: driving feel slice

**Tried:**
- Standing starts with and without TCS; launch traces sampled every 0.1 s; 0–100, 0–200, quarter mile, top speed, 100–0 with and without ABS.
- Steady-state cornering with per-wheel load and slip dumps; a skidpad ramp.
- Lane changes at 120 km/h with and without ESC; keyboard taps at 200 km/h; handbrake turns and a held drift.
- Grass, gravel traps and sand.
- A 15% slope and a 12% cross slope, parked on the handbrake.
- Reverse and back to first.
- A 2 m drop; a rollover with spin.
- A 30 m/s crash into a tire wall; a car-to-car rear-end hit.
- A bot lapping the circuit.
- Wrong-way and short-cut lap timing.
- A 10-minute random-input soak with resets.
- Real touch events (two fingers: gas plus slide steering) in phone portrait and landscape.
- The pause menu, quick key taps, and reset to road from the infield.
- Reviewing the car from 6 angles and screenshots from all three cameras.

| # | Finding | Sev | Root cause | Fix | Retest |
|---|---|---|---|---|---|
| 1 | 0–100 took 6.1 s; the engine sat at 1180 rpm for 1.3 s at launch. | S2 | An rpm-proportional clutch found an equilibrium with the off-boost turbo engine (about 200 N·m) at 23% engagement. | Launch control: a governor holds the launch rpm, and the slipping clutch passes the engine's torque at that rpm. | 0–100 now 4.74 s |
| 2 | Under acceleration the rear tires got 22% less load transfer than m·a·h/L. | S2 | Tire forces were applied 6–10 cm above the contact patch, shortening the pitch moment arm. | Forces now act at the contact patch. | Pass |
| 3 | During the launch, throttle and clutch oscillated at about 5 Hz (slip swinging 0.03–0.13). | S2 | TCS cutting throttle lowered rpm, which opened the rpm-driven clutch, which released TCS: a limit cycle. | While launching, the clutch is capped at the driven tires' traction instead of TCS cutting throttle. | Smooth 0.6–0.66 g launch |
| 4 | The wheels spun up to 2.3 slip and the clutch locked while they were spinning. | S2 | A slipping clutch passed full capacity to the wheels, skipping driveline efficiency, while the traction cap assumed efficiency was applied. | Separate kinematic ratio G and torque ratio Gt = G·η, used consistently, including in the lock equation. | Max slip 0.12 with TCS |
| 5 | Every upshift (e.g. 1→2 at 75 km/h) caused wheelspin. | S2 | The throttle stayed open with the clutch out, so the engine flared toward the limiter and dumped that energy into the tires. | Ignition cut on upshifts; rev-matching blip on downshifts. | Pass |
| 6 | The skidpad read 0.88 g although every tire was at 70–85% of peak. | S4 | The pure-pursuit test driver drifted wide with speed and failed the "within 0.6 m" check before the tires saturated. | Driver rewritten with feedforward plus PI on radius error, heading and yaw rate. | 0.948 g, mean error 7 cm |
| 7 | `Math.tan`, `Math.asin` and `x ** 2` in tire and spring setup would make physics constants differ across engines. | S3 | Init-time math didn't use dmath. | dmath everywhere; the lint now also bans `**` in sim code. | Lint passes |
| 8 | The car body rendered inside out: a hollow nose and light-grey "skirts". | S2 | The lower-body loft's sections ran counter-clockwise, so every face pointed inward. | Sections run clockwise as seen from behind. | Fixed in 6-angle review |
| 9 | Pillar boxes poked out of the roof corners like ears. | S3 | Separate boxes placed on an approximate cabin edge. | Pillars and roof are now material groups of the cabin loft itself. | Fixed |
| 10 | The glass rendered pure black. | S3 | Metallic dark glass tints its reflections by the base color. | Dielectric glass with a clearcoat layer for Fresnel sky reflections. | Fixed |
| 11 | Tail lights, valances and plates were hidden inside the rounded tail. | S3 | Details were placed from the analytic profile, which ignores the bumper rounding. | Details are raycast onto the generated body and aligned to its normal. | Fixed |
| 12 | Pressing Esc in a test did nothing. A quick real tap on a slow frame would be lost too. | S2 | Edges came only from held state, so a press and release between two polls disappeared. | Keydowns are latched until the next poll. | Pass |
| 13 | "Back to the road" did nothing more than 36 m from the track. | S2 | The road's nearest-point grid only covers cells near the road. | `nearestAny` falls back to a full scan. | Pass |
| 14 | A parked car crept 15 cm in 5 s down a 15% slope and 11 cm in 10 s across a 12% slope. | S3 | The low-speed tire model acts like a damper, which needs velocity to make force. | Braked tires at rest anchor to the ground with a tread spring plus damper, capped at μ·Fz. | 4.5 cm and 1.7 cm (settling) |
| 15 | Skid marks never appeared. | S2 | The ring-buffer quads were wound clockwise from above, so they were back-face culled. | Winding fixed; smoke made denser. | Visible in the drift shot |
| 16 | The skidpad asphalt looked blotchy, and changing one texture's repeat changed others. | S3 | RingGeometry UVs span the whole ring, and the code set `repeat` on a shared cached texture. | World-scale repeats on cloned textures. | Fixed |

**Test-only issues:**
- The test's own `Math.sin` input differed in Node and Chromium at 160 of 4800 steps, so the determinism test now uses a triangle wave. With identical inputs the sim is bit-identical.
- Holding the brake on a slope selects reverse by design, so the slope test now holds the car with the handbrake.
- The drift controller's countersteer sign was wrong.

**Verified, no bug found:**
- Validation: 0–100 4.74 s, 0–200 14.76 s, quarter mile 12.87 s at 186 km/h, top speed 297.6 km/h, 100–0 31.0 m (36.1 m with locked wheels), skidpad 0.948 g.
- Roll gradient 1.6°/g; braking pitch 1.7°.
- ESC: at 120 km/h a hard lane change spins the car to 48° of sideslip without ESC and 3° with it.
- The handbrake kicks the rear out to 42°; a basic controller holds a drift 3.4 s out of 5.
- A 2 m drop lands and settles at ride height; a rollover keeps the body above ground.
- Grass gives about 0.5× asphalt grip, and sand holds the car to 86 km/h.
- The gearbox doesn't hunt at a steady cruise.
- The bot laps the circuit with laps timed (2:02.7 at its cautious pace).
- No tunnelling through a tire wall at 30 m/s.
- Two-finger touch drives the car, and lifting the fingers releases everything.
- The 10-minute soak: no NaNs, lowest COM 0.43 m.

**Measured:**
- A physics step costs 2.7 µs per car in Node.
- The browser sim costs 4.5 ms per simulated second.
- The proving ground renders in 66 draw calls and 61K triangles, shadow pass included.

**Self-critique: the five weakest things at the end of M1:**

1. Parked cars crept on slopes. **Fixed** (finding 14).
2. Skid marks were invisible and smoke too faint, so the main slide feedback was missing. **Fixed** (findings 15 and 16).
3. Car-to-car collisions were untested. **Fixed:** a test now checks no interpenetration, the struck car moving off, and no momentum gain.
4. There is no sound yet. Engine and tire audio carry a lot of driving feel; scheduled for M3–M4 (engines) and M8 (radio). **Open.**
5. There is no cockpit camera, which needs the interior (M3). **Open.**
