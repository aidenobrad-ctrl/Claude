using UnityEngine;

namespace Pulsebreak.Shared.Movement
{
    /// <summary>
    /// The velocity maths behind the motor, kept free of physics calls so it can be unit tested.
    /// All vectors passed in are horizontal (y = 0).
    /// </summary>
    public static class MoveMath
    {
        /// <summary>
        /// Ground friction. Speed drops in proportion to itself, but never slower than as if
        /// moving at <paramref name="stopSpeed"/>, which is what makes stops crisp.
        /// </summary>
        public static Vector3 ApplyFriction(Vector3 velocity, float friction, float stopSpeed, float dt)
        {
            float speed = velocity.magnitude;
            if (speed < 0.0001f) return Vector3.zero;

            float drop = Mathf.Max(speed, stopSpeed) * friction * dt;
            float newSpeed = Mathf.Max(speed - drop, 0f);
            return velocity * (newSpeed / speed);
        }

        /// <summary>
        /// Accelerates towards <paramref name="wishDir"/> until the speed along that direction
        /// reaches <paramref name="wishSpeed"/>.
        ///
        /// The result is capped at the larger of the speed going in and <paramref name="wishSpeed"/>.
        /// So input can always redirect momentum, on the ground or in the air, but it can never
        /// build speed beyond what the stance allows: not by strafing in the air, and not by
        /// running diagonally into a wall. Speed gained elsewhere (a slide) is kept, not clamped.
        /// </summary>
        public static Vector3 Accelerate(Vector3 velocity, Vector3 wishDir, float wishSpeed, float acceleration, float dt)
        {
            float before = velocity.magnitude;
            float add = wishSpeed - Vector3.Dot(velocity, wishDir);
            if (add <= 0f) return velocity;

            Vector3 result = velocity + wishDir * Mathf.Min(acceleration * wishSpeed * dt, add);

            float limit = Mathf.Max(before, wishSpeed);
            float after = result.magnitude;
            if (after > limit && after > 0.0001f) result *= limit / after;
            return result;
        }

        /// <summary>Rotates a horizontal vector around the up axis. Positive turns right.</summary>
        public static Vector3 RotateYaw(Vector3 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            float s = Mathf.Sin(r), c = Mathf.Cos(r);
            return new Vector3(v.x * c + v.z * s, v.y, v.z * c - v.x * s);
        }
    }
}
