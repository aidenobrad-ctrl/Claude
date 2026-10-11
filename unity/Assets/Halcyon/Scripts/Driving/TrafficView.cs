// Traffic in Unity: a car rig for each traffic car in the simulation
// (Halcyon.Core.Traffic), posed from its vehicle whether it is a physics car
// or a ghost gliding along its lane, in everyday colours. Rigs come and go
// with the cars and are pooled by model, so a car appearing in the distance
// does not load a model mid-drive.
using System.Collections.Generic;
using Halcyon.Core;
using UnityEngine;

namespace Halcyon
{
    public sealed class TrafficView : MonoBehaviour
    {
        Simulation sim;
        readonly Dictionary<int, CarRig> rigs = new Dictionary<int, CarRig>();
        readonly Dictionary<string, Stack<CarRig>> pool = new Dictionary<string, Stack<CarRig>>();
        readonly HashSet<int> live = new HashSet<int>();
        readonly List<int> gone = new List<int>();

        static readonly Color[] Paints =
        {
            new Color(0.78f, 0.79f, 0.8f), new Color(0.92f, 0.92f, 0.9f), new Color(0.05f, 0.05f, 0.06f),
            new Color(0.1f, 0.16f, 0.32f), new Color(0.38f, 0.39f, 0.41f), new Color(0.42f, 0.06f, 0.07f),
            new Color(0.66f, 0.6f, 0.5f), new Color(0.12f, 0.24f, 0.17f), new Color(0.55f, 0.57f, 0.6f),
        };

        public static TrafficView Create(Transform parent, Simulation sim)
        {
            var go = new GameObject("Traffic");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TrafficView>();
            t.sim = sim;
            return t;
        }

        /// <summary>Record every traffic car's state after a simulation step.</summary>
        public void Capture()
        {
            if (sim.Traffic == null) return;
            Sync();
            foreach (var c in sim.Traffic.Cars)
                if (rigs.TryGetValue(c.Id, out var rig)) rig.Capture();
        }

        /// <summary>Pose the rigs between the last two steps.</summary>
        public void Apply(float alpha)
        {
            foreach (var r in rigs.Values) r.Apply(alpha);
        }

        /// <summary>A rig for each new car, and the rigs of cars that left back to the pool.</summary>
        void Sync()
        {
            live.Clear();
            foreach (var c in sim.Traffic.Cars)
            {
                live.Add(c.Id);
                if (rigs.ContainsKey(c.Id)) continue;
                var paint = Paints[(int)(c.Look * Paints.Length) % Paints.Length];
                CarRig rig;
                if (pool.TryGetValue(c.SpecId, out var stack) && stack.Count > 0)
                {
                    rig = stack.Pop();
                    rig.Rebind(c.Vehicle, paint);
                    rig.gameObject.SetActive(true);
                }
                else
                {
                    rig = CarRig.Create(c.Vehicle, c.SpecId, paint);
                    rig.transform.SetParent(transform, true);
                }
                rig.Snap();
                rigs[c.Id] = rig;
            }
            gone.Clear();
            foreach (var id in rigs.Keys) if (!live.Contains(id)) gone.Add(id);
            foreach (var id in gone)
            {
                var rig = rigs[id];
                rigs.Remove(id);
                rig.gameObject.SetActive(false);
                if (!pool.TryGetValue(rig.SpecId, out var stack)) pool[rig.SpecId] = stack = new Stack<CarRig>();
                stack.Push(rig);
            }
        }
    }
}
