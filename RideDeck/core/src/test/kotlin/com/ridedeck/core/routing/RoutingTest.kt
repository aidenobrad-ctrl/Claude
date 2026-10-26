package com.ridedeck.core.routing

import com.ridedeck.core.geo.PolylineCodec
import com.ridedeck.core.model.ActivityMode
import com.ridedeck.core.model.GeoPoint
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class RoutingTest {

    private val a = GeoPoint(0.0, 0.0)
    private val b = GeoPoint(0.0, 0.01)
    private val c = GeoPoint(0.01, 0.01)

    private fun sampleResponse(): String {
        val full = PolylineCodec.encode(listOf(a, b, c))
        val s1 = PolylineCodec.encode(listOf(a, b))
        val s2 = PolylineCodec.encode(listOf(b, c))
        return """
        {"code":"Ok","routes":[{"distance":2224.3,"duration":480.4,"geometry":"$full",
          "legs":[{"steps":[
            {"distance":1112.1,"duration":240.2,"geometry":"$s1","name":"Main Street",
             "maneuver":{"type":"depart","location":[0,0]}},
            {"distance":1112.2,"duration":240.2,"geometry":"$s2","name":"Oak Road",
             "maneuver":{"type":"turn","modifier":"left","location":[0.01,0]}}
          ]}]}]}
        """.trimIndent()
    }

    @Test
    fun `parses route, steps and durations`() {
        val route = OsrmApi.parseResponse(sampleResponse(), ActivityMode.BIKE)!!
        assertEquals(2224, route.distanceM)
        assertEquals(480L, route.durationSec)
        assertEquals(3, route.polyline.size)
        assertEquals(2, route.steps.size)
        assertEquals("Head off on Main Street", route.steps[0].instruction)
        assertEquals("DEPART", route.steps[0].maneuver)
        assertEquals("Turn left onto Oak Road", route.steps[1].instruction)
        assertEquals("TURN_LEFT", route.steps[1].maneuver)
    }

    @Test
    fun `no route when osrm reports an error code`() {
        assertNull(OsrmApi.parseResponse("{\"code\":\"NoRoute\",\"message\":\"Impossible route\"}", ActivityMode.WALK))
        assertEquals("Impossible route", OsrmApi.parseError("{\"code\":\"NoRoute\",\"message\":\"Impossible route\"}"))
    }

    @Test
    fun `route url uses the server for the activity and lon,lat order`() {
        val bike = OsrmApi.routeUrl(GeoPoint(51.5, -0.1), GeoPoint(51.6, -0.2), ActivityMode.BIKE)
        val walk = OsrmApi.routeUrl(GeoPoint(51.5, -0.1), GeoPoint(51.6, -0.2), ActivityMode.WALK)
        assertTrue(bike.startsWith("https://routing.openstreetmap.de/routed-bike/route/v1/driving/-0.100000,51.500000;-0.200000,51.600000"))
        assertTrue(walk.contains("/routed-foot/"))
        assertTrue(bike.contains("steps=true"))
    }

    @Test
    fun `maneuver mapping covers roundabouts, forks and plain turns`() {
        assertEquals("ROUNDABOUT_RIGHT", OsrmApi.maneuverCode("roundabout", "right"))
        assertEquals("FORK_LEFT", OsrmApi.maneuverCode("fork", "slight left"))
        assertEquals("TURN_SHARP_RIGHT", OsrmApi.maneuverCode("turn", "sharp right"))
        assertEquals("STRAIGHT", OsrmApi.maneuverCode("new name", null))
        assertEquals("At the roundabout, take exit 2 onto A1", OsrmApi.instruction("roundabout", "right", "A1", "2"))
    }

    @Test
    fun `progress tracks the current step and remaining distance`() {
        val route = OsrmApi.parseResponse(sampleResponse(), ActivityMode.BIKE)!!
        val p = RouteProgressCalculator.progress(route, GeoPoint(0.0, 0.005))!!
        assertEquals(0, p.currentStepIndex)
        assertEquals(1668.0, p.remainingDistanceM, 15.0)
        assertEquals(556.0, p.distanceToManeuverM, 10.0)
        assertFalse(p.arrived)

        val later = RouteProgressCalculator.progress(route, GeoPoint(0.005, 0.01))!!
        assertEquals(1, later.currentStepIndex)

        val end = RouteProgressCalculator.progress(route, c)!!
        assertTrue(end.arrived)
    }

    @Test
    fun `off-route needs consecutive bad fixes`() {
        val d = OffRouteDetector(requiredConsecutive = 3)
        assertFalse(d.update(100.0, 5f, 40.0))
        assertFalse(d.update(100.0, 5f, 40.0))
        assertFalse(d.update(10.0, 5f, 40.0)) // back on route resets the count
        assertFalse(d.update(100.0, 5f, 40.0))
        assertFalse(d.update(100.0, 5f, 40.0))
        assertTrue(d.update(100.0, 5f, 40.0))
    }

    @Test
    fun `poor accuracy widens the off-route threshold`() {
        val d = OffRouteDetector(requiredConsecutive = 1)
        assertFalse(d.update(60.0, 25f, 40.0))
        assertTrue(d.update(60.0, 5f, 40.0))
    }
}
