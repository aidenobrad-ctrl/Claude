using UnityEngine;
using UnityEngine.Rendering;

namespace Pulsebreak.Client.FiringRange
{
    /// <summary>
    /// Lighting and atmosphere for the range, set from code so the level looks the same
    /// whatever the scene's own lighting settings are and needs no light bake.
    /// </summary>
    public static class RangeEnvironment
    {
        static readonly Color Sky = new Color(0.56f, 0.7f, 0.86f);
        static readonly Color Horizon = new Color(0.66f, 0.74f, 0.82f);
        static readonly Color Ground = new Color(0.24f, 0.25f, 0.28f);

        public static void Apply(bool createSunLight)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.7f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.46f, 0.48f, 0.52f);
            RenderSettings.ambientGroundColor = Ground;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Horizon;
            RenderSettings.fogStartDistance = 70f;
            RenderSettings.fogEndDistance = 320f;

            if (!createSunLight) return;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            RenderSettings.sun = sun;
        }

        public static void ConfigureCamera(Camera camera)
        {
            // Use the scene's skybox when it has one, otherwise a flat sky colour.
            camera.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            camera.backgroundColor = Sky;
        }
    }
}
