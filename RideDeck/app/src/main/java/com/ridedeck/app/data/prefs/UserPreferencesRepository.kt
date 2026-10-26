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

/** Where the music panel gets its music from. */
enum class MusicSource { PHONE, SPOTIFY }

/** The three views of the Ride screen. */
enum class RideView { MAP, SPEEDOMETER, MUSIC }

/** Which map is drawn inside the app. */
enum class MapProvider { OSM, GOOGLE }

data class UserPreferences(
    /** Always BIKE: Walk mode was removed. Kept so the tracking engine and old sessions keep working. */
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
    val showCycleLanes: Boolean = true,
    val musicPanelVisible: Boolean = true,
    val musicPanelFraction: Float = DEFAULT_PANEL_FRACTION,
    val lyricsEnabled: Boolean = true,
    val onboardingDone: Boolean = false,
    /** Optional Spotify integration: control the Spotify app from the music panel. */
    val spotifyEnabled: Boolean = false,
    val musicSource: MusicSource = MusicSource.PHONE,
    /** Optional Google Maps integration: hand turn-by-turn directions to the Google Maps app. */
    val googleMapsEnabled: Boolean = false,
    val speedOverlayExpanded: Boolean = true,
    val mapProvider: MapProvider = MapProvider.OSM,
    /** Which views the Ride screen shows; any combination, at least one. Music uses [musicPanelVisible]. */
    val showMap: Boolean = true,
    val showSpeedometer: Boolean = true,
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
        val SPEED_UNIT = stringPreferencesKey("speed_unit")
        val ALERT_ENABLED = booleanPreferencesKey("speed_alert_enabled")
        val ALERT_LIMIT = doublePreferencesKey("speed_alert_limit_mps")
        val ALERT_SOUND = booleanPreferencesKey("speed_alert_sound")
        val ALERT_VIBRATE = booleanPreferencesKey("speed_alert_vibrate")
        val AUTO_PAUSE = booleanPreferencesKey("auto_pause")
        val KEEP_SCREEN_ON = booleanPreferencesKey("keep_screen_on")
        val RIDE_MODE_LOCK = booleanPreferencesKey("ride_mode_lock")
        val THEME = stringPreferencesKey("theme")
        val CYCLE_LANES = booleanPreferencesKey("show_cycle_lanes")
        val PANEL_VISIBLE = booleanPreferencesKey("music_panel_visible")
        val PANEL_FRACTION = floatPreferencesKey("music_panel_fraction")
        val LYRICS = booleanPreferencesKey("lyrics_enabled")
        val ONBOARDING_DONE = booleanPreferencesKey("onboarding_done")
        val SPOTIFY = booleanPreferencesKey("spotify_enabled")
        val MUSIC_SOURCE = stringPreferencesKey("music_source")
        val GOOGLE_MAPS = booleanPreferencesKey("google_maps_enabled")
        val OVERLAY_EXPANDED = booleanPreferencesKey("speed_overlay_expanded")
        val MAP_PROVIDER = stringPreferencesKey("map_provider")
        val SHOW_MAP = booleanPreferencesKey("show_map")
        val SHOW_SPEEDOMETER = booleanPreferencesKey("show_speedometer")
    }

    val preferences: Flow<UserPreferences> = dataStore.data
        .catch { e -> if (e is IOException) emit(emptyPreferences()) else throw e }
        .map { p ->
            val defaults = UserPreferences()
            UserPreferences(
                mode = ActivityMode.BIKE,
                speedUnit = SpeedUnit.fromName(p[Keys.SPEED_UNIT]),
                speedAlertEnabled = p[Keys.ALERT_ENABLED] ?: defaults.speedAlertEnabled,
                speedAlertLimitMps = p[Keys.ALERT_LIMIT] ?: defaults.speedAlertLimitMps,
                speedAlertSound = p[Keys.ALERT_SOUND] ?: defaults.speedAlertSound,
                speedAlertVibrate = p[Keys.ALERT_VIBRATE] ?: defaults.speedAlertVibrate,
                autoPauseEnabled = p[Keys.AUTO_PAUSE] ?: defaults.autoPauseEnabled,
                keepScreenOn = p[Keys.KEEP_SCREEN_ON] ?: defaults.keepScreenOn,
                rideModeLockEnabled = p[Keys.RIDE_MODE_LOCK] ?: defaults.rideModeLockEnabled,
                themeMode = ThemeMode.entries.firstOrNull { it.name == p[Keys.THEME] } ?: defaults.themeMode,
                showCycleLanes = p[Keys.CYCLE_LANES] ?: defaults.showCycleLanes,
                musicPanelVisible = p[Keys.PANEL_VISIBLE] ?: defaults.musicPanelVisible,
                musicPanelFraction = (p[Keys.PANEL_FRACTION] ?: defaults.musicPanelFraction)
                    .coerceIn(UserPreferences.MIN_PANEL_FRACTION, UserPreferences.MAX_PANEL_FRACTION),
                lyricsEnabled = p[Keys.LYRICS] ?: defaults.lyricsEnabled,
                onboardingDone = p[Keys.ONBOARDING_DONE] ?: defaults.onboardingDone,
                spotifyEnabled = p[Keys.SPOTIFY] ?: defaults.spotifyEnabled,
                musicSource = MusicSource.entries.firstOrNull { it.name == p[Keys.MUSIC_SOURCE] } ?: defaults.musicSource,
                googleMapsEnabled = p[Keys.GOOGLE_MAPS] ?: defaults.googleMapsEnabled,
                speedOverlayExpanded = p[Keys.OVERLAY_EXPANDED] ?: defaults.speedOverlayExpanded,
                mapProvider = MapProvider.entries.firstOrNull { it.name == p[Keys.MAP_PROVIDER] } ?: defaults.mapProvider,
                showMap = p[Keys.SHOW_MAP] ?: defaults.showMap,
                showSpeedometer = p[Keys.SHOW_SPEEDOMETER] ?: defaults.showSpeedometer,
            )
        }
        .distinctUntilChanged()

    val mode: Flow<ActivityMode> = preferences.map { it.mode }.distinctUntilChanged()

    suspend fun current(): UserPreferences = preferences.first()

    suspend fun setSpotifyEnabled(enabled: Boolean) = dataStore.edit {
        it[Keys.SPOTIFY] = enabled
        if (!enabled) it[Keys.MUSIC_SOURCE] = MusicSource.PHONE.name
    }
    suspend fun setMusicSource(source: MusicSource) = dataStore.edit { it[Keys.MUSIC_SOURCE] = source.name }
    suspend fun setGoogleMapsEnabled(enabled: Boolean) = dataStore.edit { it[Keys.GOOGLE_MAPS] = enabled }
    suspend fun setMapProvider(provider: MapProvider) = dataStore.edit { it[Keys.MAP_PROVIDER] = provider.name }

    /** Shows or hides one view of the Ride screen; refuses to hide the last visible one. */
    suspend fun setView(view: RideView, visible: Boolean) = dataStore.edit {
        val defaults = UserPreferences()
        val map = it[Keys.SHOW_MAP] ?: defaults.showMap
        val speed = it[Keys.SHOW_SPEEDOMETER] ?: defaults.showSpeedometer
        val music = it[Keys.PANEL_VISIBLE] ?: defaults.musicPanelVisible
        val next = when (view) {
            RideView.MAP -> Triple(visible, speed, music)
            RideView.SPEEDOMETER -> Triple(map, visible, music)
            RideView.MUSIC -> Triple(map, speed, visible)
        }
        if (next.first || next.second || next.third) {
            it[Keys.SHOW_MAP] = next.first
            it[Keys.SHOW_SPEEDOMETER] = next.second
            it[Keys.PANEL_VISIBLE] = next.third
        }
    }

    suspend fun setSpeedOverlayExpanded(expanded: Boolean) = dataStore.edit { it[Keys.OVERLAY_EXPANDED] = expanded }
    suspend fun setSpeedUnit(unit: SpeedUnit) = dataStore.edit { it[Keys.SPEED_UNIT] = unit.name }
    suspend fun setSpeedAlertEnabled(enabled: Boolean) = dataStore.edit { it[Keys.ALERT_ENABLED] = enabled }
    suspend fun setSpeedAlertLimitMps(limit: Double) = dataStore.edit { it[Keys.ALERT_LIMIT] = limit }
    suspend fun setSpeedAlertSound(enabled: Boolean) = dataStore.edit { it[Keys.ALERT_SOUND] = enabled }
    suspend fun setSpeedAlertVibrate(enabled: Boolean) = dataStore.edit { it[Keys.ALERT_VIBRATE] = enabled }
    suspend fun setAutoPause(enabled: Boolean) = dataStore.edit { it[Keys.AUTO_PAUSE] = enabled }
    suspend fun setKeepScreenOn(enabled: Boolean) = dataStore.edit { it[Keys.KEEP_SCREEN_ON] = enabled }
    suspend fun setRideModeLock(enabled: Boolean) = dataStore.edit { it[Keys.RIDE_MODE_LOCK] = enabled }
    suspend fun setTheme(theme: ThemeMode) = dataStore.edit { it[Keys.THEME] = theme.name }
    suspend fun setShowCycleLanes(enabled: Boolean) = dataStore.edit { it[Keys.CYCLE_LANES] = enabled }
    suspend fun setLyricsEnabled(enabled: Boolean) = dataStore.edit { it[Keys.LYRICS] = enabled }
    suspend fun setOnboardingDone() = dataStore.edit { it[Keys.ONBOARDING_DONE] = true }
    suspend fun setMusicPanelVisible(visible: Boolean) = dataStore.edit { it[Keys.PANEL_VISIBLE] = visible }

    suspend fun setMusicPanelFraction(fraction: Float) = dataStore.edit {
        it[Keys.PANEL_FRACTION] = fraction.coerceIn(UserPreferences.MIN_PANEL_FRACTION, UserPreferences.MAX_PANEL_FRACTION)
    }
}
