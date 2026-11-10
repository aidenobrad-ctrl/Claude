using UnityEngine;
using Pulsebreak.Client.Visuals;

namespace Pulsebreak.Client.Weapons
{
    /// <summary>What the first-person weapon model needs for one rendered frame.</summary>
    public struct ViewModelInput
    {
        public float AdsAmount;
        public bool Sprinting;
        /// <summary>Hands are busy (mantling): drop the weapon out of view.</summary>
        public bool Lowered;
        public bool Reloading;
        public float ReloadProgress;
        /// <summary>0 = standing still, 1 = sprint speed. 0 while airborne.</summary>
        public float MoveAmount;
        public float Yaw;
        public float Pitch;
    }

    /// <summary>
    /// The gun you see in first person. Purely cosmetic: it is posed from weapon and movement
    /// state each frame and never feeds back into the simulation. The model is a placeholder
    /// built from boxes, and all motion is procedural, so no art or animation clips are needed yet.
    /// </summary>
    public sealed class WeaponViewModel : MonoBehaviour
    {
        // Poses are in camera space. The aimed pose puts the sight line through screen centre.
        static readonly Vector3 HipPosition = new Vector3(0.17f, -0.17f, 0.36f);
        static readonly Vector3 AdsPosition = new Vector3(0f, -0.0605f, 0.24f);
        static readonly Vector3 SprintPosition = new Vector3(0.1f, -0.2f, 0.3f);
        static readonly Vector3 SprintEuler = new Vector3(10f, -35f, 6f);
        static readonly Vector3 ReloadOffset = new Vector3(0f, -0.09f, -0.02f);
        static readonly Vector3 ReloadEuler = new Vector3(-22f, 8f, 22f);
        static readonly Vector3 LoweredOffset = new Vector3(0f, -0.32f, -0.05f);
        static readonly Vector3 LoweredEuler = new Vector3(28f, 0f, 0f);

        Transform _gun;
        Transform _muzzle;

        float _kick, _kickVelocity;
        float _sprintBlend, _reloadBlend, _lowerBlend;
        Vector2 _sway;
        float _bobTime;
        float _lastYaw, _lastPitch;
        bool _hasLastAngles;

        public Transform Muzzle => _muzzle;

        /// <summary>Builds the placeholder rifle under this object.</summary>
        public void Build()
        {
            _gun = new GameObject("Gun").transform;
            _gun.SetParent(transform, false);

            Material body = GreyboxMaterials.Flat(new Color(0.16f, 0.17f, 0.2f));
            Material dark = GreyboxMaterials.Flat(new Color(0.08f, 0.08f, 0.1f));
            Material accent = GreyboxMaterials.Unlit(new Color(0.15f, 0.85f, 1f));

            Part("Receiver", new Vector3(0f, 0f, 0f), new Vector3(0.055f, 0.075f, 0.3f), body);
            Part("Handguard", new Vector3(0f, 0.004f, 0.27f), new Vector3(0.046f, 0.052f, 0.26f), dark);
            Part("Barrel", new Vector3(0f, 0.008f, 0.44f), new Vector3(0.022f, 0.022f, 0.1f), dark);
            Part("Stock", new Vector3(0f, -0.012f, -0.25f), new Vector3(0.045f, 0.065f, 0.2f), body);
            Part("Magazine", new Vector3(0f, -0.095f, 0.035f), new Vector3(0.034f, 0.14f, 0.06f), dark)
                .localRotation = Quaternion.Euler(-8f, 0f, 0f);
            Part("Grip", new Vector3(0f, -0.075f, -0.085f), new Vector3(0.034f, 0.09f, 0.045f), dark)
                .localRotation = Quaternion.Euler(18f, 0f, 0f);
            Part("Accent", new Vector3(0f, 0.012f, 0.2f), new Vector3(0.05f, 0.012f, 0.1f), accent);

            // Open sights: two rear posts and one front post, tips just under the aim line.
            Part("RearSightL", new Vector3(-0.012f, 0.047f, -0.06f), new Vector3(0.007f, 0.02f, 0.012f), dark);
            Part("RearSightR", new Vector3(0.012f, 0.047f, -0.06f), new Vector3(0.007f, 0.02f, 0.012f), dark);
            Part("FrontSight", new Vector3(0f, 0.043f, 0.38f), new Vector3(0.005f, 0.03f, 0.005f), dark);

            _muzzle = new GameObject("Muzzle").transform;
            _muzzle.SetParent(_gun, false);
            _muzzle.localPosition = new Vector3(0f, 0.008f, 0.5f);

            // Start lowered so the weapon rises into view when it is first drawn.
            _lowerBlend = 1f;
            _gun.localPosition = HipPosition + LoweredOffset;
        }

