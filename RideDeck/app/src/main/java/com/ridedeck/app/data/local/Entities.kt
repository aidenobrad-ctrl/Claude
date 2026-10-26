package com.ridedeck.app.data.local

import androidx.room.Entity
import androidx.room.ForeignKey
import androidx.room.Index
import androidx.room.PrimaryKey
import com.ridedeck.core.model.ActivityMode

/**
 * One recorded ride or walk. [mode] is the dominant mode (the one that covered most distance);
 * per-mode distance and max speed are kept separately so mid-session switches stay accurate.
 * [endedAtMs] is null while the session is being recorded.
 */
@Entity(
    tableName = "sessions",
    indices = [Index("mode"), Index("startedAtMs")],
)
data class SessionEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val mode: ActivityMode,
    val startedAtMs: Long,
    val endedAtMs: Long? = null,
    val distanceM: Double = 0.0,
    val bikeDistanceM: Double = 0.0,
    val walkDistanceM: Double = 0.0,
    val movingTimeMs: Long = 0,
    val elapsedTimeMs: Long = 0,
    val maxSpeedMps: Double = 0.0,
    val bikeMaxSpeedMps: Double = 0.0,
    val walkMaxSpeedMps: Double = 0.0,
    val avgSpeedMps: Double = 0.0,
    val lastSegment: Int = 0,
)

@Entity(
    tableName = "route_points",
    foreignKeys = [
        ForeignKey(
            entity = SessionEntity::class,
            parentColumns = ["id"],
            childColumns = ["sessionId"],
            onDelete = ForeignKey.CASCADE,
        ),
    ],
    indices = [Index("sessionId")],
)
data class RoutePointEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val sessionId: Long,
    /** Route segment; a new segment starts after a pause or GPS gap so the map does not bridge it. */
    val segment: Int,
    val latitude: Double,
    val longitude: Double,
    val timeMs: Long,
    val speedMps: Float,
    val accuracyM: Float,
    val altitudeM: Double?,
)

/** Lifetime odometers and all-time max-speed records per mode. */
data class LifetimeTotals(
    val bikeDistanceM: Double,
    val walkDistanceM: Double,
    val bikeMaxSpeedMps: Double,
    val walkMaxSpeedMps: Double,
    val bikeSessions: Int,
    val walkSessions: Int,
) {
    fun distance(mode: ActivityMode) = if (mode == ActivityMode.BIKE) bikeDistanceM else walkDistanceM
    fun record(mode: ActivityMode) = if (mode == ActivityMode.BIKE) bikeMaxSpeedMps else walkMaxSpeedMps

    companion object {
        val EMPTY = LifetimeTotals(0.0, 0.0, 0.0, 0.0, 0, 0)
    }
}
