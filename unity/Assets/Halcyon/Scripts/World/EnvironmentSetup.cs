// Sun, sky, fog and post-processing for the island. Post-processing uses
// URP's Volume system when URP is present (looked up by reflection so this
// compiles and runs in any pipeline).
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace Halcyon
{
    public static class EnvironmentSetup
    {
        public static Light Build(Transform parent)
        {
            // The sun: late-afternoon light from the south-west.
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(parent, false);
            sunGo.transform.rotation = Quaternion.Euler(36f, 215f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1.0f, 0.93f, 0.82f);
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.9f;
            sun.shadowBias = 0.04f;
            sun.shadowNormalBias = 0.3f;

            var skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                var sky = new Material(skyShader) { name = "Sky" };
                sky.SetFloat("_SunSize", 0.035f);
                sky.SetFloat("_SunSizeConvergence", 6f);
                sky.SetFloat("_AtmosphereThickness", 1.05f);
                sky.SetColor("_SkyTint", new Color(0.52f, 0.62f, 0.78f));
                sky.SetColor("_GroundColor", new Color(0.36f, 0.38f, 0.40f));
                sky.SetFloat("_Exposure", 1.2f);
                RenderSettings.skybox = sky;
            }
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.0f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1.0f;
            // Aerial perspective: distant hills fade into a sky-coloured haze.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.00022f;
            RenderSettings.fogColor = new Color(0.66f, 0.74f, 0.84f);
            DynamicGI.UpdateEnvironment();
            QualitySettings.shadowDistance = Mathf.Max(QualitySettings.shadowDistance, 450f);
            QualitySettings.shadowCascades = 4;
            return sun;
        }

        /// <summary>Camera defaults; post-processing (bloom, ACES, grading, vignette) through URP if available.</summary>
        public static void SetupCamera(Camera cam, Transform parent)
        {
            cam.nearClipPlane = 0.25f;
            cam.farClipPlane = 9000f;
            cam.allowHDR = true;
            cam.clearFlags = CameraClearFlags.Skybox;
            TryUrpPost(cam, parent);
        }

        static Type T(string name) => Type.GetType(name);

        static void TryUrpPost(Camera cam, Transform parent)
        {
            try
            {
                var volumeT = T("UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime");
                var profileT = T("UnityEngine.Rendering.VolumeProfile, Unity.RenderPipelines.Core.Runtime");
                var camDataT = T("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
                if (volumeT == null || profileT == null || camDataT == null) return;
                // Camera: post-processing on, anti-aliasing.
                var camData = cam.GetComponent(camDataT);
                if (camData == null) camData = cam.gameObject.AddComponent(camDataT);
                Set(camData, "renderPostProcessing", true);
                var aaT = T("UnityEngine.Rendering.Universal.AntialiasingMode, Unity.RenderPipelines.Universal.Runtime");
                if (aaT != null)
                {
                    object aa = Enum.IsDefined(aaT, "TemporalAntiAliasing") ? Enum.Parse(aaT, "TemporalAntiAliasing") : Enum.Parse(aaT, "SubpixelMorphologicalAntiAliasing");
                    Set(camData, "antialiasing", aa);
                }
                // A global volume.
                var go = new GameObject("PostProcessing");
                go.transform.SetParent(parent, false);
                var volume = go.AddComponent(volumeT);
                Set(volume, "isGlobal", true);
                var profile = ScriptableObject.CreateInstance(profileT);
                Set(volume, "sharedProfile", profile);
                var add = profileT.GetMethod("Add", new[] { typeof(Type), typeof(bool) });
                object Comp(string type)
                {
                    var t = T($"UnityEngine.Rendering.Universal.{type}, Unity.RenderPipelines.Universal.Runtime");
                    return t == null ? null : add.Invoke(profile, new object[] { t, true });
                }
                var bloom = Comp("Bloom");
                Param(bloom, "intensity", 0.55f);
                Param(bloom, "threshold", 1.05f);
                Param(bloom, "scatter", 0.65f);
                var tone = Comp("Tonemapping");
                var tmT = T("UnityEngine.Rendering.Universal.TonemappingMode, Unity.RenderPipelines.Universal.Runtime");
                if (tmT != null) Param(tone, "mode", Enum.Parse(tmT, "ACES"));
                var grade = Comp("ColorAdjustments");
                Param(grade, "postExposure", 0.15f);
                Param(grade, "contrast", 12f);
                Param(grade, "saturation", 12f);
                var vignette = Comp("Vignette");
                Param(vignette, "intensity", 0.18f);
                Param(vignette, "smoothness", 0.45f);
                var blur = Comp("MotionBlur");
                Param(blur, "intensity", 0.12f);
                var wb = Comp("WhiteBalance");
                Param(wb, "temperature", 6f);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Halcyon: post-processing setup skipped: " + e.Message);
            }
        }

        static void Set(object o, string name, object value)
        {
            if (o == null) return;
            var t = o.GetType();
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite)
            {
                p.SetValue(o, value);
                return;
            }
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f != null) f.SetValue(o, value);
        }

        /// <summary>Set VolumeParameter field `name` of a VolumeComponent: value and override.</summary>
        static void Param(object comp, string name, object value)
        {
            if (comp == null) return;
            var f = comp.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f == null) return;
            var param = f.GetValue(comp);
            if (param == null) return;
            var pt = param.GetType();
            var vp = pt.GetProperty("value", BindingFlags.Public | BindingFlags.Instance);
            if (vp != null && vp.CanWrite) vp.SetValue(param, value);
            var op = pt.GetProperty("overrideState", BindingFlags.Public | BindingFlags.Instance);
            if (op != null && op.CanWrite) op.SetValue(param, true);
        }
    }
}
