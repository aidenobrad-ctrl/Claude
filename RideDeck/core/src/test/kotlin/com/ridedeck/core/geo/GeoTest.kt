package com.ridedeck.core.geo

import com.ridedeck.core.model.GeoPoint
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class GeoTest {

    @Test
    fun `haversine matches known city distance`() {
        // London (Charing Cross) to Paris (Notre-Dame) is about 343.5 km.
        val d = Geo.distanceMeters(51.5074, -0.1278, 48.8530, 2.3499)
        assertEquals(343_500.0, d, 1_500.0)
    }

    @Test
    fun `one degree of latitude is about 111 km`() {
        assertEquals(111_195.0, Geo.distanceMeters(0.0, 0.0, 1.0, 0.0), 50.0)
    }

    @Test
    fun `distance between identical points is zero`() {
        assertEquals(0.0, Geo.distanceMeters(40.0, -74.0, 40.0, -74.0), 0.0)
    }

    @Test
    fun `polyline length sums its segments`() {
        val pts = listOf(GeoPoint(0.0, 0.0), GeoPoint(0.0, 0.01), GeoPoint(0.0, 0.02))
        assertEquals(Geo.distanceMeters(0.0, 0.0, 0.0, 0.02), Geo.polylineLengthMeters(pts), 0.01)
    }

    @Test
    fun `projection reports perpendicular distance and along-track position`() {
        val line = listOf(GeoPoint(0.0, 0.0), GeoPoint(0.0, 0.01))
        val p = Geo.projectOntoPolyline(GeoPoint(0.0002, 0.005), line)!!
        assertEquals(22.2, p.distanceM, 0.5)
        assertEquals(Geo.distanceMeters(0.0, 0.0, 0.0, 0.005), p.alongM, 1.0)
        assertEquals(0, p.segmentIndex)
    }

    @Test
    fun `polyline codec round trips Google's reference example`() {
        val encoded = "_p~iF~ps|U_ulLnnqC_mqNvxq`@"
        val decoded = PolylineCodec.decode(encoded)
        assertEquals(3, decoded.size)
        assertEquals(38.5, decoded[0].latitude, 1e-5)
        assertEquals(-120.2, decoded[0].longitude, 1e-5)
        assertEquals(43.252, decoded[2].latitude, 1e-5)
        assertEquals(-126.453, decoded[2].longitude, 1e-5)
        assertEquals(encoded, PolylineCodec.encode(decoded))
    }

    @Test
    fun `bearing due east is 90 degrees`() {
        val b = Geo.bearingDegrees(GeoPoint(0.0, 0.0), GeoPoint(0.0, 1.0))
        assertTrue(b in 89.9..90.1)
    }
}
