using System;
using UnityEngine;

namespace Pulsebreak.Client.Settings
{
    /// <summary>How much the graphics cost, from fastest to best looking.</summary>
    public enum GraphicsQuality
    {
        Low,
        Medium,
        High,
        Ultra,
    }

    /// <summary>How the game sits on the screen.</summary>
    public enum WindowMode
    {
        Fullscreen,
        Borderless,
        Windowed,
    }

    /// <summary>
    /// Player-adjustable settings, saved on this machine with PlayerPrefs. Gameplay and sound
    /// settings also follow a signed-in account to other machines; video settings belong to
    /// this machine and stay here.
    /// </summary>
    public static class GameSettings
    {
        public const float MinSensitivity = 0.2f, MaxSensitivity = 8f;
        public const float MinAdsMultiplier = 0.3f, MaxAdsMultiplier = 1.5f;
        public const float MinFieldOfView = 70f, MaxFieldOfView = 120f;
        public const float MinRenderScale = 0.5f, MaxRenderScale = 1f;
        /// <summary>The frame rate caps offered. 0 means no cap.</summary>
        public static readonly int[] FrameCaps = { 60, 120, 144, 165, 240, 0 };

        // ------------------------------------------------------------------ gameplay

        /// <summary>Hip-fire mouse sensitivity. One mouse count turns the view by 0.022° × this.</summary>
        public static float Sensitivity = 2.2f;
        /// <summary>Extra multiplier on sensitivity while aiming, on top of the zoom scaling.</summary>
        public static float AdsSensitivityMultiplier = 1f;
        /// <summary>Horizontal field of view in degrees, measured at 16:9.</summary>
        public static float FieldOfView = 100f;
        public static bool ShowDamageNumbers = true;
        /// <summary>Sprint stays on after one press of the key, instead of only while it is held.</summary>
        public static bool ToggleSprint;

        // ------------------------------------------------------------------ sound

        public static float MasterVolume = 0.8f;
        /// <summary>Weapons, footsteps, hits, explosions.</summary>
        public static float EffectsVolume = 1f;
        public static float MusicVolume = 0.6f;
        /// <summary>Menu clicks, countdown beeps, announcements.</summary>
        public static float InterfaceVolume = 0.8f;

        // ------------------------------------------------------------------ video (this machine only)

        public static GraphicsQuality Quality = GraphicsQuality.High;
        /// <summary>The share of the screen's resolution the 3D view is drawn at. The interface is always sharp.</summary>
        public static float RenderScale = 1f;
        public static bool VSync;
        /// <summary>Frames a second at most; 0 for no cap. Ignored while VSync is on.</summary>
        public static int FrameCap = 240;
        public static WindowMode Window = WindowMode.Fullscreen;
        /// <summary>Frames a second and frame time in the corner.</summary>
        public static bool ShowFps;
        /// <summary>Explosions shake the view and hits make it flinch.</summary>
        public static bool CameraShake = true;

        /// <summary>When the synced settings were last changed by the player. Seconds since 1970 (UTC). Decides which copy wins when they sync.</summary>
        public static long UpdatedAt;

        public static event Action Changed;
        /// <summary>The player finished changing settings and they were saved.</summary>
        public static event Action Saved;

        // Statics survive between Play sessions when domain reload is off, so reset them explicitly.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Changed = null;
            Saved = null;
        }

