using System.Collections.Generic;
using UnityEngine;
using Pulsebreak.Shared.Combat;
using Pulsebreak.Shared.Movement;
using Pulsebreak.Shared.Simulation;

namespace Pulsebreak.Shared.Match
{
    /// <summary>
    /// A character's body made of real Unity colliders: a head, a torso, two arms and two
    /// legs, each a separate hitbox. The parts shrink towards the ground as the eye height
    /// drops, so a crouching or sliding player is a smaller target.
    ///
    /// The body parts hang under their own root rather than under the movement capsule.
    /// That lets the server put the parts back where they were a moment ago to judge a shot
    /// without disturbing where the player actually is.
    /// </summary>
    public sealed class UnityCharacterBody : ICharacterBody
    {
        /// <summary>One body part. The client hangs a mesh on <see cref="Transform"/> to make it visible.</summary>
        public sealed class Part
        {
            public Transform Transform;
            public Collider Collider;
            public Hitbox Hitbox;
            public HitZone Zone;
            public bool Round;
            /// <summary>Local position and size when standing upright.</summary>
            public Vector3 Offset, Size;
            /// <summary>How much of the lean this part follows: 1 for the head, 0 for the legs.</summary>
            public float Lean;
        }

        readonly GameObject _capsule;
        readonly Transform _root;
        readonly List<Part> _parts = new List<Part>(6);
        readonly float _standEyeHeight;

        public CharacterController Controller { get; }
        /// <summary>At the feet, turned to face the way the character faces.</summary>
        public Transform Root => _root;
        public IReadOnlyList<Part> Parts => _parts;

        public UnityCharacterBody(string name, MovementConfig movement, bool withController, bool server)
        {
            _standEyeHeight = movement.standEyeHeight;

            if (withController)
            {
                _capsule = new GameObject(name) { layer = server ? GameLayers.ServerCharacter : GameLayers.Character };
                Controller = _capsule.AddComponent<CharacterController>();
            }

            _root = new GameObject(name + " Body").transform;
            int layer = server ? GameLayers.ServerHitbox : GameLayers.Hitbox;

            // The same proportions as the firing range's target dummy.
            AddPart("Head", HitZone.Head, true, new Vector3(0f, 1.63f, 0f), new Vector3(0.25f, 0.27f, 0.25f), 1f, layer);
            AddPart("Torso", HitZone.Body, false, new Vector3(0f, 1.17f, 0f), new Vector3(0.44f, 0.62f, 0.26f), 0.5f, layer);
            AddPart("ArmL", HitZone.Limb, false, new Vector3(-0.3f, 1.17f, 0f), new Vector3(0.12f, 0.6f, 0.14f), 0.5f, layer);
            AddPart("ArmR", HitZone.Limb, false, new Vector3(0.3f, 1.17f, 0f), new Vector3(0.12f, 0.6f, 0.14f), 0.5f, layer);
            AddPart("LegL", HitZone.Limb, false, new Vector3(-0.115f, 0.43f, 0f), new Vector3(0.17f, 0.86f, 0.19f), 0f, layer);
            AddPart("LegR", HitZone.Limb, false, new Vector3(0.115f, 0.43f, 0f), new Vector3(0.17f, 0.86f, 0.19f), 0f, layer);

            SetPose(new BodyPose { EyeHeight = _standEyeHeight });
        }

        void AddPart(string name, HitZone zone, bool round, Vector3 offset, Vector3 size, float lean, int layer)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(_root, false);

            // Unit-sized colliders; the part's scale gives them their real size.
            Collider collider;
            if (round)
            {
                var sphere = go.AddComponent<SphereCollider>();
                sphere.radius = 0.5f;
                collider = sphere;
            }
            else
            {
                var box = go.AddComponent<BoxCollider>();
                box.size = Vector3.one;
                collider = box;
            }

            var hitbox = go.AddComponent<Hitbox>();
            hitbox.zone = zone;
            _parts.Add(new Part
            {
                Transform = go.transform,
                Collider = collider,
                Hitbox = hitbox,
                Zone = zone,
                Round = round,
                Offset = offset,
                Size = size,
                Lean = lean,
            });
        }

        public void SetOwner(IDamageable owner)
        {
            foreach (Part part in _parts) part.Hitbox.SetOwner(owner);
        }

        public void SetPose(in BodyPose pose)
        {
            _root.SetPositionAndRotation(pose.Position, Quaternion.Euler(0f, pose.Yaw, 0f));

            float squash = Mathf.Clamp(pose.EyeHeight / _standEyeHeight, 0.3f, 1.2f);
            foreach (Part part in _parts)
            {
                part.Transform.localPosition = new Vector3(
                    part.Offset.x + pose.LeanOffset * part.Lean, part.Offset.y * squash, part.Offset.z);
                part.Transform.localScale = part.Round
                    ? part.Size
                    : new Vector3(part.Size.x, part.Size.y * squash, part.Size.z);
            }
        }

        public void SetHittable(bool hittable)
        {
            foreach (Part part in _parts) part.Collider.enabled = hittable;
        }

        public void Destroy()
        {
            if (_root != null) Object.Destroy(_root.gameObject);
            if (_capsule != null) Object.Destroy(_capsule);
        }
    }

    /// <summary>The physical world, made of Unity physics.</summary>
    public sealed class UnityGameWorld : IGameWorld
    {
        // One process can hold a server and a client at once (hosting from the game). The
        // level's collision is the same for both, so it is built once and shared.
        static readonly Dictionary<string, GameObject> Arenas = new Dictionary<string, GameObject>();

        readonly bool _server;

        /// <param name="server">True for the server's side of the world: its bodies go on the server's layers.</param>
        public UnityGameWorld(bool server)
        {
            _server = server;
        }

        public void BuildArena(ArenaLayout layout)
        {
            if (Arenas.TryGetValue(layout.Id, out GameObject existing) && existing != null) return;

            var root = new GameObject("Arena Collision (" + layout.DisplayName + ")");
            foreach (ArenaBox box in layout.Boxes)
            {
                var block = new GameObject("Block") { layer = GameLayers.Default };
                block.transform.SetParent(root.transform, false);
                block.transform.localPosition = box.Center;
                block.AddComponent<BoxCollider>().size = box.Size;
            }
            Arenas[layout.Id] = root;

            // New static colliders are not seen by queries until the physics scene catches up.
            Physics.SyncTransforms();
        }

        public ICharacterBody CreateBody(string name, MovementConfig movement, bool withController) =>
            new UnityCharacterBody(name, movement, withController, _server);

        public void SyncPhysics() => Physics.SyncTransforms();
    }
}
