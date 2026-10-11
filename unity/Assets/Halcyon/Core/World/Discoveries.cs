// What the player has found on the island:
//   - places (the festival, the city, the villages, the harbour and the
//     proving ground) and landmarks, found by driving near them, each
//     paying XP the first time (landmarks credits too);
//   - roads: every 4 m sample of the network driven over, with a reward at
//     each tenth of the network;
//   - the fog of war over the map: a 128 x 128 grid of the island, cleared
//     within 280 m of wherever the player has been.
// Fast travel goes to any place found, onto its nearest road. Everything
// is kept in the career profile (compact base64 bitsets). No UnityEngine.
using System;
using System.Collections.Generic;

namespace Halcyon.Core
{
    public sealed class Place
    {
        public string Id, Name, Kind;
        public double X, Y, Z, Radius;
        public bool Landmark;
        public int Xp, Credits;
    }

    public sealed class Discoveries
    {
        public const int FogN = 128;
        public const double FogRadius = 280;
        public const int MilestoneXp = 1000, MilestoneCredits = 5000;

        public readonly WorldData W;
        public readonly IslandGround Ground;
        public readonly Profile P;
        public readonly List<Place> Places = new List<Place>();
        public readonly List<Landmark> Landmarks;
        readonly int[] edgeStart;
        readonly int samples;
        readonly byte[] driven;
        readonly byte[] fog;
        readonly RoadHit hit = new RoadHit();
        double lastX = double.NaN, lastZ = double.NaN;
        public int DrivenSamples { get; private set; }
        public int FogCleared { get; private set; }

        /// <summary>The share of the road network driven, 0 to 1.</summary>
        public double RoadShare => samples > 0 ? (double)DrivenSamples / samples : 0;
        /// <summary>The share of the map uncovered, 0 to 1.</summary>
        public double MapShare => (double)FogCleared / (FogN * FogN);

        public Discoveries(WorldData w, IslandGround g, Profile p)
        {
            W = w;
            Ground = g;
            P = p;
            foreach (var poi in w.Pois)
            {
                double radius = poi.kind == "city" ? 600 : poi.kind == "festival" ? 350 : poi.kind == "track" ? 450 : 260;
                Places.Add(new Place { Id = "place:" + poi.name, Name = poi.name, Kind = poi.kind, X = poi.x, Y = poi.y, Z = poi.z, Radius = radius, Xp = poi.kind == "city" ? 600 : 300 });
            }
            Landmarks = Halcyon.Core.Landmarks.Find(w, g);
            foreach (var l in Landmarks)
                Places.Add(new Place { Id = l.Id, Name = l.Name, Kind = l.Kind, X = l.X, Y = l.Y, Z = l.Z, Radius = l.Radius, Landmark = true, Xp = 500, Credits = 2000 });
            edgeStart = new int[w.Edges.Length];
            for (int i = 0; i < w.Edges.Length; i++)
            {
                edgeStart[i] = samples;
                samples += w.Edges[i].n;
            }
            driven = Bits(p.RoadsDriven, samples);
            fog = Bits(p.Fog, FogN * FogN);
            for (int i = 0; i < samples; i++) if (Get(driven, i)) DrivenSamples++;
            for (int i = 0; i < FogN * FogN; i++) if (Get(fog, i)) FogCleared++;
        }

        static byte[] Bits(string saved, int count)
        {
            var b = new byte[(count + 7) / 8];
            if (string.IsNullOrEmpty(saved)) return b;
            try
            {
                var s = Convert.FromBase64String(saved);
                if (s.Length == b.Length) return s; // a different island (or version): start again
            }
            catch (FormatException)
            {
            }
            return b;
        }

        static bool Get(byte[] b, int i) => (b[i >> 3] & (1 << (i & 7))) != 0;

        static bool Set(byte[] b, int i)
        {
            if (Get(b, i)) return false;
            b[i >> 3] |= (byte)(1 << (i & 7));
            return true;
        }

        public bool Found(Place p) => P.Found.Contains(p.Id);

        public bool Uncovered(double x, double z)
        {
            int i = (int)Math.Floor((x + W.WorldHalf) / (2 * W.WorldHalf) * FogN), j = (int)Math.Floor((z + W.WorldHalf) / (2 * W.WorldHalf) * FogN);
            return i >= 0 && j >= 0 && i < FogN && j < FogN && Get(fog, j * FogN + i);
        }

        /// <summary>The fog grid, one byte per cell (255 uncovered): for the map's overlay.</summary>
        public void FogBytes(byte[] into)
        {
            for (int k = 0; k < FogN * FogN; k++) into[k] = Get(fog, k) ? (byte)255 : (byte)0;
        }

