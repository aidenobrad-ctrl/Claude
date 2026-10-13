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

## M5: Tuning and upgrades

**Tried (headless in .NET: `dotnet/Halcyon.Career.Tests` and `dotnet/Halcyon.AI.Tests`):**
- The PI of every stock car, measured twice; each kind of upgrade on the Aster GT; more brake torque on the Ironside 454.
- Every part ladder priced on the Aster GT; payouts for every position, class and difficulty.
- A career: start, buy, fit parts, save, load, sell; a save that names a car the game doesn't have.
- Each of the 24 sliders and the gear ratios, moved alone, against the default car's path.
- Share codes for all 12 cars; damaged codes and non-codes.
- Auto-tunes for circuit and dirt on a Kyst Sport with race suspension, a sport transmission, a sport differential and race brakes; the understeer gradient of every car.
- The rivals' upgrade ladder: the PI of all 12 cars at all 9 steps.
- Every slider at its minimum and maximum on every car, and 72 random combinations of extremes with every part (672 tunes).
- Power from +0% to +25% in 1% steps on four tyre compounds, for every car (1,200 acceleration runs), and launches with the wheel turned.
- PI-matched eight-car races, as the game builds them, on all five events: 28 races over 4 seeds, plus every car alone, stock and with every part, on every event (120 runs).
- Every car with random tunes and parts, two laps with the best driver (24 runs).
- The full M4 suite again: lines, solo laps, sprints, the 300-race tournament, grids and mixed fields.

| # | Finding | Sev | Root cause | Fix | Retest |
|---|---|---|---|---|---|
| 1 | The caster slider did nothing. | S2 | The tune had a caster value, but no formula read it. The test that moves each slider alone found it. | Caster now leans the steered wheels as they turn (dynamic camber), in the TypeScript and the C# together. At the stock 5.5° nothing changes. | 24/24 sliders and the gear ratios change the car's path; still bit-exact |
| 2 | Loading a save that named an unknown car crashed. | S1 | `Cars.ById` throws for an unknown id, so renaming or removing a car would have made every save with it unloadable. | Unknown cars are skipped on load. | Pass |
| 3 | The first auto-tune made cars worse. | S2 | It followed rules of thumb: circuit tunes low and stiff, dirt tunes high and soft. On gravel the dirt tune gripped less than the circuit tune (0.467 g against 0.494 g). On asphalt the circuit tune gripped no better than stock (0.904 g against 0.906 g). | The auto-tune measures. A coordinate search over the unlocked sliders keeps a change only if it improves a score measured on the event's surface. | Kyst Sport: circuit score 0.6271 → 0.6640; dirt score on gravel 0.1367 → 0.1556 |
| 4 | The measured auto-tune made cars tail-happy. | S2 | Steady test-track runs reward rotation. The search chose rear toe-out, and the Kyst's understeer fell from +0.60 to +0.21 deg/g; rear toe-out of −0.35° alone takes it to +0.28. | A new understeer-gradient test. A change is kept only if the car still understeers by at least max(0.25, min(stock − 0.15, 0.6)) deg/g, and rear toe-out is never used. | +0.64 deg/g after the circuit tune |
| 5 | An engine upgrade lowered the Ironside 454's PI (518 → 512 on sport tyres). | S2 | Launch control capped the clutch at the tyres' grip without their load sensitivity. As the car squatted, the real grip fell below the cap, the rear wheels spun up (slip ratio 3), and the clutch locked early with no cap at all. Traction control, idle during the launch, then cut so hard that the engine bogged. With 3–25% more power, 0–100 got up to 0.36 s slower. | The cap includes the tyres' load sensitivity and standing water (TypeScript and C#). | No runaway wheelspin; the Ironside's PI rises at every ladder step |
| 6 | With that fix, launches left grip unused: the Aster GT's 0–100 fell to 4.83 s, 9.8% over its reference. | S2 | An open-loop cap cannot see the torque that wheel and driveline inertia take. During the launch the tyres delivered only 92–95% of their grip, against 100% once traction control took over. | Slip-steered launch control: a trim on the cap holds the driven wheels' slip at 80% of the tyres' peak. | 99–100% of peak grip in the launch; every stock 0–100 is as fast or faster than before (Aster GT 4.73 s, Ironside 6.14 s, Saetta 3.06 s); all 12 cars within 10% of their references (`docs/CARS.md`) |
| 7 | The first slip trim spun the tyres in 26 of 1,200 power and tyre combinations (Saetta on slicks, Ironside, Prairie). | S2 | The trim kept rising while the engine, not the tyres, was the limit, then a load swing put the cap 6% over the grip. On slicks, which slide at 74% of their peak, its 0.8 floor could not bring a spinning wheel back. | The trim only moves while the tyres are the limit. It rises at 1/s and falls at 8/s, between 0.5 and 1.06. | 0 of 1,200 runs above slip 0.5; the C# port is still bit-exact (91 checks, 339,299 numbers) |
| 8 | Pulling away with the wheel turned, the car crawled: 28 km/h after 3 s at half lock, against 42 km/h before M5. The browser test of the phone's touch pedals and slide steering caught it. | S2 | Launch control steered on the highest wheel slip. In a turn a limited-slip differential spins the light inner wheel faster than its patch of road; that read as wheelspin, and the trim cut the clutch to 20%. A slower car then got more steering angle from the steering assist and turned tighter still. | An axle's slip is that of its less-slipping wheel, since the loaded outer wheel does the work; the cap is still set by the weaker wheel. Straight launches are unchanged, bit for bit. | 43 km/h at half lock; the touch test passes; still 0 of 1,200 straight runs spin |
| 9 | In rival fields, trucks and off-roaders with every part rolled over: 14 of 37 resets in 28 races. | S2 | Slicks gave the Prairie 1.02 g and the Ridgeback 0.99 g of grip, against static rollover limits (half the track over the CG height) of 1.10 g and 1.03 g. The AI planned with tyre grip alone. | The AI corners within 85% of the rollover limit, and the PI counts grip only up to it. | One rollover in 28 races (a fully upgraded Ridgeback) |
| 10 | Passes at 220+ km/h ran cars off the road. | S2 | A pass put the steering target 0.25 m from the edge, and a lane change took about 0.6 s at any speed. At 220 km/h, pure pursuit then asked for 3–4 g to reach a target 4 m aside, and the car ran 3 m past the edge. | Lane changes are capped at 2.5 m/s sideways. The target keeps 0.25 m from the edges, plus 0.025 m per m/s over 20 m/s. | See 14 |
| 11 | Off the road at speed, the AI held a quarter throttle and steered for the line 42 m ahead: full lock on grass, then a spin. | S2 | Off the road, the only rule was a throttle cap. | Off the road above 80 km/h the AI brakes (less in a slide) and aims 2.5 times its lateral error ahead, so it rejoins at about 22°. | Placed 30 m off the road at 144 km/h, it is back on the road within 6 s |
| 12 | A driver behind a car spinning backwards braked at full lock from 220 km/h. | S2 | Following and the "never drive into it" rule copied the car ahead's speed, which went down to −58 m/s. | A car ahead going backwards counts as stopped. | See 14 |
| 13 | Selling a car listed before your current one would have put you in a different car. | S2 | `SellCar` removed the car but kept the current index, which then pointed at the next car. Found by reading the new garage code against the profile. | The index moves with the car. | A new test, which fails on the old code |
| 14 | Resets in PI-matched races, all fixes together. | — | — | — | Resets fell from 37 to 12 in 28 races (1.32 → 0.43 a race). Without the PI 984 case, from 24 to 1 in 24 races (1.00 → 0.04). In the tournament the stronger driver now wins 299 of 300 races (was 294), with 13 light contacts (was 15), no resets and no wall hits. Grids are 98% in skill order (was 96%), with 1.4 contacts a race (was 2.5). |

