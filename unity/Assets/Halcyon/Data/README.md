# Halcyon island data

The island, exported from the web version's deterministic world by
`tools/export-unity.ts` (`node tools/run-ts.mjs tools/export-unity.ts`). Don't edit
these files by hand; re-export. The files live in `Resources/HalcyonIsland/` so a
build can load them at runtime (`Resources.Load<TextAsset>("HalcyonIsland/height")`).
`Halcyon.Core.WorldData` (in `../Core/World/`) loads all of it; Unity code should use
that loader rather than parse the files itself.

## Coordinate frame

Everything here is in the web game's frame:

- x points east, y up, z south.
- A heading (yaw) of 0 faces **-z**, so cars drive "up" the map at yaw 0.
- Units are metres.

The simulation in `Halcyon.Core` runs in this frame too. **Unity is left-handed**, so
every position crosses over with z negated: `Unity (x, y, z) = (x, y, -z)`. For a
rotation quaternion, `Unity (qx, qy, qz, qw) = (-qx, -qy, qz, qw)`. A car's forward
(-z here) becomes Unity's +z forward, and its right (+x) stays +x.
`WorldData.ToUnity…` helpers do this; terrain helpers return arrays already flipped for
Unity.

The island fits in a square of side 2 × `worldHalf` = 10,240 m, centred on the origin.
Sea level is y = 0.

## Files

| File | What |
|---|---|
| `meta.json` | Everything small and human-readable: grid parameters, height range, spawn, map POIs, road classes, surfaces, tree/building kinds, all buildings, festival layout, proving-ground layout, counts. |
| `height.bytes` | Terrain heights on the 4 m physics grid (2561 × 2561 vertices). |
| `ground.bytes` | Per-vertex surface id, water level, region, forest and desert cover, distance to road. |
| `splat.bytes` | Per-vertex terrain layer weights: rock, dirt, sand, snow (grass is the rest). |
| `roads.bytes` | Road network nodes and edges (sampled every ~4 m, with heights, tangents, bridges), guardrail runs, and the proving-ground circuit. |
| `trees.bytes` | Every tree and bush (184,755), grouped by 64 m tile. |
| `colliders.bytes` | Static collision walls and posts: bridge parapets, guardrails, buildings, festival, proving ground. Tree trunks come from `trees.bytes`. |

### Binary container

Every `.bytes` file starts with a 16-byte little-endian header:

| Offset | Size | Field |
|---|---|---|
| 0 | 4 | magic (ASCII): `HLHT`, `HLGR`, `HLSP`, `HLRD`, `HLTR` or `HLCL` |
| 4 | 4 | format version (uint32, currently 1) |
| 8 | 4 | payload length after decompression (uint32) |
| 12 | 4 | flags (0) |

The header is followed by the payload, compressed with raw DEFLATE (no zlib
header; `System.IO.Compression.DeflateStream` reads it). All payload values are
little-endian.

### Grids

The grid has n = 2561 vertices per side, 4 m apart. Vertex (i, j) sits at:

- x = origin + 4·i
- z = origin + 4·j

The origin is -5120. Arrays are row-major: row j = z index, column i = x index, and
`index = j·n + i`.

The physics treats each 4 × 4 m cell as two triangles split along the
(i, j)–(i+1, j+1) diagonal. Within a cell (u = fx, v = fz in [0, 1)):

- if u ≥ v: y = h00 + (h10 − h00)·u + (h11 − h10)·v
- else: y = h00 + (h01 − h00)·v + (h11 − h01)·u

Per-vertex attributes (surface, water) use the nearest vertex.

**`height.bytes` (HLHT)** payload:

- int32 n, float32 spacing, float32 origin, float32 hmin, float32 hmax
- then n² uint16 values, stored as two byte planes: all low bytes, then all high bytes.

Each row is delta-coded:

- stored[0] = q[0]
- stored[i] = (q[i] − q[i−1]) mod 65536

The height is hmin + q·(hmax − hmin)/65535, a step of about 1.3 cm.

**`ground.bytes` (HLGR)** payload: int32 n, then these per-vertex arrays of n²
entries, in order:

1. uint8 surface id (see `meta.json` → `surfaces`)
2. int16 water surface height in cm. −32768 means no water. Stored as two byte planes
   like the heights, but not delta-coded. Water depth at a point = level − ground
   height when positive.
3. uint8 region id (`meta.json` → `regions`)
4. uint8 forest cover (0–255 ↔ 0–1)
5. uint8 desert cover (0–255 ↔ 0–1)
6. uint8 distance beyond the nearest road's paved edge, in decimetres (255 = 25.5 m or more)

