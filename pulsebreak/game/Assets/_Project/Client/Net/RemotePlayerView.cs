using UnityEngine;
using Pulsebreak.Client.Visuals;
using Pulsebreak.Shared.Cosmetics;
using Pulsebreak.Shared.Match;
using Pulsebreak.Shared.Movement;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Client.Net
{
    /// <summary>
    /// Another player, as drawn on this client: a body made of the same parts the server
    /// shoots at, dressed in the look they chose, holding a block in their weapon's skin.
    /// Shoulder pads show the side they are on (blue for a teammate, red for anyone else), and
    /// teammates have a marker over their heads so they are easy to pick out.
    ///
    /// It never simulates anything. Each frame it asks the session where that player was a
    /// moment ago and stands the body there.
    /// </summary>
    [DefaultExecutionOrder(10)]
    public sealed class RemotePlayerView : MonoBehaviour
    {
        const float FlashTime = 0.12f;
        static readonly Color MarkerColor = new Color(0.55f, 0.85f, 1f);

        ClientSession _session;
        int _playerId;
        UnityCharacterBody _body;
        CharacterLookView _look;
        Transform _weapon;
        MeshRenderer _weaponRenderer;
        Material _weaponMaterial, _markerMaterial;
        Transform _marker;
        bool _friendly;
        float _flash;
        int _shownSlot = -1;
        byte _shownSpawn;
        CharacterLook _shownLook;

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
            _look = new CharacterLookView(_body);

            _weaponMaterial = new Material(GreyboxMaterials.Flat(new Color(0.2f, 0.2f, 0.22f)));
            GameObject weapon = GreyboxKit.Visual(_body.Root, "Weapon", PrimitiveType.Cube, Vector3.zero, new Vector3(0.07f, 0.1f, 0.6f), _weaponMaterial);
            _weapon = weapon.transform;
            _weaponRenderer = weapon.GetComponent<MeshRenderer>();

            _markerMaterial = new Material(GreyboxMaterials.Unlit(MarkerColor));
            _marker = GreyboxKit.Visual(_body.Root, "Team Marker", PrimitiveType.Cube, Vector3.zero,
                new Vector3(0.18f, 0.18f, 0.18f), _markerMaterial, castShadows: false).transform;
            _marker.localRotation = Quaternion.Euler(45f, 0f, 45f);
            _marker.gameObject.SetActive(false);

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

            ClientPlayer player = _session.Players[_playerId];
            _look.Apply(player.Look);
            _body.SetPose(view.Pose);
            _look.Follow();
            UpdateSide(view.Pose.EyeHeight);

            // The weapon sits at the right shoulder and tips with where they are looking.
            _weapon.localPosition = new Vector3(0.2f, view.Pose.EyeHeight - 0.22f, 0.3f);
            _weapon.localRotation = Quaternion.Euler(view.Pitch, 0f, 0f);
            ShowWeapon(view.ActiveSlot);

            if (_flash > 0f)
            {
                _flash -= Time.unscaledDeltaTime;
                _look.Flash(Mathf.Clamp01(_flash / FlashTime));
                if (_flash <= 0f) _look.Flash(0f);
            }
        }

        /// <summary>
        /// Pads in the colour of their side, and a marker over teammates. Checked every frame:
        /// teams can change between matches.
        /// </summary>
        void UpdateSide(float eyeHeight)
        {
            ClientPlayer local = _session.Local;
            ClientPlayer player = _session.Players[_playerId];
            bool friendly = local != null && _session.Settings.TeamBased && player.Team != Teams.None && player.Team == local.Team;
            if (friendly != _friendly || !_marker.gameObject.activeSelf && friendly)
            {
                _friendly = friendly;
                _marker.gameObject.SetActive(friendly);
            }
            _look.SetSide(true, friendly);
            if (friendly) _marker.localPosition = new Vector3(0f, eyeHeight + 0.6f + Mathf.Sin(Time.time * 3f) * 0.05f, 0f);
        }

        /// <summary>Sizes the weapon block to hint at what they are holding, in that weapon's skin.</summary>
        void ShowWeapon(int slot)
        {
            ClientPlayer player = _session.Players[_playerId];
            if (slot == _shownSlot && player.SpawnCount == _shownSpawn && ReferenceEquals(player.Look, _shownLook)) return;
            _shownSlot = slot;
            _shownSpawn = player.SpawnCount;
            _shownLook = player.Look;

            WeaponBuild build = player.Loadout != null && slot >= 0 && slot < player.Loadout.Weapons.Length ? player.Loadout.Weapons[slot] : null;
            if (build == null) return;
            WeaponViewProfile look = build.Weapon.view;
            float length = Mathf.Clamp(look.bodyLength + look.barrelLength + look.stockLength, 0.2f, 1.1f);
            _weapon.localScale = new Vector3(0.07f, 0.1f, length);

            Material skin = LookMaterials.SkinBody(player.Look.SkinFor(build.Weapon.weaponId));
            if (skin != null)
            {
                _weaponRenderer.sharedMaterial = skin;
                return;
            }
            GreyboxMaterials.SetColor(_weaponMaterial, look.bodyColor);
            _weaponRenderer.sharedMaterial = _weaponMaterial;
        }

        void OnDestroy()
        {
            // The body's root is our child and goes with us; the materials are ours to clean up.
            _look?.Destroy();
            if (_weaponMaterial != null) Destroy(_weaponMaterial);
            if (_markerMaterial != null) Destroy(_markerMaterial);
        }
    }
}
