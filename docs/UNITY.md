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
   seconds and you start on the festival road. A new career starts with 50,000 credits
   and the Halden Aster GT, and saves itself as you play.
4. Press **G** for the garage on the festival stage: drive, upgrade, tune, test and paint
   your cars, buy and sell at the dealer, and copy your career to the clipboard. Press
   **T** for the festival: circuits, sprints, a drag race, a rally, a head-to-head with
   the festival's rival and a time trial against her ghost, and three championships. Each
   has a class limit and a festival level; your rivals are matched to your car's PI, and
   results pay credits, XP and accolades.
5. The island lives: a day lasts 24 minutes, the weather changes by itself (rain wets the
   roads, and wet roads grip less), and traffic drives on the right. Press **M** for the
   map: pick a destination for the GPS, or fast travel to a place you have found.
6. Stunts are on the roads: speed traps, speed zones, drift zones and danger signs (jumps),
   each with three stars to earn. **Esc** opens the menu: settings and accessibility,
   your accolades, the controls. **P** is photo mode.
7. Everything you hear is synthesized as you play: each car's engine from its firing
   order and exhaust, tires on every surface, wind, impacts, and three radio stations that
   write their own music. **N** changes station.

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
| N | Radio: off, Halcyon Pulse, Coastline FM, Redline Radio |
| Esc | Menu: resume, settings and accessibility, accolades, photo mode, controls, quit |
| P | Photo mode (right mouse or J/L, I/K to orbit; wheel or U/O to zoom; ↑/↓ ←/→ the settings; Enter save a PNG; X hide the panel; Esc back) |
| G | Garage (←/→ car, ↑/↓ choose, Enter select, Esc back; in tuning, ←/→ adjust, 1-4 auto-tune, C/V copy/paste a tune code, R reset, Enter test track) |
| T | Festival (↑/↓ event or championship, ←/→ rivals' difficulty, Enter race, X abandon a championship, Esc back); during a race hold T for a second to leave |
| M | Map (↑/↓ destination, Enter GPS route, F twice fast travel, X clear the route, click the map for a route to there, right-click to clear, M or Esc close) |
| [ / ] | Clock an hour back / forward |
| \ | Weather: each sky in turn, then back to dynamic (held during races) |

A gamepad works through the Input System: stick, triggers, A for handbrake, B/X to
shift, Y for camera, LB for the garage, d-pad up for events, d-pad down for the map, the
right stick click for photo mode, d-pad right for the radio. In menus the d-pad or stick moves, A picks and B goes
back; on the map X fast travels and Y clears the route. In photo mode the right stick
orbits and the triggers zoom.

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
| `unity/Assets/Halcyon/Core/World/Weather.cs` | The clock (sun, moon, daylight) and the weather (skies, rain, fog, wind, lightning, road wetness) |
| `unity/Assets/Halcyon/Core/AI/Traffic.cs` | Traffic: ghosts far away, physics cars near the player, lanes, junctions, overtaking |
| `unity/Assets/Halcyon/Core/World/Landmarks.cs`, `Discoveries.cs`, `Gps.cs` | The five landmarks; places, roads driven, the fog of war and fast travel; the GPS |
| `unity/Assets/Halcyon/Scripts/World/` | Runtime builders, listed below |
| `unity/Assets/Halcyon/Scripts/Driving/` | The car rig, chase camera and input, listed below |
| `unity/Assets/Halcyon/Scripts/Game/HalcyonGame.cs` | Boots everything when Play is pressed, runs the 240 Hz loop, draws the HUD, capture mode |
| `unity/Assets/Halcyon/Scripts/Game/Garage.cs`, `Workshop.cs` | The garage: your cars, the dealer, upgrades, tuning (graphs, auto-tune, share codes), the test track, career data |
| `unity/Assets/Halcyon/Scripts/Game/Career.cs` | The career save in `Application.persistentDataPath`: autosave, backup, recovery, clipboard export and import |
| `unity/Assets/Halcyon/Scripts/Game/Ui.cs` | Shared IMGUI styles, panels, rows and plots, scaled to the screen |
| `unity/Assets/Halcyon/Scripts/Game/RaceController.cs` | The events menu with class limits and conditions, PI-matched AI grids in qualifying order (fastest at the front, the player mid-grid), race HUD (countdown, position, laps, gaps, wrong way), results and payouts |
| `unity/Assets/Halcyon/Scripts/Game/MapScreen.cs` | The map (fog of war, roads driven, places, the GPS route, fast travel) and the minimap |
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
- Buildings, the festival (the stage, the wheel, tents and flags; since M8 also the cars on
  show on lit plinths, the car park, food trucks, light towers, barriers and crowds, all from
  the shared layout and solid in the physics), and sun/sky/fog/post-processing.
- `SkyDirector`: the sun and moon on the clock, sky colours, clouds, ambient light, fog, a
  reflection probe, rain and splashes, lightning and thunder, wet road materials, and
  lit windows and lamps at night (`ProceduralAudio` makes the rain and thunder sounds).
- `LandmarkBuilder`: the mast with blinking lights, the lighthouse with its turning beam,
  the wind turbines, the arch and the observatory.

**Car rig, camera and input** (`Scripts/Driving/`):

- The car rig poses the body and wheels from the physics with interpolation.
- `CarVisual` loads a car's FBX and style file, remaps its Blender materials to URP
  (paint, glass, chrome, lights that glow with the headlights and brakes) and sets up one
  LODGroup.
- The chase camera is a port of the web rig.
- Input comes from the keyboard and gamepad.
- Headlights (with a spotlight within 180 m) come on at dusk; spray rises from the
  wheels on wet roads.
- `TrafficView` gives each traffic car a pooled rig in an everyday colour.
- `GpsRibbon` draws the GPS route on the road: violet chevrons in the right-hand lane.

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
  - mixed fields in qualifying order, as the game lines them up (`-- field`);
  - fields matched to a player's PI, with parts, in qualifying order (`-- pifield`), and their reset statistics
    over seeds (`-- pistats`);
  - every car with random tunes and parts (`-- tuned`), and every car alone, stock and with
    every part, on every event (`-- matrix`);
  - debugging traces (`-- duel`, `-- compare`, `-- trace2`, `-- skillsolo`, `-- pirace`,
    `-- crests`, `-- rejoin`);
  - the references the web's port of the AI is tested against (`-- airef`, written to
    `dotnet/ref/ai/ai-ref.json`), and the reference race step by step (`-- airace EVERY UNTIL`,
    to diff against `tools/dev/ai-race-trace.ts`).

  Results are in `docs/PLAYTEST.md` (M4).