        public static void Load()
        {
            Sensitivity = Mathf.Clamp(PlayerPrefs.GetFloat("pb.sensitivity", 2.2f), MinSensitivity, MaxSensitivity);
            AdsSensitivityMultiplier = Mathf.Clamp(PlayerPrefs.GetFloat("pb.adsSensitivity", 1f), MinAdsMultiplier, MaxAdsMultiplier);
            FieldOfView = Mathf.Clamp(PlayerPrefs.GetFloat("pb.fov", 100f), MinFieldOfView, MaxFieldOfView);
            ShowDamageNumbers = PlayerPrefs.GetInt("pb.damageNumbers", 1) != 0;
            ToggleSprint = PlayerPrefs.GetInt("pb.toggleSprint", 0) != 0;
            MasterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("pb.masterVolume", 0.8f));
            EffectsVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("pb.effectsVolume", 1f));
            MusicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("pb.musicVolume", 0.6f));
            InterfaceVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("pb.interfaceVolume", 0.8f));
            UpdatedAt = long.TryParse(PlayerPrefs.GetString("pb.settingsUpdatedAt", "0"), out long stamp) ? stamp : 0;

            Quality = (GraphicsQuality)Mathf.Clamp(PlayerPrefs.GetInt("pb.quality", (int)GraphicsQuality.High), 0, (int)GraphicsQuality.Ultra);
            RenderScale = Mathf.Clamp(PlayerPrefs.GetFloat("pb.renderScale", 1f), MinRenderScale, MaxRenderScale);
            VSync = PlayerPrefs.GetInt("pb.vsync", 0) != 0;
            FrameCap = PlayerPrefs.GetInt("pb.frameCap", 240);
            if (Array.IndexOf(FrameCaps, FrameCap) < 0) FrameCap = 240;
            Window = (WindowMode)Mathf.Clamp(PlayerPrefs.GetInt("pb.window", (int)WindowMode.Fullscreen), 0, (int)WindowMode.Windowed);
            ShowFps = PlayerPrefs.GetInt("pb.showFps", 0) != 0;
            CameraShake = PlayerPrefs.GetInt("pb.cameraShake", 1) != 0;
            Apply();
        }

        /// <summary>Saves the settings as the player's latest choice.</summary>
        public static void Save()
        {
            UpdatedAt = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), UpdatedAt + 1);
            Write();
            Saved?.Invoke();
        }

        /// <summary>The settings that follow an account between machines.</summary>
        public struct Synced
        {
            public float Sensitivity, AdsMultiplier, FieldOfView;
            public bool DamageNumbers, ToggleSprint;
            public float Master, Effects, Music, Interface;
        }

        public static Synced Current() => new Synced
        {
            Sensitivity = Sensitivity,
            AdsMultiplier = AdsSensitivityMultiplier,
            FieldOfView = FieldOfView,
            DamageNumbers = ShowDamageNumbers,
            ToggleSprint = ToggleSprint,
            Master = MasterVolume,
            Effects = EffectsVolume,
            Music = MusicVolume,
            Interface = InterfaceVolume,
        };

        /// <summary>Takes settings that came from the player's account. They keep the time they were made, so they do not look newer than they are.</summary>
        public static void Adopt(in Synced from, long updatedAt)
        {
            Sensitivity = Mathf.Clamp(from.Sensitivity, MinSensitivity, MaxSensitivity);
            AdsSensitivityMultiplier = Mathf.Clamp(from.AdsMultiplier, MinAdsMultiplier, MaxAdsMultiplier);
            FieldOfView = Mathf.Clamp(from.FieldOfView, MinFieldOfView, MaxFieldOfView);
            ShowDamageNumbers = from.DamageNumbers;
            ToggleSprint = from.ToggleSprint;
            MasterVolume = Mathf.Clamp01(from.Master);
            EffectsVolume = Mathf.Clamp01(from.Effects);
            MusicVolume = Mathf.Clamp01(from.Music);
            InterfaceVolume = Mathf.Clamp01(from.Interface);
            UpdatedAt = updatedAt;
            Write();
            Apply();
        }

        static void Write()
        {
            PlayerPrefs.SetString("pb.settingsUpdatedAt", UpdatedAt.ToString());
            PlayerPrefs.SetFloat("pb.sensitivity", Sensitivity);
            PlayerPrefs.SetFloat("pb.adsSensitivity", AdsSensitivityMultiplier);
            PlayerPrefs.SetFloat("pb.fov", FieldOfView);
            PlayerPrefs.SetInt("pb.damageNumbers", ShowDamageNumbers ? 1 : 0);
            PlayerPrefs.SetInt("pb.toggleSprint", ToggleSprint ? 1 : 0);
            PlayerPrefs.SetFloat("pb.masterVolume", MasterVolume);
            PlayerPrefs.SetFloat("pb.effectsVolume", EffectsVolume);
            PlayerPrefs.SetFloat("pb.musicVolume", MusicVolume);
            PlayerPrefs.SetFloat("pb.interfaceVolume", InterfaceVolume);
            PlayerPrefs.SetInt("pb.quality", (int)Quality);
            PlayerPrefs.SetFloat("pb.renderScale", RenderScale);
            PlayerPrefs.SetInt("pb.vsync", VSync ? 1 : 0);
            PlayerPrefs.SetInt("pb.frameCap", FrameCap);
            PlayerPrefs.SetInt("pb.window", (int)Window);
            PlayerPrefs.SetInt("pb.showFps", ShowFps ? 1 : 0);
            PlayerPrefs.SetInt("pb.cameraShake", CameraShake ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>Pushes the values to the engine and tells listeners. Call after changing any field.</summary>
        public static void Apply()
        {
            AudioListener.volume = MasterVolume;
            Changed?.Invoke();
        }
    }
}
