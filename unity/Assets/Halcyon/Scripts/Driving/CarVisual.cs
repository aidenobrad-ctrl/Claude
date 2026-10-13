// A car's model as the player sees it: the Blender FBX from Resources/Cars,
// its materials remapped to URP materials (clear-coat paint, tinted glass,
// chrome, lights that the rig can switch), and one LODGroup over the
// <part>_LOD0..2 meshes. Used by the driving rig and the showroom alike.
using System.Collections.Generic;
using Halcyon.Core;
using UnityEngine;

namespace Halcyon
{
    public sealed class CarVisual
    {
        /// <summary>Colours and finishes from Resources/Cars/&lt;id&gt;.style.json (written by blender/build_car.py).</summary>
        public sealed class Style
        {
            public string id, name;
            public Color paint = new Color(0.78f, 0.15f, 0.18f), accent = new Color(0.07f, 0.07f, 0.07f);
            public Color rim = new Color(0.6f, 0.62f, 0.65f), caliper = new Color(0.85f, 0.17f, 0.11f), interior = new Color(0.1f, 0.1f, 0.11f);
            public float metallic = 0.55f, paintRoughness = 0.32f, accentMetallic = 0.3f, rimMetallic = 1f, rimRoughness = 0.24f;
        }

        public GameObject Root { get; private set; }
        public Style Look { get; private set; }
        /// <summary>FL, FR, RL, RR (null when the model has none).</summary>
        public readonly Transform[] Wheels = new Transform[4];
        public readonly Transform[] Calipers = new Transform[4];
        public Material PaintMat { get; private set; }
        Material front, drl, rear, rearBar;

        static readonly string[] WheelTags = { "FL", "FR", "RL", "RR" };

        public static Style LoadStyle(string specId)
        {
            var s = new Style { id = specId, name = specId };
            var ta = Resources.Load<TextAsset>("Cars/" + specId + ".style");
            if (ta == null)
            {
                foreach (var c in Halcyon.Core.Cars.All)
                {
                    if (c.id != specId) continue;
                    s.name = c.make + " " + c.model;
                    s.paint = Hex(c.visual.paint);
                }
                return s;
            }
            if (!(MiniJson.Parse(ta.text) is Dictionary<string, object> d)) return s;
            string Str(string k, string def) => d.TryGetValue(k, out var v) && v is string t ? t : def;
            float Num(string k, float def) => (float)d.Num(k, def);
            s.name = Str("name", s.name);
            s.paint = Hex(Str("paint", "#C7262E"));
            s.accent = Hex(Str("accent", "#111111"));
            s.rim = Hex(Str("rim", "#9A9FA6"));
            s.caliper = Hex(Str("caliper", "#D92B1C"));
            s.interior = Hex(Str("interior", "#1A1A1C"));
            s.metallic = Num("metallic", 0.55f);
            s.paintRoughness = Num("paintRoughness", 0.32f);
            s.accentMetallic = Num("accentMetallic", 0.3f);
            s.rimMetallic = Num("rimMetallic", 1f);
            s.rimRoughness = Num("rimRoughness", 0.24f);
            return s;
        }

        public static Color Hex(string h)
        {
            return ColorUtility.TryParseHtmlString(h, out var c) ? c : Color.magenta;
        }

        public static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        /// <summary>Instantiate the car's model (or a plain stand-in) with its materials and LODs.</summary>
        public static CarVisual Create(string specId, VehicleParams p, Color? paint = null)
        {
            var v = new CarVisual { Look = LoadStyle(specId) };
            if (paint.HasValue) v.Look.paint = paint.Value;
            var prefab = Resources.Load<GameObject>("Cars/" + specId);
            v.Root = prefab != null ? Object.Instantiate(prefab) : Fallback(p, v.Look.paint);
            v.Root.name = "Model_" + specId;
            v.AssignMaterials();
            SetupLods(v.Root);
            for (int i = 0; i < 4; i++)
            {
                v.Wheels[i] = FindDeep(v.Root.transform, "Wheel_" + WheelTags[i]);
                v.Calipers[i] = FindDeep(v.Root.transform, "Caliper_" + WheelTags[i]);
            }
            foreach (var r in v.Root.GetComponentsInChildren<Renderer>(true))
            {
                bool glass = r.sharedMaterial != null && r.sharedMaterial.name == "CarGlass";
                r.shadowCastingMode = glass ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
            }
            return v;
        }

