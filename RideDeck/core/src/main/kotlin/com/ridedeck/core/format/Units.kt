package com.ridedeck.core.format

import com.ridedeck.core.model.SpeedUnit
import java.util.Locale
import kotlin.math.roundToLong

object Units {
    const val METERS_PER_MILE = 1609.344
    const val MPS_TO_KMH = 3.6
    const val MPS_TO_MPH = 3600.0 / METERS_PER_MILE

    fun speed(mps: Double, unit: SpeedUnit): Double = when (unit) {
        SpeedUnit.KMH -> mps * MPS_TO_KMH
        SpeedUnit.MPH -> mps * MPS_TO_MPH
    }

    fun toMps(value: Double, unit: SpeedUnit): Double = when (unit) {
        SpeedUnit.KMH -> value / MPS_TO_KMH
        SpeedUnit.MPH -> value / MPS_TO_MPH
    }

    /** Distance in kilometres or miles, matching the speed unit. */
    fun distance(meters: Double, unit: SpeedUnit): Double = when (unit) {
        SpeedUnit.KMH -> meters / 1000.0
        SpeedUnit.MPH -> meters / METERS_PER_MILE
    }

    fun speedLabel(unit: SpeedUnit): String = if (unit == SpeedUnit.KMH) "km/h" else "mph"
    fun distanceLabel(unit: SpeedUnit): String = if (unit == SpeedUnit.KMH) "km" else "mi"
    fun paceLabel(unit: SpeedUnit): String = if (unit == SpeedUnit.KMH) "min/km" else "min/mi"
}

object Pace {
    /** Below this speed the user is effectively standing still and pace is undefined. */
    const val MIN_SPEED_FOR_PACE_MPS = 0.3

    /** Longest pace still shown (59:59 per unit). */
    const val MAX_PACE_SECONDS = 3599L

    /**
     * Seconds needed to cover one kilometre or one mile at [speedMps], or null when the user is
     * stationary or the pace would be meaninglessly slow.
     */
    fun secondsPerUnit(speedMps: Double, unit: SpeedUnit): Long? {
        if (speedMps.isNaN() || speedMps < MIN_SPEED_FOR_PACE_MPS) return null
        val unitMeters = if (unit == SpeedUnit.KMH) 1000.0 else Units.METERS_PER_MILE
        val seconds = (unitMeters / speedMps).roundToLong()
        return seconds.takeIf { it <= MAX_PACE_SECONDS }
    }

    /** Average pace for a whole session. */
    fun averageSecondsPerUnit(distanceM: Double, movingTimeMs: Long, unit: SpeedUnit): Long? {
        if (distanceM <= 0.0 || movingTimeMs <= 0) return null
        return secondsPerUnit(distanceM / (movingTimeMs / 1000.0), unit)
    }

    fun format(secondsPerUnit: Long?): String {
        if (secondsPerUnit == null) return "--:--"
        return String.format(Locale.US, "%d:%02d", secondsPerUnit / 60, secondsPerUnit % 60)
    }
}

object Formatters {

    fun duration(ms: Long): String {
        val totalSeconds = (ms / 1000).coerceAtLeast(0)
        val h = totalSeconds / 3600
        val m = (totalSeconds % 3600) / 60
        val s = totalSeconds % 60
        return if (h > 0) {
            String.format(Locale.US, "%d:%02d:%02d", h, m, s)
        } else {
            String.format(Locale.US, "%02d:%02d", m, s)
        }
    }

    fun speed(mps: Double, unit: SpeedUnit, decimals: Int = 0): String =
        String.format(Locale.getDefault(), "%.${decimals}f", Units.speed(mps, unit))

    fun distance(meters: Double, unit: SpeedUnit, decimals: Int = 2): String =
        String.format(Locale.getDefault(), "%.${decimals}f", Units.distance(meters, unit))

    /** Short human distance for navigation ("350 m", "1.2 km", "0.3 mi"). */
    fun navDistance(meters: Double, unit: SpeedUnit): String = when (unit) {
        SpeedUnit.KMH -> if (meters < 1000) "${roundTo(meters, 10)} m"
        else String.format(Locale.getDefault(), "%.1f km", meters / 1000.0)
        SpeedUnit.MPH -> {
            val feet = meters * 3.28084
            if (feet < 1000) "${roundTo(feet, 50)} ft"
            else String.format(Locale.getDefault(), "%.1f mi", meters / Units.METERS_PER_MILE)
        }
    }

    /** Spoken form of a duration, used for accessibility labels. */
    fun spokenDuration(seconds: Long): String {
        val h = seconds / 3600
        val m = (seconds % 3600) / 60
        return when {
            h > 0 -> "$h hours $m minutes"
            m > 0 -> "$m minutes"
            else -> "less than a minute"
        }
    }

    private fun roundTo(value: Double, step: Int): Long = ((value / step).roundToLong() * step).coerceAtLeast(0)
}
