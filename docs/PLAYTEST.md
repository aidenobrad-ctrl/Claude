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

## M3: cars v1 (Blender models, Unity garage)

**Tried:**
- Building all 12 cars headless in Blender 5.1 (bpy), three LODs each, exported to FBX with a style file.
- Rendering a six-view contact sheet per car (front three-quarter, rear three-quarter, side, front, rear, top) with Cycles and judging each as an art director: silhouette, clipping, floating parts, z-fighting, missing details.
- Close-ups of grilles, lights, mirrors, arches, interiors and wheels.
- The physics roster: 0–100, 0–200, 100–0, skidpad and top speed for every car against a real reference car of its class (`docs/CARS.md`).
- Compiling the Unity garage, the car visuals and the new menu keys against the UnityEngine reference assemblies.

| # | Finding | Sev | Root cause | Fix | Retest |
|---|---|---|---|---|---|
| 1 | Most cars had no headlights. | S2 | The headlight regions were built in a loop of lambdas that captured the loop variables by reference, so every light used the last car-side values. | A `grown()` helper binds each region's values at creation. | Lights on all 12 sheets |
| 2 | The dashboard poked up through the bonnet. | S2 | The dash was placed from the cabin's bounding box, which ignores the windscreen's slope. | The dash sits below the windscreen line, inside the cabin air. | Fixed in the six-view review |
| 3 | The wheel-arch lips hung below the sills like fins. | S3 | The lip was swept around the whole arch circle. | The lip stops at `max(0.35 × tyre radius, clearance + 9 cm)`. | Fixed |
| 4 | Mirrors looked like balls on sticks. | S3 | A rounded head on a thin stalk. | A wedge head with a flat back and its own mirror glass. | Fixed |
| 5 | The pickup's tail lights wrapped over the bed sides. | S3 | Tail-light regions had no limit on which way the surface faces. | Every tail-light style has a facing limit. | Fixed |
| 6 | Patches of some bodies shaded as if lit from inside. | S2 | Decimation flipped some faces, whose custom normals (from the field gradient) then pointed against the geometry. | `fix_winding` after decimation for bodies, inserts and patches. | No dark patches on any sheet |
| 7 | Racing stripes were painted across the windscreen and rear window. | S3 | The stripe region ignored the glass regions. | Glass is cut out of stripes. | Fixed |
| 8 | The Kiri RS's rear seats stuck out through the rear window. | S2 | Seat height came from the cabin box, not the sloping roof. | Seats are clamped to 31 cm under the roofline at their position. | Fixed |
| 9 | The Vif R's wing floated above the roof. | S3 | A trunk wing on a hatch with a short tail. | Removed; the hatch keeps its roof spoiler. | Fixed |
| 10 | Blender segfaulted while rendering cars with a wing. | S1 (tool) | The wing's faces were offset twice when merged, so indices pointed past the vertex array. | `merge` offsets indices itself; `_obj` and `new_object` reject out-of-range indices with an error instead of crashing. | 12/12 build and render |
| 11 | The Kyst and Varg grilles read as flat grey plates. | S3 | Lighting in the review stage, not geometry, but the grilles had no depth cue. | Grille lattices (vertical and horizontal bars) for both. | Fixed |
| 12 | Close-up review renders were black. | S4 | The close-up tool rendered without the review stage. | It builds the stage when it is missing. | Fixed |
| 13 | The Vanta showed a curved "hook" over its windscreen in the top and front views. | S3 | It looked like the wiper but was the steering wheel. Its rim was placed from the cabin profile and rose through the low, steeply raked screen; a close-up with the wheel hidden proved it. | The rim is lowered until it is 5 cm under the body above every rim point, found by casting rays down onto the body (the screen drops towards its sides, so the centreline profile was not enough). | Gone in the cockpit close-up |
| 14 | Wipers bent along the base of the screen and could run onto a raised fender. | S3 | Each point of a blade was found by its own ray, so the blade followed every curve of the screen base. | Blades are straight seen from above: one ray finds the screen base at the blade's middle, then every point is dropped onto the body there. Mid-engined cars get one central blade. | Straight blades on the Aster GT and Vanta |
| 15 | The rear vents of the Vanta and the Saetta read as empty black boxes. | S3 | A dark hole with nothing in it. | Three horizontal slats across each vent. | Fixed on both sheets |
| 16 | The Vanta's front-left fender had a torn, jagged patch above the wheel. | S2 | Only 1.1 cm of skin was left between the fender top and the wheel-arch cut, where the channel at the base of the screen dips. Surface nets at 3 cm voxels need about two voxels of material to close a shell, so the skin broke up. The first guess (two feature cuts meeting on a crease) was wrong; measuring the skin over every arch found it. | `ARCH_SKIN`: over each arch the body's top surface is lifted, with a smooth max, until it is at least 8 cm above the arch. | No tear in the close-up. Every car has at least 6.4 cm of skin over its arches, except the Prairie's bed floor over the rear wheels (4.9 cm, still closed) |