- `Halcyon.Career.Tests`: the PI of every car, parts and prices, payouts, a career saved
  and loaded, every slider against the physics, share codes and the auto-tune
  (`-- pi`, `-- parts`, `-- save`, `-- tune`). Results are in `docs/PLAYTEST.md` (M5).
- `Halcyon.Sim.Tests`: the living island: the clock (`-- clock`), the weather
  (`-- weather`), the simulation's wiring (`-- sim`), the AI in the wet (`-- wetrace`),
  events with conditions (`-- festival`), traffic (`-- traffic`), the Highcrest hill climb
  (`-- highcrest`), discoveries and fast travel (`-- discover`) and the GPS (`-- gps`).
  Results are in `docs/PLAYTEST.md` (M6).
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
in Unity, with its origin on the ground at mid-wheelbase. `--glb DIR` also exports one LOD
(`--glb-lod`, default 1) as GLB for the web build; `node tools/pack-cars.mjs` does that for
every car and packs them into `src/vehicles/models/`.

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
- [x] Career: measured PI, parts, tuning, the dealer, saves, PI-matched rivals (M5)
- [x] The living island: day and night, weather and wet roads, traffic, the rebuilt
  Highcrest pass, landmarks, discoveries, the map with fast travel, and the GPS (M6;
  headless-tested, Unity compile-checked)
- [x] Everything else (M7): stunts, drag, rally, head-to-head, the ghost time trial,
  championships, levels and accolades, settings and accessibility, photo mode, the racing
  line (headless-tested, Unity compile-checked)
- [x] Polish (M8): drifting in the shared physics (the handbrake declutches, the automatic
  box upshifts in a long slide, loose ground), sound (engines, tires, wind, impacts, UI,
  three generative radio stations; the same synthesis as the web, tested against it), and
  a bug bash of the scripts (compile-checked, not yet heard or run)
- [ ] First run in a real Unity Editor: fix whatever only Unity reports, play the garage, a
  race, a night and a storm, a championship round and photo mode, listen to the cars and
  the radio, then look at screenshots
- [ ] CC0 PBR textures, a real water shader, Blender trees
