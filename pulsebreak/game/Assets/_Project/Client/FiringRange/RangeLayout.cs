using UnityEngine;
using Pulsebreak.Client.Visuals;

namespace Pulsebreak.Client.FiringRange
{
    /// <summary>
    /// Builds the firing range out of prototype boxes. The origin is the firing line and +Z
    /// points downrange.
    ///
    ///   Centre   shooting lanes with targets at 10, 20, 35, 50 and 75 m, some of them moving,
    ///            and a tight group of three to the right for explosives
    ///   West     movement course: mantle steps up to a platform, a ramp, a slide tunnel, jump gaps
    ///   East     a raised perch reached by stairs, for long shots and testing steps
    ///
    /// This stands in for hand-built scenes until there is level art; the layout is data in
    /// code so it is easy to change while movement is being tuned.
    /// </summary>
    public sealed class RangeLayout
    {
        static readonly Color FloorColor = new Color(0.62f, 0.64f, 0.68f);
        static readonly Color WallColor = new Color(0.4f, 0.44f, 0.5f);
        static readonly Color CoverColor = new Color(0.36f, 0.5f, 0.7f);
        static readonly Color CourseColor = new Color(0.9f, 0.6f, 0.28f);
        static readonly Color TunnelColor = new Color(0.55f, 0.42f, 0.72f);
        static readonly Color PerchColor = new Color(0.34f, 0.62f, 0.55f);
        static readonly Color LineColor = new Color(1f, 0.82f, 0.25f);

        public Vector3 PlayerSpawn { get; private set; }
        public float SpawnYaw { get; private set; }

        public static RangeLayout Build(Transform root)
        {
            var layout = new RangeLayout
            {
                PlayerSpawn = new Vector3(0f, 0.05f, -4f),
                SpawnYaw = 0f,
            };

            BuildShell(Group(root, "Shell"));
            BuildLanes(Group(root, "Lanes"));
            BuildMovementCourse(Group(root, "Movement Course"));
            BuildPerch(Group(root, "Perch"));
            return layout;
        }

        static Transform Group(Transform root, string name)
        {
            var group = new GameObject(name).transform;
            group.SetParent(root, false);
            return group;
        }

        static void BuildShell(Transform parent)
        {
            // Floor top is at y = 0. The range spans x -40..40 and z -12..100.
            GreyboxKit.Box(parent, "Floor", new Vector3(0f, -0.5f, 44f), new Vector3(80f, 1f, 112f), FloorColor);
            GreyboxKit.Box(parent, "Wall North", new Vector3(0f, 4f, 100.5f), new Vector3(82f, 8f, 1f), WallColor);
            GreyboxKit.Box(parent, "Wall South", new Vector3(0f, 4f, -12.5f), new Vector3(82f, 8f, 1f), WallColor);
            GreyboxKit.Box(parent, "Wall West", new Vector3(-40.5f, 4f, 44f), new Vector3(1f, 8f, 112f), WallColor);
            GreyboxKit.Box(parent, "Wall East", new Vector3(40.5f, 4f, 44f), new Vector3(1f, 8f, 112f), WallColor);
        }

        static void BuildLanes(Transform parent)
        {
            // A painted line every 10 m. Visual only, so there is nothing to trip over.
            Material line = GreyboxMaterials.Flat(LineColor);
            for (int z = 10; z <= 80; z += 10)
            {
                GreyboxKit.Visual(parent, "Line " + z + "m", PrimitiveType.Cube,
                    new Vector3(0f, 0.006f, z), new Vector3(26f, 0.01f, 0.2f), line, castShadows: false);
            }

            // Cover at the firing line: a tall wall to peek around and a low one to crouch behind.
            GreyboxKit.Box(parent, "Cover Tall", new Vector3(-6f, 1.3f, 2.5f), new Vector3(2.6f, 2.6f, 0.4f), CoverColor);
            GreyboxKit.Box(parent, "Cover Low", new Vector3(6f, 0.55f, 2.5f), new Vector3(3f, 1.1f, 0.5f), CoverColor);

            Target(parent, -3f, 10f);
            Target(parent, 3f, 10f);
            Target(parent, -7f, 20f);
            Target(parent, 7f, 20f);
            Target(parent, 0f, 20f, travel: 4f, speed: 3f);
            Target(parent, -5f, 35f);
            Target(parent, 5f, 35f);
            Target(parent, -9f, 50f);
            Target(parent, 9f, 50f);
            Target(parent, 0f, 50f, travel: 6f, speed: 4.5f);
            Target(parent, -4f, 75f);
            Target(parent, 4f, 75f);

            // A tight group off to the right, for explosives and for shots that pierce.
            Target(parent, 13f, 16f);
            Target(parent, 14.4f, 17.2f);
            Target(parent, 15.6f, 15.4f);
        }

