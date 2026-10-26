package com.ridedeck.core.gbfs

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive

data class GbfsFeeds(val stationInformationUrl: String?, val stationStatusUrl: String?)

data class StationInfo(
    val id: String,
    val name: String,
    val latitude: Double,
    val longitude: Double,
    val capacity: Int?,
)

data class StationStatus(
    val id: String,
    val bikesAvailable: Int,
    val docksAvailable: Int,
    val isRenting: Boolean,
)

data class BikeShareStation(
    val id: String,
    val name: String,
    val latitude: Double,
    val longitude: Double,
    val capacity: Int?,
    val bikesAvailable: Int?,
    val docksAvailable: Int?,
    val isRenting: Boolean,
)

/** One row of MobilityData's public `systems.csv` catalogue. */
data class GbfsSystem(
    val countryCode: String,
    val name: String,
    val location: String,
    val systemId: String,
    val autoDiscoveryUrl: String,
)

/**
 * Parses GBFS feeds. Supports v1/v2 (`data.<lang>.feeds`, `num_bikes_available`, plain names)
 * and v3 (`data.feeds`, `num_vehicles_available`, localized name arrays).
 */
object GbfsParser {
    private val json = Json { ignoreUnknownKeys = true; isLenient = true }

    fun parseDiscovery(body: String, preferredLanguage: String): GbfsFeeds {
        val data = root(body)["data"]?.jsonObject ?: return GbfsFeeds(null, null)
        val feeds: JsonArray? = (data["feeds"] as? JsonArray) ?: run {
            val byLang = data[preferredLanguage] as? JsonObject
                ?: data["en"] as? JsonObject
                ?: data.values.firstOrNull { it is JsonObject } as? JsonObject
            byLang?.get("feeds") as? JsonArray
        }
        val urls = feeds.orEmpty().mapNotNull { it as? JsonObject }.associate {
            it.string("name").orEmpty() to it.string("url").orEmpty()
        }
        return GbfsFeeds(
            stationInformationUrl = urls["station_information"]?.takeIf { it.isNotBlank() },
            stationStatusUrl = urls["station_status"]?.takeIf { it.isNotBlank() },
        )
    }

    fun parseStationInformation(body: String, preferredLanguage: String): List<StationInfo> =
        stations(body).mapNotNull { s ->
            val id = s.string("station_id") ?: return@mapNotNull null
            val lat = s.double("lat") ?: return@mapNotNull null
            val lon = s.double("lon") ?: return@mapNotNull null
            StationInfo(
                id = id,
                name = localizedName(s["name"], preferredLanguage) ?: id,
                latitude = lat,
                longitude = lon,
                capacity = s.int("capacity"),
            )
        }

    fun parseStationStatus(body: String): Map<String, StationStatus> =
        stations(body).mapNotNull { s ->
            val id = s.string("station_id") ?: return@mapNotNull null
            StationStatus(
                id = id,
                bikesAvailable = s.int("num_vehicles_available") ?: s.int("num_bikes_available") ?: 0,
                docksAvailable = s.int("num_docks_available") ?: 0,
                isRenting = s.bool("is_renting") ?: true,
            )
        }.associateBy { it.id }

    /** Feed time-to-live in seconds, used to avoid polling faster than the operator allows. */
    fun parseTtlSeconds(body: String): Int = root(body).int("ttl") ?: 60

    fun merge(info: List<StationInfo>, status: Map<String, StationStatus>): List<BikeShareStation> =
        info.map { i ->
            val s = status[i.id]
            BikeShareStation(
                id = i.id,
                name = i.name,
                latitude = i.latitude,
                longitude = i.longitude,
                capacity = i.capacity,
                bikesAvailable = s?.bikesAvailable,
                docksAvailable = s?.docksAvailable,
                isRenting = s?.isRenting ?: true,
            )
        }

    private fun root(body: String): JsonObject = json.parseToJsonElement(body).jsonObject

    private fun stations(body: String): List<JsonObject> =
        ((root(body)["data"] as? JsonObject)?.get("stations") as? JsonArray)
            .orEmpty().mapNotNull { it as? JsonObject }

