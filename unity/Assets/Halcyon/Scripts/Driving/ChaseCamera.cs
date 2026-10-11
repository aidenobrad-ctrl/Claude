// Player cameras: chase, far chase and bonnet. The chase cameras damp their
// offset relative to the car (not their absolute position), so they never
// fall behind at speed, and swing toward the direction of travel in a slide.
// Ported from the web version's src/engine/render/camera-rig.ts.
using UnityEngine;

namespace Halcyon
{
    public enum CamMode { Chase, Far, Bonnet }

    public sealed class ChaseCamera
    {
        public readonly Camera Cam;
        public CamMode Mode = CamMode.Chase;
        public bool LookBack;
        public bool ReducedShake;
        Vector3 offset = new Vector3(0, 2, -6);
        Vector3 dir = Vector3.forward;
        float shakeT, kick, speedFov;
        bool snapNext = true;
        Quaternion bonnetRot = Quaternion.identity;

        public ChaseCamera(Camera cam)
        {
            Cam = cam;
        }

        public CamMode Cycle()
        {
            Mode = (CamMode)(((int)Mode + 1) % 3);
            snapNext = true;
            return Mode;
        }

        public void Snap() => snapNext = true;

        public void Impact(float strength) => kick = Mathf.Min(1, kick + strength);

        /// <summary>
        /// pos/rot: the car body (centre of mass) in Unity space; vel its velocity;
        /// bonnetY/bonnetZ: the bonnet camera above and ahead of the centre of mass.
        /// </summary>
        public void Update(float dt, Vector3 pos, Quaternion rot, Vector3 vel, float bonnetY, float bonnetZ, System.Func<Vector3, float> groundY)
        {
            var t = Cam.transform;
            float speed = vel.magnitude;
            var fwd = rot * Vector3.forward;
            bool portrait = Cam.aspect < 0.9f;
            bool snap = snapNext;
            snapNext = false;
            float baseFov;
            if (Mode == CamMode.Bonnet)
            {
                // Rigidly attached, with a little rotational smoothing against jitter.
                t.position = pos + rot * new Vector3(0, bonnetY, bonnetZ);
                bonnetRot = Quaternion.Slerp(bonnetRot, rot, snap ? 1 : 1 - Mathf.Exp(-dt * 25));
                t.rotation = LookBack ? bonnetRot * Quaternion.Euler(0, 180, 0) : bonnetRot;
                baseFov = 68;
            }
            else
            {
                bool far = Mode == CamMode.Far;
                float dist = (far ? 8.6f : 5.6f) * (portrait ? 1.35f : 1) + Mathf.Min(speed, 70) * 0.012f;
                float height = (far ? 2.9f : 1.75f) * (portrait ? 1.25f : 1);
                // Aim along a blend of heading and travel so drifts stay readable.
                var flat = new Vector3(fwd.x, 0, fwd.z).normalized;
                if (speed > 4)
                {
                    var tv = new Vector3(vel.x, 0, vel.z).normalized;
                    if (Vector3.Dot(tv, flat) > 0) flat = Vector3.Lerp(flat, tv, 0.35f).normalized;
                }
                dir = snap ? flat : Vector3.Lerp(dir, flat, 1 - Mathf.Exp(-dt * (far ? 4 : 5))).normalized;
                float back = LookBack ? -1 : 1;
                var desired = dir * (-dist * back) + Vector3.up * height;
                offset = snap ? desired : Vector3.Lerp(offset, desired, 1 - Mathf.Exp(-dt * 7));
                var p = pos + offset;
                if (groundY != null)
                {
                    float gy = groundY(p);
                    if (p.y < gy + 0.6f) p.y = gy + 0.6f;
                }
                t.position = p;
                var target = pos + Vector3.up * (far ? 1.1f : 0.85f) + dir * (2.6f * back);
                t.rotation = Quaternion.LookRotation(target - p, Vector3.up);
                baseFov = far ? 58 : 62;
            }
            // Speed adds a little FOV; impacts and high speed add a little shake.
            float targetFov = Mathf.Min(14, Mathf.Max(0, (speed - 15) * 0.16f));
            speedFov += (targetFov - speedFov) * (1 - Mathf.Exp(-dt * 2));
            Cam.fieldOfView = FovForAspect(baseFov + speedFov, 72, Cam.aspect);
            shakeT += dt;
            float shakeAmp = ReducedShake ? 0 : Mathf.Max(0, speed - 45) * 0.0006f + kick * 0.06f;
            if (shakeAmp > 0)
                t.position += new Vector3(Mathf.Sin(shakeT * 37.1f) * shakeAmp, Mathf.Sin(shakeT * 29.3f + 1.3f) * shakeAmp * 0.7f, 0);
            kick *= Mathf.Exp(-dt * 6);
        }

        /// <summary>Vertical FOV that keeps at least minHorizontal degrees across the screen (tall phones).</summary>
        public static float FovForAspect(float vertical, float minHorizontal, float aspect)
        {
            float hFromV = 2 * Mathf.Atan(Mathf.Tan(vertical * Mathf.Deg2Rad / 2) * aspect) * Mathf.Rad2Deg;
            if (hFromV >= minHorizontal) return vertical;
            float v = 2 * Mathf.Atan(Mathf.Tan(minHorizontal * Mathf.Deg2Rad / 2) / aspect) * Mathf.Rad2Deg;
            return Mathf.Min(v, 100);
        }
    }
}