        static void Target(Transform parent, float x, float z, float travel = 0f, float speed = 0f)
        {
            TargetDummy dummy = TargetDummy.Create(parent, "Target " + z + "m", new Vector3(x, 0f, z));
            if (travel <= 0f) return;

            var mover = dummy.gameObject.AddComponent<TargetMover>();
            mover.axis = Vector3.right;
            mover.amplitude = travel;
            mover.speed = speed;
        }

        static void BuildMovementCourse(Transform parent)
        {
            // Mantle steps: 0.9 m, then 1.5 m, then a 2.6 m platform. From the ground the
            // platform is too high to climb directly; it needs a jump first.
            GreyboxKit.Box(parent, "Crate Low", new Vector3(-20f, 0.45f, 6f), new Vector3(3f, 0.9f, 3f), CourseColor);
            GreyboxKit.Box(parent, "Crate Mid", new Vector3(-20f, 0.75f, 9f), new Vector3(3f, 1.5f, 3f), CourseColor);
            GreyboxKit.Box(parent, "Platform", new Vector3(-20f, 1.3f, 13.5f), new Vector3(5f, 2.6f, 6f), CourseColor);

            // Ramp down from the platform: slide down it to feel slope acceleration.
            Ramp(parent, "Ramp", topEdge: new Vector3(-20f, 2.6f, 16.5f), width: 5f, angle: 22f, CourseColor);

            // Slide tunnel: 1.3 m of clearance. Too low to walk through, fine to crouch or slide.
            GreyboxKit.Box(parent, "Tunnel Roof", new Vector3(-30f, 1.65f, 14f), new Vector3(3f, 0.7f, 8f), TunnelColor);
            GreyboxKit.Box(parent, "Tunnel Wall L", new Vector3(-31.75f, 1f, 14f), new Vector3(0.5f, 2f, 8f), TunnelColor);
            GreyboxKit.Box(parent, "Tunnel Wall R", new Vector3(-28.25f, 1f, 14f), new Vector3(0.5f, 2f, 8f), TunnelColor);

            // Jump gaps: 3 m, then 4.5 m. The second needs a sprint jump.
            GreyboxKit.Box(parent, "Jump Pad 1", new Vector3(-30f, 0.5f, 30f), new Vector3(3f, 1f, 3f), CourseColor);
            GreyboxKit.Box(parent, "Jump Pad 2", new Vector3(-30f, 0.5f, 36f), new Vector3(3f, 1f, 3f), CourseColor);
            GreyboxKit.Box(parent, "Jump Pad 3", new Vector3(-30f, 0.5f, 43.5f), new Vector3(3f, 1f, 3f), CourseColor);
        }

        static void BuildPerch(Transform parent)
        {
            const float top = 3.5f;
            const float stepHeight = 0.25f, stepDepth = 0.45f;
            const int steps = 13;

            GreyboxKit.Box(parent, "Perch", new Vector3(24f, top * 0.5f, -4f), new Vector3(8f, top, 6f), PerchColor);

            // Stairs climbing towards the perch's front edge (z = -1) from further downrange.
            for (int i = 0; i < steps; i++)
            {
                float height = stepHeight * (i + 1);
                float z = -1f + stepDepth * (steps - 1 - i) + stepDepth * 0.5f;
                GreyboxKit.Box(parent, "Step " + (i + 1), new Vector3(24f, height * 0.5f, z), new Vector3(3f, height, stepDepth), PerchColor);
            }
        }

        /// <summary>A ramp that starts at <paramref name="topEdge"/> and descends towards +Z until it meets y = 0.</summary>
        static void Ramp(Transform parent, string name, Vector3 topEdge, float width, float angle, Color color)
        {
            const float thickness = 0.3f;
            float radians = angle * Mathf.Deg2Rad;
            float length = topEdge.y / Mathf.Sin(radians);

            // Tilting about X by +angle points the box's forward axis down the slope.
            Quaternion rotation = Quaternion.Euler(angle, 0f, 0f);
            Vector3 downSlope = rotation * Vector3.forward;
            Vector3 normal = rotation * Vector3.up;
            Vector3 centre = topEdge + downSlope * (length * 0.5f) - normal * (thickness * 0.5f);

            GameObject ramp = GreyboxKit.Box(parent, name, centre, new Vector3(width, thickness, length), color);
            ramp.transform.localRotation = rotation;
        }
    }
}
