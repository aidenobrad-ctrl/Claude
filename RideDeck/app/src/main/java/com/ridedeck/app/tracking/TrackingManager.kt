package com.ridedeck.app.tracking

import android.os.SystemClock
import com.ridedeck.app.data.local.RoutePointEntity
import com.ridedeck.app.data.local.SessionEntity
import com.ridedeck.app.data.location.LocationAccuracy
import com.ridedeck.app.data.location.LocationRepository
import com.ridedeck.app.data.prefs.UserPreferences
import com.ridedeck.app.data.prefs.UserPreferencesRepository
import com.ridedeck.app.data.session.SessionRepository
import com.ridedeck.app.di.ApplicationScope
import com.ridedeck.core.model.ActivityMode
import com.ridedeck.core.model.LocationFix
import com.ridedeck.core.tracking.SpeedAlertMonitor
import com.ridedeck.core.tracking.TripAccumulator
import com.ridedeck.core.tracking.TripSnapshot
import com.ridedeck.core.tracking.TripStatus
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.emptyFlow
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.retryWhen
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import javax.inject.Inject
import javax.inject.Singleton

sealed interface TrackingEvent {
    data class SessionSaved(val sessionId: Long) : TrackingEvent
    data object SessionDiscarded : TrackingEvent
    data class Error(val message: String) : TrackingEvent
}

/**
 * Owns the live session: consumes location fixes, runs the [TripAccumulator], fires speed alerts and
 * persists route points in batches. All mutable state is confined to a single-threaded dispatcher,
 * so no locks are needed. The foreground [TrackingService] keeps the process alive while this runs.
 */
