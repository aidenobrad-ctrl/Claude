package com.ridedeck.core.model

/** The two activity types RideDeck supports. The selected mode drives tracking, routing and UI. */
enum class ActivityMode {
    BIKE,
    WALK;

    companion object {
        fun fromName(name: String?): ActivityMode = entries.firstOrNull { it.name == name } ?: BIKE
    }
}

enum class SpeedUnit {
    KMH,
    MPH;

    companion object {
        fun fromName(name: String?): SpeedUnit = entries.firstOrNull { it.name == name } ?: MPH
    }
}

data class GeoPoint(val latitude: Double, val longitude: Double)

/**
 * A platform-independent location sample.
 *
 * [elapsedRealtimeMs] is a monotonic timestamp (Location.elapsedRealtimeNanos / 1e6 on Android)
 * and is used for every time delta, so wall-clock changes never corrupt speed or duration.
 */
data class LocationFix(
    val latitude: Double,
    val longitude: Double,
    val accuracyM: Float,
    val speedMps: Float?,
    val speedAccuracyMps: Float?,
    val altitudeM: Double?,
    val elapsedRealtimeMs: Long,
    val wallTimeMs: Long,
) {
    val point: GeoPoint get() = GeoPoint(latitude, longitude)
}