**`splat.bytes` (HLSP)** payload: int32 n, then four uint8 planes of n² in this
order: rock, dirt, sand, snow (0–255 ↔ 0–1).

- grass = max(0, 1 − rock − dirt − sand − snow)
- Unity alphamap texels sit at cell centres, so average the four corner vertices.
  `WorldData` has a helper for this.

### `roads.bytes` (HLRD)

Strings are a uint8 byte length followed by UTF-8 bytes. The payload holds:

1. int32 nodeCount, then per node: string id, float32 x, y, z.
2. int32 classCount, then per class:
   - string id (`highway`, `main`, `pass`, `street`, `dirt`)
   - float32 halfWidth, shoulder, maxGrade, traffic (m/s)
   - uint8 surface, lanes, center (0 none, 1 dashed, 2 double, 3 solid), edgeLines, sidewalk
3. int32 edgeCount, then per edge:
   - uint8 class, uint16 nodeA, uint16 nodeB, int32 n, float32 length
   - then n float32 for each of x, y, z, tx, tz, s, base (terrain height before cut and fill)
   - then n uint8 bridge flags
   - Samples are ~4 m apart.
   - (tx, tz) is the unit tangent. "Right" of travel is (−tz, tx).
   - The paved half width is the class's halfWidth. The shoulder lies beyond it, either
     gravel or a raised sidewalk (CURB_H = 0.12 m) for `street`.
4. int32 railCount, then per guardrail run:
   - uint16 edge, int8 side (+1 right, −1 left), int32 i0, int32 i1 (inclusive), float32 offset
   - offset is the lateral distance of the rail from the centreline.
   - A retaining wall drops from the deck edge to the ground (foot = terrain height just
     outside the rail − 0.6 m).
   - The deck runs out to the rail.
5. The proving-ground circuit:
   - float32 originX, originZ, height, int32 n, float32 length, float32 startS
   - then n float32 for each of x, z, tx, tz, s
   - then n uint8 cornerKind (0 none, 1 kerb, 2 kerb + gravel trap) and n int8 outside
     (+1/−1, which side the gravel trap is on)

### `trees.bytes` (HLTR)

The payload starts with int32 tilesX, tilesZ (160 × 160), float32 tile (64), origin
(−5120), hmin, hmax. Then, for each 64 m tile in row-major order (tz major, tx minor):

- uint16 count
- per tree:
  - uint16 lx, lz: position in the tile, 0–65535 ↔ 0–64 m
  - uint16 y: same encoding as heights
  - uint8 scale: 0.6 + 0.8·v/255
  - uint8 yaw: v/256·2π
  - uint8 kind
  - uint8 rank: 0–255; lower = more important. Thin trees for lower quality by dropping
    high ranks first.

Kinds:

| id | kind |
|---|---|
| 0 | broadleaf |
| 1 | conifer |
| 2 | palm |
| 3 | cypress |
| 4 | bush (breakable) |
| 5 | dead |

Trunk collision radius at scale 1:

| Kind | Radius (m) |
|---|---|
| broadleaf | 0.38 |
| conifer | 0.32 |
| palm | 0.24 |
| cypress | 0.26 |
| bush | 0.9 |
| dead | 0.22 |

Each trunk spans y − 1 to y + 6.

### `colliders.bytes` (HLCL)

1. int32 segmentCount, then per segment:
   - float32 ax, az, bx, bz, top, bottom, bounce, friction
   - uint8 kind (0 wall, 1 barrier, 2 tires, 3 rail, 4 fence)
2. int32 circleCount, then per circle:
   - float32 x, z, r, top, bottom
   - uint8 kind (0 post, 1 tree, 2 rock, 3 pier, 4 building)
   - uint8 breakable

## meta.json highlights

- **Buildings:** `buildings` is a list of `[x, z, fx, fz, w, d, h, y0, floor, kind, roof, seed]`.
  - (fx, fz) is the unit direction the front faces (towards its road).
  - w runs along the front, d front to back, and h is the wall height above the floor.
  - y0 is the foundation bottom.
  - kind is from `buildingKinds`, roof from `roofKinds`.
  - seed is a stable random in [0, 1) for colours and details.
- **Festival:** `festival` has the stage, the big wheel, the entrance arch, tents and flag
  poles. Each piece has x, z, a facing (fx, fz), y, w, d, h and a 0xRRGGBB colour. It
  also holds the session `spawn`.
- **Spawn:** `spawn` is where a session starts: on the festival road, facing the arch.
  y is the ground height there.
- **POIs:** `pois` are the named places shown on the map.
