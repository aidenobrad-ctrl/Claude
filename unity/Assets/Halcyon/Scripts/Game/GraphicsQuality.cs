// The graphics presets (Settings.Graphics): shadow distance and cascades,
// level of detail, texture filtering, the render scale and anti-aliasing of
// the URP asset in use, and how far the trees are drawn in full and at all.
// High is what the island was built and screenshotted at.
using Halcyon.Core;
using UnityEngine;

namespace Halcyon
{
    public static class GraphicsQuality
    {
        public static void Apply(GraphicsPreset p, VegetationRenderer veg)
        {
            float shadow = p == GraphicsPreset.Low ? 150 : p == GraphicsPreset.Medium ? 280 : p == GraphicsPreset.High ? 450 : 700;
            int cascades = p <= GraphicsPreset.Medium ? 2 : 4;
            QualitySettings.shadowDistance = shadow;
            QualitySettings.shadowCascades = cascades;
            QualitySettings.shadows = p == GraphicsPreset.Low ? ShadowQuality.HardOnly : ShadowQuality.All;
            QualitySettings.lodBias = p == GraphicsPreset.Low ? 0.7f : p == GraphicsPreset.Medium ? 1f : p == GraphicsPreset.High ? 1.5f : 2.2f;
            QualitySettings.anisotropicFiltering = p == GraphicsPreset.Low ? AnisotropicFiltering.Disable : AnisotropicFiltering.ForceEnable;
            // URP keeps its own shadow distance, render scale and MSAA on the pipeline asset.
            EnvironmentSetup.SetPipeline("shadowDistance", shadow);
            EnvironmentSetup.SetPipeline("shadowCascadeCount", cascades);
            EnvironmentSetup.SetPipeline("renderScale", p == GraphicsPreset.Low ? 0.75f : p == GraphicsPreset.Medium ? 0.9f : 1f);
            EnvironmentSetup.SetPipeline("msaaSampleCount", p == GraphicsPreset.Ultra ? 4 : 1);
            if (veg != null)
            {
                veg.NearDistance = p == GraphicsPreset.Low ? 200 : p == GraphicsPreset.Medium ? 300 : p == GraphicsPreset.High ? 420 : 640;
                veg.FarDistance = p == GraphicsPreset.Low ? 1300 : p == GraphicsPreset.Medium ? 1900 : p == GraphicsPreset.High ? 2600 : 3600;
            }
        }
    }
}
