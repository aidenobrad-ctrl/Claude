package com.ridedeck.core

import com.ridedeck.core.model.LocationFix
import kotlin.math.cos

/** Builds synthetic fixes moving due east at a constant speed, one per second. */
object TestFixes {
    const val START_LAT = 51.5
    const val START_LON = -0.12

    fun eastward(
        speedMps: Double,
        seconds: Int,
        startMs: Long = 0,
        startOffsetM: Double = 0.0,
        accuracy: Float = 5f,
        reportSpeed: Boolean = true,
    ): List<LocationFix> = (0..seconds).map { s ->
        fix(
            offsetEastM = startOffsetM + speedMps * s,
            timeMs = startMs + s * 1000L,
            speedMps = if (reportSpeed) speedMps.toFloat() else null,
            accuracy = accuracy,
        )
    }

    fun fix(
        offsetEastM: Double,
        timeMs: Long,
        speedMps: Float? = null,
        accuracy: Float = 5f,
        offsetNorthM: Double = 0.0,
    ): LocationFix {
        val metersPerDegLat = 111_320.0
        val metersPerDegLon = metersPerDegLat * cos(Math.toRadians(START_LAT))
        return LocationFix(
            latitude = START_LAT + offsetNorthM / metersPerDegLat,
            longitude = START_LON + offsetEastM / metersPerDegLon,
            accuracyM = accuracy,
            speedMps = speedMps,
            speedAccuracyMps = 0.5f,
            altitudeM = null,
            elapsedRealtimeMs = timeMs,
            wallTimeMs = 1_700_000_000_000 + timeMs,
        )
    }
}
