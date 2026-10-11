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
        Transform model;
        readonly Transform[] wheels = new Transform[4];
        readonly Transform[] calipers = new Transform[4];
        Vector3 prevPos, curPos;
        Quaternion prevRot, curRot;
        readonly double[] prevLen = new double[4], curLen = new double[4];
        readonly double[] prevSpin = new double[4], curSpin = new double[4];
        Material rearLights, frontLights;
        readonly V3 tmp = new V3();
        public bool Headlights = true;

        static readonly string[] WheelNames = { "FL", "FR", "RL", "RR" };

        public static CarRig Create(Vehicle v, string specId, Color paint)
        {
            var go = new GameObject("Car_" + specId);
            var rig = go.AddComponent<CarRig>();
            rig.Vehicle = v;
            rig.Build(specId, paint);
            rig.Snap();
            return rig;
        }

        void Build(string specId, Color paint)
        {
            var p = Vehicle.Params;
            var prefab = Resources.Load<GameObject>("Cars/" + specId);
            GameObject m = prefab != null ? Instantiate(prefab) : FallbackModel(p, paint);
            m.name = "Model";
            model = m.transform;
            model.SetParent(transform, false);
            // The model's origin is the ground at mid-wheelbase; the root is the centre of mass.
            model.localPosition = new Vector3(0, (float)-p.cgHeight, (float)((p.a - p.b) / 2));
            model.localRotation = Quaternion.identity;
            AssignMaterials(m, paint);
            // Wheels and callipers move in the root's frame from the physics.
            for (int i = 0; i < 4; i++)
            {
                var wp = p.wheels[i];
                string tag = (wp.front ? "F" : "R") + (wp.left ? "L" : "R");
                wheels[i] = Pivot(FindDeep(model, "Wheel_" + tag), "WheelPivot_" + tag);
                calipers[i] = Pivot(FindDeep(model, "Caliper_" + tag), "CaliperPivot_" + tag);
            }
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
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

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        void AssignMaterials(GameObject m, Color paint)
        {
            var paintMat = Mats.Paint(paint);
            var glass = Mats.Make("CarGlass", new Color(0.02f, 0.025f, 0.03f), 0.96f, 0f, shader: Mats.ComplexLit);
            glass.SetFloat("_ClearCoatMask", 1f);
            glass.SetFloat("_ClearCoatSmoothness", 1f);
            glass.EnableKeyword("_CLEARCOAT");
            var trim = Mats.Make("CarTrim", new Color(0.03f, 0.03f, 0.035f), 0.45f);
            frontLights = Mats.Make("CarLightFront", new Color(0.9f, 0.92f, 0.95f), 0.95f);
            Mats.SetEmission(frontLights, new Color(0.9f, 0.95f, 1f) * 2f);
            rearLights = Mats.Make("CarLightRear", new Color(0.25f, 0.01f, 0.01f), 0.92f);
            Mats.SetEmission(rearLights, new Color(1f, 0.05f, 0.03f) * 0.6f);
            var rubber = Mats.Make("CarRubber", new Color(0.035f, 0.035f, 0.038f), 0.12f);
            var rim = Mats.Make("CarRim", new Color(0.45f, 0.47f, 0.5f), 0.8f, 1f);
            var brake = Mats.Make("CarBrake", new Color(0.3f, 0.3f, 0.31f), 0.5f, 1f);
            var caliper = Mats.Make("CarCaliper", new Color(0.95f, 0.65f, 0.1f), 0.7f, 0.2f);
            var chrome = Mats.Make("CarChrome", new Color(0.9f, 0.9f, 0.92f), 0.95f, 1f);
            foreach (var r in m.GetComponentsInChildren<Renderer>())
            {
                var src = r.sharedMaterials;
                var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    string n = src[i] != null ? src[i].name : "";
                    dst[i] =
                        n.Contains("Paint") ? paintMat :
                        n.Contains("Glass") ? glass :
                        n.Contains("LightFront") ? frontLights :
                        n.Contains("LightRear") ? rearLights :
                        n.Contains("Trim") ? trim :
                        n.Contains("Rubber") ? rubber :
                        n.Contains("Rim") ? rim :
                        n.Contains("Brake") ? brake :
                        n.Contains("Caliper") ? caliper :
                        n.Contains("Chrome") ? chrome :
                        src[i];
                }
                r.sharedMaterials = dst;
            }
        }

        /// <summary>A plain stand-in when the Blender model is not in Resources.</summary>
        static GameObject FallbackModel(VehicleParams p, Color paint)
        {
            var root = new GameObject("Fallback");
            var spec = p.spec;
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3((float)spec.width, (float)(spec.height * 0.45), (float)spec.length);
            body.transform.localPosition = new Vector3(0, (float)(spec.groundClearance + spec.height * 0.25), 0);
            Object.Destroy(body.GetComponent<Collider>());
            body.GetComponent<Renderer>().sharedMaterial = Mats.Paint(paint);
            body.GetComponent<Renderer>().sharedMaterial.name = "Halcyon_Paint";
            var cabin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cabin.transform.SetParent(root.transform, false);
            cabin.transform.localScale = new Vector3((float)spec.width * 0.75f, (float)(spec.height * 0.4), (float)spec.length * 0.45f);
            cabin.transform.localPosition = new Vector3(0, (float)(spec.height * 0.72), -0.2f);
            Object.Destroy(cabin.GetComponent<Collider>());
            cabin.GetComponent<Renderer>().sharedMaterial = Mats.Make("Halcyon_Glass", new Color(0.05f, 0.06f, 0.07f), 0.9f);
            for (int i = 0; i < 4; i++)
            {
                var wp = p.wheels[i];
                var w = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.Destroy(w.GetComponent<Collider>());
                var holder = new GameObject("Wheel_" + (wp.front ? "F" : "R") + (wp.left ? "L" : "R"));
                holder.transform.SetParent(root.transform, false);
                w.transform.SetParent(holder.transform, false);
                w.transform.localRotation = Quaternion.Euler(0, 0, 90);
                float r = (float)wp.radius;
                w.transform.localScale = new Vector3(r * 2, 0.12f, r * 2);
                w.GetComponent<Renderer>().sharedMaterial = Mats.Make("Halcyon_Rubber", new Color(0.04f, 0.04f, 0.04f), 0.1f);
            }
            return root;
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
            if (rearLights != null) Mats.SetEmission(rearLights, new Color(1f, 0.05f, 0.03f) * (braking ? 4f : Headlights ? 1.2f : 0.5f));
            if (frontLights != null) Mats.SetEmission(frontLights, new Color(0.9f, 0.95f, 1f) * (Headlights ? 3f : 0.6f));
        }
    }
}
