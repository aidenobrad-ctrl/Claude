package com.ridedeck.app.data.session

import com.ridedeck.app.data.local.LifetimeTotals
import com.ridedeck.app.data.local.RoutePointEntity
import com.ridedeck.app.data.local.SessionDao
import com.ridedeck.app.data.local.SessionEntity
import com.ridedeck.core.model.ActivityMode
import com.ridedeck.core.tracking.TripSnapshot
import kotlinx.coroutines.flow.Flow
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class SessionRepository @Inject constructor(
    private val dao: SessionDao,
) {
    fun observeSessions(filter: ActivityMode?): Flow<List<SessionEntity>> =
        if (filter == null) dao.observeCompleted() else dao.observeCompletedByMode(filter)

    fun observeSession(id: Long): Flow<SessionEntity?> = dao.observeSession(id)

    fun observeTotals(): Flow<LifetimeTotals> = dao.observeTotals()

    suspend fun getSession(id: Long): SessionEntity? = dao.getSession(id)

    suspend fun getPoints(id: Long): List<RoutePointEntity> = dao.getPoints(id)

    suspend fun createSession(mode: ActivityMode, startedAtMs: Long): Long =
        dao.insertSession(SessionEntity(mode = mode, startedAtMs = startedAtMs))

    suspend fun appendPoints(points: List<RoutePointEntity>) {
        if (points.isNotEmpty()) dao.insertPoints(points)
    }

    /** Writes running totals so a crash loses at most one flush interval. */
    suspend fun saveProgress(id: Long, snapshot: TripSnapshot, endedAtMs: Long? = null) {
        val existing = dao.getSession(id) ?: return
        dao.updateSession(existing.applySnapshot(snapshot, endedAtMs))
    }

    suspend fun deleteSession(id: Long) = dao.deleteSession(id)

    suspend fun getOpenSessions(): List<SessionEntity> = dao.getOpenSessions()

    /** Wall-clock time of the last recorded point, or the start time if none were recorded. */
    suspend fun lastActivityTime(session: SessionEntity): Long =
        dao.lastPointTime(session.id) ?: session.startedAtMs

    /**
     * Closes sessions left open by a process that died while tracking. Empty ones are removed;
     * the rest end at their last recorded point.
     */
    suspend fun finalizeOrphanedSessions(exceptId: Long?) {
        dao.getOpenSessions().filter { it.id != exceptId }.forEach { s ->
            if (s.distanceM <= 0.0) {
                dao.deleteSession(s.id)
            } else {
                val end = dao.lastPointTime(s.id) ?: (s.startedAtMs + s.elapsedTimeMs)
                dao.updateSession(s.copy(endedAtMs = end))
            }
        }
    }

    private fun SessionEntity.applySnapshot(snapshot: TripSnapshot, endedAtMs: Long?) = copy(
        mode = snapshot.dominantMode,
        endedAtMs = endedAtMs,
        distanceM = snapshot.distanceM,
        bikeDistanceM = snapshot.distanceByMode[ActivityMode.BIKE] ?: 0.0,
        walkDistanceM = snapshot.distanceByMode[ActivityMode.WALK] ?: 0.0,
        movingTimeMs = snapshot.movingTimeMs,
        elapsedTimeMs = snapshot.elapsedTimeMs,
        maxSpeedMps = snapshot.maxSpeedMps,
        bikeMaxSpeedMps = snapshot.maxSpeedByMode[ActivityMode.BIKE] ?: 0.0,
        walkMaxSpeedMps = snapshot.maxSpeedByMode[ActivityMode.WALK] ?: 0.0,
        avgSpeedMps = snapshot.averageSpeedMps,
        lastSegment = snapshot.segment,
    )
}
