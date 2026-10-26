package com.ridedeck.core.tracking

import com.ridedeck.core.geo.Geo
import com.ridedeck.core.mode.ModeProfile
import com.ridedeck.core.model.ActivityMode
import com.ridedeck.core.model.LocationFix

enum class TripStatus { IDLE, RUNNING, PAUSED, AUTO_PAUSED }

data class TripSnapshot(
    val status: TripStatus,
    val mode: ActivityMode,
    val currentSpeedMps: Double,
    val maxSpeedMps: Double,
    val maxSpeedByMode: Map<ActivityMode, Double>,
    val distanceM: Double,
    val distanceByMode: Map<ActivityMode, Double>,
    /** Time spent actually moving (excludes manual and auto pauses). */
    val movingTimeMs: Long,
    /** Time since start, excluding manual pauses. */
    val elapsedTimeMs: Long,
    val averageSpeedMps: Double,
    val dominantMode: ActivityMode,
    val lastFix: LocationFix?,
    val segment: Int,
) {
    val isActive: Boolean get() = status != TripStatus.IDLE

    companion object {
        fun idle(mode: ActivityMode) = TripSnapshot(
            status = TripStatus.IDLE,
            mode = mode,
            currentSpeedMps = 0.0,
            maxSpeedMps = 0.0,
            maxSpeedByMode = emptyMap(),
            distanceM = 0.0,
            distanceByMode = emptyMap(),
            movingTimeMs = 0,
            elapsedTimeMs = 0,
            averageSpeedMps = 0.0,
            dominantMode = mode,
            lastFix = null,
            segment = 0,
        )
    }
}

/** What the caller should do with a fix it fed into [TripAccumulator.onFix]. */
data class FixResult(
    val accepted: Boolean,
    /** True when the fix extends the recorded route and should be persisted. */
    val recordPoint: Boolean,
    val segment: Int,
)

/**
 * The tracking state machine: distance, speeds, timers, auto-pause and per-mode attribution.
 *
 * All timestamps are monotonic milliseconds. The class is not thread-safe; the owner serialises
 * calls (the Android side runs it on a single coroutine).
 *
 * Mode can be switched mid-session. Distance and max speed are attributed to the mode active when
 * they happened, so lifetime odometers and per-mode records stay correct, and the session is saved
 * under its [TripSnapshot.dominantMode].
 */