**Verified, no bug found:**
- Brake upgrades help only cars whose stops are limited by brake torque. With 25% more torque the Ironside 454 (rear drums) stops in 39.2 m instead of 44.7 m, and its PI rises. The Aster GT's stop is limited by its tyres, so its PI doesn't change. That is right.
- Every kind of upgrade raises the Aster GT's PI: +20% power 721, −10% mass 718, semi-slicks 746, the race aero kit 722 (from 703).
- Measuring a car twice gives identical results. The PI never falls along the rivals' upgrade ladder, for any car.
- Prices rise with PI. Payouts fall down the order and rise with class and difficulty. Each part ladder costs more at every level.
- A save reads back identically (635 bytes), with parts, tune and stats. Removing a part returns the sliders it unlocked to their defaults.
- Share codes survive the round trip for all 12 cars. Damaged codes and non-codes are refused with a reason.
- Without parts, the auto-tune only touches tyre pressures.
- Every one of the 672 extreme tunes stays finite and physical. 12 of the 72 random combinations of extremes spin on the 60 m circle (the test track then says "spins"); no single slider at its limit does.
- All 120 solo runs (every car, stock and with every part, on every event) finish without a reset, and all 24 randomly tuned cars drive two laps without one.

**Measured:**

| Car | PI | Price (credits) | Understeer (deg/g) | PI with every part |
|---|---|---|---|---|
| Ironbark Ridgeback | D 353 | 13,500 | +0.85 | 560 |
| Halden 280 Sprint | D 428 | 21,500 | +0.53 | 714 |
| Aurelle Pico | D 459 | 25,500 | +0.62 | 646 |
| Stallworth Prairie | D 472 | 28,000 | +0.66 | 654 |
| Stallworth Ironside 454 | D 480 | 29,000 | +0.87 | 721 |
| Hoshida Kiri RS | B 607 | 62,500 | +0.98 | 807 |
| Aurelle Vif R | B 677 | 95,000 | +0.73 | 808 |
| Halden Aster GT | A 703 | 111,000 | +0.55 | 849 |
| Nordvik Kyst Sport | A 732 | 132,000 | +0.61 | 871 |
| Nordvik Varg | A 739 | 138,000 | +0.50 | 851 |
| Orsini Saetta | S1 864 | 308,000 | +0.35 | 927 |
| Arcline Vanta | S2 928 | 2,255,500 | +0.29 | 984 |

- A Proving Ground GP in class A against Pro rivals pays 7,300 credits for a win, 5,100 for second and 1,100 for eighth. A win is worth 1,416 XP.
- One PI measurement takes 70–120 ms once warmed up. The 108 ladder measurements take 2.6 s on 4 cores. An auto-tune with every slider unlocked takes about 3.6 s.
- PI-matched GP at A 703 (2 laps, Pro): the field finished between 3:20.9 and 3:35.2 (7%). Equal PI is not equal pace: an upgraded Ironside 454 at PI 710 with a 0.84-skill driver won in 3:20.9, while a stock Aster GT at 703 with a 0.86-skill driver took 3:29.4.

**In Unity (compile-checked against the UnityEngine reference assemblies; not yet run in a real Editor):**
- The garage (G or LB) has six pages: your cars, the dealer, upgrades with measured PI before and after, tuning, the test track, and career data. Tuning has every unlocked slider with its formula, power, torque and gear graphs, balance bars, auto-tune for road, circuit, dirt and drag, share codes (C and V), and reset (R).
- The career is saved as JSON in `Application.persistentDataPath`. It saves a moment after every change and when the game quits, keeps the previous save as `.bak`, sets a damaged save aside and restores the backup, and exports to or imports from the clipboard.
- Events show their class and whether your car may enter. Rival grids are matched to your PI on a worker thread while the grid lines up, and the results add credits and XP to the career.

**Self-critique: the five weakest things at the end of M5:**

