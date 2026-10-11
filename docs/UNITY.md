# Halcyon in Unity + Blender

The Unity version of Halcyon. It reuses the web version's island and driving physics
exactly, and adds Unity rendering and Blender-made cars on top.

## Play it

1. Install Unity 6 (6000.x) with Unity Hub. Then **Add project from disk** and pick the
   `unity/` folder of this repo. The packages (URP, Input System) are in
   `unity/Packages/manifest.json`.
2. On first open, `Halcyon > Set Up Project` runs by itself. It:
   - switches to linear colour;
   - creates and assigns a URP asset (HDR, 4 shadow cascades over 450 m, SSAO);
   - creates `Assets/Scenes/Halcyon.unity`.
3. Open that scene, or any scene, and press **Play**. The island builds itself in a few
   seconds and you start on the festival road in the Halden Aster GT.
4. Press **G** for the garage: the 12 cars on a turntable at the festival stage, with
   paint presets and measured stats. Press **T** for festival events: pick an event and
   your rivals' difficulty, then race the AI.

**Controls**

| Key | Action |
|---|---|
| W / S or arrows | Throttle / brake |
| A / D | Steer |
| Space | Handbrake |
| E / Q | Shift up / down |
| R | Reset to road |
| C | Camera (chase, far, bonnet) |
| B | Look back |
| L | Lights |
| Esc | Pause |
| G | Garage (←/→ car, ↑/↓ paint, Enter drive it, Esc back) |
| T | Events (↑/↓ event, ←/→ rivals' difficulty, Enter race, Esc back); during a race hold T for a second to leave |

A gamepad works through the Input System: stick, triggers, A for handbrake, B/X to
shift, Y for camera, LB for the garage, d-pad up for events. In menus the d-pad or stick
moves, A picks and B goes back.

**Screenshots** (for the playtest loop):

```sh
Unity -batchmode -quit -projectPath unity -executeMethod Halcyon.EditorTools.HalcyonBuild.BuildWindows
unity/Builds/Windows/Halcyon.exe -screen-width 1280 -screen-height 720 -halcyon-capture shots
```

The captured views are chase, far chase, the festival, the whole island, the city and a
village.

## What builds the game

| Path | What |
|---|---|
| `unity/Assets/Halcyon/Data/` | The island, exported from the web version (`tools/export-unity.ts`); formats in its README |
| `unity/Assets/Halcyon/Core/` | Pure C# simulation (no UnityEngine): deterministic math, ground, colliders, island loader, the car physics, the island simulation |
| `unity/Assets/Halcyon/Core/AI/` | Race routes, racing lines, car abilities, speed profiles, the AI driver, races and the event catalogue (design in `docs/DESIGN.md`) |
| `unity/Assets/Halcyon/Core/Career/` | The performance index, parts, prices and payouts, the player's profile and saves, tuning sliders, share codes, the test track and the auto-tune |
| `unity/Assets/Halcyon/Scripts/World/` | Runtime builders, listed below |
| `unity/Assets/Halcyon/Scripts/Driving/` | The car rig, chase camera and input, listed below |
| `unity/Assets/Halcyon/Scripts/Game/HalcyonGame.cs` | Boots everything when Play is pressed, runs the 240 Hz loop, draws the HUD, capture mode |
| `unity/Assets/Halcyon/Scripts/Game/Garage.cs` | The garage: turntable, paint, stats, car switch |
| `unity/Assets/Halcyon/Scripts/Game/RaceController.cs` | The events menu, AI car rigs, race HUD (countdown, position, laps, gaps, wrong way) and results |
| `unity/Assets/Halcyon/Scripts/Editor/` | Project setup (URP, scene, build settings) and command-line builds |
| `unity/Assets/Halcyon/Art/Resources/Cars/` | Car models from Blender (`blender/build_car.py`) |
| `blender/` | The Blender car generator, run headless or in Blender 5.1 |
| `dotnet/` | Builds and tests everything outside Unity (.NET 8), listed below |

**World builders** (`Scripts/World/`):

- Terrain: 5 × 5 Unity terrains whose heightmaps sit exactly on the physics grid, with
  splat layers.
- Roads: markings by class, shoulders and kerbs, bridges with parapets and piers,
  guardrails on retaining walls.
- Water: sea, lakes and rivers.
- Trees: 185k, GPU-instanced with near and far meshes.
- Buildings, the festival, and sun/sky/fog/post-processing.

**Car rig, camera and input** (`Scripts/Driving/`):

- The car rig poses the body and wheels from the physics with interpolation.
- `CarVisual` loads a car's FBX and style file, remaps its Blender materials to URP
  (paint, glass, chrome, lights that glow with the headlights and brakes) and sets up one
  LODGroup.
- The chase camera is a port of the web rig.
- Input comes from the keyboard and gamepad.

**Checks in `dotnet/`**:

- `Halcyon.Core.Tests`: the car physics against the TypeScript. It compares 339,299
  numbers across 91 checks (all 12 cars), and every one is bit-exact.
- `Halcyon.World.Tests`: the island's ground, roads, trees, colliders, terrain tiles and a
  scripted drive against the TypeScript.
- `Halcyon.AI.Tests`: the AI on the island, headless, in these modes:
  - racing lines;
  - every car alone;
  - sprints;
  - the head-to-head tournament (every driver against every other, every route, 4 seeds;
    `-- tournament`);
  - full grids (`-- grids`);
  - mixed fields (`-- field`);
  - debugging traces (`-- duel`, `-- compare`, `-- trace2`, `-- skillsolo`).

  Results are in `docs/PLAYTEST.md` (M4).
- `Halcyon.Career.Tests`: the PI of every car, parts and prices, payouts, a career saved
  and loaded, every slider against the physics, share codes and the auto-tune
  (`-- pi`, `-- parts`, `-- save`, `-- tune`). Results are in `docs/PLAYTEST.md` (M5).
- `Halcyon.Unity` and `Halcyon.UnityEditor`: compile every Unity script against UnityEngine
  and UnityEditor reference assemblies (`node tools/fetch-unity-ref.mjs`). The input-system
  code path compiles only in Unity.

## Coordinate frames

`Core` runs in the web game's right-handed frame:

- x is east, y is up, z is south.
- Yaw 0 faces -z.

Unity is left-handed, so positions cross over as `(x, y, -z)` and rotations as
`(-qx, -qy, qz, qw)`. `Frame` does this in the Unity scripts; `TerrainTiles` returns
heightmaps and alphamaps already flipped.

## Determinism and equivalence

`Core` is a line-by-line port of the TypeScript simulation:

- doubles throughout
- the same operation order
- `DMath`'s fdlibm trig in place of `System.Math`

Results against the web version:

- **Car physics:** bit-exact. The Halden Aster GT does 0–100 km/h in 4.74 s, tops out at
  297.6 km/h, stops from 100 km/h in 31.01 m and pulls 0.948 g on the skidpad, the same as
  `docs/CARS.md`.
- **Island:** heights agree to 0.6 cm at the 99.9th percentile, because the terrain is
  stored as 16-bit, as Unity does.
- **Scripted drive:** identical on the road, and within 1.8 cm on a dirt shoulder.

To re-run everything:

```sh
node tools/run-ts.mjs tools/export-unity.ts    # re-export the island (31 s)
node tools/run-ts.mjs tools/dev/world-ref.ts   # world references
node tools/run-ts.mjs tools/dev/sim-ref.ts     # scripted-drive reference
node tools/run-ts.mjs tools/dev/vehicle-ref.ts # vehicle references
node tools/unity-meta.mjs                      # stable .meta files for new assets
node tools/fetch-unity-ref.mjs                 # Unity reference assemblies (compile checks only)
cd dotnet/Halcyon.Core.Tests && dotnet run -c Release
cd ../Halcyon.World.Tests && dotnet run -c Release
cd ../Halcyon.Unity && dotnet build && cd ../Halcyon.UnityEditor && dotnet build
```

## Blender

```sh
blender -b -P blender/build_car.py -- --car halden-aster-gt --render blender/renders/halden-aster-gt.png
```

`--car all` (or a comma-separated list) builds several cars. `--sheet 'blender/renders/{car}.png'` renders a six-view contact sheet per car.

**Body.** The body is a smooth signed distance field, polygonized with surface nets
(numpy only). Glass, lights, grilles, intakes, vents, panel gaps and stripes are regions
cut along their exact contours in every LOD, so their edges stay crisp. Holes get walls and
insert surfaces.

**Detail.** LODs are decimated, and their normals come from the exact field. Wheels come in
seven rim styles, with lathed tyres, discs and callipers. Wings, spoilers, roof racks, bull
bars, snorkels, spare wheels and interiors come from a parts registry.

**Cars.** All 12 cars are data in `blender/halcyon/cars.py`. Review sheets are in
`blender/renders/<car>.png`.

**Output.** The FBX and `<car>.style.json` land in `Art/Resources/Cars/`. The FBX faces +Z
in Unity, with its origin on the ground at mid-wheelbase.

## .meta files

`tools/unity-meta.mjs` writes a `.meta` with a GUID derived from the file's path for every
script, data file and folder that lacks one. GUIDs are the same on every machine. Model
and texture metas are left to Unity, because their import settings live there.

## Status

- [x] Island export: terrain, surfaces, water, splat, roads, guardrails, proving circuit,
  trees, colliders, buildings, festival, spawn, POIs
- [x] Core: math, ground, colliders, island loader (equivalence-tested)
- [x] Core: car physics (bit-exact) and island simulation
- [x] Blender: car generator; hero car exported with LODs
- [x] Unity: runtime world builders, car rig, chase camera, input, HUD, boot, capture
  mode, project setup. Compile-checked here, not yet run in a real Editor.
- [x] 12-car roster (bit-exact physics), Blender models with LODs, garage
- [x] AI (racing lines, controller, personalities, racecraft), races, 5 events, race HUD;
  tournament 98% headless
- [ ] First run in a real Unity Editor: fix whatever only Unity reports, play the garage and
  a race, then look at screenshots
- [ ] CC0 PBR textures, a real water shader, Blender trees, audio, more event types
