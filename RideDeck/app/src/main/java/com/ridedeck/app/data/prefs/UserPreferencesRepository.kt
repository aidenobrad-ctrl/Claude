package com.ridedeck.app.data.prefs

import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.booleanPreferencesKey
import androidx.datastore.preferences.core.doublePreferencesKey
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.emptyPreferences
import androidx.datastore.preferences.core.floatPreferencesKey
import androidx.datastore.preferences.core.stringPreferencesKey
import com.ridedeck.app.di.SettingsStore
import com.ridedeck.core.model.ActivityMode
import com.ridedeck.core.model.SpeedUnit
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.catch
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import java.io.IOException
import javax.inject.Inject
import javax.inject.Singleton

enum class ThemeMode { SYSTEM, DARK, OLED }

data class UserPreferences(
    val mode: ActivityMode = ActivityMode.BIKE,
    val speedUnit: SpeedUnit = SpeedUnit.KMH,
    val speedAlertEnabled: Boolean = false,
    val speedAlertLimitMps: Double = DEFAULT_ALERT_LIMIT_MPS,
    val speedAlertSound: Boolean = true,
    val speedAlertVibrate: Boolean = true,
    val autoPauseEnabled: Boolean = true,
    val keepScreenOn: Boolean = true,
    val rideModeLockEnabled: Boolean = true,
    val themeMode: ThemeMode = ThemeMode.OLED,
    val showBikeShops: Boolean = true,
    val showBikeShare: Boolean = true,
    val showBikeShareInWalkMode: Boolean = false,
    val onlyStationsWithBikes: Boolean = false,
    val showCycleLanes: Boolean = true,
    val gbfsDiscoveryUrl: String? = null,
    val gbfsSystemName: String? = null,
    val musicPanelVisible: Boolean = false,
    val musicPanelFraction: Float = DEFAULT_PANEL_FRACTION,
    val lyricsEnabled: Boolean = true,
    val onboardingDone: Boolean = false,
) {
    companion object {
        const val DEFAULT_ALERT_LIMIT_MPS = 30.0 / 3.6
        const val DEFAULT_PANEL_FRACTION = 0.38f
        const val MIN_PANEL_FRACTION = 0.18f
        const val MAX_PANEL_FRACTION = 0.75f
    }
}