**How these were found:** the six-view sheets (640×360 per view) hid findings 13–16 and the open items below. Close-ups at 960×540 with only LOD0 visible found them, so close-ups of the cockpit, the front corners and the sides are now part of the car review.

**Open (cosmetic, close range only):**
- The arch lips are saw-toothed in 960×540 close-ups. The lip is a sharp crease, and surface nets put one vertex in each 3 cm voxel, so the edge steps; no feature cut sharpens it. It is not visible in the sheets.
- The lower edge of the Vanta's side window steps like a staircase near the rear quarter glass.
- Number plates are blank white.

**Verified, no bug found:**
- Every car's 0–100, 0–200, 100–0, skidpad and top speed are within 10% of its reference car (`docs/CARS.md`); none passes 1.3 g on the skidpad without aero.
- The C# roster (generated from the TypeScript) is bit-exact with it: 91 checks, 339,270 numbers.
- Triangle counts per LOD are in budget, for example the Aster GT at 66.7k / 19.7k / 7.8k.

## M4: AI v1 and first events

**Tried (all headless in .NET, `dotnet/Halcyon.AI.Tests`):**
- Racing lines on the proving circuit and four island sprints.
- Every car alone for two laps with the best driver; each sprint alone.
- Pace by skill alone (six skills × five routes × three seeds).
- The head-to-head tournament from the spec: every driver against every other on every route, with the stronger one starting from pole in half the runs and from behind in the other half (300 races).
- Full six-car grids in random grid order, and mixed eight-car fields of different cars around Pro difficulty.
- Traces of single drivers and duels: speeds, pedals, gears, yaw, wheel surfaces, pass states.
- Contacts and wall hits logged with positions.

