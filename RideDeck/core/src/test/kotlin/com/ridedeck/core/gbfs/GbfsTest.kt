package com.ridedeck.core.gbfs

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class GbfsTest {

    @Test
    fun `v2 discovery picks the preferred language`() {
        val body = """{"ttl":60,"data":{"fr":{"feeds":[{"name":"station_information","url":"https://x/fr/si"}]},
            "en":{"feeds":[{"name":"station_information","url":"https://x/en/si"},
            {"name":"station_status","url":"https://x/en/ss"}]}}}"""
        val feeds = GbfsParser.parseDiscovery(body, "en")
        assertEquals("https://x/en/si", feeds.stationInformationUrl)
        assertEquals("https://x/en/ss", feeds.stationStatusUrl)
    }

    @Test
    fun `v3 discovery uses the flat feed list`() {
        val body = """{"data":{"feeds":[{"name":"station_status","url":"https://y/ss"}]}}"""
        val feeds = GbfsParser.parseDiscovery(body, "en")
        assertNull(feeds.stationInformationUrl)
        assertEquals("https://y/ss", feeds.stationStatusUrl)
    }

    @Test
    fun `station info supports plain and localized names`() {
        val v2 = """{"data":{"stations":[{"station_id":"1","name":"Main St","lat":1.5,"lon":2.5,"capacity":12}]}}"""
        val v3 = """{"data":{"stations":[{"station_id":"2","name":[{"text":"Rue","language":"fr"},
            {"text":"Street","language":"en"}],"lat":1,"lon":2}]}}"""
        assertEquals("Main St", GbfsParser.parseStationInformation(v2, "en").single().name)
        assertEquals(12, GbfsParser.parseStationInformation(v2, "en").single().capacity)
        assertEquals("Street", GbfsParser.parseStationInformation(v3, "en").single().name)
    }

    @Test
    fun `station status reads v2 and v3 counts and merges`() {
        val status = GbfsParser.parseStationStatus(
            """{"data":{"stations":[{"station_id":"1","num_bikes_available":3,"num_docks_available":9,"is_renting":1},
               {"station_id":"2","num_vehicles_available":5,"num_docks_available":0,"is_renting":false}]}}""",
        )
        assertEquals(3, status["1"]!!.bikesAvailable)
        assertEquals(5, status["2"]!!.bikesAvailable)
        assertEquals(false, status["2"]!!.isRenting)

        val merged = GbfsParser.merge(listOf(StationInfo("1", "A", 0.0, 0.0, 12)), status)
        assertEquals(3, merged.single().bikesAvailable)
    }

    @Test
    fun `systems csv handles quoting and ranks local systems first`() {
        val csv = "Country Code,Name,Location,System ID,URL,Auto-Discovery URL,Supported Versions\n" +
            "US,Citi Bike,\"New York, NY\",citibike,https://citibikenyc.com,https://gbfs.citibikenyc.com/gbfs/gbfs.json,2.3\n" +
            "US,Divvy,\"Chicago, IL\",divvy,https://divvybikes.com,https://gbfs.divvybikes.com/gbfs/gbfs.json,2.3\n" +
            "GB,Santander Cycles,London,santander,https://tfl.gov.uk,https://example.org/gbfs.json,2.3\n" +
            "US,Broken,Nowhere,broken,https://x,,2.3\n"
        val systems = GbfsSystemsCsv.parse(csv)
        assertEquals(3, systems.size)
        assertEquals("New York, NY", systems[0].location)
        val ranked = GbfsSystemsCsv.rankForArea(systems, "US", "Chicago")
        assertEquals(listOf("Divvy", "Citi Bike"), ranked.map { it.name })
    }
}