        /// <summary>
        /// The player is at (x, z) (call a few times a second while driving): clears the fog,
        /// marks the road driven, finds places, and pays rewards. Returns what to tell the player.
        /// </summary>
        public List<string> Update(double x, double z)
        {
            var notes = new List<string>();
            // Fog: only when the player has moved on a little.
            if (double.IsNaN(lastX) || (x - lastX) * (x - lastX) + (z - lastZ) * (z - lastZ) > 20 * 20)
            {
                lastX = x;
                lastZ = z;
                double cell = 2 * W.WorldHalf / FogN;
                int r = (int)Math.Ceiling(FogRadius / cell);
                int ci = (int)Math.Floor((x + W.WorldHalf) / cell), cj = (int)Math.Floor((z + W.WorldHalf) / cell);
                for (int j = cj - r; j <= cj + r; j++)
                    for (int i = ci - r; i <= ci + r; i++)
                    {
                        if (i < 0 || j < 0 || i >= FogN || j >= FogN) continue;
                        double dx = (i + 0.5) * cell - W.WorldHalf - x, dz = (j + 0.5) * cell - W.WorldHalf - z;
                        if (dx * dx + dz * dz > FogRadius * FogRadius) continue;
                        if (Set(fog, j * FogN + i)) FogCleared++;
                    }
            }
            // Roads: the sample under the car (and the next, so 4 m steps at speed leave no gaps).
            if (Ground.NearestRoad(x, z, 1, hit) && hit.dist <= hit.halfWidth + 1)
            {
                var e = W.Edges[hit.edge];
                for (int k = Math.Max(0, hit.i - 2); k <= Math.Min(e.n - 1, hit.i + 2); k++)
                    if (Set(driven, edgeStart[hit.edge] + k)) DrivenSamples++;
                // Each tenth of the network pays once.
                int tenths = (int)Math.Floor(10.0 * DrivenSamples / Math.Max(1, samples));
                while (P.RoadMilestones < tenths)
                {
                    P.RoadMilestones++;
                    P.Xp += MilestoneXp;
                    P.Credits += MilestoneCredits;
                    notes.Add($"{P.RoadMilestones * 10}% of the island's roads driven: +{MilestoneCredits:N0} cr, +{MilestoneXp:N0} XP");
                }
            }
            // Places and landmarks.
            foreach (var p in Places)
            {
                if (P.Found.Contains(p.Id)) continue;
                if ((p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z) > p.Radius * p.Radius) continue;
                P.Found.Add(p.Id);
                P.Xp += p.Xp;
                P.Credits += p.Credits;
                notes.Add($"Discovered {p.Name}: +{p.Xp:N0} XP{(p.Credits > 0 ? $", +{p.Credits:N0} cr" : "")}{(p.Landmark ? "" : " (fast travel unlocked)")}");
            }
            return notes;
        }

        /// <summary>Writes the bitsets into the profile (before saving it).</summary>
        public void Save()
        {
            P.RoadsDriven = Convert.ToBase64String(driven);
            P.Fog = Convert.ToBase64String(fog);
        }

        /// <summary>The places the player can fast travel to: found places (not landmarks), the festival always.</summary>
        public List<Place> FastTravelTargets()
        {
            var list = new List<Place>();
            foreach (var p in Places)
                if (!p.Landmark && (p.Kind == "festival" || P.Found.Contains(p.Id))) list.Add(p);
            return list;
        }

        /// <summary>Where fast travel to a place puts the car: its nearest road sample, facing along the road.</summary>
        public bool FastTravel(Place p, out double x, out double z, out double yaw)
        {
            x = z = yaw = 0;
            if (p.Kind == "track")
            {
                // The proving ground: its own paddock.
                x = W.Proving.spawnX;
                z = W.Proving.spawnZ;
                yaw = W.Proving.spawnYaw;
                return true;
            }
            double best = 1e18;
            foreach (var e in W.Edges)
            {
                for (int i = 0; i < e.n; i += 2)
                {
                    double d = (e.x[i] - p.X) * (e.x[i] - p.X) + (e.z[i] - p.Z) * (e.z[i] - p.Z);
                    if (d >= best || e.bridge[i] != 0) continue;
                    best = d;
                    // The right-hand lane, facing along the road.
                    double off = e.info.halfWidth * 0.5;
                    x = e.x[i] - e.tz[i] * off;
                    z = e.z[i] + e.tx[i] * off;
                    yaw = Simulation.YawOf(e.tx[i], e.tz[i]);
                }
            }
            return best < 1e18;
        }
    }
}
