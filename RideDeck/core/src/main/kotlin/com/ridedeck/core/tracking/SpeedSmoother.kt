package com.ridedeck.core.tracking

import com.ridedeck.core.mode.ModeProfile
import kotlin.math.exp

/**
 * Removes GPS speed jitter without adding noticeable lag.
 *
 * Each sample is first clamped to a physically plausible range (no faster than the mode allows,
 * no larger change than the mode's max acceleration since the previous sample), then blended in
 * with a time-aware exponential moving average whose weight depends on the gap between samples.
 * Speeds under [noiseFloorMps] are reported as exactly zero so the display rests at 0 when stopped.
 */
class SpeedSmoother(
    profile: ModeProfile,
    private val noiseFloorMps: Double = 0.25,
) {
    var profile: ModeProfile = profile
        private set

    private var lastSpeed: Double? = null
    private var lastTimeMs: Long? = null

    val current: Double get() = snap(lastSpeed ?: 0.0)

    fun setProfile(profile: ModeProfile) {
        this.profile = profile
    }

    fun reset() {
        lastSpeed = null
        lastTimeMs = null
    }

    fun update(rawSpeedMps: Double, timeMs: Long): Double {
        val raw = if (rawSpeedMps.isNaN()) 0.0 else rawSpeedMps.coerceIn(0.0, profile.maxPlausibleSpeedMps)
        val prev = lastSpeed
        val prevTime = lastTimeMs
        if (prev == null || prevTime == null) {
            lastSpeed = raw
            lastTimeMs = timeMs
            return snap(raw)
        }
        if (timeMs <= prevTime) return snap(prev)

        val dtSec = (timeMs - prevTime) / 1000.0
        val maxDelta = profile.maxAccelerationMps2 * dtSec
        val clamped = raw.coerceIn(prev - maxDelta, prev + maxDelta)
        val alpha = 1.0 - exp(-dtSec / profile.smoothingTauSec)
        val smoothed = (prev + alpha * (clamped - prev)).coerceAtLeast(0.0)
        lastSpeed = smoothed
        lastTimeMs = timeMs
        return snap(smoothed)
    }

    private fun snap(v: Double): Double = if (v < noiseFloorMps) 0.0 else v
}
