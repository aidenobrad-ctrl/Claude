package com.ridedeck.app.data.local

import android.content.Context
import androidx.room.Room
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.ridedeck.core.model.ActivityMode
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.test.runTest
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class SessionDaoTest {

    private lateinit var db: RideDeckDatabase
    private lateinit var dao: SessionDao

    @Before
    fun setUp() {
        val context = ApplicationProvider.getApplicationContext<Context>()
        db = Room.inMemoryDatabaseBuilder(context, RideDeckDatabase::class.java)
            .allowMainThreadQueries()
            .build()
        dao = db.sessionDao()
    }

    @After
    fun tearDown() {
        db.close()
    }

    private fun finished(
        mode: ActivityMode,
        startedAt: Long,
        bikeM: Double = 0.0,
        walkM: Double = 0.0,
        bikeMax: Double = 0.0,
        walkMax: Double = 0.0,
    ) = SessionEntity(
        mode = mode,
        startedAtMs = startedAt,
        endedAtMs = startedAt + 60_000,
        distanceM = bikeM + walkM,
        bikeDistanceM = bikeM,
        walkDistanceM = walkM,
        movingTimeMs = 60_000,
        elapsedTimeMs = 60_000,
        maxSpeedMps = maxOf(bikeMax, walkMax),
        bikeMaxSpeedMps = bikeMax,
        walkMaxSpeedMps = walkMax,
    )

    @Test
    fun insertAndReadBackSessionWithPoints() = runTest {
        val id = dao.insertSession(finished(ActivityMode.BIKE, 1_000, bikeM = 5_000.0, bikeMax = 10.0))
        dao.insertPoints(
            listOf(
                RoutePointEntity(sessionId = id, segment = 0, latitude = 1.0, longitude = 2.0, timeMs = 2, speedMps = 5f, accuracyM = 4f, altitudeM = null),
                RoutePointEntity(sessionId = id, segment = 0, latitude = 1.1, longitude = 2.1, timeMs = 1, speedMps = 6f, accuracyM = 4f, altitudeM = 12.0),
            ),
        )
        val session = dao.getSession(id)!!
        assertEquals(5_000.0, session.distanceM, 0.0)
        val points = dao.getPoints(id)
        assertEquals(listOf(1L, 2L), points.map { it.timeMs }) // ordered by time
        assertEquals(2L, dao.lastPointTime(id))
    }

    @Test
    fun completedSessionsAreFilteredByModeAndNewestFirst() = runTest {
        dao.insertSession(finished(ActivityMode.BIKE, 1_000, bikeM = 100.0))
        dao.insertSession(finished(ActivityMode.WALK, 2_000, walkM = 50.0))
        dao.insertSession(finished(ActivityMode.BIKE, 3_000, bikeM = 200.0))
        dao.insertSession(SessionEntity(mode = ActivityMode.BIKE, startedAtMs = 4_000)) // still recording

        val all = dao.observeCompleted().first()
        assertEquals(listOf(3_000L, 2_000L, 1_000L), all.map { it.startedAtMs })

        val bikes = dao.observeCompletedByMode(ActivityMode.BIKE).first()
        assertEquals(listOf(3_000L, 1_000L), bikes.map { it.startedAtMs })

        val walks = dao.observeCompletedByMode(ActivityMode.WALK).first()
        assertEquals(1, walks.size)

        assertEquals(1, dao.getOpenSessions().size)
    }

    @Test
    fun totalsKeepSeparateOdometersAndRecordsPerMode() = runTest {
        dao.insertSession(finished(ActivityMode.BIKE, 1_000, bikeM = 10_000.0, walkM = 300.0, bikeMax = 12.0, walkMax = 1.8))
        dao.insertSession(finished(ActivityMode.WALK, 2_000, walkM = 4_000.0, walkMax = 2.2))
        dao.insertSession(finished(ActivityMode.BIKE, 3_000, bikeM = 5_000.0, bikeMax = 14.5))
        dao.insertSession(SessionEntity(mode = ActivityMode.BIKE, startedAtMs = 4_000, bikeDistanceM = 999.0)) // open: excluded

        val totals = dao.observeTotals().first()
        assertEquals(15_000.0, totals.bikeDistanceM, 0.0)
        assertEquals(4_300.0, totals.walkDistanceM, 0.0)
        assertEquals(14.5, totals.bikeMaxSpeedMps, 0.0)
        assertEquals(2.2, totals.walkMaxSpeedMps, 0.0)
        assertEquals(2, totals.bikeSessions)
        assertEquals(1, totals.walkSessions)
    }

    @Test
    fun totalsAreZeroForAnEmptyDatabase() = runTest {
        assertEquals(LifetimeTotals.EMPTY, dao.observeTotals().first())
    }

    @Test
    fun deletingASessionCascadesToItsPoints() = runTest {
        val id = dao.insertSession(finished(ActivityMode.WALK, 1_000, walkM = 10.0))
        dao.insertPoints(listOf(RoutePointEntity(sessionId = id, segment = 0, latitude = 0.0, longitude = 0.0, timeMs = 0, speedMps = 0f, accuracyM = 3f, altitudeM = null)))
        dao.deleteSession(id)
        assertNull(dao.getSession(id))
        assertTrue(dao.getPoints(id).isEmpty())
    }

    @Test
    fun updateFinalisesAnOpenSession() = runTest {
        val id = dao.insertSession(SessionEntity(mode = ActivityMode.BIKE, startedAtMs = 1_000))
        val open = dao.getSession(id)!!
        dao.updateSession(open.copy(endedAtMs = 9_000, distanceM = 1_234.0, bikeDistanceM = 1_234.0))
        assertTrue(dao.getOpenSessions().isEmpty())
        assertEquals(1_234.0, dao.observeTotals().first().bikeDistanceM, 0.0)
    }
}
