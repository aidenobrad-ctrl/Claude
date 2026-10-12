// Sun, sky, fog and post-processing for the island. Post-processing uses
// URP's Volume system when URP is present (looked up by reflection so this
// compiles and runs in any pipeline). The grade, motion blur, vignette and
// depth of field are kept so the settings and photo mode can change them.
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

        // The post-processing components, kept to change later (null without URP).
        static object grade, blur, vignette, whiteBalance, dof;
        static Type dofModeT;
        public const float BaseExposure = 0.15f, BaseContrast = 12f, BaseSaturation = 12f, BaseTemperature = 6f, BaseVignette = 0.18f, BaseBlur = 0.12f;

        /// <summary>Whether URP post-processing is running (photo mode's grading and depth of field need it).</summary>
        public static bool HasPost => grade != null;

        public static void SetMotionBlur(bool on) => Param(blur, "intensity", on ? BaseBlur : 0f);

        /// <summary>A look for photos, added to the island's own grade: exposure (EV), saturation, contrast, white balance, a colour filter, vignette.</summary>
        public static void SetLook(float exposure, float saturation, float contrast, float temperature, Color filter, float vignetteAdd)
        {
            Param(grade, "postExposure", BaseExposure + exposure);
            Param(grade, "saturation", Mathf.Clamp(BaseSaturation + saturation, -100f, 100f));
            Param(grade, "contrast", Mathf.Clamp(BaseContrast + contrast, -100f, 100f));
            Param(grade, "colorFilter", filter);
            Param(whiteBalance, "temperature", Mathf.Clamp(BaseTemperature + temperature, -100f, 100f));
            Param(vignette, "intensity", Mathf.Clamp01(BaseVignette + vignetteAdd));
        }

        /// <summary>The island's own grade again.</summary>
        public static void ResetLook() => SetLook(0, 0, 0, 0, Color.white, 0);

        /// <summary>Bokeh depth of field focused at focus metres (off: none).</summary>
        public static void SetDepthOfField(bool on, float focus = 10, float aperture = 5.6f, float focalLength = 50)
        {
            if (dof == null || dofModeT == null) return;
            Param(dof, "mode", Enum.Parse(dofModeT, on ? "Bokeh" : "Off"));
            if (!on) return;
            Param(dof, "focusDistance", Mathf.Max(0.1f, focus));
            Param(dof, "aperture", Mathf.Clamp(aperture, 1f, 32f));
            Param(dof, "focalLength", Mathf.Clamp(focalLength, 1f, 300f));
        }

        /// <summary>A property of the URP pipeline asset in use (render scale, shadow distance, MSAA), if there is one.</summary>
        public static void SetPipeline(string name, object value)
        {
            try
            {
                Set(GraphicsSettings.currentRenderPipeline, name, value);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Halcyon: could not set {name}: {e.Message}");
            }
        }

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
                grade = Comp("ColorAdjustments");
                Param(grade, "postExposure", BaseExposure);
                Param(grade, "contrast", BaseContrast);
                Param(grade, "saturation", BaseSaturation);
                vignette = Comp("Vignette");
                Param(vignette, "intensity", BaseVignette);
                Param(vignette, "smoothness", 0.45f);
                blur = Comp("MotionBlur");
                Param(blur, "intensity", BaseBlur);
                whiteBalance = Comp("WhiteBalance");
                Param(whiteBalance, "temperature", BaseTemperature);
                // Depth of field, off until photo mode wants it.
                dof = Comp("DepthOfField");
                dofModeT = T("UnityEngine.Rendering.Universal.DepthOfFieldMode, Unity.RenderPipelines.Universal.Runtime");
                if (dofModeT != null) Param(dof, "mode", Enum.Parse(dofModeT, "Off"));
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
