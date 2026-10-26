package com.ridedeck.core.osm

import com.ridedeck.core.model.GeoPoint
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class OsmApisTest {

    @Test
    fun `photon results become suggestions with names, details and coordinates`() {
        val body = """{"type":"FeatureCollection","features":[
            {"geometry":{"type":"Point","coordinates":[13.3777,52.5163]},
             "properties":{"osm_type":"W","osm_id":518071791,"name":"Brandenburger Tor","city":"Berlin","country":"Deutschland"}},
            {"geometry":{"type":"Point","coordinates":[2.0,48.0]},
             "properties":{"street":"Rue X","housenumber":"4","city":"Paris"}},
            {"geometry":{"type":"Point","coordinates":[]},"properties":{"name":"broken"}}]}"""
        val results = PhotonApi.parse(body)
        assertEquals(2, results.size)
        assertEquals("Brandenburger Tor", results[0].name)
        assertEquals("Berlin, Deutschland", results[0].detail)
        assertEquals(52.5163, results[0].location.latitude, 1e-9)
        assertEquals("Rue X 4", results[1].name)
        assertEquals("Paris", results[1].detail)
    }

    @Test
    fun `photon url encodes the query, biases location and falls back to english`() {
        val url = PhotonApi.searchUrl("café & co", GeoPoint(1.5, 2.25), "es")
        assertTrue(url.contains("q=caf%C3%A9+%26+co"))
        assertTrue(url.contains("lang=en"))
        assertTrue(url.contains("lat=1.50000&lon=2.25000"))
    }

    @Test
    fun `overpass parses nodes and ways with centres`() {
        val body = """{"elements":[
            {"type":"node","id":1,"lat":51.5,"lon":-0.1,"tags":{"shop":"bicycle","name":"Spokes","addr:street":"High St","addr:housenumber":"12","opening_hours":"Mo-Sa 09:00-18:00"}},
            {"type":"way","id":2,"center":{"lat":51.6,"lon":-0.2},"tags":{"shop":"bicycle"}},
            {"type":"node","id":3,"tags":{"name":"no coords"}}]}"""
        val shops = OverpassApi.parseBikeShops(body)
        assertEquals(2, shops.size)
        assertEquals("Spokes", shops[0].name)
        assertEquals("High St 12", shops[0].address)
        assertEquals("Mo-Sa 09:00-18:00", shops[0].openingHours)
        assertEquals("Bike shop", shops[1].name)
        assertEquals(51.6, shops[1].location.latitude, 1e-9)
        assertNull(shops[1].address)
    }

    @Test
    fun `overpass query searches nodes and ways around the point`() {
        val q = OverpassApi.bikeShopQuery(GeoPoint(51.5, -0.12), 3000)
        assertTrue(q.contains("node[\"shop\"=\"bicycle\"](around:3000,51.500000,-0.120000)"))
        assertTrue(q.contains("way[\"shop\"=\"bicycle\"]"))
    }
}