| # | Finding | Sev | Root cause | Fix | Retest |
|---|---|---|---|---|---|
| 1 | In hairpins the racing line bunched on the inside, tighter than any car can turn. | S2 | Minimum curvature pulls the line onto the inside edge, where the samples' normals converge. | The inside offset is limited so the line's radius stays above 7.5 m. | Lines stay inside the track and are smoother than the centreline on all routes |
| 2 | The AI oversteered out of every corner. | S2 | A fixed 0.55 throttle baseline. | Pedals come from the car's own measured acceleration and braking curves. | Clean laps |
| 3 | The AI braked late and ran wide. | S2 | Speed was tracked with a proportional term only. | Feedforward from the speed profile's slope, yaw damping and slide handling. | Clean laps |
| 4 | Cars crashed at 2.3 km on the proving circuit. | S2 | AI-only races left the player's parked car in the simulation, in the racing line. | AI-only races take the player's car out; `End()` puts it back. | Pass |
| 5 | Cars were launched at bridge ends. | S1 | Island routes used the full width including shoulders, so the line ran into parapet ends. | Routes use the paved width only, narrowed near rails and on bridges. | Pass |
| 6 | Guardrails crossed the road on the Highcrest pass. | S1 | On the inside of switchbacks tighter than the rail's offset, the rail folded back across the road. | No rail on the inside of bends tighter than its offset plus 0.8 m (web and Unity). | World tests pass; Highcrest is drivable but its ~6 m switchbacks still flip cars, so it is not an event yet |
| 7 | The race reported millions of contacts. | S4 | Every impact in the simulation's event buffer was counted on every step. | Only this step's events count, as contact episodes (0.5 s), with car-to-car and wall hits kept apart. | Pass |
| 8 | Finished cars drove off the end of the sprints. | S2 | Nothing told the AI to stop. | A stop point after the finish line, with 160 m of run-off. | Pass |
| 9 | The AI sometimes held full throttle above its target speed. | S2 | The feedforward compared a capped target with an uncapped one. | One target function for both points; the feedforward is clamped. | Pass |
| 10 | The first tournament failed: skill order held in 0 of 20 six-car races (59% of pairs, against 50% by chance). | S2 | (a) The stability control braked the AI in ordinary fast corners: its reference assumes about 3°/g of understeer, so a neutral car "oversteers" at 0.9 g. It cost 2–8 s a lap (Aster GT 1:40.6 with ESC, 1:35.6 without). (b) Skill moved pace by only about 0.3% a step, less than one mistake. | The AI drives with ESC off (ABS and TCS on) and does its own stability work. Skill now sets line accuracy (the line blended towards the centreline, with a speed profile built for that line), grip, braking and throttle commitment, and reaction (earlier braking, later throttle). A late-braking mistake can't exceed the car's real grip. | See 15 |
| 11 | The best driver spun under braking from 250 km/h into a long left. | S2 | The line ran 0.35 m from the edge; the right wheels dropped onto the dirt, and braking harder on the left wheels yawed the car. | The line keeps 0.7 m from the edge. | No slides there |
| 12 | Braking hard in gentle curves at high speed, the Aster GT's rear stepped out. | S2 | The car puts 36% of its brake force on the rear. At 1.1 g the rear axle carries only 29% of the weight, so it saturates as soon as there is lateral load. | Each car's stable braking at a given lateral acceleration is computed from its brake split, CG height, wheelbase and weight split. The speed profile and the brake pedal both respect it. | 12/12 cars clean; the Aster GT laps in 1:36.7 |
| 13 | The car starting second lost half a second at the start. | S2 | The new yaw check treated the merge onto the racing line as a slide and cut the throttle, and the slide check also ran at walking pace. | The yaw reference is the arc the car is steering on; slide handling only above 8 m/s. | Equal launches |
| 14 | Faster drivers followed slower ones for whole races. | S2 | Passes started 45 m back, so the car weaved off-line and lost about 4 s over two laps. A pass also cancelled itself as soon as the car moved aside, because the other car was then no longer "in its path". | A pass is a state machine on the car being passed. It starts within about half a second, or from the slipstream on a straight, takes the inside of the next corner, and lasts until that car is behind. The car slows for the tighter inside line. | See 15 |
| 15 | With 1% pace per skill step, passes rarely completed in time. | S2 | A small advantage needs long straights to turn into a pass. | Skill now spans about 15–20% of pace from 0.3 to 1.0, about 3% a step (Novice to Legend). | Tournament: the stronger driver wins 294 of 300 head-to-head races (98%), 46 of 50 adjacent-skill races from behind |
| 16 | A driver squeezed onto the dirt during a pass rolled 20 m off the road and sat there for 803 s. | S1 | The pass clamped itself against the road edge instead of giving up. The recovery then looped (stuck, reverse, stuck timer cleared, stuck again) and never reset. | A pass backs out when neither side has room. One reverse attempt, then a reset; tumbling cars reset as soon as they settle. | Max stuck 1.1 s in every suite |
| 17 | Winners reversed into walls after finishing a sprint. | S3 | The stuck detector saw a parked car. | Parked cars hold the brakes and skip recovery. | No wall hits after finishes |
| 18 | 37 of 40 contacts in a set of duels happened in the first 5 seconds. | S2 | Both front-row cars merged onto the racing line together, and the "give room" nudge was too weak and slow. | A car alongside sets a hard limit on where the steering target may go. | 15 contacts in 300 duels (0.05 a race) |
| 19 | Running races in parallel crashed in the collision code. | S1 (latent) | The collision scratch lived in static fields shared by all threads. The Unity garage measures cars on a worker thread, so this could also hit the game. | A collision worker per thread. | Still bit-exact (91 checks); 300 races on 4 threads |
| 20 | A tap of T, or d-pad up on a pad, left a race at once. | S2 | The events key ended the race on its first frame, and d-pad up is easy to hit while driving. | Leaving needs the key held for a second, with a countdown on the HUD and a hint at the start. | Compile-checked; not yet run in Unity |

