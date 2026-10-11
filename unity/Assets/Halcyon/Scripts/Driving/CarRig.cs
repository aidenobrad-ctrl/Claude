// A car's visible body: the Blender model (Resources/Cars/<id>), posed from
// the simulation each frame. The root follows the physics body (centre of
// mass) interpolated between 240 Hz steps; wheels move with the suspension,
// steer and spin; callipers steer but do not spin; brake and reverse lights
// glow from the driver's inputs.
using Halcyon.Core;
using UnityEngine;

namespace Halcyon
{
    public sealed class CarRig : MonoBehaviour
    {
        public Vehicle Vehicle { get; private set; }
        public CarVisual Visual { get; private set; }
        Transform model;
        readonly Transform[] wheels = new Transform[4];
        readonly Transform[] calipers = new Transform[4];
        Vector3 prevPos, curPos;
        Quaternion prevRot, curRot;
        readonly double[] prevLen = new double[4], curLen = new double[4];
        readonly double[] prevSpin = new double[4], curSpin = new double[4];
        public bool Headlights = true;

        public static CarRig Create(Vehicle v, string specId, Color? paint = null)
        {
            var go = new GameObject("Car_" + specId);
            var rig = go.AddComponent<CarRig>();
            rig.Vehicle = v;
            rig.Build(specId, paint);
            rig.Snap();
            return rig;
        }

        void Build(string specId, Color? paint)
        {
            var p = Vehicle.Params;
            Visual = CarVisual.Create(specId, p, paint);
            model = Visual.Root.transform;
            model.SetParent(transform, false);
            // The model's origin is the ground at mid-wheelbase; the root is the centre of mass.
            model.localPosition = new Vector3(0, (float)-p.cgHeight, (float)((p.a - p.b) / 2));
            model.localRotation = Quaternion.identity;
            // Wheels and callipers move in the root's frame from the physics.
            for (int i = 0; i < 4; i++)
            {
                var wp = p.wheels[i];
                string tag = (wp.front ? "F" : "R") + (wp.left ? "L" : "R");
                int k = (wp.front ? 0 : 2) + (wp.left ? 0 : 1);
                wheels[i] = Pivot(Visual.Wheels[k], "WheelPivot_" + tag);
                calipers[i] = Pivot(Visual.Calipers[k], "CaliperPivot_" + tag);
            }
        }

        /// <summary>
        /// A pivot at the part's origin (the hub) aligned with the car, holding the
        /// part; the pivot is what moves, so the import's own axes never matter.
        /// </summary>
        Transform Pivot(Transform part, string name)
        {
            if (part == null) return null;
            var pivot = new GameObject(name).transform;
            pivot.SetParent(transform, false);
            pivot.position = part.position;
            pivot.rotation = transform.rotation;
            part.SetParent(pivot, true);
            return pivot;
        }

        /// <summary>Record the simulation state after a step (call once per fixed step).</summary>
        public void Capture()
        {
            prevPos = curPos;
            prevRot = curRot;
            var v = Vehicle;
            curPos = Frame.Pos(v.pos.x, v.pos.y, v.pos.z);
            curRot = Frame.Rot(v.quat);
            for (int i = 0; i < 4; i++)
            {
                prevLen[i] = curLen[i];
                prevSpin[i] = curSpin[i];
                curLen[i] = v.wheels[i].length;
                curSpin[i] = v.wheels[i].spin;
            }
        }

        /// <summary>Jump straight to the current state (after spawns, resets, teleports).</summary>
        public void Snap()
        {
            Capture();
            Capture();
            Apply(1f);
        }

        /// <summary>Pose the model between the previous and current physics states.</summary>
        public void Apply(float alpha)
        {
            transform.SetPositionAndRotation(Vector3.LerpUnclamped(prevPos, curPos, alpha), Quaternion.Slerp(prevRot, curRot, alpha));
            var v = Vehicle;
            var p = v.Params;
            for (int i = 0; i < 4; i++)
            {
                var h = p.wheels[i].hardpoint;
                double len = prevLen[i] + (curLen[i] - prevLen[i]) * alpha;
                double spin = prevSpin[i] + (curSpin[i] - prevSpin[i]) * alpha;
                var local = Frame.Pos(h.x, h.y - len, h.z);
                var steer = Quaternion.Euler(0, (float)(-v.wheels[i].steer * Mathf.Rad2Deg), 0);
                if (wheels[i] != null)
                {
                    wheels[i].localPosition = local;
                    wheels[i].localRotation = steer * Quaternion.Euler((float)(spin * Mathf.Rad2Deg), 0, 0);
                }
                if (calipers[i] != null)
                {
                    calipers[i].localPosition = local;
                    calipers[i].localRotation = steer;
                }
            }
            bool braking = v.gear == -1 ? v.input.throttle > 0.05 : v.input.brake > 0.05;
            Visual.SetLights(Headlights, braking);
        }
    }
}
