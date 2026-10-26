package com.ridedeck.app.ui.main

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.ridedeck.app.data.location.LocationRepository
import com.ridedeck.app.data.prefs.UserPreferences
import com.ridedeck.app.data.prefs.UserPreferencesRepository
import com.ridedeck.app.tracking.TrackingEvent
import com.ridedeck.app.tracking.TrackingManager
import com.ridedeck.core.mode.RideModePolicy
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import javax.inject.Inject

data class MainUiState(
    val loaded: Boolean = false,
    val preferences: UserPreferences = UserPreferences(),
    val sessionActive: Boolean = false,
    val rideLocked: Boolean = false,
) {
    val keepScreenOn: Boolean get() = sessionActive && preferences.keepScreenOn
}

@HiltViewModel
class MainViewModel @Inject constructor(
    private val preferencesRepository: UserPreferencesRepository,
    private val trackingManager: TrackingManager,
    private val locationRepository: LocationRepository,
) : ViewModel() {

    val uiState: StateFlow<MainUiState> = combine(
        preferencesRepository.preferences,
        trackingManager.snapshot,
    ) { prefs, snap ->
        MainUiState(
            loaded = true,
            preferences = prefs,
            sessionActive = snap.isActive,
            rideLocked = RideModePolicy.isLocked(
                enabled = prefs.rideModeLockEnabled,
                sessionActive = snap.isActive,
                mode = snap.mode,
                currentSpeedMps = snap.currentSpeedMps,
            ),
        )
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), MainUiState())

    val trackingEvents: SharedFlow<TrackingEvent> = trackingManager.events

    init {
        viewModelScope.launch { trackingManager.cleanUpOrphans() }
    }

    fun needsPermissionOnboarding(prefs: UserPreferences): Boolean =
        !prefs.onboardingDone || !locationRepository.hasLocationPermission()

    fun setMusicPanelVisible(visible: Boolean) = viewModelScope.launch {
        preferencesRepository.setMusicPanelVisible(visible)
    }

    fun setMusicPanelFraction(fraction: Float) = viewModelScope.launch {
        preferencesRepository.setMusicPanelFraction(fraction)
    }
}
