using System.Collections.Generic;
using UnityEngine;

namespace Pulsebreak.Shared.Match
{
    public enum ArenaPaint : byte
    {
        Floor,
        Wall,
        Cover,
        Accent,
    }

    /// <summary>One solid, axis-aligned block of level.</summary>
    public struct ArenaBox
    {
        public Vector3 Center;
        public Vector3 Size;
        public ArenaPaint Paint;
    }

    public struct ArenaSpawn
    {
        /// <summary>Feet position.</summary>
        public Vector3 Position;
        public float Yaw;
    }

    /// <summary>A capture zone: a circle on the ground, and a short height above it that counts as inside.</summary>
    public struct ArenaZone
    {
        public string Name;
        public Vector3 Center;
        public float Radius;

        /// <summary>True if a player standing with their feet here is inside the zone.</summary>
        public bool Contains(Vector3 feet)
        {
            float dx = feet.x - Center.x, dz = feet.z - Center.z;
            return dx * dx + dz * dz <= Radius * Radius && feet.y > Center.y - 1f && feet.y < Center.y + 2.5f;
        }
    }

    /// <summary>
    /// A level described as data: solid blocks, spawn points and capture zones. The server and
    /// every client build their collision from the same description, which is what lets a
    /// client predict its own movement and get the answer the server gets.
    ///
    /// Levels are blocks in code for now. When real maps arrive (a later phase) this becomes
    /// the collision exported from a scene, and nothing that reads it has to change.
    /// </summary>
    public sealed class ArenaLayout
    {
        public string Id;
        public string DisplayName;
        public ArenaBox[] Boxes;
        /// <summary>Two sides, one per team. Each side has a few places to start from.</summary>
        public ArenaSpawn[][] Sides;
        /// <summary>Places to (re)appear in modes without teams. Every one faces into the map.</summary>
        public ArenaSpawn[] FreeSpawns;
        /// <summary>Domination's zones, in letter order. Empty on maps that do not offer it.</summary>
        public ArenaZone[] Zones = new ArenaZone[0];
        /// <summary>Hardpoint's zones, in the order they come into play. Empty on maps that do not offer it.</summary>
        public ArenaZone[] Hardpoints = new ArenaZone[0];
        /// <summary>The most players the map is meant for.</summary>
        public int MaxPlayers;

        public const string DefaultId = "relay_yard";
        public const string CrossroadsId = "crossroads";
        public const string KilnId = "kiln";
        public const string SaltworksId = "saltworks";
        public const string LatticeId = "lattice";

        /// <summary>Every map, in the order they are offered.</summary>
        public static readonly string[] Ids = { DefaultId, KilnId, SaltworksId, CrossroadsId, LatticeId };

        public static ArenaLayout Find(string id)
        {
            switch (id)
            {
                case CrossroadsId: return Crossroads();
                case KilnId: return Kiln();
                case SaltworksId: return Saltworks();
                case LatticeId: return Lattice();
                default: return RelayYard();       // anything unknown falls back to the first map
            }
        }

        /// <summary>The map's display name without building it.</summary>
        public static string Title(string id)
        {
            switch (id)
            {
                case CrossroadsId: return "Crossroads";
                case KilnId: return "Kiln";
                case SaltworksId: return "Saltworks";
                case LatticeId: return "Lattice";
                default: return "Relay Yard";
            }
        }

        /// <summary>
        /// Every map that can hold a match with these rules, for matchmaking to pick from.
        /// The result is in the order maps are offered.
        /// </summary>
        public static List<string> SupportingIds(MatchSettings settings)
        {
            var ids = new List<string>();
            foreach (string id in Ids)
            {
                if (Find(id).Supports(settings)) ids.Add(id);
            }
            return ids;
        }

