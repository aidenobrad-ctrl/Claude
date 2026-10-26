package com.ridedeck.app.data.local

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.Query
import androidx.room.Update
import com.ridedeck.core.model.ActivityMode
import kotlinx.coroutines.flow.Flow

@Dao
interface SessionDao {

    @Insert
    suspend fun insertSession(session: SessionEntity): Long

    @Update
    suspend fun updateSession(session: SessionEntity)

    @Insert
    suspend fun insertPoints(points: List<RoutePointEntity>)

    @Query("SELECT * FROM sessions WHERE id = :id")
    suspend fun getSession(id: Long): SessionEntity?

    @Query("SELECT * FROM sessions WHERE id = :id")
    fun observeSession(id: Long): Flow<SessionEntity?>

    @Query("SELECT * FROM sessions WHERE endedAtMs IS NOT NULL ORDER BY startedAtMs DESC")
    fun observeCompleted(): Flow<List<SessionEntity>>

    @Query("SELECT * FROM sessions WHERE endedAtMs IS NOT NULL AND mode = :mode ORDER BY startedAtMs DESC")
    fun observeCompletedByMode(mode: ActivityMode): Flow<List<SessionEntity>>

    @Query("SELECT * FROM route_points WHERE sessionId = :sessionId ORDER BY timeMs ASC, id ASC")
    suspend fun getPoints(sessionId: Long): List<RoutePointEntity>

    @Query("SELECT MAX(timeMs) FROM route_points WHERE sessionId = :sessionId")
    suspend fun lastPointTime(sessionId: Long): Long?

    @Query("SELECT * FROM sessions WHERE endedAtMs IS NULL")
    suspend fun getOpenSessions(): List<SessionEntity>

    @Query("DELETE FROM sessions WHERE id = :id")
    suspend fun deleteSession(id: Long)

    @Query(
        """
        SELECT
            COALESCE(SUM(bikeDistanceM), 0) AS bikeDistanceM,
            COALESCE(SUM(walkDistanceM), 0) AS walkDistanceM,
            COALESCE(MAX(bikeMaxSpeedMps), 0) AS bikeMaxSpeedMps,
            COALESCE(MAX(walkMaxSpeedMps), 0) AS walkMaxSpeedMps,
            COALESCE(SUM(CASE WHEN mode = 'BIKE' THEN 1 ELSE 0 END), 0) AS bikeSessions,
            COALESCE(SUM(CASE WHEN mode = 'WALK' THEN 1 ELSE 0 END), 0) AS walkSessions
        FROM sessions
        WHERE endedAtMs IS NOT NULL
        """,
    )
    fun observeTotals(): Flow<LifetimeTotals>
}
