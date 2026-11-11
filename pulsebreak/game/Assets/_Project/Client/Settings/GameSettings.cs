using System;
using UnityEngine;

namespace Pulsebreak.Client.Settings
{
    /// <summary>
    /// Player-adjustable settings, saved on this machine with PlayerPrefs. Phase 1 only has
    /// the handful needed to judge gun and movement feel; the full settings menu comes later.
    /// </summary>
    public static class GameSettings
    {
        public const float MinSensitivity = 0.2f, MaxSensitivity = 8f;
        public const float MinAdsMultiplier = 0.3f, MaxAdsMultiplier = 1.5f;
        public const float MinFieldOfView = 70f, MaxFieldOfView = 120f;

        /// <summary>Hip-fire mouse sensitivity. One mouse count turns the view by 0.022° × this.</summary>
        public static float Sensitivity = 2.2f;
        /// <summary>Extra multiplier on sensitivity while aiming, on top of the zoom scaling.</summary>
        public static float AdsSensitivityMultiplier = 1f;
        /// <summary>Horizontal field of view in degrees, measured at 16:9.</summary>
        public static float FieldOfView = 100f;
        public static bool ShowDamageNumbers = true;
        public static float MasterVolume = 0.8f;

        /// <summary>When the settings were last changed by the player. Seconds since 1970 (UTC). Decides which copy wins when they sync.</summary>
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
            MasterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("pb.masterVolume", 0.8f));
            UpdatedAt = long.TryParse(PlayerPrefs.GetString("pb.settingsUpdatedAt", "0"), out long stamp) ? stamp : 0;
            Apply();
        }

        /// <summary>Saves the settings as the player's latest choice.</summary>
        public static void Save()
        {
            UpdatedAt = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), UpdatedAt + 1);
            Write();
            Saved?.Invoke();
        }

        /// <summary>Takes settings that came from the player's account. They keep the time they were made, so they do not look newer than they are.</summary>
        public static void Adopt(float sensitivity, float adsMultiplier, float fieldOfView, bool damageNumbers, float volume, long updatedAt)
        {
            Sensitivity = Mathf.Clamp(sensitivity, MinSensitivity, MaxSensitivity);
            AdsSensitivityMultiplier = Mathf.Clamp(adsMultiplier, MinAdsMultiplier, MaxAdsMultiplier);
            FieldOfView = Mathf.Clamp(fieldOfView, MinFieldOfView, MaxFieldOfView);
            ShowDamageNumbers = damageNumbers;
            MasterVolume = Mathf.Clamp01(volume);
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
            PlayerPrefs.SetFloat("pb.masterVolume", MasterVolume);
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