        /// <summary>
        /// Where a point is, in words a teammate would use: "on B" or "at North gantry" near a
        /// zone, "in the middle", or a compass direction ("north-west").
        /// </summary>
        public string PlaceName(Vector3 point)
        {
            foreach (ArenaZone zone in Zones)
            {
                if (Distance(zone.Center, point) < zone.Radius + 3f) return "on " + zone.Name;
            }
            foreach (ArenaZone zone in Hardpoints)
            {
                if (Distance(zone.Center, point) < zone.Radius + 3f) return "at " + zone.Name;
            }
            if (Distance(Vector3.zero, point) < 6f) return "in the middle";
            float angle = Mathf.Atan2(point.x, point.z) * Mathf.Rad2Deg;
            int sector = Mathf.RoundToInt(Mathf.Repeat(angle, 360f) / 45f) % 8;
            return Compass[sector];
        }

        static readonly string[] Compass = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };

        static float Distance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        /// <summary>True if the map has what a mode needs: zones for Domination and Hardpoint, room for the players.</summary>
        public bool Supports(MatchSettings settings)
        {
            if (settings.mode == GameModeKind.Domination && Zones.Length == 0) return false;
            if (settings.mode == GameModeKind.Hardpoint && Hardpoints.Length == 0) return false;
            return settings.Capacity <= MaxPlayers;
        }

        /// <summary>
        /// Relay Yard: a walled yard, 38 m by 26 m, for duels and small fights. A tall block in
        /// the middle hides the two starts from each other; either side of it there is a lane
        /// with a crate to vault, a wall to peek round and a low barrier to crouch behind. Each
        /// end has a raised deck reached by steps or a mantle. Symmetric through the centre.
        /// </summary>
        public static ArenaLayout RelayYard()
        {
            var boxes = new List<ArenaBox>();

            // Ground and the four walls around it.
            Add(boxes, new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 28f), ArenaPaint.Floor);
            Add(boxes, new Vector3(0f, 2.5f, 13.5f), new Vector3(40f, 5f, 1f), ArenaPaint.Wall);
            Add(boxes, new Vector3(0f, 2.5f, -13.5f), new Vector3(40f, 5f, 1f), ArenaPaint.Wall);
            Add(boxes, new Vector3(19.5f, 2.5f, 0f), new Vector3(1f, 5f, 28f), ArenaPaint.Wall);
            Add(boxes, new Vector3(-19.5f, 2.5f, 0f), new Vector3(1f, 5f, 28f), ArenaPaint.Wall);

            // The block in the middle.
            Add(boxes, new Vector3(0f, 1.75f, 0f), new Vector3(4f, 3.5f, 6f), ArenaPaint.Accent);

            // Everything below is added twice: once as written, once mirrored through the centre.
            Mirrored(boxes, new Vector3(0f, 0.6f, 7.5f), new Vector3(3f, 1.2f, 1.6f), ArenaPaint.Cover);      // crate to vault
            Mirrored(boxes, new Vector3(-8f, 1.1f, 4f), new Vector3(1f, 2.2f, 5f), ArenaPaint.Wall);          // wall to peek round
            Mirrored(boxes, new Vector3(-6.5f, 0.5f, -3f), new Vector3(4f, 1f, 1f), ArenaPaint.Cover);        // low barrier
            Mirrored(boxes, new Vector3(-4f, 1.5f, 10.5f), new Vector3(1.2f, 3f, 1.2f), ArenaPaint.Accent);   // pillar

            // A raised deck, with three steps up its inner side.
            Mirrored(boxes, new Vector3(-14f, 0.6f, -8f), new Vector3(6f, 1.2f, 6f), ArenaPaint.Cover);
            Mirrored(boxes, new Vector3(-10.7f, 0.45f, -8f), new Vector3(0.6f, 0.9f, 3f), ArenaPaint.Floor);
            Mirrored(boxes, new Vector3(-10.1f, 0.3f, -8f), new Vector3(0.6f, 0.6f, 3f), ArenaPaint.Floor);
            Mirrored(boxes, new Vector3(-9.5f, 0.15f, -8f), new Vector3(0.6f, 0.3f, 3f), ArenaPaint.Floor);

            ArenaSpawn[] west =
            {
                At(-17f, 0f, 9f, 100f),
                At(-17f, 0f, 1.5f, 90f),
                At(-15f, 1.2f, -8f, 80f),
            };
            ArenaSpawn[] east = Mirror(west);
            var free = new List<ArenaSpawn>(west);
            free.AddRange(east);
            free.Add(At(0f, 0f, 11f, 180f));
            free.Add(At(0f, 0f, -11f, 0f));