    private fun localizedName(element: JsonElement?, lang: String): String? = when (element) {
        is JsonPrimitive -> element.contentOrNull
        is JsonArray -> {
            val entries = element.mapNotNull { it as? JsonObject }
            (entries.firstOrNull { it.string("language")?.startsWith(lang) == true } ?: entries.firstOrNull())
                ?.string("text")
        }
        else -> null
    }

    private fun JsonObject.string(key: String) = (this[key] as? JsonPrimitive)?.contentOrNull
    private fun JsonObject.double(key: String) = (this[key] as? JsonPrimitive)?.doubleOrNull
    private fun JsonObject.int(key: String) =
        (this[key] as? JsonPrimitive)?.let { it.intOrNull ?: it.doubleOrNull?.toInt() }
    private fun JsonObject.bool(key: String) =
        (this[key] as? JsonPrimitive)?.let { it.booleanOrNull ?: it.intOrNull?.let { v -> v != 0 } }
    private fun JsonArray?.orEmpty(): List<JsonElement> = this ?: emptyList()
}

/** Parser for MobilityData's systems.csv (RFC 4180 quoting). */
object GbfsSystemsCsv {

    fun parse(csv: String): List<GbfsSystem> {
        val rows = parseRows(csv)
        if (rows.isEmpty()) return emptyList()
        val header = rows.first().map { it.trim().lowercase() }
        fun idx(name: String) = header.indexOf(name)
        val country = idx("country code")
        val name = idx("name")
        val location = idx("location")
        val systemId = idx("system id")
        val discovery = idx("auto-discovery url")
        if (listOf(country, name, discovery).any { it < 0 }) return emptyList()
        return rows.drop(1).mapNotNull { r ->
            val url = r.getOrNull(discovery)?.trim().orEmpty()
            if (!url.startsWith("http")) return@mapNotNull null
            GbfsSystem(
                countryCode = r.getOrNull(country)?.trim().orEmpty(),
                name = r.getOrNull(name)?.trim().orEmpty(),
                location = r.getOrNull(location)?.trim().orEmpty(),
                systemId = r.getOrNull(systemId)?.trim().orEmpty(),
                autoDiscoveryUrl = url,
            )
        }
    }

    /**
     * Ranks systems for the user's area: same country first, then by how well the system's
     * location matches the user's locality.
     */
    fun rankForArea(systems: List<GbfsSystem>, countryCode: String?, locality: String?): List<GbfsSystem> {
        val inCountry = if (countryCode.isNullOrBlank()) systems
        else systems.filter { it.countryCode.equals(countryCode, ignoreCase = true) }
        val city = locality?.lowercase()?.trim().orEmpty()
        return inCountry.sortedWith(
            compareByDescending<GbfsSystem> { city.isNotEmpty() && it.location.lowercase().contains(city) }
                .thenBy { it.location.lowercase() }
                .thenBy { it.name.lowercase() },
        )
    }

    private fun parseRows(csv: String): List<List<String>> {
        val rows = mutableListOf<List<String>>()
        var row = mutableListOf<String>()
        val field = StringBuilder()
        var inQuotes = false
        var i = 0
        while (i < csv.length) {
            val c = csv[i]
            if (inQuotes) {
                if (c == '"') {
                    if (i + 1 < csv.length && csv[i + 1] == '"') {
                        field.append('"')
                        i++
                    } else {
                        inQuotes = false
                    }
                } else {
                    field.append(c)
                }
            } else {
                when (c) {
                    '"' -> inQuotes = true
                    ',' -> {
                        row.add(field.toString())
                        field.clear()
                    }
                    '\r' -> Unit
                    '\n' -> {
                        row.add(field.toString())
                        field.clear()
                        if (row.any { it.isNotBlank() }) rows.add(row)
                        row = mutableListOf()
                    }
                    else -> field.append(c)
                }
            }
            i++
        }
        if (field.isNotEmpty() || row.isNotEmpty()) {
            row.add(field.toString())
            if (row.any { it.isNotBlank() }) rows.add(row)
        }
        return rows
    }
}
