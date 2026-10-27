package com.ridedeck.app.ui.settings

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import android.os.PowerManager
import android.provider.Settings
import com.ridedeck.app.data.location.LocationRepository
import com.ridedeck.app.integrations.GoogleMapsLauncher
import com.ridedeck.app.integrations.SpotifyControl
import com.ridedeck.app.ui.permissions.openAppSettings
import dagger.hilt.android.qualifiers.ApplicationContext
import com.ridedeck.app.data.prefs.MapStyle
import com.ridedeck.app.data.prefs.RideStat
import com.ridedeck.app.data.prefs.RideView
import com.ridedeck.app.data.prefs.ThemeMode
import com.ridedeck.app.data.prefs.UserPreferences
import com.ridedeck.app.data.prefs.UserPreferencesRepository
import com.ridedeck.core.format.Units
import com.ridedeck.core.model.SpeedUnit
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import javax.inject.Inject

data class SettingsUiState(
    val prefs: UserPreferences = UserPreferences(),
    val message: String? = null,
    val spotifyInstalled: Boolean = false,
    val spotifyAccess: Boolean = false,
    val googleMapsInstalled: Boolean = false,
    val batteryUnrestricted: Boolean = true,
    val isSamsung: Boolean = android.os.Build.MANUFACTURER.equals("samsung", ignoreCase = true),
)

private data class IntegrationStatus(
    val spotifyInstalled: Boolean = false,
    val spotifyAccess: Boolean = false,
    val googleMapsInstalled: Boolean = false,
    val batteryUnrestricted: Boolean = true,
)

@HiltViewModel
class SettingsViewModel @Inject constructor(
    private val prefs: UserPreferencesRepository,
    private val locationRepository: LocationRepository,
    private val spotify: SpotifyControl,
    @ApplicationContext private val appContext: Context,
) : ViewModel() {

    private val integrations = MutableStateFlow(IntegrationStatus())

    private val message = MutableStateFlow<String?>(null)

    val uiState: StateFlow<SettingsUiState> = combine(
        prefs.preferences,
        message,
        integrations,
    ) { p, msg, i ->
        SettingsUiState(
            prefs = p,
            message = msg,
            spotifyInstalled = i.spotifyInstalled,
            spotifyAccess = i.spotifyAccess,
            googleMapsInstalled = i.googleMapsInstalled,
            batteryUnrestricted = i.batteryUnrestricted,
        )
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), SettingsUiState())

    fun setUnit(unit: SpeedUnit) = launch { prefs.setSpeedUnit(unit) }
    fun setTheme(theme: ThemeMode) = launch { prefs.setTheme(theme) }
    fun setAutoPause(v: Boolean) = launch { prefs.setAutoPause(v) }
    fun setKeepScreenOn(v: Boolean) = launch { prefs.setKeepScreenOn(v) }
    fun setRideModeLock(v: Boolean) = launch { prefs.setRideModeLock(v) }
    fun setSpeedAlertEnabled(v: Boolean) = launch { prefs.setSpeedAlertEnabled(v) }
    fun setSpeedAlertSound(v: Boolean) = launch { prefs.setSpeedAlertSound(v) }
    fun setSpeedAlertVibrate(v: Boolean) = launch { prefs.setSpeedAlertVibrate(v) }
    fun setSpotifyEnabled(v: Boolean) = launch { prefs.setSpotifyEnabled(v) }
    fun setGoogleMapsEnabled(v: Boolean) = launch { prefs.setGoogleMapsEnabled(v) }

    fun refreshIntegrations() {
        integrations.value = IntegrationStatus(
            spotifyInstalled = spotify.isInstalled(),
            spotifyAccess = spotify.hasAccess(),
            googleMapsInstalled = GoogleMapsLauncher.isInstalled(appContext),
            batteryUnrestricted = appContext.getSystemService(PowerManager::class.java)
                ?.isIgnoringBatteryOptimizations(appContext.packageName) ?: true,
        )
    }

    /**
     * Opens Android's battery optimization list, where RideDeck can be set to "Not optimized".
     * One UI is aggressive about this and can end a ride recording or music some time after the
     * screen goes off. (The list rather than a direct prompt, which Play only allows for a few
     * kinds of app.)
     */
    fun openBatterySettings() {
        try {
            appContext.startActivity(Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
        } catch (e: ActivityNotFoundException) {
            appContext.openAppSettings()
        }
    }

    fun openSpotify() = spotify.openSpotify()
    fun openNotificationAccess() = spotify.openAccessSettings()
    fun setView(view: RideView, visible: Boolean, landscape: Boolean) = launch { prefs.setView(view, visible, landscape) }
    fun setStatVisible(stat: RideStat, visible: Boolean) = launch { prefs.setStatVisible(stat, visible) }
    fun setMapStyle(style: MapStyle) = launch { prefs.setMapStyle(style) }
    fun setVoiceDirections(v: Boolean) = launch { prefs.setVoiceDirections(v) }

    fun openGoogleMapsStore() = GoogleMapsLauncher.openStore(appContext)
    fun openAppInfo() = appContext.openAppSettings()
    fun setLyricsEnabled(v: Boolean) = launch { prefs.setLyricsEnabled(v) }

    /** [value] is in the user's display unit. */
    fun setSpeedAlertLimit(value: Float, unit: SpeedUnit) = launch {
        prefs.setSpeedAlertLimitMps(Units.toMps(value.toDouble(), unit))
    }

    fun dismissMessage() {
        message.value = null
    }

    private fun launch(block: suspend () -> Unit) = viewModelScope.launch { block() }
}
