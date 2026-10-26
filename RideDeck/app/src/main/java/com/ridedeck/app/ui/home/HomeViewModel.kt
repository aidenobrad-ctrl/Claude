package com.ridedeck.app.ui.home

import android.content.Context
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.ridedeck.app.data.local.LifetimeTotals
import com.ridedeck.app.data.location.LocationAccuracy
import com.ridedeck.app.data.location.LocationRepository
import com.ridedeck.app.data.prefs.UserPreferences
import com.ridedeck.app.data.prefs.UserPreferencesRepository
import com.ridedeck.app.data.session.SessionRepository
import com.ridedeck.app.tracking.TrackingManager
import com.ridedeck.app.tracking.TrackingService
import com.ridedeck.core.mode.ModeProfile
import com.ridedeck.core.model.ActivityMode
import com.ridedeck.core.model.SpeedUnit
import com.ridedeck.core.tracking.SpeedSmoother
import com.ridedeck.core.tracking.TripSnapshot
import dagger.hilt.android.lifecycle.HiltViewModel
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.catch
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.onStart
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import javax.inject.Inject

data class HomeUiState(
    val mode: ActivityMode = ActivityMode.BIKE,
    val unit: SpeedUnit = SpeedUnit.KMH,
    val snapshot: TripSnapshot = TripSnapshot.idle(ActivityMode.BIKE),
    /** Live speed while no session is recording (only while this screen is visible). */
    val previewSpeedMps: Double = 0.0,
    val totals: LifetimeTotals = LifetimeTotals.EMPTY,
    val hasLocationPermission: Boolean = true,
    val speedAlertEnabled: Boolean = false,
    val speedAlertLimitMps: Double = UserPreferences.DEFAULT_ALERT_LIMIT_MPS,
    val expanded: Boolean = true,
) {
    val sessionActive: Boolean get() = snapshot.isActive
    val profile: ModeProfile get() = ModeProfile.of(mode)
    val displaySpeedMps: Double get() = if (sessionActive) snapshot.currentSpeedMps else previewSpeedMps

    /** Lifetime odometer for the selected mode including the session in progress. */
    val odometerM: Double get() = totals.distance(mode) + (snapshot.distanceByMode[mode] ?: 0.0)

    val recordMps: Double get() = maxOf(totals.record(mode), snapshot.maxSpeedByMode[mode] ?: 0.0)

    val isNewRecord: Boolean get() {
        val live = snapshot.maxSpeedByMode[mode] ?: 0.0
        return sessionActive && live > 0.5 && live > totals.record(mode)
    }
}

@OptIn(ExperimentalCoroutinesApi::class)
@HiltViewModel
class HomeViewModel @Inject constructor(
    @ApplicationContext private val appContext: Context,
    private val preferencesRepository: UserPreferencesRepository,
    private val trackingManager: TrackingManager,
    private val locationRepository: LocationRepository,
    sessionRepository: SessionRepository,
) : ViewModel() {

    private val permissionTick = MutableStateFlow(0)

    private val previewSpeed: Flow<Double> = combine(
        trackingManager.snapshot.map { it.isActive }.distinctUntilChanged(),
        preferencesRepository.mode,
        permissionTick,
    ) { active, mode, _ -> active to mode }
        .flatMapLatest { (active, mode) ->
            if (active || !locationRepository.hasLocationPermission()) {
                flowOf(0.0)
            } else {
                val smoother = SpeedSmoother(ModeProfile.of(mode))
                locationRepository.updates(1_000, LocationAccuracy.HIGH)
                    .map { fix ->
                        if (fix.accuracyM > ModeProfile.of(mode).maxAcceptedAccuracyM) {
                            smoother.current
                        } else {
                            smoother.update(fix.speedMps?.toDouble() ?: 0.0, fix.elapsedRealtimeMs)
                        }
                    }
                    .catch { emit(0.0) }
            }
        }
        .onStart { emit(0.0) }

    val uiState: StateFlow<HomeUiState> = combine(
        preferencesRepository.preferences,
        trackingManager.snapshot,
        sessionRepository.observeTotals(),
        previewSpeed,
        permissionTick,
    ) { prefs, snapshot, totals, preview, _ ->
        HomeUiState(
            mode = prefs.mode,
            unit = prefs.speedUnit,
            snapshot = snapshot,
            previewSpeedMps = preview,
            totals = totals,
            hasLocationPermission = locationRepository.hasPreciseLocationPermission(),
            speedAlertEnabled = prefs.speedAlertEnabled,
            speedAlertLimitMps = prefs.speedAlertLimitMps,
            expanded = prefs.speedOverlayExpanded,
        )
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), HomeUiState())

    /** Called when the screen resumes, since permissions may have changed in system settings. */
    fun refreshPermissions() {
        permissionTick.value++
    }

    fun setExpanded(expanded: Boolean) = viewModelScope.launch { preferencesRepository.setSpeedOverlayExpanded(expanded) }

    fun toggleUnit() = viewModelScope.launch {
        val current = preferencesRepository.current().speedUnit
        preferencesRepository.setSpeedUnit(if (current == SpeedUnit.KMH) SpeedUnit.MPH else SpeedUnit.KMH)
    }

    /** Returns false when location permission is missing, so the UI can explain why. */
    fun start(): Boolean {
        if (!locationRepository.hasPreciseLocationPermission()) return false
        TrackingService.start(appContext)
        return true
    }

    fun pause() = trackingManager.pause()
    fun resume() = trackingManager.resume()
    fun stop() = trackingManager.stop()
    fun resetMax() = trackingManager.resetMaxSpeed()
}