            return new ArenaLayout
            {
                Id = DefaultId,
                DisplayName = "Relay Yard",
                Boxes = boxes.ToArray(),
                Sides = new[] { west, east },
                FreeSpawns = free.ToArray(),
                MaxPlayers = 6,
            };
        }

        /// <summary>
        /// Crossroads: 64 m by 48 m, for teams of up to five. Three lanes run between the two
        /// starts, split by walls at the quarter lines; the middle one crosses an open square
        /// with four crates. Each start has a raised deck and a low wall to fight from. Zones A
        /// and C sit in front of each start, B in the square. The hardpoint moves between the
        /// square and the alleys along the north and south walls. Symmetric through the centre.
        /// </summary>
        public static ArenaLayout Crossroads()
        {
            var boxes = new List<ArenaBox>();

            Add(boxes, new Vector3(0f, -0.5f, 0f), new Vector3(68f, 1f, 52f), ArenaPaint.Floor);
            Add(boxes, new Vector3(0f, 3f, 24.5f), new Vector3(66f, 6f, 1f), ArenaPaint.Wall);
            Add(boxes, new Vector3(0f, 3f, -24.5f), new Vector3(66f, 6f, 1f), ArenaPaint.Wall);
            Add(boxes, new Vector3(32.5f, 3f, 0f), new Vector3(1f, 6f, 50f), ArenaPaint.Wall);
            Add(boxes, new Vector3(-32.5f, 3f, 0f), new Vector3(1f, 6f, 50f), ArenaPaint.Wall);

            // The square: four crates around zone B.
            Mirrored(boxes, new Vector3(3.5f, 0.6f, 3.5f), new Vector3(1.6f, 1.2f, 1.6f), ArenaPaint.Cover);
            Mirrored(boxes, new Vector3(3.5f, 0.6f, -3.5f), new Vector3(1.6f, 1.2f, 1.6f), ArenaPaint.Cover);

            // Walls along the quarter lines split the map into three lanes.
            Mirrored(boxes, new Vector3(-10f, 1.5f, 8f), new Vector3(8f, 3f, 1f), ArenaPaint.Wall);
            Mirrored(boxes, new Vector3(-10f, 1.5f, -8f), new Vector3(8f, 3f, 1f), ArenaPaint.Wall);

            // Cover in the north and south lanes, and pillars to break up long sightlines.
            Mirrored(boxes, new Vector3(0f, 1.1f, 15f), new Vector3(8f, 2.2f, 1f), ArenaPaint.Cover);
            Mirrored(boxes, new Vector3(-12f, 2f, 17f), new Vector3(1.5f, 4f, 1.5f), ArenaPaint.Accent);
            Mirrored(boxes, new Vector3(-12f, 2f, -17f), new Vector3(1.5f, 4f, 1.5f), ArenaPaint.Accent);
            Mirrored(boxes, new Vector3(-20f, 1f, 19f), new Vector3(4f, 2f, 3f), ArenaPaint.Cover);

            // Each start: a low wall to fight from, a barrier in front of its zone, and a raised deck with steps.
            Mirrored(boxes, new Vector3(-25f, 0.6f, 0f), new Vector3(1f, 1.2f, 8f), ArenaPaint.Cover);
            Mirrored(boxes, new Vector3(-13f, 0.5f, 0f), new Vector3(1f, 1f, 4f), ArenaPaint.Cover);
            Mirrored(boxes, new Vector3(-24f, 0.6f, -15f), new Vector3(6f, 1.2f, 6f), ArenaPaint.Cover);
            Mirrored(boxes, new Vector3(-20.7f, 0.45f, -15f), new Vector3(0.6f, 0.9f, 3f), ArenaPaint.Floor);
            Mirrored(boxes, new Vector3(-20.1f, 0.3f, -15f), new Vector3(0.6f, 0.6f, 3f), ArenaPaint.Floor);
            Mirrored(boxes, new Vector3(-19.5f, 0.15f, -15f), new Vector3(0.6f, 0.3f, 3f), ArenaPaint.Floor);

            ArenaSpawn[] west =
            {
                At(-29f, 0f, -8f, 90f),
                At(-29f, 0f, -3f, 90f),
                At(-29f, 0f, 3f, 90f),
                At(-29f, 0f, 8f, 90f),
                At(-24f, 1.2f, -15f, 70f),
                At(-27f, 0f, 15f, 100f),
            };
            ArenaSpawn[] east = Mirror(west);
            var free = new List<ArenaSpawn>(west);
            free.AddRange(east);
            free.Add(At(0f, 0f, 20f, 180f));
            free.Add(At(0f, 0f, -20f, 0f));
            free.Add(At(-8f, 0f, 0f, 90f));
            free.Add(At(8f, 0f, 0f, 270f));

            return new ArenaLayout
            {
                Id = CrossroadsId,
                DisplayName = "Crossroads",
                Boxes = boxes.ToArray(),
                Sides = new[] { west, east },
                FreeSpawns = free.ToArray(),
                Zones = new[]
                {
                    new ArenaZone { Name = "A", Center = new Vector3(-17f, 0f, 0f), Radius = 3.5f },
                    new ArenaZone { Name = "B", Center = Vector3.zero, Radius = 4f },
                    new ArenaZone { Name = "C", Center = new Vector3(17f, 0f, 0f), Radius = 3.5f },
                },
                Hardpoints = new[]
                {
                    new ArenaZone { Name = "Square", Center = Vector3.zero, Radius = 4f },
                    new ArenaZone { Name = "North alley", Center = new Vector3(0f, 0f, 20f), Radius = 3f },
                    new ArenaZone { Name = "South alley", Center = new Vector3(0f, 0f, -20f), Radius = 3f },
                },
                MaxPlayers = 12,
            };
        }

        /// <summary>
        /// Kiln: a small square arena, 32 m by 24 m, for duels and two against two. A pillar in
        /// the middle ringed by knee-high walls; long walls either side make two lanes; each
        /// start has a raised ledge with stairs. Symmetric through the centre.
        /// </summary>
        public static ArenaLayout Kiln()
        {
            var boxes = new List<ArenaBox>();
            Add(boxes, new Vector3(0f, -0.5f, 0f), new Vector3(34f, 1f, 26f), ArenaPaint.Floor);
            Add(boxes, new Vector3(0f, 2.5f, 12.5f), new Vector3(34f, 5f, 1f), ArenaPaint.Wall);
            Add(boxes, new Vector3(0f, 2.5f, -12.5f), new Vector3(34f, 5f, 1f), ArenaPaint.Wall);
            Add(boxes, new Vector3(16.5f, 2.5f, 0f), new Vector3(1f, 5f, 26f), ArenaPaint.Wall);
            Add(boxes, new Vector3(-16.5f, 2.5f, 0f), new Vector3(1f, 5f, 26f), ArenaPaint.Wall);

            // The pillar, and the low ring round it.
            Add(boxes, new Vector3(0f, 1.5f, 0f), new Vector3(1.6f, 3f, 1.6f), ArenaPaint.Accent);
            Mirrored(boxes, new Vector3(3.5f, 0.55f, 2.5f), new Vector3(3f, 1.1f, 0.5f), ArenaPaint.Cover);
            Mirrored(boxes, new Vector3(-3.5f, 0.55f, 2.5f), new Vector3(3f, 1.1f, 0.5f), ArenaPaint.Cover);

            // Long walls that make the lanes, and crates.
            Mirrored(boxes, new Vector3(-7f, 1.5f, 6.5f), new Vector3(1f, 3f, 7f), ArenaPaint.Wall);
            Mirrored(boxes, new Vector3(-4f, 0.5f, -8f), new Vector3(1.2f, 1f, 1.2f), ArenaPaint.Cover);
            Mirrored(boxes, new Vector3(-10.5f, 0.6f, 2f), new Vector3(1.6f, 1.2f, 1.6f), ArenaPaint.Cover);

            // A ledge at each start, with stairs up its inner side.
            Mirrored(boxes, new Vector3(-13.5f, 0.6f, -7f), new Vector3(5f, 1.2f, 8f), ArenaPaint.Cover);
            MirroredSteps(boxes, -11f, -7f, -1f, 0f, 1.2f, 3f);

            ArenaSpawn[] west = { At(-14f, 0f, 4f, 90f), At(-14f, 0f, 9f, 100f), At(-13.5f, 1.2f, -7f, 80f) };
            ArenaSpawn[] east = Mirror(west);
            var free = new List<ArenaSpawn>(west);
            free.AddRange(east);
            free.Add(At(0f, 0f, 9.5f, 180f));
            free.Add(At(0f, 0f, -9.5f, 0f));

            return new ArenaLayout
            {
                Id = KilnId,
                DisplayName = "Kiln",
                Boxes = boxes.ToArray(),
                Sides = new[] { west, east },
                FreeSpawns = free.ToArray(),
                MaxPlayers = 4,
            };
        }

        /// <summary>
        /// Saltworks: 50 m by 40 m, for teams of up to four. Two roofed sheds face each other
        /// across a square of shallow pans, with doors on three sides. A raised gantry runs
        /// along the north and south walls; shipping crates and barriers break up the open
        /// ground. Zones A and C are inside the sheds, B among the pans; the hardpoint moves
        /// between the pans and the two gantries. Symmetric through the centre.
        /// </summary>
        public static ArenaLayout Saltworks()
        {
            var boxes = new List<ArenaBox>();
            Add(boxes, new Vector3(0f, -0.5f, 0f), new Vector3(52f, 1f, 42f), ArenaPaint.Floor);
            Add(boxes, new Vector3(0f, 3f, 20.5f), new Vector3(52f, 6f, 1f), ArenaPaint.Wall);
            Add(boxes, new Vector3(0f, 3f, -20.5f), new Vector3(52f, 6f, 1f), ArenaPaint.Wall);
            Add(boxes, new Vector3(25.5f, 3f, 0f), new Vector3(1f, 6f, 42f), ArenaPaint.Wall);
            Add(boxes, new Vector3(-25.5f, 3f, 0f), new Vector3(1f, 6f, 42f), ArenaPaint.Wall);

            // The sheds: doors to the pans and to the north and south.
            MirroredRoom(boxes, -14f, 0f, 9f, 10f, 3.4f, north: true, south: true, east: true, west: false);

            // The pans: knee-high rims round the middle, and two tanks.
            Mirrored(boxes, new Vector3(-2.5f, 0.25f, 4.5f), new Vector3(4f, 0.5f, 0.4f), ArenaPaint.Cover);
            Mirrored(boxes, new Vector3(5f, 0.25f, 2f), new Vector3(0.4f, 0.5f, 4f), ArenaPaint.Cover);
            Mirrored(boxes, new Vector3(-2.5f, 1f, -4.5f), new Vector3(1.6f, 2f, 1.6f), ArenaPaint.Accent);

            // The gantries, with stairs down towards the middle.
            Mirrored(boxes, new Vector3(-6f, 0.6f, 17.5f), new Vector3(16f, 1.2f, 5f), ArenaPaint.Cover);
            MirroredSteps(boxes, -6f, 15f, 0f, 1f, 1.2f, 3f);
            MirroredSteps(boxes, -14f, 17.5f, 1f, 0f, 1.2f, 3f);

            // Shipping crates and barriers.
            Mirrored(boxes, new Vector3(-20f, 1.3f, 12f), new Vector3(2.6f, 2.6f, 6f), ArenaPaint.Cover);
            Mirrored(boxes, new Vector3(8f, 1.3f, 9f), new Vector3(6f, 2.6f, 2.6f), ArenaPaint.Cover);
            Mirrored(boxes, new Vector3(-8f, 0.55f, -13f), new Vector3(4f, 1.1f, 0.5f), ArenaPaint.Cover);
            Mirrored(boxes, new Vector3(-21f, 0.6f, -9f), new Vector3(1.6f, 1.2f, 1.6f), ArenaPaint.Cover);

            ArenaSpawn[] west =
            {
                At(-23f, 0f, -12f, 70f), At(-23f, 0f, -6f, 90f), At(-23f, 0f, 6f, 90f), At(-23f, 0f, 12f, 110f),
            };
            ArenaSpawn[] east = Mirror(west);
            var free = new List<ArenaSpawn>(west);
            free.AddRange(east);
            free.Add(At(0f, 0f, 12.5f, 180f));
            free.Add(At(0f, 0f, -12.5f, 0f));
            free.Add(At(-10f, 0f, 9f, 135f));
            free.Add(At(10f, 0f, -9f, 315f));

            return new ArenaLayout
            {
                Id = SaltworksId,
                DisplayName = "Saltworks",
                Boxes = boxes.ToArray(),
                Sides = new[] { west, east },
                FreeSpawns = free.ToArray(),
                Zones = new[]
                {
                    new ArenaZone { Name = "A", Center = new Vector3(-14f, 0f, 0f), Radius = 3f },
                    new ArenaZone { Name = "B", Center = Vector3.zero, Radius = 3.5f },
                    new ArenaZone { Name = "C", Center = new Vector3(14f, 0f, 0f), Radius = 3f },
                },
                Hardpoints = new[]
                {
                    new ArenaZone { Name = "Pans", Center = Vector3.zero, Radius = 3.5f },
                    new ArenaZone { Name = "North gantry", Center = new Vector3(-6f, 1.2f, 17.5f), Radius = 2.5f },
                    new ArenaZone { Name = "South gantry", Center = new Vector3(6f, 1.2f, -17.5f), Radius = 2.5f },
                },
                MaxPlayers = 8,
            };
        }

        /// <summary>
        /// Lattice: 66 m by 50 m, for teams of five. A grid of tall blocks with streets between
        /// them; the block in the middle of each half is hollow, with a door at each end, so the
        /// centre lane runs through it. In the very middle a raised plaza with stairs on every
        /// side. Zones A and C are inside the hollow blocks, B on the plaza; the hardpoint moves
        /// between the plaza and the north and south streets. Symmetric through the centre.
        /// </summary>
        public static ArenaLayout Lattice()
        {
            var boxes = new List<ArenaBox>();
            Add(boxes, new Vector3(0f, -0.5f, 0f), new Vector3(68f, 1f, 52f), ArenaPaint.Floor);
            Add(boxes, new Vector3(0f, 3f, 25.5f), new Vector3(68f, 6f, 1f), ArenaPaint.Wall);
            Add(boxes, new Vector3(0f, 3f, -25.5f), new Vector3(68f, 6f, 1f), ArenaPaint.Wall);
            Add(boxes, new Vector3(33.5f, 3f, 0f), new Vector3(1f, 6f, 52f), ArenaPaint.Wall);
            Add(boxes, new Vector3(-33.5f, 3f, 0f), new Vector3(1f, 6f, 52f), ArenaPaint.Wall);

            // The blocks: solid at the corners and outer middles, hollow in the centre lane.
            Mirrored(boxes, new Vector3(-24f, 2f, 14f), new Vector3(7f, 4f, 7f), ArenaPaint.Wall);
            Mirrored(boxes, new Vector3(-24f, 2f, -14f), new Vector3(7f, 4f, 7f), ArenaPaint.Wall);
            Mirrored(boxes, new Vector3(-12f, 2f, 14f), new Vector3(7f, 4f, 7f), ArenaPaint.Wall);
            Mirrored(boxes, new Vector3(-12f, 2f, -14f), new Vector3(7f, 4f, 7f), ArenaPaint.Wall);
            Mirrored(boxes, new Vector3(-24f, 2f, 0f), new Vector3(7f, 4f, 7f), ArenaPaint.Wall);
            MirroredRoom(boxes, -12f, 0f, 7f, 7f, 3.4f, north: false, south: false, east: true, west: true);

            // The plaza, with stairs in the middle of each side and low walls on top.
            Add(boxes, new Vector3(0f, 0.6f, 0f), new Vector3(10f, 1.2f, 10f), ArenaPaint.Cover);
            MirroredSteps(boxes, -5f, 0f, 1f, 0f, 1.2f, 3f);
            MirroredSteps(boxes, 0f, -5f, 0f, 1f, 1.2f, 3f);
            Mirrored(boxes, new Vector3(-3f, 1.65f, 3f), new Vector3(2.4f, 0.9f, 0.5f), ArenaPaint.Accent);
            Mirrored(boxes, new Vector3(3f, 1.65f, 3f), new Vector3(0.5f, 0.9f, 2.4f), ArenaPaint.Accent);

            // Cover in the streets.
            Mirrored(boxes, new Vector3(-18f, 0.6f, 7f), new Vector3(1.6f, 1.2f, 1.6f), ArenaPaint.Cover);
            Mirrored(boxes, new Vector3(-6f, 0.55f, 12f), new Vector3(4f, 1.1f, 0.5f), ArenaPaint.Cover);
            Mirrored(boxes, new Vector3(-18f, 1f, -20f), new Vector3(1.4f, 2f, 1.4f), ArenaPaint.Accent);
            Mirrored(boxes, new Vector3(-29f, 0.6f, 7f), new Vector3(2f, 1.2f, 1.2f), ArenaPaint.Cover);

            ArenaSpawn[] west =
            {
                At(-31f, 0f, -20f, 60f), At(-31f, 0f, -9f, 90f), At(-31f, 0f, -3f, 90f),
                At(-31f, 0f, 3f, 90f), At(-31f, 0f, 11f, 100f), At(-31f, 0f, 20f, 120f),
            };
            ArenaSpawn[] east = Mirror(west);
            var free = new List<ArenaSpawn>(west);
            free.AddRange(east);
            free.Add(At(0f, 0f, 21f, 180f));
            free.Add(At(0f, 0f, -21f, 0f));
            free.Add(At(-18f, 0f, -7f, 90f));
            free.Add(At(18f, 0f, 7f, 270f));

            return new ArenaLayout
            {
                Id = LatticeId,
                DisplayName = "Lattice",
                Boxes = boxes.ToArray(),
                Sides = new[] { west, east },
                FreeSpawns = free.ToArray(),
                Zones = new[]
                {
                    new ArenaZone { Name = "A", Center = new Vector3(-12f, 0f, 0f), Radius = 2.5f },
                    new ArenaZone { Name = "B", Center = new Vector3(0f, 1.2f, 0f), Radius = 3f },
                    new ArenaZone { Name = "C", Center = new Vector3(12f, 0f, 0f), Radius = 2.5f },
                },
                Hardpoints = new[]
                {
                    new ArenaZone { Name = "Plaza", Center = new Vector3(0f, 1.2f, 0f), Radius = 3f },
                    new ArenaZone { Name = "North street", Center = new Vector3(0f, 0f, 21f), Radius = 3f },
                    new ArenaZone { Name = "South street", Center = new Vector3(0f, 0f, -21f), Radius = 3f },
                },
                MaxPlayers = 12,
            };
        }

        // ------------------------------------------------------------------ building blocks

        /// <summary>
        /// Stairs up to a platform, rising 0.3 m a step: they climb in the direction
        /// (<paramref name="dx"/>, <paramref name="dz"/>), one of the four axis directions, and
        /// end at the platform's edge, at (<paramref name="x"/>, <paramref name="z"/>).
        /// </summary>
        static void Steps(List<ArenaBox> boxes, float x, float z, float dx, float dz, float height, float width)
        {
            int count = Mathf.CeilToInt(height / 0.3f - 0.001f);
            for (int i = 1; i < count; i++)
            {
                float rise = height * i / count;
                float back = (count - i) * 0.6f - 0.3f;
                var center = new Vector3(x - dx * back, rise * 0.5f, z - dz * back);
                Vector3 size = dx != 0f ? new Vector3(0.6f, rise, width) : new Vector3(width, rise, 0.6f);
                Add(boxes, center, size, ArenaPaint.Floor);
            }
        }

        static void MirroredSteps(List<ArenaBox> boxes, float x, float z, float dx, float dz, float height, float width)
        {
            Steps(boxes, x, z, dx, dz, height, width);
            Steps(boxes, -x, -z, -dx, -dz, height, width);
        }

        /// <summary>A roofed room with doors in the middle of whichever walls are asked for.</summary>
        static void Room(List<ArenaBox> boxes, float cx, float cz, float width, float depth, float height,
            bool north, bool south, bool east, bool west)
        {
            const float wall = 0.4f, door = 2.2f;
            float y = height * 0.5f;
            WallAlongX(boxes, cx, cz + depth * 0.5f - wall * 0.5f, width, height, north, door);
            WallAlongX(boxes, cx, cz - depth * 0.5f + wall * 0.5f, width, height, south, door);
            WallAlongZ(boxes, cx + width * 0.5f - wall * 0.5f, cz, depth, height, east, door);
            WallAlongZ(boxes, cx - width * 0.5f + wall * 0.5f, cz, depth, height, west, door);
            Add(boxes, new Vector3(cx, height + 0.15f, cz), new Vector3(width, 0.3f, depth), ArenaPaint.Wall);

            void WallAlongX(List<ArenaBox> list, float x, float z, float length, float h, bool hasDoor, float gap)
            {
                if (!hasDoor)
                {
                    Add(list, new Vector3(x, y, z), new Vector3(length, h, wall), ArenaPaint.Wall);
                    return;
                }
                float piece = (length - gap) * 0.5f;
                Add(list, new Vector3(x - (gap + piece) * 0.5f, y, z), new Vector3(piece, h, wall), ArenaPaint.Wall);
                Add(list, new Vector3(x + (gap + piece) * 0.5f, y, z), new Vector3(piece, h, wall), ArenaPaint.Wall);
            }

            void WallAlongZ(List<ArenaBox> list, float x, float z, float length, float h, bool hasDoor, float gap)
            {
                if (!hasDoor)
                {
                    Add(list, new Vector3(x, y, z), new Vector3(wall, h, length), ArenaPaint.Wall);
                    return;
                }
                float piece = (length - gap) * 0.5f;
                Add(list, new Vector3(x, y, z - (gap + piece) * 0.5f), new Vector3(wall, h, piece), ArenaPaint.Wall);
                Add(list, new Vector3(x, y, z + (gap + piece) * 0.5f), new Vector3(wall, h, piece), ArenaPaint.Wall);
            }
        }

        /// <summary>A room and its twin half a turn round the centre, with the doors turned to match.</summary>
        static void MirroredRoom(List<ArenaBox> boxes, float cx, float cz, float width, float depth, float height,
            bool north, bool south, bool east, bool west)
        {
            Room(boxes, cx, cz, width, depth, height, north, south, east, west);
            Room(boxes, -cx, -cz, width, depth, height, south, north, west, east);
        }

        static ArenaSpawn At(float x, float y, float z, float yaw) => new ArenaSpawn { Position = new Vector3(x, y, z), Yaw = yaw };

        /// <summary>The same spawns turned half a turn about the centre, for the other team.</summary>
        static ArenaSpawn[] Mirror(ArenaSpawn[] side)
        {
            var mirrored = new ArenaSpawn[side.Length];
            for (int i = 0; i < side.Length; i++)
            {
                mirrored[i] = new ArenaSpawn
                {
                    Position = new Vector3(-side[i].Position.x, side[i].Position.y, -side[i].Position.z),
                    Yaw = side[i].Yaw + 180f,
                };
            }
            return mirrored;
        }

        static void Add(List<ArenaBox> boxes, Vector3 center, Vector3 size, ArenaPaint paint) =>
            boxes.Add(new ArenaBox { Center = center, Size = size, Paint = paint });

        static void Mirrored(List<ArenaBox> boxes, Vector3 center, Vector3 size, ArenaPaint paint)
        {
            Add(boxes, center, size, paint);
            Add(boxes, new Vector3(-center.x, center.y, -center.z), size, paint);
        }
    }
}
