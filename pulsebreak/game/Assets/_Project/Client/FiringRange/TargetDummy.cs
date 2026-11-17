using System.Collections.Generic;
using UnityEngine;
using Pulsebreak.Client.Visuals;
using Pulsebreak.Shared.Combat;
using Pulsebreak.Shared.Simulation;

namespace Pulsebreak.Client.FiringRange
{
    /// <summary>
    /// A practice target shaped like a player, with head, body and limb hitboxes. It takes
    /// damage like a real opponent, drops when its health runs out and stands back up a
    /// moment later.
    /// </summary>
    public sealed class TargetDummy : MonoBehaviour
    {
        static readonly Color BodyColor = new Color(0.92f, 0.38f, 0.24f);
        static readonly Color HeadColor = new Color(1f, 0.72f, 0.3f);
        static readonly Color FlashColor = new Color(1f, 1f, 1f);
        const float FlashTime = 0.1f;
        const float BarWidth = 0.6f;

        public float respawnDelay = 1.5f;

        readonly List<Collider> _colliders = new List<Collider>();
        readonly List<Renderer> _renderers = new List<Renderer>();
        Health _health;
        Material _bodyMaterial, _headMaterial, _barMaterial;
        Transform _healthBar;
        float _flash;
        float _respawnTimer;

        public Health Health => _health;

        public static TargetDummy Create(Transform parent, string name, Vector3 position, float maxHealth = 100f)
        {
            var go = new GameObject(name) { layer = GameLayers.Character };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;

            var dummy = go.AddComponent<TargetDummy>();
            dummy.Build(maxHealth);
            return dummy;
        }

        void Build(float maxHealth)
        {
            _health = gameObject.AddComponent<Health>();
            _health.maxHealth = maxHealth;
            _health.Revive();
            _health.Damaged += OnDamaged;
            _health.Died += OnDied;

            // Kinematic body: lets the target be moved around without rebuilding static colliders.
            var body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            // Blocks movement. On the Character layer, so bullets pass through it to the hitboxes.
            var blocker = gameObject.AddComponent<CapsuleCollider>();
            blocker.radius = 0.3f;
            blocker.height = 1.8f;
            blocker.center = new Vector3(0f, 0.9f, 0f);
            _colliders.Add(blocker);

            // Each dummy owns its materials so a hit flash only lights up the one that was hit.
            _bodyMaterial = new Material(GreyboxMaterials.Flat(BodyColor));
            _headMaterial = new Material(GreyboxMaterials.Flat(HeadColor));
            _barMaterial = new Material(GreyboxMaterials.Unlit(Color.green));

            BodyPart("Head", PrimitiveType.Sphere, HitZone.Head, new Vector3(0f, 1.63f, 0f), new Vector3(0.25f, 0.27f, 0.25f), _headMaterial);
            BodyPart("Torso", PrimitiveType.Cube, HitZone.Body, new Vector3(0f, 1.17f, 0f), new Vector3(0.44f, 0.62f, 0.26f), _bodyMaterial);
            BodyPart("ArmL", PrimitiveType.Cube, HitZone.Limb, new Vector3(-0.3f, 1.17f, 0f), new Vector3(0.12f, 0.6f, 0.14f), _bodyMaterial);
            BodyPart("ArmR", PrimitiveType.Cube, HitZone.Limb, new Vector3(0.3f, 1.17f, 0f), new Vector3(0.12f, 0.6f, 0.14f), _bodyMaterial);
            BodyPart("LegL", PrimitiveType.Cube, HitZone.Limb, new Vector3(-0.115f, 0.43f, 0f), new Vector3(0.17f, 0.86f, 0.19f), _bodyMaterial);
            BodyPart("LegR", PrimitiveType.Cube, HitZone.Limb, new Vector3(0.115f, 0.43f, 0f), new Vector3(0.17f, 0.86f, 0.19f), _bodyMaterial);

            GameObject bar = GreyboxKit.Visual(transform, "HealthBar", PrimitiveType.Cube,
                new Vector3(0f, 1.95f, 0f), new Vector3(BarWidth, 0.05f, 0.03f), _barMaterial, castShadows: false);
            _healthBar = bar.transform;
            _renderers.Add(bar.GetComponent<Renderer>());
            RefreshHealthBar();
        }

        void BodyPart(string partName, PrimitiveType shape, HitZone zone, Vector3 position, Vector3 scale, Material material)
        {
            GameObject part = GreyboxKit.Visual(transform, partName, shape, position, scale, material);
            part.layer = GameLayers.Hitbox;

            // Unit-sized colliders; the part's scale gives them their real size.
            Collider collider;
            if (shape == PrimitiveType.Sphere)
            {
                var sphere = part.AddComponent<SphereCollider>();
                sphere.radius = 0.5f;
                collider = sphere;
            }
            else
            {
                var box = part.AddComponent<BoxCollider>();
                box.size = Vector3.one;
                collider = box;
            }

            part.AddComponent<Hitbox>().zone = zone;
            _colliders.Add(collider);
            _renderers.Add(part.GetComponent<Renderer>());
        }

        void OnDamaged(DamageInfo info, DamageResult result)
        {
            _flash = FlashTime;
            RefreshHealthBar();
        }

        void OnDied(DamageInfo info)
        {
            SetPresent(false);
            _respawnTimer = respawnDelay;
        }

        void Update()
        {
            if (_flash > 0f)
            {
                _flash -= Time.deltaTime;
                float t = Mathf.Clamp01(_flash / FlashTime);
                GreyboxMaterials.SetColor(_bodyMaterial, Color.Lerp(BodyColor, FlashColor, t));
                GreyboxMaterials.SetColor(_headMaterial, Color.Lerp(HeadColor, FlashColor, t));
            }

            if (_respawnTimer > 0f)
            {
                _respawnTimer -= Time.deltaTime;
                if (_respawnTimer <= 0f)
                {
                    _health.Revive();
                    SetPresent(true);
                    RefreshHealthBar();
                }
            }
        }

        void SetPresent(bool present)
        {
            foreach (Collider collider in _colliders) collider.enabled = present;
            foreach (Renderer renderer in _renderers) renderer.enabled = present;
        }

        void RefreshHealthBar()
        {
            float fraction = _health.Fraction;
            _healthBar.localScale = new Vector3(BarWidth * Mathf.Max(fraction, 0.001f), 0.05f, 0.03f);
            GreyboxMaterials.SetColor(_barMaterial, Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(0.3f, 1f, 0.45f), fraction));
        }

        void OnDestroy()
        {
            if (_bodyMaterial != null) Destroy(_bodyMaterial);
            if (_headMaterial != null) Destroy(_headMaterial);
            if (_barMaterial != null) Destroy(_barMaterial);
        }
    }
}