@OptIn(ExperimentalCoroutinesApi::class)
@Singleton
class TrackingManager @Inject constructor(
    private val locationRepository: LocationRepository,
    private val sessionRepository: SessionRepository,
    private val preferencesRepository: UserPreferencesRepository,
    private val alertPlayer: SpeedAlertPlayer,
    @ApplicationScope private val appScope: CoroutineScope,
) {
    private val dispatcher = Dispatchers.Default.limitedParallelism(1)

    private val _snapshot = MutableStateFlow(TripSnapshot.idle(ActivityMode.BIKE))
    val snapshot: StateFlow<TripSnapshot> = _snapshot.asStateFlow()

    private val _sessionId = MutableStateFlow<Long?>(null)
    val sessionId: StateFlow<Long?> = _sessionId.asStateFlow()

    private val _events = MutableSharedFlow<TrackingEvent>(extraBufferCapacity = 8)
    val events: SharedFlow<TrackingEvent> = _events.asSharedFlow()

    val hasSession: Boolean get() = _sessionId.value != null

    private var trip: TripAccumulator? = null
    private var sessionJob: Job? = null
    private val pendingPoints = ArrayList<RoutePointEntity>()
    private val alertMonitor = SpeedAlertMonitor()
    private var prefs = UserPreferences()

    /** Starts a new session. Returns false when it could not start (e.g. no location permission). */
    suspend fun startSession(): Boolean = withContext(dispatcher) {
        if (trip != null) return@withContext true
        beginSession(recover = null)
    }

    /**
     * Continues the most recent unfinished session after Android restarted the service.
     * Returns false when there is nothing to recover.
     */
    suspend fun recoverOpenSession(): Boolean = withContext(dispatcher) {
        if (trip != null) return@withContext true
        val open = sessionRepository.getOpenSessions().maxByOrNull { it.startedAtMs } ?: return@withContext false
        val lastActivity = sessionRepository.lastActivityTime(open)
        if (System.currentTimeMillis() - lastActivity > MAX_RECOVERY_GAP_MS) {
            // Too long ago to be the same outing: close it instead of stitching a new ride onto it.
            sessionRepository.finalizeOrphanedSessions(exceptId = null)
            return@withContext false
        }
        sessionRepository.finalizeOrphanedSessions(exceptId = open.id)
        beginSession(recover = open)
    }

    /** Closes sessions left open by a dead process, unless one is being recorded right now. */
    suspend fun cleanUpOrphans() = withContext(dispatcher) {
        if (trip == null) sessionRepository.finalizeOrphanedSessions(exceptId = null)
    }

    fun pause() = appScope.launch(dispatcher) {
        val t = trip ?: return@launch
        t.pause(SystemClock.elapsedRealtime())
        publish(t)
        flush()
    }

    fun resume() = appScope.launch(dispatcher) {
        val t = trip ?: return@launch
        t.resume(SystemClock.elapsedRealtime())
        publish(t)
    }

    fun stop() = appScope.launch(dispatcher) { stopInternal() }

    fun resetMaxSpeed() = appScope.launch(dispatcher) {
        val t = trip ?: return@launch
        t.resetMaxSpeed()
        publish(t)
    }

    private suspend fun beginSession(recover: SessionEntity?): Boolean {
        if (!locationRepository.hasPreciseLocationPermission()) {
            _events.tryEmit(TrackingEvent.Error("Precise location is needed to record a session."))
            return false
        }
        prefs = preferencesRepository.current()
        val now = SystemClock.elapsedRealtime()
        val t = TripAccumulator(prefs.mode, prefs.autoPauseEnabled)
        val id = if (recover != null) {
            t.restore(
                nowMs = now,
                distanceByMode = mapOf(ActivityMode.BIKE to recover.bikeDistanceM, ActivityMode.WALK to recover.walkDistanceM),
                maxSpeedByMode = mapOf(ActivityMode.BIKE to recover.bikeMaxSpeedMps, ActivityMode.WALK to recover.walkMaxSpeedMps),
                maxSpeedMps = recover.maxSpeedMps,
                movingTimeMs = recover.movingTimeMs,
                elapsedTimeMs = recover.elapsedTimeMs,
                lastSegment = recover.lastSegment,
            )
            recover.id
        } else {
            t.start(now)
            sessionRepository.createSession(prefs.mode, System.currentTimeMillis())
        }
        trip = t
        _sessionId.value = id
        pendingPoints.clear()
        alertMonitor.reset()
        publish(t)

        sessionJob = appScope.launch(dispatcher) {
            launch { preferencesRepository.preferences.collect { onPreferences(it) } }
            launch { tickLoop() }
            launch { locationFixes().collect { onFix(it) } }
        }
        return true
    }

    /**
     * GPS runs at 1 Hz while moving, 0.5 Hz while auto-paused (to detect riding off again) and is
     * switched off entirely during a manual pause.
     */
    private fun locationFixes(): Flow<LocationFix> = _snapshot
        .map { it.status }
        .distinctUntilChanged()
        .flatMapLatest { status ->
            when (status) {
                TripStatus.RUNNING -> locationRepository.updates(1_000, LocationAccuracy.HIGH)
                TripStatus.AUTO_PAUSED -> locationRepository.updates(2_000, LocationAccuracy.HIGH)
                else -> emptyFlow()
            }
        }
        .retryWhen { cause, _ ->
            if (cause is SecurityException) {
                _events.tryEmit(TrackingEvent.Error("Location permission was revoked. Recording stopped."))
                appScope.launch(dispatcher) { stopInternal() }
                false
            } else {
                delay(5_000)
                true
            }
        }

    private suspend fun tickLoop() {
        var ticks = 0
        while (true) {
            delay(1_000)
            val t = trip ?: return
            t.tick(SystemClock.elapsedRealtime())
            publish(t)
            if (++ticks % FLUSH_EVERY_TICKS == 0) flush()
        }
    }

    private fun onPreferences(p: UserPreferences) {
        prefs = p
        val t = trip ?: return
        if (t.mode != p.mode) {
            t.switchMode(p.mode, SystemClock.elapsedRealtime())
            alertMonitor.reset()
        }
        t.autoPauseEnabled = p.autoPauseEnabled
        publish(t)
    }

    private suspend fun onFix(fix: LocationFix) {
        val t = trip ?: return
        val id = _sessionId.value ?: return
        val result = t.onFix(fix)
        val snap = publish(t)
        if (result.recordPoint) {
            pendingPoints += RoutePointEntity(
                sessionId = id,
                segment = result.segment,
                latitude = fix.latitude,
                longitude = fix.longitude,
                timeMs = fix.wallTimeMs,
                speedMps = snap.currentSpeedMps.toFloat(),
                accuracyM = fix.accuracyM,
                altitudeM = fix.altitudeM,
            )
            if (pendingPoints.size >= FLUSH_POINTS) flush()
        }
        val alert = alertMonitor.evaluate(
            mode = snap.mode,
            enabled = prefs.speedAlertEnabled && snap.status == TripStatus.RUNNING,
            limitMps = prefs.speedAlertLimitMps,
            speedMps = snap.currentSpeedMps,
            nowMs = fix.elapsedRealtimeMs,
        )
        if (alert) alertPlayer.play(prefs.speedAlertSound, prefs.speedAlertVibrate)
    }

    private fun publish(t: TripAccumulator): TripSnapshot =
        t.snapshot(SystemClock.elapsedRealtime()).also { _snapshot.value = it }

    private suspend fun flush() {
        val id = _sessionId.value ?: return
        val t = trip ?: return
        val points = pendingPoints.toList()
        pendingPoints.clear()
        try {
            sessionRepository.appendPoints(points)
            sessionRepository.saveProgress(id, t.snapshot(SystemClock.elapsedRealtime()))
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            // Keep the points for the next attempt rather than losing part of the route.
            pendingPoints.addAll(0, points)
        }
    }

    private suspend fun stopInternal() {
        val t = trip ?: return
        val id = _sessionId.value
        val final = t.stop(SystemClock.elapsedRealtime())
        sessionJob?.cancel()
        sessionJob = null
        trip = null
        try {
            if (id != null) {
                sessionRepository.appendPoints(pendingPoints.toList())
                pendingPoints.clear()
                if (final.distanceM < MIN_SAVED_DISTANCE_M && final.movingTimeMs < MIN_SAVED_MOVING_MS) {
                    sessionRepository.deleteSession(id)
                    _events.tryEmit(TrackingEvent.SessionDiscarded)
                } else {
                    sessionRepository.saveProgress(id, final, endedAtMs = System.currentTimeMillis())
                    _events.tryEmit(TrackingEvent.SessionSaved(id))
                }
            }
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            _events.tryEmit(TrackingEvent.Error("Couldn't save the session: ${e.message}"))
        } finally {
            pendingPoints.clear()
            _sessionId.value = null
            _snapshot.value = TripSnapshot.idle(prefs.mode)
        }
    }

    private companion object {
        const val FLUSH_POINTS = 20
        const val FLUSH_EVERY_TICKS = 30
        /** Sessions shorter than both limits are treated as accidental starts and not kept. */
        const val MIN_SAVED_DISTANCE_M = 20.0
        const val MIN_SAVED_MOVING_MS = 60_000L
        const val MAX_RECOVERY_GAP_MS = 30 * 60_000L
    }
}
