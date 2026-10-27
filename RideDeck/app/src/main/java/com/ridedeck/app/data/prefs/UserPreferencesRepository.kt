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

/** Trip figures that can be hidden from the speedometer. */
enum class RideStat { DISTANCE, TIME, AVERAGE, MAX, ODOMETER }

/** How the map looks. AUTO follows the app theme (color in a light theme, dark otherwise). */
enum class MapStyle { AUTO, COLOR, CLEAN, DARK }

data class OverlayPosition(val x: Float, val y: Float)

data class UserPreferences(
    /** Always BIKE: Walk mode was removed. Kept so the tracking engine and old sessions keep working. */
    val mode: ActivityMode = ActivityMode.BIKE,
    val speedUnit: SpeedUnit = SpeedUnit.MPH,
    val speedAlertEnabled: Boolean = false,
    val speedAlertLimitMps: Double = DEFAULT_ALERT_LIMIT_MPS,
    val speedAlertSound: Boolean = true,
    val speedAlertVibrate: Boolean = true,
    val autoPauseEnabled: Boolean = true,
    val keepScreenOn: Boolean = true,
    val rideModeLockEnabled: Boolean = true,
    val themeMode: ThemeMode = ThemeMode.OLED,
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
    val mapStyle: MapStyle = MapStyle.COLOR,
    /** Speak upcoming turns while navigating. */
    val voiceDirections: Boolean = true,
    /** Which views the Ride screen shows; any combination, at least one. Music uses [musicPanelVisible]. */
    val showMap: Boolean = true,
    val showSpeedometer: Boolean = true,
    /**
     * Where the speedometer floats over the map, as fractions (0..1) of the free space across and
     * down. Null means docked: centred just above the bottom controls.
     */
    val speedOverlayPosition: OverlayPosition? = null,
    /** Views remembered separately for when the phone is turned sideways. */
    val showMapLandscape: Boolean = true,
    val showSpeedometerLandscape: Boolean = true,
    val showMusicLandscape: Boolean = true,
    /** Which trip figures the speedometer shows. */
    val showDistance: Boolean = true,
    val showTime: Boolean = true,
    val showAverage: Boolean = true,
    val showMax: Boolean = true,
    val showOdometer: Boolean = true,
) {
    /** The Ride screen views for the current orientation: map, speedometer, music. */
    fun views(landscape: Boolean): Triple<Boolean, Boolean, Boolean> =
        if (landscape) Triple(showMapLandscape, showSpeedometerLandscape, showMusicLandscape)
        else Triple(showMap, showSpeedometer, musicPanelVisible)

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
        val PANEL_VISIBLE = booleanPreferencesKey("music_panel_visible")
        val PANEL_FRACTION = floatPreferencesKey("music_panel_fraction")
        val LYRICS = booleanPreferencesKey("lyrics_enabled")
        val ONBOARDING_DONE = booleanPreferencesKey("onboarding_done")
        val SPOTIFY = booleanPreferencesKey("spotify_enabled")
        val MUSIC_SOURCE = stringPreferencesKey("music_source")
        val GOOGLE_MAPS = booleanPreferencesKey("google_maps_enabled")
        val OVERLAY_EXPANDED = booleanPreferencesKey("speed_overlay_expanded")
        val MAP_STYLE = stringPreferencesKey("map_style")
        val VOICE_DIRECTIONS = booleanPreferencesKey("voice_directions")
        val SHOW_MAP = booleanPreferencesKey("show_map")
        val SHOW_SPEEDOMETER = booleanPreferencesKey("show_speedometer")
        val SHOW_MAP_LAND = booleanPreferencesKey("show_map_landscape")
        val SHOW_SPEEDOMETER_LAND = booleanPreferencesKey("show_speedometer_landscape")
        val SHOW_MUSIC_LAND = booleanPreferencesKey("show_music_landscape")
        val STAT_DISTANCE = booleanPreferencesKey("stat_distance")
        val STAT_TIME = booleanPreferencesKey("stat_time")
        val STAT_AVERAGE = booleanPreferencesKey("stat_average")
        val STAT_MAX = booleanPreferencesKey("stat_max")
        val STAT_ODOMETER = booleanPreferencesKey("stat_odometer")
        val OVERLAY_X = floatPreferencesKey("speed_overlay_x")
        val OVERLAY_Y = floatPreferencesKey("speed_overlay_y")
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
                musicPanelVisible = p[Keys.PANEL_VISIBLE] ?: defaults.musicPanelVisible,
                musicPanelFraction = (p[Keys.PANEL_FRACTION] ?: defaults.musicPanelFraction)
                    .coerceIn(UserPreferences.MIN_PANEL_FRACTION, UserPreferences.MAX_PANEL_FRACTION),
                lyricsEnabled = p[Keys.LYRICS] ?: defaults.lyricsEnabled,
                onboardingDone = p[Keys.ONBOARDING_DONE] ?: defaults.onboardingDone,
                spotifyEnabled = p[Keys.SPOTIFY] ?: defaults.spotifyEnabled,
                musicSource = MusicSource.entries.firstOrNull { it.name == p[Keys.MUSIC_SOURCE] } ?: defaults.musicSource,
                googleMapsEnabled = p[Keys.GOOGLE_MAPS] ?: defaults.googleMapsEnabled,
                speedOverlayExpanded = p[Keys.OVERLAY_EXPANDED] ?: defaults.speedOverlayExpanded,
                // "COLOURFUL" was the name of COLOR in 1.1 test builds.
                mapStyle = MapStyle.entries.firstOrNull { it.name == p[Keys.MAP_STYLE]?.replace("COLOURFUL", "COLOR") } ?: defaults.mapStyle,
                voiceDirections = p[Keys.VOICE_DIRECTIONS] ?: defaults.voiceDirections,
                showMap = p[Keys.SHOW_MAP] ?: defaults.showMap,
                showSpeedometer = p[Keys.SHOW_SPEEDOMETER] ?: defaults.showSpeedometer,
                showMapLandscape = p[Keys.SHOW_MAP_LAND] ?: defaults.showMapLandscape,
                showSpeedometerLandscape = p[Keys.SHOW_SPEEDOMETER_LAND] ?: defaults.showSpeedometerLandscape,
                showMusicLandscape = p[Keys.SHOW_MUSIC_LAND] ?: defaults.showMusicLandscape,
                showDistance = p[Keys.STAT_DISTANCE] ?: defaults.showDistance,
                showTime = p[Keys.STAT_TIME] ?: defaults.showTime,
                showAverage = p[Keys.STAT_AVERAGE] ?: defaults.showAverage,
                showMax = p[Keys.STAT_MAX] ?: defaults.showMax,
                showOdometer = p[Keys.STAT_ODOMETER] ?: defaults.showOdometer,
                speedOverlayPosition = p[Keys.OVERLAY_X]?.let { x ->
                    p[Keys.OVERLAY_Y]?.let { y -> OverlayPosition(x.coerceIn(0f, 1f), y.coerceIn(0f, 1f)) }
                },
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
    suspend fun setMapStyle(style: MapStyle) = dataStore.edit { it[Keys.MAP_STYLE] = style.name }
    suspend fun setVoiceDirections(enabled: Boolean) = dataStore.edit { it[Keys.VOICE_DIRECTIONS] = enabled }

    /**
     * Shows or hides one view of the Ride screen, for portrait or for [landscape]; refuses to hide
     * the last visible one.
     */
    suspend fun setView(view: RideView, visible: Boolean, landscape: Boolean = false) = dataStore.edit {
        val defaults = UserPreferences()
        val keys = if (landscape) {
            Triple(Keys.SHOW_MAP_LAND, Keys.SHOW_SPEEDOMETER_LAND, Keys.SHOW_MUSIC_LAND)
        } else {
            Triple(Keys.SHOW_MAP, Keys.SHOW_SPEEDOMETER, Keys.PANEL_VISIBLE)
        }
        val (defMap, defSpeed, defMusic) = defaults.views(landscape)
        val map = it[keys.first] ?: defMap
        val speed = it[keys.second] ?: defSpeed
        val music = it[keys.third] ?: defMusic
        val next = when (view) {
            RideView.MAP -> Triple(visible, speed, music)
            RideView.SPEEDOMETER -> Triple(map, visible, music)
            RideView.MUSIC -> Triple(map, speed, visible)
        }
        if (next.first || next.second || next.third) {
            it[keys.first] = next.first
            it[keys.second] = next.second
            it[keys.third] = next.third
        }
    }

    suspend fun setStatVisible(stat: RideStat, visible: Boolean) = dataStore.edit {
        val key = when (stat) {
            RideStat.DISTANCE -> Keys.STAT_DISTANCE
            RideStat.TIME -> Keys.STAT_TIME
            RideStat.AVERAGE -> Keys.STAT_AVERAGE
            RideStat.MAX -> Keys.STAT_MAX
            RideStat.ODOMETER -> Keys.STAT_ODOMETER
        }
        it[key] = visible
    }

    /** Null docks the speedometer back above the bottom controls. */
    suspend fun setSpeedOverlayPosition(position: OverlayPosition?) = dataStore.edit {
        if (position == null) {
            it.remove(Keys.OVERLAY_X)
            it.remove(Keys.OVERLAY_Y)
        } else {
            it[Keys.OVERLAY_X] = position.x.coerceIn(0f, 1f)
            it[Keys.OVERLAY_Y] = position.y.coerceIn(0f, 1f)
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
    suspend fun setLyricsEnabled(enabled: Boolean) = dataStore.edit { it[Keys.LYRICS] = enabled }
    suspend fun setOnboardingDone() = dataStore.edit { it[Keys.ONBOARDING_DONE] = true }
    suspend fun setMusicPanelVisible(visible: Boolean) = dataStore.edit { it[Keys.PANEL_VISIBLE] = visible }

    suspend fun setMusicPanelFraction(fraction: Float) = dataStore.edit {
        it[Keys.PANEL_FRACTION] = fraction.coerceIn(UserPreferences.MIN_PANEL_FRACTION, UserPreferences.MAX_PANEL_FRACTION)
    }
}
