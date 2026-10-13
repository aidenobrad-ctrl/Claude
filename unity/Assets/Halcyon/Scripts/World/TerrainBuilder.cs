// The island's terrain: 5 x 5 Unity terrains of 2048 m whose 513^2
// heightmaps line up exactly with the physics grid, splatted with grass,
// rock, dirt, sand and snow from the exported weights.
using Halcyon.Core;
using UnityEngine;

namespace Halcyon
{
    public static class TerrainBuilder
    {
        public const int Quads = 512;

        public static Terrain[,] Build(WorldData w, Transform parent, TerrainLayer[] layers = null)
        {
            layers = layers ?? DefaultLayers();
            int n = TerrainTiles.TilesPerSide(w, Quads);
            var terrains = new Terrain[n, n];
            Material template = null;
            if (Mats.TerrainLit != null) template = new Material(Mats.TerrainLit) { name = "TerrainLit" };
            for (int tz = 0; tz < n; tz++)
            {
                for (int tx = 0; tx < n; tx++)
                {
                    var tile = TerrainTiles.UnityHeights(w, tx, tz, Quads);
                    var td = new TerrainData { name = $"Island_{tx}_{tz}" };
                    td.heightmapResolution = Quads + 1;
                    td.alphamapResolution = Quads;
                    td.baseMapResolution = 1024;
                    td.size = new Vector3((float)tile.Size, (float)(tile.YMax - tile.YMin), (float)tile.Size);
                    td.SetHeights(0, 0, tile.Heights);
                    td.terrainLayers = layers;
                    td.SetAlphamaps(0, 0, TerrainTiles.UnityAlphamaps(w, tx, tz, Quads));
                    var go = Terrain.CreateTerrainGameObject(td);
                    go.name = td.name;
                    go.transform.SetParent(parent, false);
                    go.transform.position = new Vector3((float)tile.X0, (float)tile.YMin, (float)tile.Z0);
                    var t = go.GetComponent<Terrain>();
                    if (template != null) t.materialTemplate = template;
                    t.heightmapPixelError = 3;
                    t.basemapDistance = 600;
                    t.drawInstanced = true;
                    t.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    t.treeDistance = 0;
                    t.detailObjectDistance = 0;
                    // The cars drive on Halcyon.Core's ground, not PhysX.
                    var col = go.GetComponent<TerrainCollider>();
                    if (col != null) col.enabled = false;
                    terrains[tx, tz] = t;
                }
            }
            for (int tz = 0; tz < n; tz++)
            {
                for (int tx = 0; tx < n; tx++)
                {
                    Terrain left = tx > 0 ? terrains[tx - 1, tz] : null;
                    Terrain right = tx < n - 1 ? terrains[tx + 1, tz] : null;
                    Terrain bottom = tz > 0 ? terrains[tx, tz - 1] : null;
                    Terrain top = tz < n - 1 ? terrains[tx, tz + 1] : null;
                    terrains[tx, tz].SetNeighbors(left, top, right, bottom);
                }
            }
            return terrains;
        }

        /// <summary>Grass, rock, dirt, sand, snow (TerrainTiles.Layers order) from procedural textures.</summary>
        public static TerrainLayer[] DefaultLayers()
        {
            TerrainLayer Layer(string name, Texture2D a, Texture2D nrm, float tile, float smooth)
            {
                return new TerrainLayer
                {
                    name = name,
                    diffuseTexture = a,
                    normalMapTexture = nrm,
                    tileSize = new Vector2(tile, tile),
                    normalScale = 1f,
                    smoothness = smooth,
                    metallic = 0f,
                };
            }
            ProceduralTextures.Grass(out var ga, out var gn);
            ProceduralTextures.Rock(out var ra, out var rn);
            ProceduralTextures.Dirt(out var da, out var dn);
            ProceduralTextures.Sand(out var sa, out var sn);
            ProceduralTextures.Snow(out var na, out var nn);
            return new[]
            {
                Layer("Grass", ga, gn, 9f, 0.05f),
                Layer("Rock", ra, rn, 14f, 0.12f),
                Layer("Dirt", da, dn, 8f, 0.08f),
                Layer("Sand", sa, sn, 10f, 0.15f),
                Layer("Snow", na, nn, 12f, 0.35f),
            };
        }
    }
}