@Singleton
class UserPreferencesRepository @Inject constructor(
    @SettingsStore private val dataStore: DataStore<Preferences>,
) {
    private object Keys {
        val MODE = stringPreferencesKey("mode")
        val SPEED_UNIT = stringPreferencesKey("speed_unit")
        val ALERT_ENABLED = booleanPreferencesKey("speed_alert_enabled")
        val ALERT_LIMIT = doublePreferencesKey("speed_alert_limit_mps")
        val ALERT_SOUND = booleanPreferencesKey("speed_alert_sound")
        val ALERT_VIBRATE = booleanPreferencesKey("speed_alert_vibrate")
        val AUTO_PAUSE = booleanPreferencesKey("auto_pause")
        val KEEP_SCREEN_ON = booleanPreferencesKey("keep_screen_on")
        val RIDE_MODE_LOCK = booleanPreferencesKey("ride_mode_lock")
        val THEME = stringPreferencesKey("theme")
        val SHOW_SHOPS = booleanPreferencesKey("show_bike_shops")
        val SHOW_SHARE = booleanPreferencesKey("show_bike_share")
        val SHARE_IN_WALK = booleanPreferencesKey("show_bike_share_walk")
        val ONLY_WITH_BIKES = booleanPreferencesKey("only_stations_with_bikes")
        val CYCLE_LANES = booleanPreferencesKey("show_cycle_lanes")
        val GBFS_URL = stringPreferencesKey("gbfs_url")
        val GBFS_NAME = stringPreferencesKey("gbfs_name")
        val PANEL_VISIBLE = booleanPreferencesKey("music_panel_visible")
        val PANEL_FRACTION = floatPreferencesKey("music_panel_fraction")
        val LYRICS = booleanPreferencesKey("lyrics_enabled")
        val ONBOARDING_DONE = booleanPreferencesKey("onboarding_done")
    }

    val preferences: Flow<UserPreferences> = dataStore.data
        .catch { e -> if (e is IOException) emit(emptyPreferences()) else throw e }
        .map { p ->
            val defaults = UserPreferences()
            UserPreferences(
                mode = ActivityMode.fromName(p[Keys.MODE]),
                speedUnit = SpeedUnit.fromName(p[Keys.SPEED_UNIT]),
                speedAlertEnabled = p[Keys.ALERT_ENABLED] ?: defaults.speedAlertEnabled,
                speedAlertLimitMps = p[Keys.ALERT_LIMIT] ?: defaults.speedAlertLimitMps,
                speedAlertSound = p[Keys.ALERT_SOUND] ?: defaults.speedAlertSound,
                speedAlertVibrate = p[Keys.ALERT_VIBRATE] ?: defaults.speedAlertVibrate,
                autoPauseEnabled = p[Keys.AUTO_PAUSE] ?: defaults.autoPauseEnabled,
                keepScreenOn = p[Keys.KEEP_SCREEN_ON] ?: defaults.keepScreenOn,
                rideModeLockEnabled = p[Keys.RIDE_MODE_LOCK] ?: defaults.rideModeLockEnabled,
                themeMode = ThemeMode.entries.firstOrNull { it.name == p[Keys.THEME] } ?: defaults.themeMode,
                showBikeShops = p[Keys.SHOW_SHOPS] ?: defaults.showBikeShops,
                showBikeShare = p[Keys.SHOW_SHARE] ?: defaults.showBikeShare,
                showBikeShareInWalkMode = p[Keys.SHARE_IN_WALK] ?: defaults.showBikeShareInWalkMode,
                onlyStationsWithBikes = p[Keys.ONLY_WITH_BIKES] ?: defaults.onlyStationsWithBikes,
                showCycleLanes = p[Keys.CYCLE_LANES] ?: defaults.showCycleLanes,
                gbfsDiscoveryUrl = p[Keys.GBFS_URL]?.takeIf { it.isNotBlank() },
                gbfsSystemName = p[Keys.GBFS_NAME]?.takeIf { it.isNotBlank() },
                musicPanelVisible = p[Keys.PANEL_VISIBLE] ?: defaults.musicPanelVisible,
                musicPanelFraction = (p[Keys.PANEL_FRACTION] ?: defaults.musicPanelFraction)
                    .coerceIn(UserPreferences.MIN_PANEL_FRACTION, UserPreferences.MAX_PANEL_FRACTION),
                lyricsEnabled = p[Keys.LYRICS] ?: defaults.lyricsEnabled,
                onboardingDone = p[Keys.ONBOARDING_DONE] ?: defaults.onboardingDone,
            )
        }
        .distinctUntilChanged()

    val mode: Flow<ActivityMode> = preferences.map { it.mode }.distinctUntilChanged()

    suspend fun current(): UserPreferences = preferences.first()

    suspend fun setMode(mode: ActivityMode) = dataStore.edit { it[Keys.MODE] = mode.name }
    suspend fun setSpeedUnit(unit: SpeedUnit) = dataStore.edit { it[Keys.SPEED_UNIT] = unit.name }
    suspend fun setSpeedAlertEnabled(enabled: Boolean) = dataStore.edit { it[Keys.ALERT_ENABLED] = enabled }
    suspend fun setSpeedAlertLimitMps(limit: Double) = dataStore.edit { it[Keys.ALERT_LIMIT] = limit }
    suspend fun setSpeedAlertSound(enabled: Boolean) = dataStore.edit { it[Keys.ALERT_SOUND] = enabled }
    suspend fun setSpeedAlertVibrate(enabled: Boolean) = dataStore.edit { it[Keys.ALERT_VIBRATE] = enabled }
    suspend fun setAutoPause(enabled: Boolean) = dataStore.edit { it[Keys.AUTO_PAUSE] = enabled }
    suspend fun setKeepScreenOn(enabled: Boolean) = dataStore.edit { it[Keys.KEEP_SCREEN_ON] = enabled }
    suspend fun setRideModeLock(enabled: Boolean) = dataStore.edit { it[Keys.RIDE_MODE_LOCK] = enabled }
    suspend fun setTheme(theme: ThemeMode) = dataStore.edit { it[Keys.THEME] = theme.name }
    suspend fun setShowBikeShops(enabled: Boolean) = dataStore.edit { it[Keys.SHOW_SHOPS] = enabled }
    suspend fun setShowBikeShare(enabled: Boolean) = dataStore.edit { it[Keys.SHOW_SHARE] = enabled }
    suspend fun setShowBikeShareInWalkMode(enabled: Boolean) = dataStore.edit { it[Keys.SHARE_IN_WALK] = enabled }
    suspend fun setOnlyStationsWithBikes(enabled: Boolean) = dataStore.edit { it[Keys.ONLY_WITH_BIKES] = enabled }
    suspend fun setShowCycleLanes(enabled: Boolean) = dataStore.edit { it[Keys.CYCLE_LANES] = enabled }
    suspend fun setLyricsEnabled(enabled: Boolean) = dataStore.edit { it[Keys.LYRICS] = enabled }
    suspend fun setOnboardingDone() = dataStore.edit { it[Keys.ONBOARDING_DONE] = true }
    suspend fun setMusicPanelVisible(visible: Boolean) = dataStore.edit { it[Keys.PANEL_VISIBLE] = visible }

    suspend fun setMusicPanelFraction(fraction: Float) = dataStore.edit {
        it[Keys.PANEL_FRACTION] = fraction.coerceIn(UserPreferences.MIN_PANEL_FRACTION, UserPreferences.MAX_PANEL_FRACTION)
    }

    suspend fun setGbfsSystem(name: String?, discoveryUrl: String?) = dataStore.edit {
        if (discoveryUrl.isNullOrBlank()) {
            it.remove(Keys.GBFS_URL)
            it.remove(Keys.GBFS_NAME)
        } else {
            it[Keys.GBFS_URL] = discoveryUrl.trim()
            it[Keys.GBFS_NAME] = name?.trim().orEmpty()
        }
    }
}
