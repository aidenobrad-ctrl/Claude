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

        /// <summary>Every map, in the order they are offered.</summary>
        public static readonly string[] Ids = { DefaultId, CrossroadsId };

        public static ArenaLayout Find(string id)
        {
            switch (id)
            {
                case CrossroadsId: return Crossroads();
                default: return RelayYard();       // anything unknown falls back to the first map
            }
        }

        /// <summary>The map's display name without building it.</summary>
        public static string Title(string id) => id == CrossroadsId ? "Crossroads" : "Relay Yard";

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
