package com.ridedeck.app.ui.settings

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.ridedeck.app.data.location.LocationRepository
import com.ridedeck.app.data.maps.GbfsRepository
import com.ridedeck.app.data.prefs.ThemeMode
import com.ridedeck.app.data.prefs.UserPreferences
import com.ridedeck.app.data.prefs.UserPreferencesRepository
import com.ridedeck.core.format.Units
import com.ridedeck.core.gbfs.GbfsSystem
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

data class BikeShareSearch(
    val loading: Boolean = false,
    val systems: List<GbfsSystem> = emptyList(),
    val error: String? = null,
)

data class SettingsUiState(
    val prefs: UserPreferences = UserPreferences(),
    val bikeShare: BikeShareSearch = BikeShareSearch(),
    val message: String? = null,
)

@HiltViewModel
class SettingsViewModel @Inject constructor(
    private val prefs: UserPreferencesRepository,
    private val gbfsRepository: GbfsRepository,
    private val locationRepository: LocationRepository,
) : ViewModel() {

    private val bikeShare = MutableStateFlow(BikeShareSearch())
    private val message = MutableStateFlow<String?>(null)

    val uiState: StateFlow<SettingsUiState> = combine(
        prefs.preferences,
        bikeShare,
        message,
    ) { p, share, msg ->
        SettingsUiState(prefs = p, bikeShare = share, message = msg)
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), SettingsUiState())

    fun setUnit(unit: SpeedUnit) = launch { prefs.setSpeedUnit(unit) }
    fun setTheme(theme: ThemeMode) = launch { prefs.setTheme(theme) }
    fun setAutoPause(v: Boolean) = launch { prefs.setAutoPause(v) }
    fun setKeepScreenOn(v: Boolean) = launch { prefs.setKeepScreenOn(v) }
    fun setRideModeLock(v: Boolean) = launch { prefs.setRideModeLock(v) }
    fun setSpeedAlertEnabled(v: Boolean) = launch { prefs.setSpeedAlertEnabled(v) }
    fun setSpeedAlertSound(v: Boolean) = launch { prefs.setSpeedAlertSound(v) }
    fun setSpeedAlertVibrate(v: Boolean) = launch { prefs.setSpeedAlertVibrate(v) }
    fun setShowBikeShops(v: Boolean) = launch { prefs.setShowBikeShops(v) }
    fun setShowBikeShare(v: Boolean) = launch { prefs.setShowBikeShare(v) }
    fun setShowBikeShareInWalk(v: Boolean) = launch { prefs.setShowBikeShareInWalkMode(v) }
    fun setOnlyWithBikes(v: Boolean) = launch { prefs.setOnlyStationsWithBikes(v) }
    fun setShowCycleLanes(v: Boolean) = launch { prefs.setShowCycleLanes(v) }
    fun setLyricsEnabled(v: Boolean) = launch { prefs.setLyricsEnabled(v) }

    /** [value] is in the user's display unit. */
    fun setSpeedAlertLimit(value: Float, unit: SpeedUnit) = launch {
        prefs.setSpeedAlertLimitMps(Units.toMps(value.toDouble(), unit))
    }

    fun findBikeShareSystems() {
        bikeShare.update { it.copy(loading = true, error = null) }
        viewModelScope.launch {
            try {
                val here = locationRepository.lastKnown()?.point
                val systems = gbfsRepository.systemsNear(here).take(MAX_SYSTEMS)
                bikeShare.value = BikeShareSearch(systems = systems, error = if (systems.isEmpty()) "No public systems found nearby" else null)
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                bikeShare.value = BikeShareSearch(error = "Couldn't load the bike-share catalogue")
            }
        }
    }

    fun chooseBikeShareSystem(name: String?, url: String?) = launch {
        val clean = url?.trim()
        if (clean != null && clean.isNotEmpty() && !clean.startsWith("https://") && !clean.startsWith("http://")) {
            message.value = "Feed URL must start with https://"
            return@launch
        }
        prefs.setGbfsSystem(name, clean)
        bikeShare.value = BikeShareSearch()
    }

    fun dismissMessage() {
        message.value = null
    }

    private fun launch(block: suspend () -> Unit) = viewModelScope.launch { block() }

    private companion object {
        const val MAX_SYSTEMS = 40
    }
}
