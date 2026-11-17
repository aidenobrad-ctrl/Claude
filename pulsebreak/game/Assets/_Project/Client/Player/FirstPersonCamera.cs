using UnityEngine;
using Pulsebreak.Client.Settings;

namespace Pulsebreak.Client.Player
{
    /// <summary>Everything the camera needs for one rendered frame.</summary>
    public struct CameraPose
    {
        public Vector3 EyePosition;
        public float Yaw;
        public float Pitch;
        public float AdsAmount;
        public float AdsZoom;
        public bool Sprinting;
        public bool Sliding;
    }

    /// <summary>
    /// The first-person view. It only presents: position and angles come from the player
    /// controller each frame, and this adds the cosmetic layer on top (zoom, landing dip,
    /// speed-based field of view, shake from explosions and a flinch when hit).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class FirstPersonCamera : MonoBehaviour
    {
        const float ReferenceAspect = 16f / 9f;
        const float SprintFovKick = 3f;
        const float SlideFovKick = 6f;

        /// <summary>How far the view can be thrown at full shake, in degrees: yaw, pitch, roll.</summary>
        static readonly Vector3 MaxShake = new Vector3(2.2f, 1.8f, 3f);

        Camera _camera;
        float _dip, _dipVelocity, _fovKick;
        float _trauma, _shakeTime;
        Vector2 _flinch, _flinchVelocity;

        public Camera Camera => _camera;

        void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        /// <summary>Dips the view on landing. <paramref name="impactSpeed"/> is the fall speed in m/s.</summary>
        public void AddLandingImpulse(float impactSpeed)
        {
            _dipVelocity -= Mathf.Clamp(impactSpeed * 0.16f, 0f, 2.4f);
        }

        /// <summary>
        /// Shakes the view, for an explosion nearby. 0 to 1; shakes add up and die away over
        /// about a second. Only the picture moves: where the gun points is untouched.
        /// </summary>
        public void AddShake(float amount)
        {
            if (!GameSettings.CameraShake) return;
            _trauma = Mathf.Clamp01(_trauma + amount);
        }

        /// <summary>Knocks the view a little away from where a hit came from. <paramref name="fromRight"/> is -1 (left) to 1 (right).</summary>
        public void AddFlinch(float strength, float fromRight)
        {
            if (!GameSettings.CameraShake) return;
            _flinchVelocity += new Vector2(-fromRight, 1f) * Mathf.Clamp01(strength) * 22f;
        }

        public void Apply(in CameraPose pose, float deltaTime)
        {
            float dt = Mathf.Min(deltaTime, 1f / 30f);

            // Damped spring pulling the landing dip back to rest.
            const float stiffness = 170f, damping = 20f;
            _dipVelocity += (-stiffness * _dip - damping * _dipVelocity) * dt;
            _dip = Mathf.Clamp(_dip + _dipVelocity * dt, -0.3f, 0.1f);

            // The same kind of spring for a flinch, in degrees.
            _flinchVelocity += (-260f * _flinch - 24f * _flinchVelocity) * dt;
            _flinch += _flinchVelocity * dt;

            // Shake: smooth noise, scaled by trauma squared so small knocks stay small.
            Vector3 shake = Vector3.zero;
            if (_trauma > 0f)
            {
                _shakeTime += dt * 22f;
                float power = _trauma * _trauma;
                shake = new Vector3(
                    (Mathf.PerlinNoise(_shakeTime, 0.3f) * 2f - 1f) * MaxShake.x,
                    (Mathf.PerlinNoise(1.7f, _shakeTime) * 2f - 1f) * MaxShake.y,
                    (Mathf.PerlinNoise(_shakeTime, 4.1f) * 2f - 1f) * MaxShake.z) * power;
                _trauma = Mathf.Max(0f, _trauma - dt * 1.4f);
            }

            transform.SetPositionAndRotation(
                pose.EyePosition + Vector3.up * _dip,
                Quaternion.Euler(pose.Pitch + shake.y - _flinch.y, pose.Yaw + shake.x + _flinch.x, shake.z));

            // The setting is a horizontal angle at 16:9. Holding the vertical angle fixed
            // means wider screens see more to the sides, never less.
            float hipFov = Camera.HorizontalToVerticalFieldOfView(GameSettings.FieldOfView, ReferenceAspect);
            float adsFov = 2f * Mathf.Atan(Mathf.Tan(hipFov * 0.5f * Mathf.Deg2Rad) / Mathf.Max(pose.AdsZoom, 1f)) * Mathf.Rad2Deg;

            float kickTarget = pose.Sliding ? SlideFovKick : pose.Sprinting ? SprintFovKick : 0f;
            _fovKick = Mathf.Lerp(_fovKick, kickTarget, 1f - Mathf.Exp(-10f * dt));

            float ads = Mathf.SmoothStep(0f, 1f, pose.AdsAmount);
            _camera.fieldOfView = Mathf.Lerp(hipFov + _fovKick, adsFov, ads);
        }
    }
}
