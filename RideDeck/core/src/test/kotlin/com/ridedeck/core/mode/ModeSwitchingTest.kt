package com.ridedeck.core.mode

import com.ridedeck.core.TestFixes
import com.ridedeck.core.model.ActivityMode
import com.ridedeck.core.routing.ReroutePolicy
import com.ridedeck.core.routing.RerouteReason
import com.ridedeck.core.tracking.SpeedAlertMonitor
import com.ridedeck.core.tracking.TripAccumulator
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class ModeSwitchingTest {

    @Test
    fun `distance and max speed are attributed to the mode active at the time`() {
        val trip = TripAccumulator(ActivityMode.WALK, autoPauseEnabled = false)
        trip.start(0)
        TestFixes.eastward(1.5, 60).forEach(trip::onFix) // 90 m walking
        trip.switchMode(ActivityMode.BIKE, 60_000)
        TestFixes.eastward(6.0, 60, startMs = 61_000, startOffsetM = 91.5).forEach(trip::onFix)

        val snap = trip.snapshot(121_000)
        assertEquals(ActivityMode.BIKE, snap.mode)
        assertEquals(90.0, snap.distanceByMode[ActivityMode.WALK]!!, 3.0)
        assertEquals(360.0, snap.distanceByMode[ActivityMode.BIKE]!!, 8.0)
        assertEquals(snap.distanceM, snap.distanceByMode.values.sum(), 1e-6)
        assertEquals(1.5, snap.maxSpeedByMode[ActivityMode.WALK]!!, 0.1)
        assertEquals(6.0, snap.maxSpeedByMode[ActivityMode.BIKE]!!, 0.1)
        assertEquals(ActivityMode.BIKE, snap.dominantMode)
    }

    @Test
    fun `dominant mode falls back to current mode when nothing was covered`() {
        val trip = TripAccumulator(ActivityMode.BIKE)
        trip.start(0)
        trip.switchMode(ActivityMode.WALK, 10)
        assertEquals(ActivityMode.WALK, trip.dominantMode())
    }

    @Test
    fun `switching to the same mode is a no-op`() {
        val trip = TripAccumulator(ActivityMode.BIKE)
        trip.start(0)
        trip.switchMode(ActivityMode.BIKE, 10)
        assertEquals(ActivityMode.BIKE, trip.mode)
    }

    @Test
    fun `walk mode applies walk plausibility limits after a switch`() {
        val trip = TripAccumulator(ActivityMode.BIKE, autoPauseEnabled = false)
        trip.start(0)
        trip.switchMode(ActivityMode.WALK, 0)
        TestFixes.eastward(12.0, 10).forEach(trip::onFix) // 43 km/h is not walking
        assertTrue(trip.snapshot(10_000).currentSpeedMps <= ModeProfile.WALK.maxPlausibleSpeedMps)
    }

    @Test
    fun `mode profiles drive the readouts and routing`() {
        assertFalse(ModeProfile.of(ActivityMode.BIKE).showPaceAsPrimary)
        assertTrue(ModeProfile.of(ActivityMode.WALK).showPaceAsPrimary)
        assertTrue(ModeProfile.of(ActivityMode.BIKE).osrmBaseUrl.endsWith("routed-bike"))
        assertTrue(ModeProfile.of(ActivityMode.WALK).osrmBaseUrl.endsWith("routed-foot"))
    }

    @Test
    fun `bike share hides in walk mode unless the user opts in`() {
        assertTrue(MapLayerPolicy.showBikeShare(ActivityMode.BIKE, layerEnabled = true, showInWalkMode = false))
        assertFalse(MapLayerPolicy.showBikeShare(ActivityMode.WALK, layerEnabled = true, showInWalkMode = false))
        assertTrue(MapLayerPolicy.showBikeShare(ActivityMode.WALK, layerEnabled = true, showInWalkMode = true))
        assertFalse(MapLayerPolicy.showBikeShare(ActivityMode.BIKE, layerEnabled = false, showInWalkMode = true))
        assertTrue(MapLayerPolicy.showBicyclingLayer(ActivityMode.BIKE))
        assertFalse(MapLayerPolicy.showBicyclingLayer(ActivityMode.WALK))
    }

    @Test
    fun `speed alert only fires in bike mode`() {
        val monitor = SpeedAlertMonitor()
        assertFalse(monitor.evaluate(ActivityMode.WALK, enabled = true, limitMps = 2.0, speedMps = 5.0, nowMs = 0))
        assertTrue(monitor.evaluate(ActivityMode.BIKE, enabled = true, limitMps = 2.0, speedMps = 5.0, nowMs = 0))
    }

    @Test
    fun `speed alert fires on crossing, repeats while over, re-arms below hysteresis`() {
        val m = SpeedAlertMonitor(repeatMs = 30_000, hysteresisMps = 0.5)
        val limit = 10.0
        assertTrue(m.evaluate(ActivityMode.BIKE, true, limit, 11.0, 0))
        assertFalse(m.evaluate(ActivityMode.BIKE, true, limit, 11.0, 5_000))
        assertTrue(m.evaluate(ActivityMode.BIKE, true, limit, 11.0, 30_000))
        // Dipping just under the limit does not re-arm.
        assertFalse(m.evaluate(ActivityMode.BIKE, true, limit, 9.8, 31_000))
        assertFalse(m.evaluate(ActivityMode.BIKE, true, limit, 10.2, 32_000))
        // Clearly below re-arms; next crossing alerts immediately.
        assertFalse(m.evaluate(ActivityMode.BIKE, true, limit, 9.0, 33_000))
        assertTrue(m.evaluate(ActivityMode.BIKE, true, limit, 10.5, 34_000))
    }

    @Test
    fun `switching mode with an active route forces a reroute regardless of cooldown`() {
        val policy = ReroutePolicy(offRouteCooldownMs = 20_000)
        assertNull(policy.evaluate(ActivityMode.BIKE, offRoute = true, nowMs = 0)) // no route yet
        policy.onRouteComputed(ActivityMode.BIKE, nowMs = 0)
        assertNull(policy.evaluate(ActivityMode.BIKE, offRoute = false, nowMs = 1_000))
        assertEquals(RerouteReason.MODE_CHANGED, policy.evaluate(ActivityMode.WALK, offRoute = false, nowMs = 1_000))
    }

    @Test
    fun `off-route reroutes respect the cooldown`() {
        val policy = ReroutePolicy(offRouteCooldownMs = 20_000)
        policy.onRouteComputed(ActivityMode.WALK, nowMs = 0)
        assertNull(policy.evaluate(ActivityMode.WALK, offRoute = true, nowMs = 5_000))
        assertEquals(RerouteReason.OFF_ROUTE, policy.evaluate(ActivityMode.WALK, offRoute = true, nowMs = 20_000))
        policy.clear()
        assertNull(policy.evaluate(ActivityMode.BIKE, offRoute = true, nowMs = 60_000))
    }

    @Test
    fun `ride mode locks only while moving in an active session`() {
        assertTrue(RideModePolicy.isLocked(true, true, ActivityMode.BIKE, 5.0))
        assertFalse(RideModePolicy.isLocked(true, true, ActivityMode.BIKE, 0.5))
        assertFalse(RideModePolicy.isLocked(true, false, ActivityMode.BIKE, 5.0))
        assertFalse(RideModePolicy.isLocked(false, true, ActivityMode.BIKE, 5.0))
    }
}
