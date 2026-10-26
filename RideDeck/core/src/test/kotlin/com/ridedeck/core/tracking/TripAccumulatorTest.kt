package com.ridedeck.core.tracking

import com.ridedeck.core.TestFixes
import com.ridedeck.core.model.ActivityMode
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class TripAccumulatorTest {

    @Test
    fun `constant ride accumulates distance and time`() {
        val trip = TripAccumulator(ActivityMode.BIKE)
        trip.start(0)
        TestFixes.eastward(speedMps = 5.0, seconds = 60).forEach(trip::onFix)
        val snap = trip.snapshot(60_000)
        assertEquals(300.0, snap.distanceM, 3.0)
        assertEquals(60_000, snap.movingTimeMs)
        assertEquals(5.0, snap.averageSpeedMps, 0.1)
        assertEquals(5.0, snap.maxSpeedMps, 0.1)
        assertEquals(TripStatus.RUNNING, snap.status)
    }

    @Test
    fun `distance is computed from positions when the fix has no speed`() {
        val trip = TripAccumulator(ActivityMode.WALK)
        trip.start(0)
        TestFixes.eastward(speedMps = 1.4, seconds = 100, reportSpeed = false).forEach(trip::onFix)
        val snap = trip.snapshot(100_000)
        assertEquals(140.0, snap.distanceM, 3.0)
        assertEquals(1.4, snap.currentSpeedMps, 0.1)
    }

    @Test
    fun `standing still with GPS jitter adds no distance`() {
        val trip = TripAccumulator(ActivityMode.WALK, autoPauseEnabled = false)
        trip.start(0)
        val jitter = listOf(0.0, 1.5, -1.0, 0.8, -1.2, 1.1, 0.0, -0.7)
        for (i in 0 until 120) {
            trip.onFix(TestFixes.fix(jitter[i % jitter.size], i * 1000L, speedMps = 0f, accuracy = 8f, offsetNorthM = jitter[(i + 3) % jitter.size]))
        }
        assertEquals(0.0, trip.snapshot(120_000).distanceM, 0.0)
    }

    @Test
    fun `inaccurate fixes are rejected`() {
        val trip = TripAccumulator(ActivityMode.BIKE)
        trip.start(0)
        val result = trip.onFix(TestFixes.fix(0.0, 0, accuracy = 80f))
        assertFalse(result.accepted)
    }

    @Test
    fun `teleporting glitch is rejected`() {
        val trip = TripAccumulator(ActivityMode.BIKE)
        trip.start(0)
        trip.onFix(TestFixes.fix(0.0, 0, 5f))
        trip.onFix(TestFixes.fix(5.0, 1000, 5f))
        val glitch = trip.onFix(TestFixes.fix(2_000.0, 2000, 5f))
        assertFalse(glitch.accepted)
        assertEquals(5.0, trip.snapshot(2000).distanceM, 0.5)
    }

    @Test
    fun `manual pause stops the clocks and ignores fixes`() {
        val trip = TripAccumulator(ActivityMode.BIKE)
        trip.start(0)
        TestFixes.eastward(5.0, 10).forEach(trip::onFix)
        trip.pause(10_000)
        TestFixes.eastward(5.0, 10, startMs = 10_000, startOffsetM = 50.0).forEach(trip::onFix)
        trip.resume(20_000)
        val snap = trip.snapshot(20_000)
        assertEquals(50.0, snap.distanceM, 1.0)
        assertEquals(10_000, snap.movingTimeMs)
        assertEquals(10_000, snap.elapsedTimeMs)
        assertEquals(1, snap.segment)
    }

    @Test
    fun `auto-pause kicks in when stationary and resumes when moving`() {
        val trip = TripAccumulator(ActivityMode.BIKE)
        trip.start(0)
        TestFixes.eastward(6.0, 20).forEach(trip::onFix) // moving 0..20s, ends at 120 m
        // Stop for 20 s.
        for (s in 21..40) trip.onFix(TestFixes.fix(120.0, s * 1000L, speedMps = 0f))
        assertEquals(TripStatus.AUTO_PAUSED, trip.status)
        // Moving time stops roughly when the speed fell below the threshold, not 6 s later.
        val paused = trip.snapshot(40_000)
        assertTrue("moving ${paused.movingTimeMs}", paused.movingTimeMs in 20_000..25_000)
        assertEquals(40_000, paused.elapsedTimeMs)
        // Ride off again.
        TestFixes.eastward(6.0, 10, startMs = 41_000, startOffsetM = 120.0).forEach(trip::onFix)
        assertEquals(TripStatus.RUNNING, trip.status)
    }

    @Test
    fun `auto-pause disabled keeps the trip running`() {
        val trip = TripAccumulator(ActivityMode.BIKE, autoPauseEnabled = false)
        trip.start(0)
        for (s in 0..30) trip.onFix(TestFixes.fix(0.0, s * 1000L, speedMps = 0f))
        assertEquals(TripStatus.RUNNING, trip.status)
    }

    @Test
    fun `activity recognition still event pauses immediately when slow`() {
        val trip = TripAccumulator(ActivityMode.WALK)
        trip.start(0)
        trip.onFix(TestFixes.fix(0.0, 0, speedMps = 0f))
        trip.onStillDetected(1000)
        assertEquals(TripStatus.AUTO_PAUSED, trip.status)
    }

    @Test
    fun `stale GPS drops the current speed to zero`() {
        val trip = TripAccumulator(ActivityMode.BIKE)
        trip.start(0)
        TestFixes.eastward(6.0, 5).forEach(trip::onFix)
        trip.tick(15_000)
        assertEquals(0.0, trip.snapshot(15_000).currentSpeedMps, 0.0)
    }

    @Test
    fun `long signal gap starts a new segment without bridging distance`() {
        val trip = TripAccumulator(ActivityMode.BIKE, autoPauseEnabled = false)
        trip.start(0)
        TestFixes.eastward(5.0, 10).forEach(trip::onFix)
        val after = trip.onFix(TestFixes.fix(500.0, 70_000, speedMps = 5f))
        assertTrue(after.accepted)
        assertEquals(1, after.segment)
        assertEquals(50.0, trip.snapshot(70_000).distanceM, 1.0)
    }

    @Test
    fun `reset max clears the session max but keeps per-mode records`() {
        val trip = TripAccumulator(ActivityMode.BIKE)
        trip.start(0)
        TestFixes.eastward(8.0, 20).forEach(trip::onFix)
        trip.resetMaxSpeed()
        val snap = trip.snapshot(20_000)
        assertEquals(0.0, snap.maxSpeedMps, 0.0)
        assertEquals(8.0, snap.maxSpeedByMode[ActivityMode.BIKE]!!, 0.2)
    }

    @Test
    fun `restore continues totals in a new segment`() {
        val trip = TripAccumulator(ActivityMode.BIKE, autoPauseEnabled = false)
        trip.restore(
            nowMs = 1_000,
            distanceByMode = mapOf(ActivityMode.BIKE to 1_000.0, ActivityMode.WALK to 200.0),
            maxSpeedByMode = mapOf(ActivityMode.BIKE to 9.0),
            maxSpeedMps = 9.0,
            movingTimeMs = 300_000,
            elapsedTimeMs = 320_000,
            lastSegment = 2,
        )
        TestFixes.eastward(5.0, 10, startMs = 1_000).forEach(trip::onFix)
        val snap = trip.snapshot(11_000)
        assertEquals(1_250.0, snap.distanceM, 1.0)
        assertEquals(310_000, snap.movingTimeMs)
        assertEquals(3, snap.segment)
        assertEquals(9.0, snap.maxSpeedMps, 0.0)
    }

    @Test(expected = IllegalStateException::class)
    fun `starting twice is an error`() {
        val trip = TripAccumulator(ActivityMode.BIKE)
        trip.start(0)
        trip.start(1)
    }
}
