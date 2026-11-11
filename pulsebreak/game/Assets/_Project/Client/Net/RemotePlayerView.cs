using UnityEngine;
using Pulsebreak.Client.Visuals;
using Pulsebreak.Shared.Combat;
using Pulsebreak.Shared.Match;
using Pulsebreak.Shared.Movement;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Client.Net
{
    /// <summary>
    /// Another player, as drawn on this client: a placeholder body made of the same parts
    /// the server shoots at, holding a block where their weapon would be.
    ///
    /// It never simulates anything. Each frame it asks the session where that player was a
    /// moment ago and stands the body there.
    /// </summary>
    [DefaultExecutionOrder(10)]
    public sealed class RemotePlayerView : MonoBehaviour
    {
        static readonly Color BodyColor = new Color(0.9f, 0.32f, 0.24f);
        static readonly Color HeadColor = new Color(1f, 0.7f, 0.32f);
        const float FlashTime = 0.12f;

        ClientSession _session;
        int _playerId;
        UnityCharacterBody _body;
        Transform _weapon;
        Material _bodyMaterial, _headMaterial, _weaponMaterial;
        float _flash;
        int _shownSlot = -1;
        byte _shownSpawn;

        public int PlayerId => _playerId;
        public bool Visible { get; private set; }

        /// <summary>The front of the weapon block, where tracers start.</summary>
        public Vector3 Muzzle => _weapon.TransformPoint(new Vector3(0f, 0f, 0.5f));

        public static RemotePlayerView Create(ClientSession session, int playerId, MovementConfig movement)
        {
            var view = new GameObject("Remote Player " + playerId).AddComponent<RemotePlayerView>();
            view._session = session;
            view._playerId = playerId;
            view.Build(movement);
            return view;
        }

        void Build(MovementConfig movement)
        {
            // Hitboxes only: our own bullets are traced against them to draw where they stop.
            // Whether a shot really hit is decided on the server.
            _body = new UnityCharacterBody("Remote " + _playerId, movement, withController: false, server: false);
            _body.Root.SetParent(transform, false);

            _bodyMaterial = new Material(GreyboxMaterials.Flat(BodyColor));
            _headMaterial = new Material(GreyboxMaterials.Flat(HeadColor));
            foreach (UnityCharacterBody.Part part in _body.Parts)
            {
                GameObject go = part.Transform.gameObject;
                go.AddComponent<MeshFilter>().sharedMesh = GreyboxKit.PrimitiveMesh(part.Round ? PrimitiveType.Sphere : PrimitiveType.Cube);
                go.AddComponent<MeshRenderer>().sharedMaterial = part.Zone == HitZone.Head ? _headMaterial : _bodyMaterial;
            }

            _weaponMaterial = new Material(GreyboxMaterials.Flat(new Color(0.2f, 0.2f, 0.22f)));
            _weapon = GreyboxKit.Visual(_body.Root, "Weapon", PrimitiveType.Cube, Vector3.zero,
                new Vector3(0.07f, 0.1f, 0.6f), _weaponMaterial).transform;

            _body.Root.gameObject.SetActive(false);
        }

        /// <summary>Lights the body up for a moment. Called when one of our shots is confirmed.</summary>
        public void Flash() => _flash = FlashTime;

        void Update()
        {
            RemoteView view = default;
            Visible = _session != null && _session.TryGetView(_playerId, out view);
            if (_body.Root.gameObject.activeSelf != Visible) _body.Root.gameObject.SetActive(Visible);
            if (!Visible) return;

            _body.SetPose(view.Pose);

            // The weapon sits at the right shoulder and tips with where they are looking.
            _weapon.localPosition = new Vector3(0.2f + view.Pose.LeanOffset, view.Pose.EyeHeight - 0.22f, 0.3f);
            _weapon.localRotation = Quaternion.Euler(view.Pitch, 0f, 0f);
            ShowWeapon(view.ActiveSlot);

            if (_flash > 0f)
            {
                _flash -= Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(_flash / FlashTime);
                GreyboxMaterials.SetColor(_bodyMaterial, Color.Lerp(BodyColor, Color.white, t));
                GreyboxMaterials.SetColor(_headMaterial, Color.Lerp(HeadColor, Color.white, t));
            }
        }

        /// <summary>Sizes and colours the weapon block to hint at what they are holding.</summary>
        void ShowWeapon(int slot)
        {
            ClientPlayer player = _session.Players[_playerId];
            if (slot == _shownSlot && player.SpawnCount == _shownSpawn) return;
            _shownSlot = slot;
            _shownSpawn = player.SpawnCount;

            WeaponBuild build = player.Loadout != null && slot >= 0 && slot < player.Loadout.Weapons.Length ? player.Loadout.Weapons[slot] : null;
            if (build == null) return;
            WeaponViewProfile look = build.Weapon.view;
            float length = Mathf.Clamp(look.bodyLength + look.barrelLength + look.stockLength, 0.2f, 1.1f);
            _weapon.localScale = new Vector3(0.07f, 0.1f, length);
            GreyboxMaterials.SetColor(_weaponMaterial, look.bodyColor);
        }

        void OnDestroy()
        {
            // The body's root is our child and goes with us; the materials are ours to clean up.
            if (_bodyMaterial != null) Destroy(_bodyMaterial);
            if (_headMaterial != null) Destroy(_headMaterial);
            if (_weaponMaterial != null) Destroy(_weaponMaterial);
        }
    }
}