        public static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        /// <summary>One LODGroup on the root over every *_LODn renderer; parts without a suffix show at every LOD.</summary>
        public static void SetupLods(GameObject root)
        {
            foreach (var g in root.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(g);
            var lists = new[] { new List<Renderer>(), new List<Renderer>(), new List<Renderer>() };
            var always = new List<Renderer>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name;
                int k = n.LastIndexOf("_LOD");
                if (k >= 0 && k + 4 < n.Length && char.IsDigit(n[k + 4]) && n[k + 4] - '0' < 3) lists[n[k + 4] - '0'].Add(r);
                else always.Add(r);
                r.enabled = true;
            }
            if (lists[0].Count == 0) return;
            for (int i = 0; i < 3; i++)
            {
                if (lists[i].Count == 0) lists[i].AddRange(lists[i - 1]);
                else lists[i].AddRange(always);
            }
            var group = root.AddComponent<LODGroup>();
            group.SetLODs(new[]
            {
                new LOD(0.22f, lists[0].ToArray()),
                new LOD(0.07f, lists[1].ToArray()),
                new LOD(0.008f, lists[2].ToArray()),
            });
            group.fadeMode = LODFadeMode.None;
            group.RecalculateBounds();
        }

        void AssignMaterials()
        {
            var L = Look;
            PaintMat = Mats.Paint(L.paint, L.metallic);
            PaintMat.SetFloat("_Smoothness", 1f - L.paintRoughness * 0.9f);
            var accent = Mats.Paint(L.accent, L.accentMetallic);
            accent.name = "Accent";
            var glass = Mats.Make("CarGlass", new Color(0.03f, 0.04f, 0.045f, 0.62f), 0.97f, 0f);
            Mats.MakeTransparent(glass);
            var trim = Mats.Make("CarTrim", new Color(0.035f, 0.035f, 0.04f), 0.35f);
            var gloss = Mats.Make("CarGloss", new Color(0.012f, 0.012f, 0.014f), 0.92f, 0f, shader: Mats.ComplexLit);
            gloss.SetFloat("_ClearCoatMask", 1f);
            gloss.SetFloat("_ClearCoatSmoothness", 0.95f);
            gloss.EnableKeyword("_CLEARCOAT");
            var chrome = Mats.Make("CarChrome", new Color(0.92f, 0.92f, 0.94f), 0.96f, 1f);
            var grille = Mats.Make("CarGrille", new Color(0.02f, 0.02f, 0.022f), 0.25f);
            var gap = Mats.Make("CarGap", new Color(0.006f, 0.006f, 0.006f), 0.1f);
            var cladding = Mats.Make("CarCladding", new Color(0.045f, 0.045f, 0.048f), 0.18f);
            var carbon = Mats.Make("CarCarbon", new Color(0.03f, 0.032f, 0.036f), 0.85f, 0.3f);
            front = Mats.Make("CarLightFront", new Color(0.82f, 0.85f, 0.9f), 0.95f, 0.6f);
            drl = Mats.Make("CarDRL", new Color(0.95f, 0.97f, 1f), 0.9f);
            rear = Mats.Make("CarLightRear", new Color(0.3f, 0.01f, 0.01f), 0.92f);
            rearBar = Mats.Make("CarLightRearBar", new Color(0.6f, 0.02f, 0.02f), 0.9f);
            Mats.SetEmission(drl, new Color(0.9f, 0.95f, 1f) * 6f);
            var plate = Mats.Make("CarPlate", new Color(0.86f, 0.86f, 0.82f), 0.4f);
            var rubber = Mats.Make("CarRubber", new Color(0.035f, 0.035f, 0.038f), 0.12f);
            var rim = Mats.Make("CarRim", L.rim, 1f - L.rimRoughness * 0.85f, L.rimMetallic);
            var brake = Mats.Make("CarBrake", new Color(0.3f, 0.3f, 0.31f), 0.5f, 1f);
            var caliper = Mats.Make("CarCaliper", L.caliper, 0.7f, 0.2f);
            var dark = Mats.Make("CarDark", new Color(0.015f, 0.015f, 0.017f), 0.2f);
            var interior = Mats.Make("CarInterior", L.interior, 0.25f);
            var fabric = Mats.Make("CarFabric", new Color(0.03f, 0.03f, 0.032f), 0.05f);
            var mirror = Mats.Make("CarMirror", new Color(0.9f, 0.92f, 0.95f), 0.98f, 1f);
            foreach (var r in Root.GetComponentsInChildren<Renderer>(true))
            {
                var src = r.sharedMaterials;
                var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    string n = src[i] != null ? src[i].name : "";
                    dst[i] =
                        n.Contains("Paint") ? PaintMat :
                        n.Contains("Accent") ? accent :
                        n.Contains("Glass") ? glass :
                        n.Contains("LightFront") ? front :
                        n.Contains("DRL") ? drl :
                        n.Contains("LightRearBar") ? rearBar :
                        n.Contains("LightRear") ? rear :
                        n.Contains("Gloss") ? gloss :
                        n.Contains("Chrome") ? chrome :
                        n.Contains("Grille") ? grille :
                        n.Contains("Gap") ? gap :
                        n.Contains("Cladding") ? cladding :
                        n.Contains("Carbon") ? carbon :
                        n.Contains("Plate") ? plate :
                        n.Contains("Trim") ? trim :
                        n.Contains("Rubber") ? rubber :
                        n.Contains("Rim") ? rim :
                        n.Contains("Brake") ? brake :
                        n.Contains("Caliper") ? caliper :
                        n.Contains("Dark") ? dark :
                        n.Contains("Interior") ? interior :
                        n.Contains("Fabric") ? fabric :
                        n.Contains("Mirror") ? mirror :
                        src[i];
                }
                r.sharedMaterials = dst;
            }
        }