        Transform Part(string partName, Vector3 position, Vector3 scale, Material material) =>
            GreyboxKit.Visual(_gun, partName, PrimitiveType.Cube, position, scale, material, castShadows: false).transform;

        /// <summary>Call for every shot fired.</summary>
        public void OnShot()
        {
            _kickVelocity += 16f;
        }

        public void Tick(in ViewModelInput input, float deltaTime)
        {
            if (_gun == null) return;
            float dt = Mathf.Min(deltaTime, 1f / 30f);

            _sprintBlend = Mathf.MoveTowards(_sprintBlend, input.Sprinting ? 1f : 0f, dt / 0.16f);
            _lowerBlend = Mathf.MoveTowards(_lowerBlend, input.Lowered ? 1f : 0f, dt / (input.Lowered ? 0.12f : 0.28f));

            // Dips into the reload pose, holds, and comes back up as the reload completes.
            float reloadTarget = input.Reloading
                ? Mathf.Clamp01(Mathf.Sin(Mathf.Clamp01(input.ReloadProgress) * Mathf.PI) * 2.5f)
                : 0f;
            _reloadBlend = Mathf.Lerp(_reloadBlend, reloadTarget, 1f - Mathf.Exp(-16f * dt));

            // Recoil kick: an impulse per shot, pulled back by a damped spring.
            const float stiffness = 260f, damping = 26f;
            _kickVelocity += (-stiffness * _kick - damping * _kickVelocity) * dt;
            _kick += _kickVelocity * dt;

            // Sway: the gun lags slightly behind the view as it turns.
            if (!_hasLastAngles)
            {
                _lastYaw = input.Yaw;
                _lastPitch = input.Pitch;
                _hasLastAngles = true;
            }
            float yawRate = Mathf.DeltaAngle(_lastYaw, input.Yaw) / Mathf.Max(deltaTime, 0.0001f);
            float pitchRate = (input.Pitch - _lastPitch) / Mathf.Max(deltaTime, 0.0001f);
            _lastYaw = input.Yaw;
            _lastPitch = input.Pitch;
            var swayTarget = new Vector2(
                Mathf.Clamp(-yawRate * 0.00012f, -0.025f, 0.025f),
                Mathf.Clamp(pitchRate * 0.00012f, -0.025f, 0.025f));
            _sway = Vector2.Lerp(_sway, swayTarget, 1f - Mathf.Exp(-12f * dt));

            float ads = Mathf.SmoothStep(0f, 1f, input.AdsAmount);

            // Bob: a small figure-eight while moving on the ground, nearly gone when aiming.
            _bobTime += dt * 12f * Mathf.Sqrt(input.MoveAmount);
            float bobScale = input.MoveAmount * (1f - 0.85f * ads);
            var bob = new Vector3(Mathf.Sin(_bobTime) * 0.006f, Mathf.Sin(_bobTime * 2f) * 0.004f, 0f) * bobScale;

            Vector3 position = Vector3.Lerp(HipPosition, AdsPosition, ads);
            Quaternion rotation = Quaternion.identity;

            float sprint = _sprintBlend * (1f - ads);
            position = Vector3.Lerp(position, SprintPosition, sprint);
            rotation = Quaternion.Slerp(rotation, Quaternion.Euler(SprintEuler), sprint);

            position += ReloadOffset * _reloadBlend + LoweredOffset * _lowerBlend;
            rotation *= Quaternion.Euler(ReloadEuler * _reloadBlend + LoweredEuler * _lowerBlend);

            float kickScale = Mathf.Lerp(1f, 0.5f, ads);
            position += new Vector3(_sway.x, _sway.y, 0f) * (1f - 0.7f * ads) + bob;
            position += Vector3.back * (_kick * 0.06f * kickScale);
            rotation *= Quaternion.Euler(-_kick * 3.2f * kickScale, 0f, 0f);

            _gun.localPosition = position;
            _gun.localRotation = rotation;
        }
    }
}
