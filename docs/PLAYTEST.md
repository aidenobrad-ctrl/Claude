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
