using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Pulsebreak.Client.Settings;

namespace Pulsebreak.Client.Visuals
{
    /// <summary>
    /// Turns the video settings into engine settings: the quality preset (shadows,
    /// anti-aliasing, bloom), render scale, VSync, the frame rate cap and the window mode.
    /// Applied at start and whenever the settings change.
    ///
    /// The preset changes the render pipeline asset at run time. In the Editor that would
    /// save into the project's asset, so the Editor's original values are put back when play
    /// stops.
    /// </summary>
    public static class GraphicsSetup
    {
        struct PipelineValues
        {
            public int Msaa, Cascades, ShadowResolution;
            public float RenderScale, ShadowDistance;
        }

        static bool _initialized;
        static bool _haveOriginal;
        static PipelineValues _original;
        static WindowMode _appliedWindow;
        static bool _windowApplied;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _initialized = false;
            _haveOriginal = false;
            _windowApplied = false;
        }

        /// <summary>Call once settings are loaded. Safe to call again.</summary>
        public static void Initialize()
        {
            if (Application.isBatchMode) return;
            if (!_initialized)
            {
                _initialized = true;
                GameSettings.Changed += Apply;
                Application.quitting += RestoreEditorPipeline;
            }
            ScreenEffects.Ensure();
            Apply();
        }

        public static void Apply()
        {
            GraphicsQuality quality = GameSettings.Quality;

            QualitySettings.vSyncCount = GameSettings.VSync ? 1 : 0;
            Application.targetFrameRate = GameSettings.VSync || GameSettings.FrameCap <= 0 ? -1 : GameSettings.FrameCap;

            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)
            {
                if (!_haveOriginal)
                {
                    _haveOriginal = true;
                    _original = new PipelineValues
                    {
                        Msaa = pipeline.msaaSampleCount,
                        Cascades = pipeline.shadowCascadeCount,
                        ShadowResolution = pipeline.mainLightShadowmapResolution,
                        RenderScale = pipeline.renderScale,
                        ShadowDistance = pipeline.shadowDistance,
                    };
                }
                pipeline.msaaSampleCount = quality == GraphicsQuality.Low ? 1 : quality == GraphicsQuality.Medium ? 2 : 4;
                pipeline.shadowDistance = quality == GraphicsQuality.Low ? 30f : quality == GraphicsQuality.Medium ? 55f : quality == GraphicsQuality.High ? 90f : 120f;
                pipeline.shadowCascadeCount = quality == GraphicsQuality.Low ? 1 : quality == GraphicsQuality.Medium ? 2 : 4;
                pipeline.mainLightShadowmapResolution = quality == GraphicsQuality.Low ? 1024 : quality == GraphicsQuality.Ultra ? 4096 : 2048;
                pipeline.renderScale = Mathf.Clamp(GameSettings.RenderScale, GameSettings.MinRenderScale, GameSettings.MaxRenderScale);
            }

            if (RenderSettings.sun != null)
                RenderSettings.sun.shadows = quality == GraphicsQuality.Low ? LightShadows.Hard : LightShadows.Soft;

            foreach (Camera camera in Camera.allCameras) ConfigureCamera(camera);
            ScreenEffects.Ensure().ApplyQuality(quality);
            ApplyWindow();
        }

        /// <summary>Post-processing and anti-aliasing for a camera that draws the game.</summary>
        public static void ConfigureCamera(Camera camera)
        {
            if (camera == null || !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)) return;
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            switch (GameSettings.Quality)
            {
                case GraphicsQuality.Low:
                    data.antialiasing = AntialiasingMode.None;
                    break;
                case GraphicsQuality.Medium:
                    data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
                    break;
                case GraphicsQuality.High:
                    data.antialiasing = AntialiasingMode.None;      // four-times MSAA does it
                    break;
                default:
                    data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                    data.antialiasingQuality = AntialiasingQuality.High;
                    break;
            }
        }

        static void ApplyWindow()
        {
            if (Application.isEditor) return;
            if (_windowApplied && _appliedWindow == GameSettings.Window) return;
            _windowApplied = true;
            _appliedWindow = GameSettings.Window;

            Resolution native = Screen.currentResolution;
            switch (GameSettings.Window)
            {
                case WindowMode.Fullscreen:
                    Screen.SetResolution(native.width, native.height, FullScreenMode.ExclusiveFullScreen);
                    break;
                case WindowMode.Borderless:
                    Screen.SetResolution(native.width, native.height, FullScreenMode.FullScreenWindow);
                    break;
                default:
                    Screen.SetResolution(Mathf.RoundToInt(native.width * 0.75f), Mathf.RoundToInt(native.height * 0.75f), FullScreenMode.Windowed);
                    break;
            }
        }

        /// <summary>Leaves the project's pipeline asset as it was before play started.</summary>
        static void RestoreEditorPipeline()
        {
            if (!Application.isEditor || !_haveOriginal) return;
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)) return;
            pipeline.msaaSampleCount = _original.Msaa;
            pipeline.shadowCascadeCount = _original.Cascades;
            pipeline.mainLightShadowmapResolution = _original.ShadowResolution;
            pipeline.renderScale = _original.RenderScale;
            pipeline.shadowDistance = _original.ShadowDistance;
        }
    }
}
