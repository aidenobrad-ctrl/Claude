package com.ridedeck.core.format

import com.ridedeck.core.model.SpeedUnit
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class PaceAndUnitsTest {

    @Test
    fun `5 km per hour is a 12 minute kilometre`() {
        val mps = Units.toMps(5.0, SpeedUnit.KMH)
        assertEquals(720L, Pace.secondsPerUnit(mps, SpeedUnit.KMH))
        assertEquals("12:00", Pace.format(Pace.secondsPerUnit(mps, SpeedUnit.KMH)))
    }

    @Test
    fun `pace per mile uses statute mile`() {
        // 6 mph = 10:00 per mile.
        val mps = Units.toMps(6.0, SpeedUnit.MPH)
        assertEquals(600L, Pace.secondsPerUnit(mps, SpeedUnit.MPH))
    }

    @Test
    fun `stationary user has no pace`() {
        assertNull(Pace.secondsPerUnit(0.0, SpeedUnit.KMH))
        assertNull(Pace.secondsPerUnit(0.1, SpeedUnit.KMH))
        assertEquals("--:--", Pace.format(null))
    }

    @Test
    fun `absurdly slow pace is suppressed`() {
        // 0.35 m/s is about 47:37 per km (shown) but about 76 min per mile (hidden).
        assertEquals(2857L, Pace.secondsPerUnit(0.35, SpeedUnit.KMH))
        assertNull(Pace.secondsPerUnit(0.35, SpeedUnit.MPH))
    }

    @Test
    fun `average pace from distance and moving time`() {
        // 5 km in 25 minutes = 5:00 /km.
        assertEquals(300L, Pace.averageSecondsPerUnit(5_000.0, 25 * 60_000L, SpeedUnit.KMH))
        assertNull(Pace.averageSecondsPerUnit(0.0, 1000, SpeedUnit.KMH))
    }

    @Test
    fun `speed conversions`() {
        assertEquals(36.0, Units.speed(10.0, SpeedUnit.KMH), 1e-9)
        assertEquals(22.369, Units.speed(10.0, SpeedUnit.MPH), 1e-3)
        assertEquals(10.0, Units.toMps(Units.speed(10.0, SpeedUnit.MPH), SpeedUnit.MPH), 1e-9)
        assertEquals(1.0, Units.distance(1609.344, SpeedUnit.MPH), 1e-9)
    }

    @Test
    fun `durations format with and without hours`() {
        assertEquals("05:07", Formatters.duration(307_000))
        assertEquals("1:02:03", Formatters.duration(3_723_000))
        assertEquals("00:00", Formatters.duration(-5))
    }
}
