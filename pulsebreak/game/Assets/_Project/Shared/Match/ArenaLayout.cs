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

    /// <summary>
    /// A level described as data: solid blocks and spawn points. The server and every client
    /// build their collision from the same description, which is what lets a client predict
    /// its own movement and get the answer the server gets.
    ///
    /// Levels are blocks in code for now. When real maps arrive (a later phase) this becomes
    /// the collision exported from a scene, and nothing that reads it has to change.
    /// </summary>
    public sealed class ArenaLayout
    {
        public string Id;
        public string DisplayName;
        public ArenaBox[] Boxes;
        /// <summary>Two sides, one per duellist. Each side has a few places to start from.</summary>
        public ArenaSpawn[][] Sides;

        public const string DefaultId = "relay_yard";

        public static ArenaLayout Find(string id)
        {
            // One arena so far; anything unknown falls back to it.
            return RelayYard();
        }

        /// <summary>
        /// Relay Yard: a walled yard, 38 m by 26 m. A tall block in the middle hides the two
        /// starts from each other; either side of it there is a lane with a crate to vault,
        /// a wall to lean round and a low barrier to crouch behind. Each end has a raised
        /// deck reached by steps or a mantle. Built to be symmetric through the centre.
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
            Mirrored(boxes, new Vector3(-8f, 1.1f, 4f), new Vector3(1f, 2.2f, 5f), ArenaPaint.Wall);          // wall to lean round
            Mirrored(boxes, new Vector3(-6.5f, 0.5f, -3f), new Vector3(4f, 1f, 1f), ArenaPaint.Cover);        // low barrier
            Mirrored(boxes, new Vector3(-4f, 1.5f, 10.5f), new Vector3(1.2f, 3f, 1.2f), ArenaPaint.Accent);   // pillar

            // A raised deck, with three steps up its inner side.
            Mirrored(boxes, new Vector3(-14f, 0.6f, -8f), new Vector3(6f, 1.2f, 6f), ArenaPaint.Cover);
            Mirrored(boxes, new Vector3(-10.7f, 0.45f, -8f), new Vector3(0.6f, 0.9f, 3f), ArenaPaint.Floor);
            Mirrored(boxes, new Vector3(-10.1f, 0.3f, -8f), new Vector3(0.6f, 0.6f, 3f), ArenaPaint.Floor);
            Mirrored(boxes, new Vector3(-9.5f, 0.15f, -8f), new Vector3(0.6f, 0.3f, 3f), ArenaPaint.Floor);

            var west = new[]
            {
                new ArenaSpawn { Position = new Vector3(-17f, 0f, 9f), Yaw = 100f },
                new ArenaSpawn { Position = new Vector3(-17f, 0f, 1.5f), Yaw = 90f },
                new ArenaSpawn { Position = new Vector3(-15f, 1.2f, -8f), Yaw = 80f },
            };
            var east = new ArenaSpawn[west.Length];
            for (int i = 0; i < west.Length; i++)
            {
                east[i] = new ArenaSpawn
                {
                    Position = new Vector3(-west[i].Position.x, west[i].Position.y, -west[i].Position.z),
                    Yaw = west[i].Yaw + 180f,
                };
            }

            return new ArenaLayout
            {
                Id = DefaultId,
                DisplayName = "Relay Yard",
                Boxes = boxes.ToArray(),
                Sides = new[] { west, east },
            };
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
