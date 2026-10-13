// Loads the island (Resources/HalcyonIsland) once and owns the simulation's
// view of it: the ground the cars drive on and the static colliders.
using System;
using System.IO;
using Halcyon.Core;
using UnityEngine;

namespace Halcyon
{
    public sealed class IslandRuntime : MonoBehaviour
    {
        public static IslandRuntime Instance { get; private set; }

        public WorldData Data { get; private set; }
        public IslandGround Ground { get; private set; }
        public Colliders Colliders { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            if (Data == null) Load();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public static WorldData LoadData()
        {
            return WorldData.Load(name =>
            {
                string res = WorldData.ResourceFolder + "/" + Path.GetFileNameWithoutExtension(name);
                var ta = Resources.Load<TextAsset>(res);
                if (ta == null) throw new FileNotFoundException($"Island data missing: Resources/{res} ({name})");
                byte[] b = ta.bytes;
                Resources.UnloadAsset(ta);
                return b;
            });
        }

        public void Load()
        {
            var t = Time.realtimeSinceStartup;
            Data = LoadData();
            Ground = new IslandGround(Data);
            Colliders = Ground.BuildColliders();
            Debug.Log($"Halcyon: island loaded in {(Time.realtimeSinceStartup - t) * 1000:F0} ms ({Data.Edges.Length} roads, {Data.TreeCount} trees, {Data.Buildings.Length} buildings)");
        }

        /// <summary>Drivable surface height at a simulation-frame point (from above).</summary>
        public double SurfaceY(double x, double z) => Ground.GroundHeight(x, z);

        /// <summary>Drivable surface height under a Unity position.</summary>
        public float SurfaceY(Vector3 p) => (float)Ground.GroundHeight(p.x, -p.z);
    }
}
