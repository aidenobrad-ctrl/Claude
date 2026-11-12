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
    /// controller each frame, and this adds the cosmetic layer on top (zoom,
    /// landing dip, speed-based field of view).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class FirstPersonCamera : MonoBehaviour
    {
        const float ReferenceAspect = 16f / 9f;
        const float SprintFovKick = 3f;
        const float SlideFovKick = 6f;

        Camera _camera;
        float _dip, _dipVelocity, _fovKick;

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

        public void Apply(in CameraPose pose, float deltaTime)
        {
            float dt = Mathf.Min(deltaTime, 1f / 30f);

            // Damped spring pulling the landing dip back to rest.
            const float stiffness = 170f, damping = 20f;
            _dipVelocity += (-stiffness * _dip - damping * _dipVelocity) * dt;
            _dip = Mathf.Clamp(_dip + _dipVelocity * dt, -0.3f, 0.1f);

            transform.SetPositionAndRotation(
                pose.EyePosition + Vector3.up * _dip,
                Quaternion.Euler(pose.Pitch, pose.Yaw, 0f));

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