        /// <summary>Head and tail lights: tail lamps glow dimly with the headlights on, brightly when braking.</summary>
        public void SetLights(bool headlights, bool braking)
        {
            Mats.SetEmission(front, new Color(0.9f, 0.95f, 1f) * (headlights ? 3f : 0.4f));
            var tail = new Color(1f, 0.05f, 0.03f) * (braking ? 4f : headlights ? 1.2f : 0.4f);
            Mats.SetEmission(rear, tail);
            Mats.SetEmission(rearBar, tail * 1.6f);
        }

        public void SetPaint(Color c)
        {
            Look.paint = c;
            PaintMat.SetColor("_BaseColor", c);
            PaintMat.SetColor("_Color", c);
        }

        /// <summary>A plain stand-in when the Blender model is not in Resources.</summary>
        static GameObject Fallback(VehicleParams p, Color paint)
        {
            var root = new GameObject("Fallback");
            var spec = p.spec;
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body_LOD0";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3((float)spec.width, (float)(spec.height * 0.45), (float)spec.length);
            body.transform.localPosition = new Vector3(0, (float)(spec.groundClearance + spec.height * 0.25), 0);
            Object.Destroy(body.GetComponent<Collider>());
            body.GetComponent<Renderer>().sharedMaterial = new Material(Mats.Lit) { name = "Halcyon_Paint" };
            var cabin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cabin.name = "Glass_LOD0";
            cabin.transform.SetParent(root.transform, false);
            cabin.transform.localScale = new Vector3((float)spec.width * 0.75f, (float)(spec.height * 0.4), (float)spec.length * 0.45f);
            cabin.transform.localPosition = new Vector3(0, (float)(spec.height * 0.72), -0.2f);
            Object.Destroy(cabin.GetComponent<Collider>());
            cabin.GetComponent<Renderer>().sharedMaterial = new Material(Mats.Lit) { name = "Halcyon_Glass" };
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
                w.GetComponent<Renderer>().sharedMaterial = new Material(Mats.Lit) { name = "Halcyon_Rubber" };
            }
            return root;
        }
    }
}
