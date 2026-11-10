using UnityEngine;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Client.Weapons
{
    /// <summary>What the first-person weapon model needs for one rendered frame.</summary>
    public struct ViewModelInput
    {
        public float AdsAmount;
        public bool Sprinting;
        /// <summary>Hands are busy (mantling, or putting the weapon away): drop it out of view.</summary>
        public bool Lowered;
        public bool Reloading;
        public float ReloadProgress;
        /// <summary>Charge weapons: 0..1 while the trigger is held.</summary>
        public float Charge;
        /// <summary>Do not draw the model at all (a full-screen scope is up).</summary>
        public bool Hidden;
        /// <summary>0 = standing still, 1 = sprint speed. 0 while airborne.</summary>
        public float MoveAmount;
        public float Yaw;
        public float Pitch;
    }

    /// <summary>
    /// The weapon you see in first person. Purely cosmetic: it is posed from weapon and
    /// movement state each frame and never feeds back into the simulation. The model itself
    /// comes from <see cref="ViewModelBuilder"/>, and all motion is procedural, so no art or
    /// animation clips are needed yet.
    /// </summary>
    public sealed class WeaponViewModel : MonoBehaviour
    {
        static readonly Vector3 SprintOffset = new Vector3(-0.07f, -0.03f, -0.06f);
        static readonly Vector3 SprintEuler = new Vector3(10f, -35f, 6f);
        static readonly Vector3 ReloadOffset = new Vector3(0f, -0.09f, -0.02f);
        static readonly Vector3 ReloadEuler = new Vector3(-22f, 8f, 22f);
        static readonly Vector3 LoweredOffset = new Vector3(0f, -0.34f, -0.05f);
        static readonly Vector3 LoweredEuler = new Vector3(28f, 0f, 0f);
        const float SwingTime = 0.3f;

        ViewModelLayout _layout;
        Transform _muzzleAnchor;

        float _kick, _kickVelocity;
        float _sprintBlend, _reloadBlend, _lowerBlend = 1f;
        float _swing;
        Vector2 _sway;
        float _bobTime;
        float _lastYaw, _lastPitch;
        bool _hasLastAngles;

        /// <summary>
        /// Follows the muzzle of whichever weapon is shown. It outlives weapon switches, so
        /// effects can stay parented to it.
        /// </summary>
        public Transform Muzzle
        {
            get
            {
                if (_muzzleAnchor == null)
                {
                    _muzzleAnchor = new GameObject("MuzzleAnchor").transform;
                    _muzzleAnchor.SetParent(transform, false);
                }
                return _muzzleAnchor;
            }
        }

        /// <summary>Replaces the model with the one for this weapon and its attachments.</summary>
        public void Show(WeaponBuild build)
        {
            if (_layout.Root != null) Destroy(_layout.Root.gameObject);

            _layout = ViewModelBuilder.Build(transform, build);
            _kick = _kickVelocity = 0f;
            _swing = 0f;
            _reloadBlend = 0f;
            // Start lowered so the new weapon rises into view.
            _lowerBlend = 1f;
            _layout.Root.localPosition = _layout.HipPosition + LoweredOffset;
            _layout.Root.localRotation = Quaternion.Euler(_layout.HipEuler);
        }

        /// <summary>Call once for every shot fired or swing started.</summary>
        public void OnFired(WeaponBuild build)
        {
            if (_layout.Kind == ViewModelKind.Blade)
            {
                _swing = SwingTime;
                return;
            }

            // Heavier-hitting weapons kick the model harder.
            WeaponStats stats = build.Weapon.stats;
            float punch = stats.damage * Mathf.Max(1, stats.pellets);
            _kickVelocity += Mathf.Lerp(12f, 34f, Mathf.InverseLerp(15f, 130f, punch));
        }

        public void Tick(in ViewModelInput input, float deltaTime)
        {
            if (_layout.Root == null) return;
            float dt = Mathf.Min(deltaTime, 1f / 30f);

            if (_layout.Root.gameObject.activeSelf == input.Hidden) _layout.Root.gameObject.SetActive(!input.Hidden);

            _sprintBlend = Mathf.MoveTowards(_sprintBlend, input.Sprinting ? 1f : 0f, dt / 0.16f);
            _lowerBlend = Mathf.MoveTowards(_lowerBlend, input.Lowered ? 1f : 0f, dt / (input.Lowered ? 0.12f : 0.24f));

            // Dips into the reload pose, holds, and comes back up as the reload completes.
            float reloadTarget = input.Reloading
                ? Mathf.Clamp01(Mathf.Sin(Mathf.Clamp01(input.ReloadProgress) * Mathf.PI) * 2.5f)
                : 0f;
            _reloadBlend = Mathf.Lerp(_reloadBlend, reloadTarget, 1f - Mathf.Exp(-16f * dt));

            // Recoil kick: an impulse per shot, pulled back by a damped spring.
            const float stiffness = 260f, damping = 26f;
            _kickVelocity += (-stiffness * _kick - damping * _kickVelocity) * dt;
            _kick += _kickVelocity * dt;

            // Sway: the weapon lags slightly behind the view as it turns.
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

            Vector3 position = Vector3.Lerp(_layout.HipPosition, _layout.AdsPosition, ads);
            Quaternion rotation = Quaternion.Slerp(Quaternion.Euler(_layout.HipEuler), Quaternion.identity,
                _layout.Kind == ViewModelKind.Gun || _layout.Kind == ViewModelKind.Launcher ? 1f : ads * 0.3f);

            float sprint = _sprintBlend * (1f - ads);
            position += SprintOffset * sprint;
            rotation *= Quaternion.Euler(SprintEuler * sprint);

            position += ReloadOffset * _reloadBlend + LoweredOffset * _lowerBlend;
            rotation *= Quaternion.Euler(ReloadEuler * _reloadBlend + LoweredEuler * _lowerBlend);

            float kickScale = Mathf.Lerp(1f, 0.5f, ads);
            position += new Vector3(_sway.x, _sway.y, 0f) * (1f - 0.7f * ads) + bob;
            position += Vector3.back * (_kick * 0.06f * kickScale);
            rotation *= Quaternion.Euler(-_kick * 3.2f * kickScale, 0f, 0f);

            // Charging: the weapon draws back and trembles harder as power builds.
            if (input.Charge > 0f)
            {
                float tremble = input.Charge * 0.004f;
                position += new Vector3(Random.Range(-tremble, tremble), Random.Range(-tremble, tremble), -0.03f * input.Charge);
            }

            // A blade sweeps across the view from right to left.
            if (_swing > 0f)
            {
                _swing -= dt;
                float t = 1f - Mathf.Clamp01(_swing / SwingTime);
                float arc = Mathf.Sin(t * Mathf.PI);
                position += new Vector3(Mathf.Lerp(0.08f, -0.32f, t), 0.06f * arc, 0.12f * arc);
                rotation *= Quaternion.Euler(Mathf.Lerp(-10f, 35f, t), Mathf.Lerp(40f, -75f, t), -30f * arc);
            }

            _layout.Root.localPosition = position;
            _layout.Root.localRotation = rotation;

            Transform anchor = Muzzle;
            anchor.SetPositionAndRotation(_layout.Muzzle.position, _layout.Muzzle.rotation);
        }
    }
}
