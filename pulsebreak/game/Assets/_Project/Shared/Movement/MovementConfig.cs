using UnityEngine;

namespace Pulsebreak.Shared.Movement
{
    /// <summary>
    /// Every movement tuning value in one asset. Distances are metres, speeds metres per
    /// second, times seconds. Change values here (or on a copy of the asset) rather than in code.
    /// </summary>
    [CreateAssetMenu(menuName = "Pulsebreak/Movement Config", fileName = "MovementConfig")]
    public sealed class MovementConfig : ScriptableObject
    {
        [Header("Body")]
        public float standHeight = 1.8f;
        public float crouchHeight = 1.15f;
        public float radius = 0.35f;
        public float standEyeHeight = 1.62f;
        public float crouchEyeHeight = 0.98f;
        public float slideEyeHeight = 0.82f;
        [Tooltip("How fast the eye moves between stance heights, in m/s.")]
        public float eyeHeightSpeed = 5.5f;
        public float stepOffset = 0.35f;
        public float slopeLimit = 50f;

        [Header("Ground")]
        public float runSpeed = 5.6f;
        public float sprintSpeed = 7.8f;
        public float crouchSpeed = 3.2f;
        [Tooltip("Higher reaches top speed sooner. Must be at least groundFriction or top speed is never reached.")]
        public float groundAcceleration = 14f;
        public float groundFriction = 8f;
        [Tooltip("Below this speed friction acts as if moving at this speed, so stops are crisp.")]
        public float stopSpeed = 2.5f;
        [Tooltip("Downward speed held while grounded so the capsule stays glued to slopes and steps.")]
        public float groundStickSpeed = 3f;

        [Header("Air")]
        public float gravity = 24f;
        public float jumpHeight = 1.2f;
        [Tooltip("Steering strength in the air. Steering redirects speed but never adds any beyond the stance speed.")]
        public float airAcceleration = 3.5f;
        public float terminalVelocity = 45f;
        [Tooltip("Grace period after walking off a ledge in which a jump still works.")]
        public float coyoteTime = 0.1f;
        [Tooltip("A jump pressed this long before landing still fires on landing.")]
        public float jumpBufferTime = 0.12f;

        [Header("Slide")]
        [Tooltip("Minimum speed needed to start a slide.")]
        public float slideMinEntrySpeed = 6.2f;
        [Tooltip("Starting a slide raises speed to at least this.")]
        public float slideBoostSpeed = 10.5f;
        public float slideMaxSpeed = 13f;
        [Tooltip("Constant deceleration while sliding, in m/s².")]
        public float slideFriction = 5f;
        [Tooltip("The slide ends when speed drops below this.")]
        public float slideEndSpeed = 3.6f;
        public float slideMinDuration = 0.25f;
        public float slideMaxDuration = 1.0f;
        [Tooltip("Delay after a slide ends before another can start.")]
        public float slideCooldown = 0.6f;
        public float slideSteerDegPerSec = 45f;
        [Tooltip("How much of gravity along a slope feeds a slide. 0 turns slope acceleration off.")]
        public float slideSlopeFactor = 1f;

        [Header("Mantle")]
        [Tooltip("Lowest ledge, above the feet, that is climbed instead of stepped or jumped.")]
        public float mantleMinHeight = 0.55f;
        [Tooltip("Highest ledge, above the feet, that can be climbed. Measured from wherever the feet are, so a jump extends reach.")]
        public float mantleMaxHeight = 1.6f;
        [Tooltip("How far in front of the capsule a ledge can be.")]
        public float mantleReach = 0.45f;
        public float mantleMinDuration = 0.2f;
        public float mantleMaxDuration = 0.38f;
        public float mantleExitSpeed = 3f;
        public float mantleCooldown = 0.25f;

        [Header("Lean")]
        [Tooltip("Sideways eye offset at full lean.")]
        public float leanOffset = 0.38f;
        [Tooltip("Camera roll at full lean, in degrees.")]
        public float leanAngle = 13f;
        [Tooltip("Lean change per second (1 = full lean).")]
        public float leanSpeed = 7f;
        public float leanProbeRadius = 0.14f;

        public float JumpSpeed => Mathf.Sqrt(2f * gravity * jumpHeight);

        public float EyeHeightFor(Stance stance)
        {
            switch (stance)
            {
                case Stance.Crouching: return crouchEyeHeight;
                case Stance.Sliding: return slideEyeHeight;
                default: return standEyeHeight;
            }
        }

        public float CapsuleHeightFor(Stance stance) =>
            stance == Stance.Standing ? standHeight : crouchHeight;
    }
}
