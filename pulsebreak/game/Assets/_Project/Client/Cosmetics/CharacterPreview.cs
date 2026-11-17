using UnityEngine;
using Pulsebreak.Client.Visuals;
using Pulsebreak.Client.Weapons;
using Pulsebreak.Shared.Cosmetics;
using Pulsebreak.Shared.Match;
using Pulsebreak.Shared.Movement;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Client.Cosmetics
{
    /// <summary>
    /// A small stage far below the level: a character in the chosen look, holding a weapon in
    /// its skin, on a turntable, filmed by its own camera into a texture the Customize screen
    /// shows. Nothing on it can be hit, and nothing on it is seen by the game's camera.
    /// </summary>
    public sealed class CharacterPreview : MonoBehaviour
    {
        static readonly Vector3 StagePosition = new Vector3(0f, -400f, 0f);

        UnityCharacterBody _body;
        CharacterLookView _look;
        Transform _hand, _weaponModel;
        Camera _camera;
        RenderTexture _texture;
        float _yaw = 200f, _zoom, _zoomTarget;
        float _lastDrag = -10f;
        WeaponBuild _weapon;
        CosmeticItem _skin;
        MovementConfig _movement;

        public RenderTexture Texture => _texture;

        public static CharacterPreview Create(MovementConfig movement, int width, int height)
        {
            var preview = new GameObject("Character Preview").AddComponent<CharacterPreview>();
            preview._movement = movement != null ? movement : ScriptableObject.CreateInstance<MovementConfig>();
            preview.Build(width, height);
            return preview;
        }

        void Build(int width, int height)
        {
            transform.position = StagePosition;

            _body = new UnityCharacterBody("Preview", _movement, withController: false, server: false);
            _body.Root.SetParent(transform, false);
            _body.SetHittable(false);
            _look = new CharacterLookView(_body);
            _look.SetSide(false, false);
            Pose();

            _hand = new GameObject("Hand").transform;
            _hand.SetParent(_body.Root, false);
            _hand.localPosition = new Vector3(0.24f, 1.2f, 0.22f);
            _hand.localRotation = Quaternion.Euler(4f, -6f, 0f);

            GreyboxKit.Visual(transform, "Plinth", PrimitiveType.Cylinder, new Vector3(0f, -0.05f, 0f), new Vector3(1.6f, 0.05f, 1.6f),
                GreyboxMaterials.Flat(new Color(0.1f, 0.12f, 0.16f)));
            GreyboxKit.Visual(transform, "Plinth Edge", PrimitiveType.Cylinder, new Vector3(0f, -0.04f, 0f), new Vector3(1.66f, 0.04f, 1.66f),
                GreyboxMaterials.Unlit(new Color(0.15f, 0.85f, 1f)), castShadows: false);

            var key = new GameObject("Key Light").AddComponent<Light>();
            key.type = LightType.Point;
            key.range = 9f;
            key.intensity = 2.2f;
            key.color = new Color(1f, 0.97f, 0.92f);
            key.transform.SetParent(transform, false);
            key.transform.localPosition = new Vector3(1.6f, 2.6f, 2.2f);
            var rim = new GameObject("Rim Light").AddComponent<Light>();
            rim.type = LightType.Point;
            rim.range = 7f;
            rim.intensity = 1.4f;
            rim.color = new Color(0.55f, 0.8f, 1f);
            rim.transform.SetParent(transform, false);
            rim.transform.localPosition = new Vector3(-1.8f, 1.8f, -1.6f);

            _texture = new RenderTexture(width, height, 24) { name = "Character Preview", antiAliasing = 4 };
            _camera = new GameObject("Preview Camera").AddComponent<Camera>();
            _camera.transform.SetParent(transform, false);
            _camera.targetTexture = _texture;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.05f, 0.07f, 0.1f, 1f);
            _camera.fieldOfView = 30f;
            _camera.nearClipPlane = 0.05f;
            // Short, so the level hundreds of metres above never shows.
            _camera.farClipPlane = 25f;
            PlaceCamera(0f);
        }

        /// <summary>Dresses the character. Pass the library's look; it is a new object after each change.</summary>
        public void Show(CharacterLook look) => _look.Apply(look);

        /// <summary>The weapon in the character's hand, and its skin. Null for no weapon.</summary>
        public void Hold(WeaponBuild weapon, CosmeticItem skin)
        {
            if (weapon == _weapon && skin == _skin && _weaponModel != null) return;
            _weapon = weapon;
            _skin = skin;
            if (_weaponModel != null) Destroy(_weaponModel.gameObject);
            _weaponModel = null;
            if (weapon == null) return;
            _weaponModel = ViewModelBuilder.Build(_hand, weapon, skin).Root;
            _weaponModel.localPosition = Vector3.zero;
            _weaponModel.localRotation = Quaternion.identity;
        }

        /// <summary>Turns the character as the player drags across the picture.</summary>
        public void Turn(float degrees)
        {
            _yaw += degrees;
            _lastDrag = Time.unscaledTime;
        }

        /// <summary>Moves the camera in on the weapon (true) or back to the whole character.</summary>
        public void FocusWeapon(bool weapon) => _zoomTarget = weapon ? 1f : 0f;

        void LateUpdate()
        {
            // Left alone for a while, it turns slowly by itself.
            if (Time.unscaledTime - _lastDrag > 3f) _yaw += 12f * Time.unscaledDeltaTime;
            Pose();
            _zoom = Mathf.MoveTowards(_zoom, _zoomTarget, Time.unscaledDeltaTime * 3f);
            PlaceCamera(Mathf.SmoothStep(0f, 1f, _zoom));
        }

        /// <summary>Stands the character on the turntable, facing the way it has been turned.</summary>
        void Pose()
        {
            _body.SetPose(new BodyPose { Position = transform.position, Yaw = _yaw, EyeHeight = _movement.standEyeHeight });
            _look.Follow();
        }

        void PlaceCamera(float weapon)
        {
            Vector3 position = Vector3.Lerp(new Vector3(0f, 1.15f, 4.4f), new Vector3(0.2f, 1.3f, 2.1f), weapon);
            Vector3 target = Vector3.Lerp(new Vector3(0f, 0.95f, 0f), new Vector3(0.12f, 1.2f, 0f), weapon);
            _camera.transform.localPosition = position;
            _camera.transform.LookAt(transform.TransformPoint(target));
        }

        void OnDestroy()
        {
            _look?.Destroy();
            if (_camera != null) _camera.targetTexture = null;
            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
            }
        }
    }
}
