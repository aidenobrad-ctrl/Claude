package com.ridedeck.core.tracking

import com.ridedeck.core.mode.ModeProfile
import com.ridedeck.core.model.ActivityMode

/**
 * Decides when to fire the over-speed alert. Bike mode only.
 *
 * Fires once when the speed crosses the limit, repeats every [repeatMs] while the rider stays
 * above it, and re-arms only after dropping [hysteresisMps] below the limit so hovering around the
 * threshold does not cause a burst of alerts.
 */
class SpeedAlertMonitor(
    private val repeatMs: Long = 30_000,
    private val hysteresisMps: Double = 0.5,
) {
    private var armed = true
    private var lastAlertMs: Long? = null

    fun evaluate(
        mode: ActivityMode,
        enabled: Boolean,
        limitMps: Double,
        speedMps: Double,
        nowMs: Long,
    ): Boolean {
        if (!enabled || !ModeProfile.of(mode).speedAlertSupported || limitMps <= 0.0) {
            reset()
            return false
        }
        if (speedMps > limitMps) {
            val last = lastAlertMs
            val fire = armed || (last != null && nowMs - last >= repeatMs)
            if (fire) {
                armed = false
                lastAlertMs = nowMs
            }
            return fire
        }
        if (speedMps < limitMps - hysteresisMps) {
            armed = true
            lastAlertMs = null
        }
        return false
    }

    fun reset() {
        armed = true
        lastAlertMs = null
    }
}
