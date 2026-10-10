# Halcyon in Unity + Blender

The Unity version of Halcyon. It reuses the web version's island and driving physics
exactly, and adds Unity rendering and Blender-made assets on top.

## Layout

| Path | What | Owner |
|---|---|---|
| `tools/export-unity.ts` | Exports the island from the web version's deterministic world | cloud session |
| `unity/Assets/Halcyon/Data/` | The exported island (see its README for every format) | cloud session |
| `unity/Assets/Halcyon/Core/` | Pure C# simulation (`Halcyon.Core`, no UnityEngine): math, ground, colliders, island loader, vehicle physics | cloud session |
| `unity/Assets/Halcyon/Scripts/Driving/` | Unity glue: car controller, chase camera, input | cloud session |
| `dotnet/` | Builds and tests `Core` outside Unity (.NET 8; netstandard2.1 + C# 9 like Unity) | cloud session |
| `unity/` project settings, scenes, rendering, world builders, UI | The Unity project itself | PC session |
| `blender/`, `unity/Assets/Halcyon/Art/` | Blender scripts and the models/textures they export | PC session |

The PC session's brief is `docs/UNITY-PC-BRIEF.md`.

## Coordinate frames

`Core` runs in the web game's right-handed frame:

- x is east, y is up, z is south.
- Yaw 0 faces -z.

Unity is left-handed, so positions cross over as `(x, y, -z)` and rotations as
`(-qx, -qy, qz, qw)`. Use `WorldData.ToUnity`. `TerrainTiles` returns heightmaps and
alphamaps already flipped.

## Terrain

The island is 10,240 m square, on a 4 m grid of 2561 × 2561 vertices. In Unity that is
5 × 5 terrains of 2048 m, each with a 513 × 513 heightmap and 512 × 512 alphamaps
(`TerrainTiles.UnityHeights` / `UnityAlphamaps`).

- The tiles share edge vertices, so they line up exactly with the physics grid.
- Use one height range for every tile: `WorldData.HMin` to `WorldData.HMax`, which is
  −44 to 811 m.
- Alphamap layers, in order: grass, rock, dirt, sand, snow.

## Determinism and equivalence

`Core` is a line-by-line port of the TypeScript simulation:

- doubles throughout
- the same operation order
- the deterministic `DMath` trig in place of `System.Math`

Equivalence is tested against the TypeScript:

- `dotnet/Halcyon.World.Tests` checks ground heights, normals, surfaces and water, nearest
  road, tree trunks, colliders, and the Unity tile flips. It compares with 13,119 ground
  samples and 4,000 road queries from `tools/dev/world-ref.ts`. Heights agree to 0.6 cm
  (99.9th percentile); the terrain is stored as 16-bit, as Unity does.
- `dotnet/Halcyon.Core.Tests` checks the vehicle physics against reference traces from
  `tools/dev/vehicle-ref.ts`.

To run them:

```sh
node tools/run-ts.mjs tools/export-unity.ts    # re-export the island (31 s)
node tools/run-ts.mjs tools/dev/world-ref.ts   # refresh the world references
node tools/unity-meta.mjs                      # stable .meta files for new assets
cd dotnet/Halcyon.Core && dotnet build         # Unity-level compile check
cd ../Halcyon.World.Tests && dotnet run -c Release
cd ../Halcyon.Core.Tests && dotnet run -c Release
```

## .meta files

`tools/unity-meta.mjs` writes a `.meta` with a GUID derived from the file's path for
everything under `unity/Assets/Halcyon` that lacks one. That keeps GUIDs identical on
every machine, so Unity never has to invent them and git never sees conflicting metas.
Run it before committing new files.

## Status

- [x] Island export: terrain, ground attributes, splat, roads, guardrails, proving circuit,
  trees, colliders, buildings, festival, spawn, POIs
- [x] Core: deterministic math, RNG, surfaces, ground, colliders, island loader, ground
  sampling, Unity terrain tiles
- [ ] Core: vehicle physics port (in progress, with equivalence tests)
- [ ] Unity: driving glue (car controller, chase camera, input)
- [ ] Unity project, rendering, world builders (PC session)
- [ ] Blender: cars, trees, props (PC session)