1. None of the M5 screens has run in Unity. **Open:** the PC session.
2. Eight maxed-out hypercars (PI 984) on the Port Calder run still reset about 2.75 times a race, going off the road at 300+ km/h in traffic. The AI tests report these resets instead of failing on them. **Open.**
3. Equal PI is not equal pace: at A 703 an upgraded Ironside 454 laps about 4% faster than a stock Aster GT. The PI weighs straight-line and steady-state figures and ignores how a car puts them together on a lap. **Open:** add a simulated lap (the AI's own racing line and speed profile) to the PI.
4. Parts are bought each time they are fitted; a removed part is not kept. **Open:** an inventory of owned parts.
5. Rivals run default tunes. **Open:** auto-tuned rivals (3.6 s each, so precomputed or cached).

Also open: number plates, and the close-range model items from M3.

## M6: World v2 (the living island)

**Tried (headless in .NET: `dotnet/Halcyon.Sim.Tests`, plus the web build in headless Chromium):**
- The clock and the sun: sunrise, noon and sunset at the festival's latitude, the moon, daylight through dawn, the time labels.
- The weather over 8 seeds of 24 real hours each (60 game days): how often each sky comes, how long it lasts, fog at dawn, rain only from a heavy sky, lightning only in storms, roads soaking and drying.
- The AI on five events, dry against soaked roads, two seeds each, and soaked roads with the AI planning for dry ones (the control): 30 races.
- The two new events with their own conditions: the Proving Ground by night and a storm run to Port Calder.
- Traffic: ten minutes parked at the festival; a bot driving the 7.8 km Harbour Dash through it; the player stopped across a lane for eight minutes.
- The rebuilt Highcrest pass: the route's tightest bend, and two fields of six (from the Pico to the Vanta and the pickup) racing up it on two seeds.
- Landmarks and discoveries: placement, colliders, the Harbour Dash finding places and paying XP, the fog of war, saves (and a save from before M6), fast travel to every place found.
- The GPS: a route from the festival to every place and landmark; a bot following one for 15.8 km to Highcrest; a car moved 400 m off the route; a car facing back along it.
- The web build's GPS line, in a screenshot, while porting it to Unity.

| # | Finding | Sev | Root cause | Fix | Retest |
|---|---|---|---|---|---|
| 1 | Rain kept falling from a sky that was clearing. | S3 | Rain followed the weather state's target, and the cloud eased out on its own. | Rain is capped by the cloud cover. | 0.0 s of rain under cloud cover below 0.6 in 60 game days |
| 2 | The test of how fast rain soaks the road measured nothing. | S4 | It timed from the change of state, before any drops had fallen. | The timing starts at the first drops; the soak and dry rates were tuned with the test working. | Soaked (90%) 154 s after the rain sets in; dry 3.1–11.8 min after it stops |
| 3 | The clock read 00:29 at half past midnight. | S3 | 0.5 h × 60 came out as 29.999… minutes and was rounded down. | A one-millionth tolerance before rounding down. | 00:30 |
| 4 | The night race ended at dawn. | S2 | One game hour passes per real minute, so a five-minute race moved the clock five hours. | The clock runs at a fifth of its speed during races. | The Proving Ground by Night ends at 00:06, still dark |
| 5 | The Unity race menu would have sent the AI into a wet race planning for a dry road. | S2 | It measured the cars' abilities without the road's wetness. Found by reading the code against the headless race. | Abilities are measured at the race's wetness (rounded to a tenth, so they cache). | The control shows what it saves: planning for dry roads in the wet gave 33 resets and 104 wall hits in 10 races, against 0 and 1 |
| 6 | Traffic weaved in its lane. | S2 | The player's Standard steering assist was on for traffic and fought its lane-following controller. | Traffic drives with the assist off. | Median lane error 1.03 m → 0.25 m |
| 7 | Stopped traffic crept backwards into the car behind. | S2 | As for the player, the brake pedal held at a standstill selects reverse. | Stopped traffic holds the handbrake. | Part of the 0 impacts between traffic cars in ten minutes |
| 8 | Traffic drove into cars crossing at junctions. | S2 | Nothing gave way. | Cars about to enter a junction yield to cars already in it or close to it on another road. | Part of the 0 impacts in 10 minutes |
| 9 | Cars going round a stopped car sometimes swerved into it. | S2 | The side to pass on came from the lane, not from where the stopped car stood. | They pass on the side away from it. | See 13 |
| 10 | Rear-end hits on bends. | S2 | The gap to the car ahead was measured in a straight line, across the inside of the bend, so a car round the bend did not count as ahead. | The gap is measured along the car's own lane, every 4 m for 80 m, within 2.6 m of it. | See 12 |
| 11 | Swerves ended before the car was past. | S2 | The swerve lasted a fixed time; at low speed that was not long enough. | A swerve lasts until the car has covered the distance past the obstacle. | See 13 |
| 12 | Overtaking: lane changes caused 5 impacts between traffic cars in ten minutes. | S2 | A car pulled out in front of a faster car coming up the other lane. | The gap needed behind grows by 3 m for each m/s the other car is faster, and the gap ahead likewise. | 0 impacts in ten minutes, with 533 lane changes |
| 13 | Traffic scraped a player stopped across a lane. | S2 | A car decided to swerve when it was already 0.5 m away, or when it had to move 7 m sideways in 8.8 m. | A car swerves only when the sideways move fits in 70% of the gap less 3 m. Otherwise it backs up for 2.5 s when the road behind is clear, then tries again. | 24 cars went round the player in 8 minutes, 0 impacts |
| 14 | The Highcrest pass was a saw-tooth of hairpins (the M4 decision kept it out of the events). | S2 | The road baker's A* searched the terrain grid with no idea of heading, so it zigzagged up the slope. | Heading-aware A* on a 48 m lattice that turns at most 90° per step, with 20 m fillets, routed as one continuous road. | Tightest bend on the race route 19.2 m; the Highcrest Hill Climb is now an event |
| 15 | Kinks where the new pass met the network: 7.9 m at the junction, 11 m at the village end. | S3 | The pass was routed in pieces, and the village node sat off the pass's line. | The pass is routed as one road, and the village node was moved onto it. | No bend on the three pass roads is tighter than 20 m, end to end |
| 16 | AI cars flipped jumping crests on the pass: 6 to 14 resets a race. | S2 | The speed profile ignored vertical curvature. Over a crest the tyres' load falls, and the road profile had sharp crests. | The speed plan reads the crest curvature from the route's heights over ±12 m (cornering speed from v² ≤ μg/(k + μc), and at most 0.95·√(g/c) over a crest). The pass's road profile is smoothed (a 30 m box filter, twice). | 0 or 1 reset a race in four races of six |
| 17 | A road bake silently left the old roads in place. | S4 | A duplicate declaration in the baker script. | Removed. | The bake writes the new pass |
| 18 | The mast and the wind farm could not be found from any road. | S2 | Placement asked for the nearest road within 60 m (the reach of the ground's road query), so "no road found" read as "far enough from the roads". | A road distance over the whole network, and a landmark must be found from its nearest road. | Each landmark is 137–597 m from a road and found from it |
| 19 | The lighthouse stood on the lake shore. | S3 | "The coast" meant any water. | The sea is told apart from lakes by its height. | By the sea, 40 m up |
| 20 | Fast travel to the Proving Ground left the car on the access road outside. | S3 | Fast travel goes to the road nearest a place, and the circuit is not part of the road network. | The Proving Ground's paddock. | In the paddock, upright and still |
| 21 | In the web build, the GPS chevrons pointed back at the car. | S3 | The shader's chevron offset had the wrong sign. Found while porting the line to Unity. | Flipped. | The screenshot shows them pointing the way |
| 22 | In the web build, the GPS never said "arrived" at a waypoint more than 60 m from a road. | S3 | Arrival needed the car within 60 m of the waypoint, but the route ends at the road nearest it. Found reading the code while porting. | Arrival is at the route's end, while the car is on the route. The Unity GPS does the same. | A new browser test (`tests/browser/m2-gps.mjs`), which fails on the old build, passes |
| 23 | In Unity, the map's player arrow would rotate about the wrong point on screens not 1080 pixels high. | S3 | `GUIUtility.RotateAroundPivot` takes its pivot in screen pixels, and the menus draw on a scaled canvas. Found by reading the code. | `Ui.Rotate` rotates in canvas units. | Compile-checked only |

**Verified, no bug found:**
- The sun rises at 5:17 and sets at 18:43 (13.4 h of day), is 70° up and due south at noon, and is 40° below the horizon at midnight under a high full moon.
- Over 60 game days: clear 43% of the time, cloudy 29%, rain 13%, storm 3.5%, fog 11.5%. 30% of fog sets in at dawn and 5% by day. Lightning strikes every 9.8 s in storms and never otherwise.
- The same seed gives the same weather and the same traffic, bit for bit.
- A held weather (a race) keeps the road as wet as it was while the sky clears; released, the road dries.
- In the wet the winners are 8–13% slower than in the dry on all five events, with no resets and one wall hit in ten races.
- Discoveries survive a save and load bit for bit, and a save from before M6 loads with an undiscovered island.
- The GPS finds a road route from the festival to all 15 places and landmarks. A bot following one 15.8 km to Highcrest heard all three turns counted down, was never re-routed or told to turn around, and arrived. Moved 400 m off the route, the car had a new route from where it stood after 1.5 s.

**Measured:**
- Traffic around a parked player for ten minutes: 219 cars spawned and 187 removed, 98 became physics cars and 94 went back to ghosts. At least 24 cars within 1.1 km, up to 9 physics cars at once. Lane error: median 0.26 m, 95th percentile 1.05 m. It costs 26 ms of CPU per simulated second.
- A bot drove the Harbour Dash through traffic, 7.8 km in 4:01, past 53 physics cars: 3 contacts, 0 resets.
- Highcrest Hill Climb (7.3 km): 4:59 in a Vanta, 5:28 in a Kyst Sport, 6:19 in the Ridgeback.
- The Harbour Dash finds 5 places, 11.9% of the roads and 4.2% of the map, for 3,000 XP and 7,000 credits.

**In Unity (compile-checked against the UnityEngine reference assemblies; not yet run in a real Editor):**
- The sky director: the sun and moon on the clock, sky colours, clouds, ambient light, fog, a reflection probe, rain and splashes, lightning with thunder, wet road materials and glowing windows and lamps at night. Headlights come on by themselves at dusk (L to force them on or off).
- `[` and `]` move the clock an hour, and `\` steps the weather through each sky and back to dynamic.
- Traffic cars as pooled rigs; landmarks with a turning lighthouse beam, wind turbines and blinking mast lights.
- The map (M): the island painted from its data, the fog of war, the roads you have driven in blue, places and landmarks, completion, a GPS route to any destination (Enter, or a click on the map; X clears it), and fast travel to places found (F twice). The minimap in the corner shows the route too.
- The GPS on the road: violet chevrons in the right-hand lane flowing towards the destination, and a panel at the top of the screen with the next turn or fork, its distance, and the distance left.

**Self-critique: the five weakest things after the first pass of M6, and what was done:**

1. Traffic never overtook: on the dual carriageways a queue formed behind every slow car. **Fixed:** lane changes with gaps that grow with the closing speed, and swerves only when they fit (findings 12 and 13; 533 lane changes and 0 impacts in ten minutes).
2. The Unity map had no GPS, though the web version has had one since M2. **Fixed:** the GPS in Core, tested headless (routes to all 15 destinations, a 15.8 km drive with every turn called, re-routing, turn around, arrival), with the route on the map, the minimap and the road, and the next turn on the HUD.
3. "Roads driven" was a number on the map with nothing to show where. **Fixed:** the roads driven are drawn in blue on the map.
4. None of M6's look has been seen: the sky, night, rain, wet roads, traffic rigs, landmarks, the map and the GPS line are compile-checked only. **Open:** the PC session.
5. A traffic car stuck for 5 s is put back on its lane once the player is 60 m away, even if the player is looking at it. **Open:** let Unity tell the core what the camera can see.

Also open: the weather is the same over the whole island (no passing showers); the minimap magnifies a 10 m-a-pixel texture to 900 m across, so it is soft; the GPS takes the shortest road, not the quickest; traffic has no indicators, and nothing reacts to the horn.

## M7: Everything else (events, stunts, championships, progression, photo mode, settings)

**Tried (headless in .NET: `dotnet/Halcyon.Sim.Tests`, `Halcyon.AI.Tests`; the web build in headless Chromium):**
- The stunts: where the finder puts them, a fast driver through every speed trap and zone both ways, each danger sign at 80, 90 and 100% of its run-up speed and at 60 km/h, the AI gripping through each drift zone and drift bots in three cars, saves.
- The new events: a drag race between three pairs of cars, the Redmesa Rally with six cars planning for dirt (and the control planning for asphalt), the head-to-head with Mika Varga, and a time trial against a recorded ghost.
- Progression: the Grassroots Trophy raced through by six cars, standings and saves, accolades, the levels that open the festival, settings and units.
- The road network, rebuilt twice: tight bends, junctions, steps between roads that share pavement, and the M2 and M6 suites on the new roads (the GPS, traffic, wet races, the hill climb).
- The web build: screenshots of the new switchbacks, the canyon road and a jump.

| # | Finding | Sev | Root cause | Fix | Retest |
|---|---|---|---|---|---|
| 1 | A stunt next to another one was never detected. | S2 | One shared "where the car was" for every stunt. | Each stunt keeps its own. | Every trap and zone scored once per pass, both ways (14 of 14) |
| 2 | Zones starting at the very beginning of a road could never be entered. | S3 | Entering means crossing the start line from outside. | Zones keep 80 m clear of a road's ends. | As 1 |
| 3 | Speed traps at 80% of a straight were unfair driven the other way. | S3 | The target came from the run-up one way only. | Traps sit in the middle of their straight; targets come from the slower way in. | A fast driver earns a star at each, both ways |
| 4 | A hop off a bump at 60 km/h counted as a jump. | S3 | Any moment with four wheels off counted. | A jump must fly for a quarter of a second and 2 m. | 60 km/h over each sign scores nothing |
| 5 | Jump targets were 1.5× short of the jumps people could make. | S2 | They came from a point mass leaving the crest. | Targets are measured: a stock Aster GT driven over each crest at 80, 90 and 100% of the run-up speed. | Jumps in the game land within 2% of their targets (9 of 9) |
| 6 | A car tumbling into the canyon went NaN. | S1 | At touchdown the damper saw no compression speed, and the strut could push with any force. Both the TypeScript and the C#. | Touchdown takes the hardpoint's closing speed; struts are capped at 4 g and spin at 30 rad/s. Both languages, references regenerated. | 95 physics checks bit-exact; no NaN in any stunt run |
| 7 | An AWD car with a wheel in the air stopped dead on a hill. | S2 | Traction control took the lowest torque over the driven wheels, and a lifted wheel's was zero. | Wheels carrying under 20% of their load are left out. | Both languages, bit-exact |
| 8 | A wall of ground under roads that pass over each other. | S2 | The ground took the nearest road, even one high above the wheel. | Roads more than 1.6 m above the wheel are skipped. | World equivalence passes; no steps onto other roads |
| 9 | 7 m zigzags on desert, forest and coast roads. | S2 | Corner cutting kept the 12 m grid's saw-tooth. | Every road is simplified past it and its corners rounded with arcs. | See 10 |
| 10 | Main roads climbed in serpentines of 6–10 m bends, the coast highway had 7 m bends, and in one bend the road overlapped itself with a 1 m step in it. | S1 | The road baker's A* had no idea of heading: on a steep slope it zigzagged from grid cell to grid cell. | Highways, main roads and dirt tracks are routed on a lattice with the heading in the state (as the pass was in M6): steps of 24–36 m, limited turns, a cost per turn, and the heading carried on through each town. Climbs become real switchbacks. | Stretches of main road and highway bending tighter than 25 m: 119 in M6 → 15, all mountain switchbacks of 20 m or more; highway in bends under 100 m: 1.4 km → 76 m |
| 11 | A dirt track ran along the coast highway into a junction 80 m on, at another height (a 0.86 m lip). | S2 | It was routed to "the nearest road", which was the highway it started on. | Removed: Saltmarsh Bay is on the highway. | No lips at road edges |
| 12 | Two roads met without crossing (one ran into the other's side and away), sharing pavement at heights 2.5 m apart. | S2 | The baker only made junctions where roads cross. | Where two roads come within their half widths plus 2 m of each other, they become a junction. Two roads left side by side by this are made one. | See 13 |
| 13 | Roads leaving a junction at different grades left steps of up to 2.8 m where their pavements overlap. | S1 | Each road's profile only met the others at the junction itself. | Every road stays within 1% of level for 25 m from a junction, and on for as far as its pavement overlaps another road's there. A new test checks every overlap. | Worst step 0.35 m, at a shallow fork |
| 14 | A rally car was put down on the grid on top of the main road beside the dirt track, 1.7 m up, and rolled. | S2 | Grid and reset positions took the top surface. | Cars are put down on the race route's own road. | The rally: one reset in six cars |
| 15 | The GPS route to the Cape Lighthouse ended 16 m short of the nearest road. | S3 | Far from any road, the route end was looked for at every fourth sample, and never at a road's last few. | Every sample. | Every route ends at the road nearest its destination |
| 16 | A danger sign scored two jumps in one pass, the first 48 m before the crest. | S2 | Any takeoff once the car was near counted, and the counting started again after landing. | As the targets were measured: takeoffs from the crest's last 40 m only. Crests with a flight before the run-up's last 40 m are not signs. | One jump a pass, within 2% of target |
| 17 | The island had one danger sign, then none. | S2 | Few real crests survive the smoothed profiles, and none clean. | Yumps: on the longest flat, straight stretch of four roads, a smooth crest (1.3 m over 52 m on main roads, 2.2 m over 64 m on dirt) within the class's grade limit. | 3 danger signs: 19–34 m jumps at 132–165 km/h |
| 18 | A jump at the full run-up speed earned two stars, not three (33.4 m against 33.5). | S3 | Targets were the measured jumps rounded down to half metres, no margin. | 3% short of the measured jump. | 1, 2 and 3 stars at 80, 90 and 100% |
| 19 | Speed zone targets assumed speeds that change instantly (215 km/h for a zone the AI averaged 139 in). | S2 | Each sample's bend speed, with no accelerating or braking between them. | A speed profile: 1.05 g in bends, 4.5 m/s² accelerating (less near 250 km/h), 8 m/s² braking. | Zone targets 147–163 km/h; a fast driver earns at least a star at each |
| 20 | The racing AI ran into much slower cars (34 m/s into one at 12.6). | S2 | It only slowed within two and a half following distances. | No faster than lets it brake, at three quarters of its brakes, to the slower car's speed by the following distance. | The traffic bot: 7 contacts → 4 |
| 21 | Wedged against a traffic car, the bot rocked back and forth for four minutes. | S2 | Its stuck test wanted it standing still; a traffic car beside the player is never put back. | A racer with no headway for 8 s is reset; a traffic car in trouble for 20 s within sight is taken away. | The bot reaches the harbour in 4:17, 0 resets |
| 22 | At the start, cars held behind slower launchers reversed into the car behind. | S2 | Waiting behind a car counted as stuck (the reversing manoeuvre), and the brake held at a standstill selects reverse. | A car waiting behind one that is barely moving is not stuck, and is held on the handbrake. | Dry and wet races: 0 resets in 20 |
| 23 | The drift bots earned no stars once the drift zones moved to the new roads. | S3 | The test gave each bot one try in one style. | Three tries with more or less angle, the best kept (a player retries). The physics' snappy breakaway is open (M8). | 3 of 12 runs earn a star |

**Verified, no bug found:**
- Drag: three pairs side by side in their own lanes, 8.6–17.3 s at 135–290 km/h, no contact.
- The head-to-head: Mika Varga (skill 0.95) beats a 0.8 driver in the same car, cleanly.
- The ghost: replayed beside the run it recorded, it stays within 0.00 m and 0.008 s all lap; a car three seconds behind reads +3 s.
- Championships: three rounds of the Grassroots Trophy scored 88 points a round shared out, the standings ranked, the run under way and its rivals' difficulty saved.
- Accolades pay once and survive a save; a save from before M7 loads without them.
- Settings survive a save, out-of-range values are clamped, a broken file gives the defaults, and speeds and distances read in km/h, km and m or mph, miles and feet.
- On the new roads: the GPS bot (17.1 km to Highcrest, every turn called), traffic (0 impacts between traffic cars in ten minutes, lane error 0.24 m median and 0.84 m 95th percentile), the Highcrest hill climb (0 resets in four races of six), and the wet races (0 resets in 20).

**Measured:**
- Stunts: 4 speed traps, 3 speed zones, 4 drift zones and 3 danger signs.
- Redmesa Rally (4.0 km of dirt): won in 3:22 with one reset in six cars when the AI plans for dirt; planning for asphalt (the control), 21 resets and 39 wall hits.
- The Harbour Dash through traffic: 7.9 km in 4:17, 4 contacts with traffic for the racing bot.

**In Unity (compile-checked against the UnityEngine reference assemblies; not yet run in a real Editor):**
- Stunt boards by the road (an icon, the name, gates at zones, lit at night), on the map and the minimap, a live panel with the targets, and a result with stars and rewards.
- The festival menu (T): every event with its kind, conditions, class and level, then the three championships with their rounds and standings. Head-to-head, drag (trap speed), rally and time trial (a see-through ghost car and the gap to it) races; results with a championship's standings and the accolades earned.
- The pause menu (Esc): settings (graphics quality, units, assists, the racing line, HUD size, colour-blind palettes, camera shake, motion blur, field of view, fewer flashes, the minimap, key hints, volume), the accolades with progress bars and career numbers, the controls, and quitting.
- Photo mode (P): the world stops, the camera orbits the car (mouse, right stick or keys), with field of view, depth of field on the car, aperture, exposure, seven filters, vignette and the hour; Enter saves a PNG at twice the screen size.
- The racing line in races, coloured for the player's own car: where to keep accelerating, lift and brake.

**Self-critique: the five weakest things after the first pass of M7, and what was done:**

1. The roads: serpentines on main roads, 7 m bends on a highway, steps at junctions and a road that overlapped itself. They spoil driving, racing, traffic and the stunts. **Fixed:** lattice routing with headings, junctions where roads touch, level junctions, and two new tests (findings 10–13).
2. The danger signs depended on luck: one crest, then none, after a re-bake. **Fixed:** yumps built into four roads, and jumps measured in the game within 2% of their targets (findings 16–18).
3. Speed zone targets nobody could reach. **Fixed:** a real speed profile (finding 19).
4. The Unity race screen only knew circuits and sprints. **Fixed:** event kinds, levels, championships, the ghost and accolades in the menu, HUD and results.
5. Nothing has been seen in Unity: the menus, photo mode, the stunt boards and the racing line are compile-checked only. **Open:** the PC session.

Also open: no cross-country event (off-road racing between checkpoints would need the AI to plan across terrain and round trees); drift scoring is set by a crude bot, and the cars' breakaway is snappy (M8, driving feel); engine and impact sounds are not in Unity yet, so their volume settings are not either; one shallow fork still has a 0.35 m step between overlapping pavements; the browser determinism test failed once with the first M7 roads and has not failed since (not reproduced).

## M8: Polish and optimization (driving feel, sound, presets, art, bug bash)

**Tried (headless; the web build in headless Chromium; the C# in .NET):**
- Drifting: `tools/dev/drift-probe.ts` throws all 12 cars into a slide at 60 km/h and drives each for 8 s at 20°, 30° and 40° of slip, with three drivers (raw steering, the standard assist, and a rally driver who pins the throttle and dabs the handbrake), on asphalt, dirt and gravel; `tools/dev/drift-trace.ts` traces one run.
- The sound: every car's engine loops checked for level, clean seams and pitch against the firing frequency (`tools/dev/audio-check.ts`, with spectrograms of each sweep); the three radio stations rendered for 75 s each (`tools/dev/radio-check.ts`); the C# port against the web's renders (`AudioChecks.cs`); the web game with sound on in headless Chromium (`tests/browser/m8-audio.mjs`).
- The pause menu's new rows on a desktop, a phone in portrait and in landscape; the touch controls' new FM button; dynamic resolution in normal play (`tests/browser/m8-settings.mjs`).
- Review screenshots of the island, the festival, the city and a car at golden hour, before and after the art pass.
- Every suite again on the new physics: physics and sound in C#, the island, the career, the living island and M7, the AI.

| # | Finding | Sev | Root cause | Fix | Retest |
|---|---|---|---|---|---|
| 1 | A drift probe with a scripted entry (handbrake for half a second, then 0.7 throttle with the wheel still turned) spun four cars before the driver took over: the entry itself was the spin. | S3 (tool) | The entry did not watch the car. | The entry turns in on the handbrake until the slip is half way to the target, as a person does; the driver then works the steering and throttle on the slip and its rate. | 0 spins with raw steering |
| 2 | In a long drift the automatic gearbox sat on the limiter in first (7100 rpm at 60 km/h for six seconds), so the rear wheels could not be kept turning. | S2 | Upshifts are held while a driven wheel slips more than 35% (so a launch does not shift on wheelspin), and a drift is all wheelspin. | The hold lasts half a second at the shift point (a leaky timer); in a long slide the box shifts up. Both languages. | The Aster GT's drift settles in second at 30°; validations unchanged |
| 3 | AWD cars could not be thrown into a slide: at 60 km/h the rear wheels only reached 20–45% slip under the handbrake (a locked wheel is 100%), and with the throttle held they barely slowed (6–14%). | S1 | The handbrake only declutched below 7 m/s; above it the engine and the AWD centre coupling drove the rear wheels against it. | The handbrake declutches at any speed (as in a drift car, and in Forza). Released with the engine revved, the clutch bites like a clutch kick. Both languages. | The handbrake driver on asphalt: 62.6 s held of 288 → 134.3 s; AWD cars 7.6–8.0 s at 20° |
| 4 | On gravel the rally car held a slide for 0 s, and rear-driven cars spun out of 30° slides. | S2 | Loose ground only scaled the friction: grip peaked at the slip it peaks at on asphalt and fell away beyond it, unlike gravel. | Loose ground peaks at 1.5× the slip and its sliding grip moves halfway to the peak (a second curve per tire); ABS, TCS and launch control aim at the loose peak; the steering assist keeps the paved window, which measured steadier. Both languages, references regenerated. | The standard driver on gravel: 68.3 s and 10 spins → 81.5 s and 6; on dirt 74.1 s and 10 → 84.3 s and 4 |
| 5 | The tire curve was suspected of a snappy breakaway (M7's open item). | — | Measured: the force at four times the peak slip is 90% of the peak (street tires). The snap came from findings 2 and 3, not the curve. | Curve left as it is. | — |
| 6 | Drift zone bots earned a star in 3 of 12 runs. | S3 | Findings 2–4. | — | 6 of 12, one with two stars (1,856 points) |
| 7 | An audio test spent 90 s waiting for timers in the page. | S3 (test) | Headless Chromium throttles chained timers in a page that is not visible, and SwiftShader takes seconds to draw a frame. | The test waits on the Node side and steps the simulation without rendering (stepping presents a frame, which is all the sound needs). | 13 s |
| 8 | The electric car's motor whine would stop rising at 18,000 rpm in Unity. | S3 | Unity pitches a clip at most 3× and the loop was rendered at 6,000 rpm. | Rendered at 9,000 rpm in both languages (the web has no limit). | The Vanta's 22,000 rpm is within 2.5× |
| 9 | After a change from a turbo car, its whistle would have played on at the last boost. | S3 | The turbo bed was only set while the car had a turbo. | Turbo and intake are set every frame, to zero when the car has none or the next engine is still rendering. Web and Unity. | Code review; not heard |
| 10 | On a page that forbids blob workers, the sound would never arrive. | S3 | Requests sent to the worker before it failed were lost. | The main-thread renderer takes over and the requests are sent again. | Code review |
| 11 | Dynamic resolution never lowered the resolution on a small window. | S2 | The scale applied to the pixel budget, which only binds on big screens; under it the device pixel ratio won. | The scale applies to the final pixel ratio (never under 0.5). | Slow frames: scale 0.90 and pixel ratio 0.90 after 34 s |
| 12 | The meadows read as one flat green from a height. | S3 (art) | The variation noise sits in a narrow band around 0.5 (and averaging three scales narrowed it more), so "dry" and "lush" were the same everywhere. | Stretched before use, a third scale (1.4 km fields), sun-facing slopes dry out first, and wildflowers up close. | Before/after screenshots |
| 13 | On a phone there was no way to change the radio station while driving. | S3 | Only the N key did it. | An FM button on the touch controls. | Screenshot in landscape |
| 14 | The web version had one car: the Aster GT. | S2 | Car choice lived in the Unity garage only. | A car picker in the pause menu (a native list, which suits a phone): the new car takes the old one's place and assists, and is remembered. | `m8-settings`: 12 cars listed, the change lands within 0.5 m |

**Bug bash of the Unity code** (it has never run, so this was a review of every script M7 and M8 touched, by reading, against Unity's documented behaviour; each fix is compile-checked against the UnityEngine reference assemblies, none has been run):

| # | Finding | Sev | Root cause | Fix | Retest |
|---|---|---|---|---|---|
| 15 | With no audio device, or audio disabled, the game would not have finished loading. | S1 | The sound was created in the bootstrap without a guard, and Unity reports a 0 Hz output then: a 0 Hz radio clip throws. | The sound's creation is guarded (the game plays silent and says why in the log); the radio falls back to 48 kHz. | Compiled |
| 16 | A championship round paid at the difficulty showing in the festival menu, not the one its rivals raced at. | S2 | The payout read the menu's difficulty at the finish; a championship keeps the rivals' difficulty it began with. | The difficulty raced is kept when the race begins and pays the finish. | Compiled |
| 17 | The graphics presets changed the project's URP asset, and in the Editor the change outlived Play mode (the project kept the last preset's render scale, shadow distance and MSAA). | S2 | The asset in use is the project's own, not a copy. | The first value of each setting is kept; they are put back on quitting and on leaving Play mode. | Compiled |
| 18 | After a change of output device (headphones, Bluetooth), the rain, the engines and the radio would have stayed silent. | S2 | Unity stops every source when its audio resets. | On Unity's notice the rain plays on, the car loops start again when next heard, and the radio re-tunes at the new sample rate. | Compiled |
| 19 | The player's car, 16–18 engine loops and 9 beds, plus four other cars of 7–9 loops each, all played all the time: more than Unity's 32 real voices, so Unity would have dropped the quietest, which could be the rain or the radio. | S2 | Silent loops were left playing at volume 0. | Silent loops pause (giving their voice back) and play on when heard; priorities put the radio first, then menus, effects and weather, the player's engine, the tires and wind, and the other cars last. | Compiled |
| 20 | The bonnet camera's muffle did nothing. | S3 | The low-pass filter sat on an object with no sound of its own, and Unity filters only the source on the filter's own object. | Each of the player's sources (loops, beds, effects) carries its filter, switched off when the car is heard from outside. | Compiled |
| 21 | Other cars' engines would cut off: a voice let go of a car at 110 m, where the rolloff still left 6% of its level, and with two cars at the edge the voices jumped between them. | S2 | A logarithmic rolloff never reaches zero, and the nearest four were picked afresh each frame. | A rolloff that reaches zero at the range, a quarter-second fade in and out, and hysteresis: a voice keeps its car within 1.1× the range and among the nearest six. | Compiled |
| 22 | Opening and closing a menu made no sound, and the blips came a frame late or not at all. | S3 | The blips read the menu keys in `Update`, in an order Unity does not fix against the menus' own `Update`: by the time a key had closed a menu, `InMenu` was already false. | Blips in `LateUpdate`, after the menus: one on opening, Back (or Select) on closing, none when an event has just begun (its countdown speaks). | Compiled |
| 23 | An impact still ringing was re-pitched by the next gear shift or crackle. | S3 | Every effect played on one source, and a source's pitch applies to all it plays. | Four effect sources; a new sound takes one that has finished, or the one that ends first. | Compiled |
| 24 | An engine that failed to render was asked for again every frame (a new render and a warning each frame). | S3 | Failures were not remembered. | Remembered, and said once. | Compiled |
| 25 | A bar that failed to render would have ended the radio for the session without a word. | S3 | The worker was a task whose exception nobody observed. | A background thread that catches each bar: the station goes quiet with one warning and the next tune starts afresh. | Compiled |
| 26 | The radio made about 1.1 MB/s of garbage. | S4 | A new interleaved array for every bar. | Played bars go back to the worker to be filled again (the station's own work arrays still make about 0.8 MB/s on the worker thread: fine on a current phone, noted). | Compiled |
| 27 | A time trial's finish played the plain chime when the ghost was beaten. | S3 | The sound read the race position; a time trial ranks against the ghost. | The finish sound reads the same place as the results screen. | Compiled |
| 28 | The radio's song callback was made again every frame. | S4 | Set in `Update`. | Set once. | Compiled |
| 29 | The photo mode's shutter made no sound. | S4 | The photo sound was rendered but never played. | Played when the photo is saved. | Compiled |

**The art pass** (review screenshots of the web build at Ultra, golden hour: the island road, the festival from above, the city, a chase view, a side view of the car, then close-ups of the festival):

| # | Finding | Sev | Root cause | Fix | Retest |
|---|---|---|---|---|---|
| 30 | The web build's cars were crude: a smooth blob with a flat grey panel for windows, boxy at the back, a swirl in the rear glass. | S2 | The web drew every car with its parametric stand-in; the real models (`blender/build_car.py`, made for Unity) never reached it. | The Blender cars in the web build: one LOD of each (21k triangles for the Aster GT) exported as GLB and packed by gltfpack (meshopt, quantized: 88–156 KB a car, 1.5 MB for all twelve; `tools/pack-cars.mjs`), decoded once and cloned per car. The model replaces the drawn body when it has loaded; its wheels and callipers move into the view's steering and spinning groups, so suspension, steering and spin work as before; materials by slot name as in Unity (clear-coat paint in the car's own finish, tinted glass with the cabin behind, chrome, rubber, lights the view switches). | Side and chase screenshots; the procedural body stays as the stand-in while the model loads |
| 31 | The festival hub was an empty meadow with a stage and a wheel in it. | S2 | Only the stage, wheel, arch, tents and flag poles were laid out. | A crowd of 260 in a barriered pit before the stage, with four light-and-speaker towers; a food court of six trucks with striped awnings and 170 people; seven cars on show on lit plinths; a car park of 20 cars on gravel. All of it is solid (plinths, parked cars, trucks, towers and barriers are colliders), exported to Unity, and drawn there too. | Screenshots from above, of the pit, the food court, the cars on show and the car park |
| 32 | Twenty-seven standing cars would have cost about 950 draw calls (35 parts each) before shadows, and their shadows were most of the dressing's triangles. | S3 (perf) | One mesh per part per car, every part casting into every shadow cascade. | Standing cars are baked by finish (paint and coats, metal, matte, glass) into four meshes per group; the cars on show cast real shadows, the parked ones a soft blob each. | The whole dressing (790k triangles once) adds 29 draws and 1.13M triangles to a High frame at the spawn (2.4M before the blobs) |
| 33 | Grass blades grew through the car park's gravel and the plinths. | S3 | The grass knew roads, water and rock, not the festival. | The grass skips ground the festival keeps bare. | Car park screenshot |
| 34 | The crowd read as stacked boxes up close. | S4 (art) | Box legs, torso and head. | Rounded figures: two legs, a torso with arms, a head and neck; half the crowd jumps on the beat and everyone sways (in the vertex shader, no cost on the CPU). | Food court screenshot |
| 35 | On a flagship phone, Auto chose Medium (Auto went by cores and memory only, and Chrome reports at most 8 GB). | S3 | No look at the GPU. | Auto reads the GPU's name: Adreno 740 and up, Immortalis and Xclipse 9xx get High; dynamic resolution then holds the frame rate. Everything else as before. | Matcher checked on 11 GPU names |
| 36 | Review screenshots at Ultra timed out after 30 s in headless Chromium. | S4 (tool) | SwiftShader takes longer than Playwright's default over an Ultra frame. | Shots wait up to four minutes, and the review tool waits for the car models and the festival's cars before shooting. | Every review shot taken |

**Verified, no bug found:**
- The 12 cars' validations (0–100, 0–200, quarter mile, top speed, 100–0, skidpad) are unchanged to the last digit by the drift changes: launches, braking and the skidpad never use the declutching handbrake, a long wheelspin at the shift point, or loose ground.
- The C# physics is bit-exact with the TypeScript on 97 checks (370,639 numbers), including two new loose-ground scenarios (gravel, dirt) that exercise the loose curve, the declutching handbrake and the held upshift.
- Every suite passes on the new physics: the island equivalence, the career, the living island and M7 (the traffic bot: 7.9 km past 54 physics cars with 0 contacts and 0 resets, from 4 contacts), and the AI (every car finishes every event with 0 resets on asphalt; the rally still has one reset in six cars; wet races 0 resets in 20).
- Engine loops: every car's ladder renders (7–9 rpm points, on and off the throttle, 100–220 ms a car), loops are seamless (no jump at the seam bigger than inside the loop), and the strongest repetition in each is the firing frequency or a whole fraction of it (the cycle, for V8s and V6s: the burble). Spectrograms of each sweep show the harmonics following the rpm, the V8s' half orders, the motor's pure partials.
- The radio: 75 s of each station, finite, peaks held at 0.61 and loudness −14 dBFS, songs and idents changing; rendering runs 24–29 times faster than real time.
- In the browser with sound on: the worker renders the engine (16 loops), 9 beds and 9 UI sounds; at 4,475 rpm the mix is at 0.11–0.13 RMS; the radio keeps 2–4 s of bars queued; the station key moves to the next station; a change of car renders its engine.
- The C# sound against the web's: engine loops and beds identical, one-shots within one float ulp (6e-8), the radio within 2e-12 through 24 bars of each station (the ident, an intro and its fill, a verse and into a chorus).
- Phones: the pause menu's new rows wrap and scroll in portrait (390×844); the FM button fits in landscape (844×390).

**Measured:**
- Holding a drift, of 288 s (12 cars × 3 angles × 8 s; `docs/CARS.md`): the throttle driver with the standard assist 68.4 → 91.5 s; the handbrake driver 62.6 → 134.3 s; raw steering 56.5 → 66.7 s with no spins. Rear-driven cars hold 20–30° on the throttle; AWD cars 20° on power and the handbrake (7.6–8.0 s of 8).
- Drift zones scored by the drift bots: 3 → 6 of 12 runs earn a star.
- Dynamic resolution under SwiftShader (far below 60 frames a second): 0.90 of the preset's resolution after 34 s, stepping down 10% each 1.5 s.

**In Unity (compile-checked against the UnityEngine reference assemblies; not yet run or heard):**
- `GameSound`: the player's engine in 2D (every loop of the ladder, pitched and crossfaded as on the web), the beds, impacts, the blow-off valve, crackle, shifts; the four nearest other cars' engines in 3D (racers and traffic; a ghost is silent); menu blips from the menu keys; countdown beeps, GO, a chime per lap and at the finish; a chime with each accolade and stunt. Loops and beds render on worker threads and become clips on the main thread.
- `RadioPlayer`: a worker thread keeps three seconds of the station rendered; `OnAudioFilterRead` plays them over a looping silent clip; tuning static between stations; the song's name on screen when it starts.
- Settings: engine, effects, music and the station (N, or d-pad right, or the pause menu), saved with the rest.
- After the bug bash: silent loops pause, the radio and weather come first in Unity's 32 voices, other cars fade in and out, menus blip on opening and closing, and an audio device change restarts everything (findings 15–29).
- The festival's dressing from the shared layout: the cars on show on plinths with a lit ring and the car park (`CarVisual`, the Blender models), the food trucks, the light towers, the barriers and the crowds (static figures in the festival's colours), and the car park's gravel. The island data is re-exported, so Unity's physics has the same new colliders.

**Self-critique: the five weakest things after the first pass of M8, and what was done:**

1. The game you can play on a phone (the web build) is free roaming only: no races, rivals, traffic, stunts or career. They live in the C# Core, and the Unity version that has them has never run. **Open, and next:** porting the Core's game to the web (the AI, events, traffic, stunts, weather and the career), with equivalence tests against the C#.
2. The web build's cars were crude stand-ins. **Fixed:** the Blender models, as in Unity (finding 30).
3. The festival, where every session starts, was an empty field. **Fixed:** crowds, food trucks, towers, cars on show and a car park, solid and in both versions (findings 31–34).
4. Nobody has heard the sound; it is measured (levels, seams, pitch, spectrograms) but not judged by ear. **Partly:** a 74 s sound tour (six engines revving, then the three stations) is rendered for listening (`docs/media/halcyon-sound-tour.mp3`), and the game page has the sound. The judgement is a person's.
5. The Unity code has never run. **Partly:** a bug bash by review found and fixed 15 bugs (findings 15–29), all compile-checked. **Open:** running it, which needs the PC session (`docs/UNITY-PC-BRIEF.md`).

Also open: AWD cars hold a slide on the handbrake but not on the throttle alone (realistic, but less fun than Forza's); the radio's station worker still makes about 0.8 MB/s of short-lived garbage in Unity; the asphalt's aggregate glitters at very low camera angles; Auto picks High only on GPUs it recognises (the pause menu offers every preset).

## The races on the web (the AI ported from the C# Core)

**Tried (headless; the web build in headless Chromium; the AI in Node and in .NET):**
- The festival's races in the browser (`tests/browser/m9-races.mjs`): the menu on a desktop and on a phone in landscape (the RACE button), the Proving Ground Grand Prix from the grid (countdown, the rivals racing, the HUD, leaving from the pause menu), a drag race driven to its results, the rivals' cars measured in the worker, and the rivals' engines with sound on.
- AI-only races in Node: six Grand Prix and six Harbour Dashes, each with the field as drawn and in qualifying order, counting contacts in the opening 20 s and over the race (`tools/dev/race-grid-probe.ts`), with contacts traced car by car.
- The port against the C#: the reference race traced step by step in both languages (`tools/dev/ai-race-trace.ts` against `dotnet run -- airace`).
- The C# AI suite before and after the AI changes; review screenshots of a race (`node tools/look.mjs high golden look race`).

| # | Finding | Sev | Root cause | Fix | Retest |
|---|---|---|---|---|---|
| 1 | The rivals drove into each other: 37 contacts in the opening 20 s of six Grand Prix, 46 over the races, 4 resets. | S2 | The port's racecraft kept references to the race's view of the other cars, which is one object refilled for each car (the C#'s view is a struct, copied when assigned). By the end of the loop, "the car ahead", "the car behind" and "the car being passed" were all the last car on the grid: the player, standing at the back. | Racecraft copies the views it keeps. | 1 contact in the openings, 10 over the races, 0 resets |
| 2 | Off the grid, rivals swerved across each other. | S2 | Every car steered for the racing line from the start, so the car in the other column was in its path, and it started a pass on it. Both languages. | A car keeps to its grid column until 60 m past the start line, then merges onto the line over the next 60 m (`holdGrid`). | The columns hold (about ±2.3 m) through the first 60 m; the cars behind a player who stalls on the grid get by within about 5 s |
| 3 | A field lined up as drawn put quick starters behind slow ones, and the first seconds were a chain of rear-end contacts. | S2 | The grid order was the draw's. Both languages. | The grid in qualifying order (`qualifyingOrder`: each driver's time round the route at its own pace, fastest first). The player starts mid-grid, as in Unity. | Six Grand Prix 10 → 2 contacts, six Harbour Dashes 5 → 0. In the C#, mixed and PI-matched fields 24 → 4 contacts over seven races, resets 1 → 0 |
| 4 | The proving ground's start gantry had stood turned along the track since M1: one leg in the middle of the road, and the cars drove through it (it has no collider). | S3 | Rotated a quarter turn too far. | Turned across the track; its legs stand off the edges. | Grid screenshot |
| 5 | The first whole-race comparison of the two versions failed: the same race finished up to 0.17 s apart. | S4 (test) | Traced step by step, the versions agree to the last bit for 2.99 s of racing, until a rear wheel lands exactly on the edge of the start straight's concrete shoulder: on it in Unity's copy of the track (32-bit floats), off it in the web's. Not a bug: the island is shared to a millimetre by design, and a race is chaotic after one such step. | The test compares the countdown and two seconds of racing to the last bit, then the finishing times within 1%. | Identical through step 1,200; finishing times within 0.15%; the same contacts |
| 6 | During a Grand Prix on the proving ground, the circuit's own lap timer put its lap toasts over the race. | S3 | The free-roam lap timer runs on the same circuit. | Quiet during a race. | Code review |
| 7 | A waypoint set on the map mid-race replaced a sprint's GPS line. | S3 | The race's route and the waypoint's share the GPS. | Waypoints wait until the race is over (a toast says so). | Code review |
| 8 | The pause menu's car picker would have swapped the car under a race's entrant. | S3 | Nothing stopped it. | Disabled in a race, with a note. | Code review |
| 9 | The engine played on behind the races menu, while the world waits. | S3 | The sound paused for the pause menu and the map only. | The sound pauses with the world. | Code review |
| 10 | The first race froze the page while the rivals' cars were measured: 2.0 s on a desktop for the Grand Prix, another 2.6 s for the rally on dirt; a phone is two or three times slower. | S2 | `CarAbility.measure` drives each car round a virtual test track on the main thread. | A worker measures them: the whole pool the grid is drawn from when the menu opens, the race's own cars first when it starts. The page keeps drawing. | In the test container: 7 cars in 5.4 s in the worker, the page blocked 9 ms, the numbers the same as the page's to the last bit |
| 11 | The rivals were silent. | S2 | Only the player's engine was voiced on the web. | Four engine voices, as in Unity: each car's own loops pitched by its rpm, Unity's rolloff to 110 m, a low-pass for the air, a stereo pan, a little Doppler, quarter-second fades, and hysteresis on which cars are voiced. | Four voices on the four nearest rivals, faded in; none once the race is left |
| 12 | The race HUD said nothing about the cars around you. | S3 | Position, lap and time only. | The gaps to the cars ahead and behind, as real intervals from timing loops every 5 m. | The browser test reads the gap to the car ahead while the player stands still |
| 13 | The minimap's distance label ran off the bottom of a 720-pixel desktop screen. | S4 | Placed under the circle, which sits 18 px from the bottom edge. | Inside the circle on desktop; under it on touch screens, where the minimap is at the top. | Screenshot |
| 14 | Rivals' paints repeated. | S3 | Each drawn independently. | Drawn without repeats from twelve. | 7 rivals, 7 paints |
| 15 | Key presses in a browser test were ignored. | S4 (test) | The test API's scripted input replaces the keyboard until it is cleared. | The tests clear it before pressing keys. | — |
| 16 | On a phone the race panel covered the touch buttons: CAM and ROAD in landscape, the whole row and the minimap in portrait. | S2 | Placed for a desktop: top centre. | Below the buttons in landscape, below the gauge (and a little smaller) in portrait. | Screenshots in both orientations; no overlapping boxes |
| 17 | In portrait the row of touch buttons covered the minimap (since M8 with five buttons; six with RACE covered it fully). | S3 | Both sat at the top left. | In portrait the minimap goes below the buttons. | Portrait screenshot |
| 18 | The second race of a session showed the first race's gaps during its countdown. | S3 | The gaps carry from frame to frame (they change four times a second), and nothing cleared them. | The HUD clears when a race starts. | The browser test starts a second race and reads its countdown |
| 19 | The gaps read 0.0 after a frame that covered several timing loops (a slow frame, a tab coming back). | S3 | Every loop crossed in a frame took the frame's time. | Each loop's time is interpolated from the car's progress over the frame; no gap is shown until both cars have crossed the line. | 3 s after GO, standing at the line: "▲ 3.0" to the car that left at GO |
| 20 | A page that blocks workers without an error (one never answers) would never have built its grid: "Building the grid…" for good. | S2 | The page waited on the worker's reply. | A worker that takes more than 8 s over a car is replaced by the page, starting with that car. | A test with a worker that never answers: 7 cars measured on the page after its silence, in 12.4 s |

**Verified, no bug found:**
- The festival's nine races build on the web with the C#'s routes, racing lines, speed profiles and measured cars, to the last bit (`tests/ai.test.ts`), and an AI-only race runs as the C#'s does (finding 5).
- A drag race driven to its end: the player's quarter mile in 13.1 s at 187 km/h, results with both trap speeds, Enter back to free roam.
- The menu on a phone in landscape (844×390) and the RACE button; leaving a race from the pause menu removes the rivals and their views and gives the car back.

**Measured:**
- The C# AI suite, before and after findings 2 and 3: head-to-head duels hold the skill order in 294 → 300 of 300 races, with 2 → 1 contacts; full grids 1.0 → 0.5 contacts a race, the longest a car was stuck 2.4 → 0.4 s.
- The web's AI-only Grand Prix (five Pro drivers, two laps): 3 → 1 contact, 0 resets.
- Building a race (desktop, Node): racing lines 7–81 ms, measuring a first grid's cars 2.0 s (now in the worker), later races 3–60 ms.

**In Unity (compile-checked; not run):** the grid hold and qualifying order are in the shared Core, so Unity's races start the same way; `RaceController` lines up its PI-matched fields by pace.

**Self-critique: the five weakest things after the first pass, and what was done:**

1. The rivals were silent. **Fixed** (finding 11).
2. The first race froze the page for seconds on a phone. **Fixed** (findings 10 and 20).
3. The race said nothing about the cars around you. **Fixed** (finding 12).
4. Races give nothing on the web: no credits, XP, levels, accolades or championships (the career is in the C# Core and Unity only). **Open, and next:** porting the career, tested against the C# as the AI was.
5. Outside a race the island is still empty on the web: no traffic, stunts, weather or clock. **Open.**

Also open: the countdown holds the car, so there is no revving at the line; building the racing line (50–80 ms) still runs on the main thread when a race starts (a dropped frame or two); the racing line can't be shown on the web; a player who stalls mid-grid holds up the cars behind for up to about 5 s.
