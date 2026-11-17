using UnityEngine;
using Pulsebreak.Shared.Movement;

namespace Pulsebreak.Client.Audio
{
    /// <summary>
    /// Decides when a moving body takes a step and how loud it is. Sprinting is loud,
    /// running is clear, crouch-walking is nearly silent: being heard is a choice.
    /// </summary>
    public sealed class FootstepTimer
    {
        const float MinSpeed = 1.2f, SprintSpeed = 8.6f;
        float _travelled;

        /// <summary>The loudness of a step taken this frame, 0 to 1, or 0 for none.</summary>
        public float Advance(Vector3 velocity, bool grounded, Stance stance, float deltaTime)
        {
            float speed = new Vector2(velocity.x, velocity.z).magnitude;
            if (!grounded || stance == Stance.Sliding || speed < MinSpeed)
            {
                // The first step after stopping, landing or sliding comes quickly.
                _travelled = Mathf.Max(_travelled, Stride(speed) * 0.6f);
                return 0f;
            }

            _travelled += speed * deltaTime;
            float stride = Stride(speed);
            if (_travelled < stride) return 0f;
            _travelled -= stride;
            if (_travelled > stride) _travelled = 0f;

            if (stance == Stance.Crouching) return 0.18f;
            return Mathf.Lerp(0.45f, 1f, Mathf.InverseLerp(4f, SprintSpeed, speed));
        }

        /// <summary>Metres between steps: longer strides the faster the body goes.</summary>
        static float Stride(float speed) => Mathf.Lerp(0.9f, 2.4f, Mathf.InverseLerp(2f, SprintSpeed, speed));
    }
}