class TripAccumulator(
    initialMode: ActivityMode,
    autoPauseEnabled: Boolean = true,
    /** Fixes older than this are considered stale and the speed drops to zero. */
    private val staleFixMs: Long = 5_000,
    /** A gap longer than this re-anchors the route instead of drawing a straight jump. */
    private val maxGapMs: Long = 30_000,
) {
    var mode: ActivityMode = initialMode
        private set

    var autoPauseEnabled: Boolean = autoPauseEnabled
        set(value) {
            field = value
            if (!value && status == TripStatus.AUTO_PAUSED) transition(TripStatus.RUNNING, lastEventMs)
        }

    private val profile: ModeProfile get() = ModeProfile.of(mode)
    private val smoother = SpeedSmoother(profile)

    var status: TripStatus = TripStatus.IDLE
        private set

    private var lastTransitionMs = 0L
    private var lastEventMs = 0L
    private var movingAccumMs = 0L
    private var elapsedAccumMs = 0L

    private var distanceM = 0.0
    private val distanceByMode = mutableMapOf<ActivityMode, Double>()
    private var maxSpeedMps = 0.0
    private val maxSpeedByMode = mutableMapOf<ActivityMode, Double>()
    private var currentSpeedMps = 0.0

    /** Last accepted fix, used for speed derivation. */
    private var lastFix: LocationFix? = null
    /** Last point counted into distance; only advances once the user has really moved. */
    private var anchor: LocationFix? = null
    private var segment = 0

    private var belowPauseSinceMs: Long? = null
    private var resumeVotes = 0

    fun start(nowMs: Long) {
        check(status == TripStatus.IDLE) { "Trip already started" }
        resetTotals()
        lastTransitionMs = nowMs
        lastEventMs = nowMs
        status = TripStatus.RUNNING
    }

    fun pause(nowMs: Long) {
        if (status == TripStatus.RUNNING || status == TripStatus.AUTO_PAUSED) {
            transition(TripStatus.PAUSED, nowMs)
            currentSpeedMps = 0.0
            smoother.reset()
        }
    }

    fun resume(nowMs: Long) {
        if (status == TripStatus.PAUSED) {
            transition(TripStatus.RUNNING, nowMs)
            startNewSegment()
        }
    }

    fun stop(nowMs: Long): TripSnapshot {
        if (status != TripStatus.IDLE) transition(TripStatus.IDLE, nowMs)
        return snapshot(nowMs)
    }

    /** Clears the session max speed shown to the user. Per-mode maxima (all-time records) are kept. */
    fun resetMaxSpeed() {
        maxSpeedMps = 0.0
    }

    /**
     * Rebuilds a session from persisted totals, e.g. when Android restarted the tracking service
     * after killing the process. Tracking continues in a new route segment.
     */
    fun restore(
        nowMs: Long,
        distanceByMode: Map<ActivityMode, Double>,
        maxSpeedByMode: Map<ActivityMode, Double>,
        maxSpeedMps: Double,
        movingTimeMs: Long,
        elapsedTimeMs: Long,
        lastSegment: Int,
    ) {
        check(status == TripStatus.IDLE) { "Trip already started" }
        resetTotals()
        this.distanceByMode.putAll(distanceByMode)
        this.distanceM = distanceByMode.values.sum()
        this.maxSpeedByMode.putAll(maxSpeedByMode)
        this.maxSpeedMps = maxSpeedMps
        movingAccumMs = movingTimeMs
        elapsedAccumMs = elapsedTimeMs
        segment = lastSegment + 1
        lastTransitionMs = nowMs
        lastEventMs = nowMs
        status = TripStatus.RUNNING
    }

    /** Switches activity mode mid-session. Totals so far stay attributed to the previous mode. */
    fun switchMode(newMode: ActivityMode, nowMs: Long) {
        if (newMode == mode) return
        mode = newMode
        smoother.setProfile(ModeProfile.of(newMode))
        belowPauseSinceMs = null
        resumeVotes = 0
        lastEventMs = maxOf(lastEventMs, nowMs)
    }

    /** Activity recognition reported the device is still: pause immediately if we are crawling. */
    fun onStillDetected(nowMs: Long) {
        if (autoPauseEnabled && status == TripStatus.RUNNING && currentSpeedMps < profile.autoResumeSpeedMps) {
            transition(TripStatus.AUTO_PAUSED, nowMs)
        }
    }

    /** Called periodically so speed decays to zero if GPS fixes stop arriving. */
    fun tick(nowMs: Long) {
        lastEventMs = maxOf(lastEventMs, nowMs)
        val fix = lastFix ?: return
        if (nowMs - fix.elapsedRealtimeMs > staleFixMs) {
            currentSpeedMps = 0.0
            smoother.reset()
        }
    }

    fun onFix(fix: LocationFix): FixResult {
        val now = fix.elapsedRealtimeMs
        if (status == TripStatus.IDLE || status == TripStatus.PAUSED) {
            return FixResult(accepted = false, recordPoint = false, segment = segment)
        }
        if (fix.accuracyM > profile.maxAcceptedAccuracyM) {
            return FixResult(accepted = false, recordPoint = false, segment = segment)
        }
        lastEventMs = maxOf(lastEventMs, now)

        val prev = lastFix
        if (prev == null) {
            lastFix = fix
            anchor = fix
            val speed = fix.speedMps?.toDouble() ?: 0.0
            currentSpeedMps = smoother.update(speed, now)
            return FixResult(accepted = true, recordPoint = status == TripStatus.RUNNING, segment = segment)
        }

        val dtMs = now - prev.elapsedRealtimeMs
        if (dtMs <= 0) return FixResult(accepted = false, recordPoint = false, segment = segment)
        val dtSec = dtMs / 1000.0
        val stepM = Geo.distanceMeters(prev.latitude, prev.longitude, fix.latitude, fix.longitude)
        val derivedSpeed = stepM / dtSec

        if (dtMs > maxGapMs) {
            // Signal was lost for a while; start a fresh polyline segment instead of a straight line.
            lastFix = fix
            anchor = fix
            smoother.reset()
            currentSpeedMps = smoother.update(fix.speedMps?.toDouble() ?: 0.0, now)
            startNewSegment()
            return FixResult(accepted = true, recordPoint = status == TripStatus.RUNNING, segment = segment)
        }
        if (derivedSpeed > profile.maxPlausibleSpeedMps * 1.5) {
            // Teleport-style GPS glitch.
            return FixResult(accepted = false, recordPoint = false, segment = segment)
        }

        val reportedSpeed = fix.speedMps?.toDouble()
        val speedAccuracy = fix.speedAccuracyMps?.toDouble()
        val raw = if (reportedSpeed != null && (speedAccuracy == null || speedAccuracy <= 2.0)) reportedSpeed else derivedSpeed
        currentSpeedMps = smoother.update(raw, now)
        lastFix = fix

        updateAutoPause(now)

        var record = false
        if (status == TripStatus.RUNNING) {
            val a = anchor ?: fix
            val fromAnchor = Geo.distanceMeters(a.latitude, a.longitude, fix.latitude, fix.longitude)
            if (fromAnchor >= jitterThreshold(a, fix)) {
                distanceM += fromAnchor
                distanceByMode[mode] = (distanceByMode[mode] ?: 0.0) + fromAnchor
                anchor = fix
                record = true
            }
            if (currentSpeedMps > maxSpeedMps) maxSpeedMps = currentSpeedMps
            if (currentSpeedMps > (maxSpeedByMode[mode] ?: 0.0)) maxSpeedByMode[mode] = currentSpeedMps
        } else {
            // Auto-paused: keep the anchor on the user so drift is not counted as distance.
            anchor = fix
        }
        return FixResult(accepted = true, recordPoint = record, segment = segment)
    }

    fun snapshot(nowMs: Long): TripSnapshot {
        val sinceTransition = (nowMs - lastTransitionMs).coerceAtLeast(0)
        val moving = movingAccumMs + if (status == TripStatus.RUNNING) sinceTransition else 0
        val elapsed = elapsedAccumMs +
            if (status == TripStatus.RUNNING || status == TripStatus.AUTO_PAUSED) sinceTransition else 0
        return TripSnapshot(
            status = status,
            mode = mode,
            currentSpeedMps = if (status == TripStatus.RUNNING || status == TripStatus.AUTO_PAUSED) currentSpeedMps else 0.0,
            maxSpeedMps = maxSpeedMps,
            maxSpeedByMode = maxSpeedByMode.toMap(),
            distanceM = distanceM,
            distanceByMode = distanceByMode.toMap(),
            movingTimeMs = moving,
            elapsedTimeMs = elapsed,
            averageSpeedMps = if (moving > 0) distanceM / (moving / 1000.0) else 0.0,
            dominantMode = dominantMode(),
            lastFix = lastFix,
            segment = segment,
        )
    }

    /** The mode that covered the most distance; the current mode if nothing was covered yet. */
    fun dominantMode(): ActivityMode =
        distanceByMode.filterValues { it > 0.0 }.maxByOrNull { it.value }?.key ?: mode

    private fun updateAutoPause(now: Long) {
        if (!autoPauseEnabled) return
        when (status) {
            TripStatus.RUNNING -> {
                if (currentSpeedMps < profile.autoPauseSpeedMps) {
                    val since = belowPauseSinceMs ?: now.also { belowPauseSinceMs = it }
                    if (now - since >= profile.autoPauseDelayMs) {
                        // Moving time stops when the user actually slowed down, not when we noticed.
                        transition(TripStatus.AUTO_PAUSED, maxOf(since, lastTransitionMs))
                    }
                } else {
                    belowPauseSinceMs = null
                }
            }
            TripStatus.AUTO_PAUSED -> {
                if (currentSpeedMps >= profile.autoResumeSpeedMps) {
                    resumeVotes++
                    if (resumeVotes >= 2) transition(TripStatus.RUNNING, now)
                } else {
                    resumeVotes = 0
                }
            }
            else -> Unit
        }
    }

    private fun transition(to: TripStatus, atMs: Long) {
        val at = maxOf(atMs, lastTransitionMs)
        val span = at - lastTransitionMs
        when (status) {
            TripStatus.RUNNING -> {
                movingAccumMs += span
                elapsedAccumMs += span
            }
            TripStatus.AUTO_PAUSED -> elapsedAccumMs += span
            else -> Unit
        }
        status = to
        lastTransitionMs = at
        belowPauseSinceMs = null
        resumeVotes = 0
    }

    private fun startNewSegment() {
        segment++
        lastFix = null
        anchor = null
    }

    private fun resetTotals() {
        movingAccumMs = 0
        elapsedAccumMs = 0
        distanceM = 0.0
        distanceByMode.clear()
        maxSpeedMps = 0.0
        maxSpeedByMode.clear()
        currentSpeedMps = 0.0
        lastFix = null
        anchor = null
        segment = 0
        belowPauseSinceMs = null
        resumeVotes = 0
        smoother.reset()
    }

    /**
     * Minimum movement before distance is counted. Scales with GPS accuracy so standing still with
     * a wandering fix does not inflate the trip, while still counting slow walking.
     */
    private fun jitterThreshold(a: LocationFix, b: LocationFix): Double =
        (minOf(a.accuracyM, b.accuracyM) * 0.5).toDouble().coerceIn(2.0, 10.0)
}
