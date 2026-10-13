// Unity terrain tiles cut from the island grids, already flipped into
// Unity's frame (z negated). Pure C# so it is testable outside Unity; the
// Unity side copies the arrays into TerrainData.
//
// The island is 2560 x 2560 cells of 4 m. With 512-cell tiles that is 5 x 5
// Unity terrains of 2048 m, each with a 513 x 513 heightmap that lines up
// exactly with the physics grid (shared edge vertices between neighbours).
using System;

namespace Halcyon.Core
{
    public sealed class UnityTerrainTile
    {
        /// <summary>Normalized heights [row, col]: row runs along Unity +z, col along +x.</summary>
        public float[,] Heights;
        /// <summary>Terrain y origin and height range (TerrainData.size.y = YMax - YMin).</summary>
        public double YMin, YMax;
        /// <summary>Unity-frame position of the tile's (0, 0) corner and its size, m.</summary>
        public double X0, Z0, Size;
    }

    public static class TerrainTiles
    {
        /// <summary>Tiles per side for a given tile size in cells (512 -> 5).</summary>
        public static int TilesPerSide(WorldData w, int quads) => (w.N - 1) / quads;

        /// <summary>Web-grid row of Unity row r in Unity tile row tileZ.</summary>
        static int WebRow(WorldData w, int tileZ, int quads, int r) => (w.N - 1) - tileZ * quads - r;

        public static UnityTerrainTile UnityHeights(WorldData w, int tileX, int tileZ, int quads)
        {
            int res = quads + 1;
            var t = new UnityTerrainTile
            {
                Heights = new float[res, res],
                YMin = w.HMin,
                YMax = w.HMax,
                X0 = w.Origin + tileX * quads * w.Spacing,
                Z0 = w.Origin + tileZ * quads * w.Spacing,
                Size = quads * w.Spacing,
            };
            double inv = 1.0 / (w.HMax - w.HMin);
            for (int r = 0; r < res; r++)
            {
                int j = WebRow(w, tileZ, quads, r);
                for (int c = 0; c < res; c++)
                {
                    int i = tileX * quads + c;
                    t.Heights[r, c] = (float)((w.Height[j * w.N + i] - w.HMin) * inv);
                }
            }
            return t;
        }

        /// <summary>Layer order of UnityAlphamaps: grass, rock, dirt, sand, snow.</summary>
        public static readonly string[] Layers = { "grass", "rock", "dirt", "sand", "snow" };

        /// <summary>
        /// Splat weights for one tile at quads x quads texels (texel centres are cell
        /// centres), indexed [row, col, layer] in Layers order, summing to 1.
        /// </summary>
        public static float[,,] UnityAlphamaps(WorldData w, int tileX, int tileZ, int quads)
        {
            var a = new float[quads, quads, 5];
            int n = w.N;
            for (int r = 0; r < quads; r++)
            {
                // The cell between Unity rows r and r + 1 is web rows j - 1 .. j.
                int j1 = WebRow(w, tileZ, quads, r);
                int j0 = j1 - 1;
                for (int c = 0; c < quads; c++)
                {
                    int i0 = tileX * quads + c;
                    int k00 = j0 * n + i0, k10 = k00 + 1, k01 = k00 + n, k11 = k01 + 1;
                    float rock = Avg(w.SplatRock, k00, k10, k01, k11);
                    float dirt = Avg(w.SplatDirt, k00, k10, k01, k11);
                    float sand = Avg(w.SplatSand, k00, k10, k01, k11);
                    float snow = Avg(w.SplatSnow, k00, k10, k01, k11);
                    float sum = rock + dirt + sand + snow;
                    if (sum > 1)
                    {
                        rock /= sum;
                        dirt /= sum;
                        sand /= sum;
                        snow /= sum;
                        sum = 1;
                    }
                    a[r, c, 0] = 1 - sum;
                    a[r, c, 1] = rock;
                    a[r, c, 2] = dirt;
                    a[r, c, 3] = sand;
                    a[r, c, 4] = snow;
                }
            }
            return a;
        }

        static float Avg(byte[] g, int a, int b, int c, int d) => (g[a] + g[b] + g[c] + g[d]) / (4f * 255f);

        /// <summary>
        /// A per-texel attribute (forest, desert, region...) for a tile, [row, col],
        /// from the nearest web vertex, for detail/grass density maps.
        /// </summary>
        public static byte[,] UnityVertexMap(WorldData w, byte[] grid, int tileX, int tileZ, int quads)
        {
            var m = new byte[quads, quads];
            for (int r = 0; r < quads; r++)
            {
                int j = WebRow(w, tileZ, quads, r);
                for (int c = 0; c < quads; c++) m[r, c] = grid[j * w.N + tileX * quads + c];
            }
            return m;
        }
    }
}
