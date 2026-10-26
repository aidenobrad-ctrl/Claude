package com.ridedeck.core.tracking

import com.ridedeck.core.mode.ModeProfile
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import kotlin.math.abs

class SpeedSmootherTest {

    @Test
    fun `first sample passes through`() {
        val s = SpeedSmoother(ModeProfile.BIKE)
        assertEquals(6.0, s.update(6.0, 0), 1e-9)
    }

    @Test
    fun `jittery constant speed is smoothed toward the true value`() {
        val s = SpeedSmoother(ModeProfile.BIKE)
        val noise = listOf(0.8, -0.7, 0.6, -0.9, 0.5, -0.4, 0.9, -0.8, 0.3, -0.6)
        var out = s.update(6.0, 0)
        var maxRawDeviation = 0.0
        var maxSmoothedDeviation = 0.0
        for (i in 1..40) {
            val raw = 6.0 + noise[i % noise.size]
            out = s.update(raw, i * 1000L)
            if (i > 5) {
                maxRawDeviation = maxOf(maxRawDeviation, abs(raw - 6.0))
                maxSmoothedDeviation = maxOf(maxSmoothedDeviation, abs(out - 6.0))
            }
        }
        assertTrue("smoothed deviation $maxSmoothedDeviation < raw $maxRawDeviation", maxSmoothedDeviation < maxRawDeviation / 2)
    }

    @Test
    fun `single spike is clamped by max acceleration`() {
        val s = SpeedSmoother(ModeProfile.BIKE)
        s.update(5.0, 0)
        val afterSpike = s.update(30.0, 1000)
        // Bike max acceleration is 4 m/s^2, so the clamped input is at most 9 m/s and the
        // smoothed output is below that.
        assertTrue(afterSpike < 9.0)
    }

    @Test
    fun `converges to a real speed change within a few seconds`() {
        val s = SpeedSmoother(ModeProfile.BIKE)
        s.update(0.0, 0)
        var out = 0.0
        for (i in 1..10) out = s.update(8.0, i * 1000L)
        assertEquals(8.0, out, 0.5)
    }

    @Test
    fun `values under the noise floor read as zero`() {
        val s = SpeedSmoother(ModeProfile.WALK)
        assertEquals(0.0, s.update(0.2, 0), 0.0)
    }

    @Test
    fun `out of order timestamps are ignored`() {
        val s = SpeedSmoother(ModeProfile.BIKE)
        s.update(5.0, 2000)
        assertEquals(5.0, s.update(20.0, 1000), 1e-9)
    }

    @Test
    fun `walk profile caps implausible speeds`() {
        val s = SpeedSmoother(ModeProfile.WALK)
        assertEquals(ModeProfile.WALK.maxPlausibleSpeedMps, s.update(50.0, 0), 1e-9)
    }
}
