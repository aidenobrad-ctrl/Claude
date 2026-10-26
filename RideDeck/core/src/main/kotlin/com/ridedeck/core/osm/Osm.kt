package com.ridedeck.core.osm

import com.ridedeck.core.model.GeoPoint
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.jsonPrimitive
import java.util.Locale

/** A search result from Photon (open-source geocoder on OpenStreetMap data). */
data class PlaceSuggestion(
    val id: String,
    val name: String,
    val detail: String,
    val location: GeoPoint,
)

/** A bike shop from OpenStreetMap (`shop=bicycle`) via the Overpass API. */
data class BikeShop(
    val id: String,
    val name: String,
    val address: String?,
    val openingHours: String?,
    val location: GeoPoint,
)

object PhotonApi {
    private val supportedLanguages = setOf("en", "de", "fr")
    private val json = Json { ignoreUnknownKeys = true }

    fun searchUrl(query: String, near: GeoPoint?, language: String): String {
        val q = java.net.URLEncoder.encode(query.trim(), "UTF-8")
        val lang = language.takeIf { it in supportedLanguages } ?: "en"
        val bias = near?.let { String.format(Locale.US, "&lat=%.5f&lon=%.5f", it.latitude, it.longitude) }.orEmpty()
        return "https://photon.komoot.io/api/?q=$q&limit=6&lang=$lang$bias"
    }

    fun parse(body: String): List<PlaceSuggestion> {
        val root = json.parseToJsonElement(body) as? JsonObject ?: return emptyList()
        val features = root["features"] as? JsonArray ?: return emptyList()
        return features.mapNotNull { f ->
            val feature = f as? JsonObject ?: return@mapNotNull null
            val coords = (feature["geometry"] as? JsonObject)?.get("coordinates") as? JsonArray ?: return@mapNotNull null
            val lon = coords.getOrNull(0)?.jsonPrimitive?.doubleOrNull ?: return@mapNotNull null
            val lat = coords.getOrNull(1)?.jsonPrimitive?.doubleOrNull ?: return@mapNotNull null
            val p = feature["properties"] as? JsonObject ?: JsonObject(emptyMap())
            fun s(key: String) = p[key]?.jsonPrimitive?.contentOrNull?.takeIf { it.isNotBlank() }
            val street = listOfNotNull(s("street"), s("housenumber")).joinToString(" ").takeIf { it.isNotBlank() }
            val name = s("name") ?: street ?: s("city") ?: return@mapNotNull null
            val detail = listOfNotNull(street?.takeIf { it != name }, s("city")?.takeIf { it != name }, s("state"), s("country"))
                .distinct()
                .joinToString(", ")
            PlaceSuggestion(
                id = "${s("osm_type") ?: "x"}${s("osm_id") ?: "$lat,$lon"}",
                name = name,
                detail = detail,
                location = GeoPoint(lat, lon),
            )
        }
    }
}

object OverpassApi {
    const val ENDPOINT = "https://overpass-api.de/api/interpreter"
    private val json = Json { ignoreUnknownKeys = true }

    fun bikeShopQuery(center: GeoPoint, radiusM: Int): String {
        val around = String.format(Locale.US, "around:%d,%.6f,%.6f", radiusM, center.latitude, center.longitude)
        return "[out:json][timeout:20];(node[\"shop\"=\"bicycle\"]($around);way[\"shop\"=\"bicycle\"]($around););out center 40;"
    }

    fun parseBikeShops(body: String): List<BikeShop> {
        val root = json.parseToJsonElement(body) as? JsonObject ?: return emptyList()
        val elements = root["elements"] as? JsonArray ?: return emptyList()
        return elements.mapNotNull { e ->
            val el = e as? JsonObject ?: return@mapNotNull null
            val center = el["center"] as? JsonObject
            val lat = (el["lat"] ?: center?.get("lat"))?.jsonPrimitive?.doubleOrNull ?: return@mapNotNull null
            val lon = (el["lon"] ?: center?.get("lon"))?.jsonPrimitive?.doubleOrNull ?: return@mapNotNull null
            val tags = el["tags"] as? JsonObject ?: JsonObject(emptyMap())
            fun t(key: String) = tags[key]?.jsonPrimitive?.contentOrNull?.takeIf { it.isNotBlank() }
            val address = listOfNotNull(
                listOfNotNull(t("addr:street"), t("addr:housenumber")).joinToString(" ").takeIf { it.isNotBlank() },
                t("addr:city"),
            ).joinToString(", ").takeIf { it.isNotBlank() }
            BikeShop(
                id = "${el["type"]?.jsonPrimitive?.contentOrNull}/${el["id"]?.jsonPrimitive?.contentOrNull}",
                name = t("name") ?: "Bike shop",
                address = address,
                openingHours = t("opening_hours"),
                location = GeoPoint(lat, lon),
            )
        }
    }
}