**Verified, no bug found:**
- All 12 cars do two clean laps with the best driver (no resets, at most 0.4 s below 0.8 m/s).
- All four sprints finish without getting stuck.
- Pace rises with skill on every route and seed, by about 3% a step.
- Full six-car grids: 96% of pairs finish in skill order, 2.5 light contacts and 0.1 wall hits a race, finish spread 19.9%.
- Mixed eight-car fields finish with no resets: 6 contacts on the circuit, 8 on the Port Calder sprint.

**Measured:**

Two laps of the proving circuit, best driver, best lap:

| Car | Lap |
|---|---|
| Arcline Vanta | 1:20.38 |
| Orsini Saetta | 1:25.17 |
| Nordvik Varg | 1:36.53 |
| Halden Aster GT | 1:36.66 |
| Nordvik Kyst Sport | 1:37.18 |
| Aurelle Vif R | 1:37.59 |
| Hoshida Kiri RS | 1:44.77 |
| Stallworth Ironside 454 | 1:45.93 |
| Stallworth Prairie | 1:51.41 |
| Halden 280 Sprint | 1:51.45 |
| Aurelle Pico | 1:52.30 |
| Ironbark Ridgeback | 1:56.95 |

Sprints, Aster GT, best driver:

| Sprint | Distance | Time | Average |
|---|---|---|---|
| Festival to Port Calder | 5900 m | 2:12.04 | 161 km/h |
| Harbour Dash | 7559 m | 3:03.72 | 148 km/h |
| Ashby Sprint | 3898 m | 1:55.56 | 121 km/h |
| Saltmarsh Bay Run | 3949 m | 1:54.40 | 124 km/h |

Pace by skill, Aster GT alone, median of 3 seeds:

| Skill | Proving (2 laps) | Port Calder | Harbour | Ashby | Saltmarsh |
|---|---|---|---|---|---|
| 0.30 | 3:49.3 | 2:45.5 | 3:42.9 | 2:18.1 | 2:20.5 |
| 0.58 | 3:36.1 | 2:32.1 | 3:26.3 | 2:07.3 | 2:08.5 |
| 0.86 | 3:24.8 | 2:18.4 | 3:09.9 | 1:58.1 | 1:57.6 |
| 1.00 | 3:20.3 | 2:12.0 | 3:03.9 | 1:55.7 | 1:54.5 |

Costs:
- A racing line takes 19–37 ms to build.
- An eight-car race simulates 244 s of racing in 6.7 s of CPU, about 36× real time.
- The 300-race tournament takes about 2 minutes on 4 cores.

**Self-critique: the five weakest things at the end of M4:**

1. The AI's pace didn't follow its skill, so the tournament failed. **Fixed** (findings 10–15).
2. Starts produced most of the contact. **Fixed** (finding 18).
3. A car could stay stuck forever. **Fixed** (findings 16 and 17).
4. Garage, race menu, race HUD and AI cars in Unity are compile-checked only. Nothing has run in a real Unity Editor yet. **Open:** needs the PC session.
5. Braking stability is the physics' job too. A player with ESC off meets finding 12, and only the AI works around it. A brake-force distribution (EBD) model belongs in the shared physics, with the TypeScript and the references updated together. **Open.**

Also open: the Highcrest switchbacks (about 6 m radius) are left for world v2 (M6). Only two event types exist so far (circuit and sprint); the rest come in M7.
