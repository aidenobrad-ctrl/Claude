package com.ridedeck.core.routing

import com.ridedeck.core.geo.PolylineCodec
import com.ridedeck.core.mode.ModeProfile
import com.ridedeck.core.model.ActivityMode
import com.ridedeck.core.model.GeoPoint
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.util.Locale
import kotlin.math.roundToInt
import kotlin.math.roundToLong

/**
 * Open-source routing through OSRM (Open Source Routing Machine) on OpenStreetMap data.
 * Bike and walk use separate OSRM servers; the path segment "driving" is part of the OSRM URL
 * format and is ignored by those servers.
 */
object OsrmApi {

    fun routeUrl(origin: GeoPoint, destination: GeoPoint, mode: ActivityMode): String {
        val base = ModeProfile.of(mode).osrmBaseUrl
        val coords = String.format(
            Locale.US,
            "%.6f,%.6f;%.6f,%.6f",
            origin.longitude, origin.latitude, destination.longitude, destination.latitude,
        )
        return "$base/route/v1/driving/$coords?overview=full&geometries=polyline&steps=true"
    }

    private val json = Json { ignoreUnknownKeys = true }

    /** Parses the first route, or null when OSRM found none (`code` other than "Ok"). */
    fun parseResponse(body: String, mode: ActivityMode): Route? {
        val root = json.parseToJsonElement(body) as? JsonObject ?: return null
        if (root.string("code") != "Ok") return null
        val route = (root["routes"] as? JsonArray)?.firstOrNull() as? JsonObject ?: return null
        val steps = (route["legs"] as? JsonArray).orEmpty().flatMap { leg ->
            ((leg as? JsonObject)?.get("steps") as? JsonArray).orEmpty().mapNotNull { it as? JsonObject }.map { step ->
                val maneuver = step["maneuver"] as? JsonObject
                val type = maneuver?.string("type")
                val modifier = maneuver?.string("modifier")
                RouteStep(
                    instruction = instruction(type, modifier, step.string("name"), maneuver?.get("exit")?.jsonPrimitive?.contentOrNull),
                    maneuver = maneuverCode(type, modifier),
                    distanceM = (step.double("distance") ?: 0.0).roundToInt(),
                    durationSec = (step.double("duration") ?: 0.0).roundToLong(),
                    polyline = step.string("geometry")?.let(PolylineCodec::decode).orEmpty(),
                )
            }
        }
        return Route(
            mode = mode,
            distanceM = (route.double("distance") ?: 0.0).roundToInt(),
            durationSec = (route.double("duration") ?: 0.0).roundToLong(),
            polyline = route.string("geometry")?.let(PolylineCodec::decode).orEmpty(),
            steps = steps,
        )
    }

    fun parseError(body: String): String? = runCatching {
        (json.parseToJsonElement(body) as? JsonObject)?.let { it.string("message") ?: it.string("code") }
    }.getOrNull()

    /** Converts OSRM type/modifier into the app's maneuver codes (used to pick icons). */
    fun maneuverCode(type: String?, modifier: String?): String? = when (type) {
        "depart" -> "DEPART"
        "arrive" -> "ARRIVE"
        "roundabout", "rotary", "roundabout turn", "exit roundabout", "exit rotary" ->
            if (modifier?.contains("left") == true) "ROUNDABOUT_LEFT" else "ROUNDABOUT_RIGHT"
        "merge" -> "MERGE"
        "fork" -> if (modifier?.contains("left") == true) "FORK_LEFT" else "FORK_RIGHT"
        "on ramp", "off ramp" -> if (modifier?.contains("left") == true) "RAMP_LEFT" else "RAMP_RIGHT"
        else -> when (modifier) {
            "left" -> "TURN_LEFT"
            "right" -> "TURN_RIGHT"
            "slight left" -> "TURN_SLIGHT_LEFT"
            "slight right" -> "TURN_SLIGHT_RIGHT"
            "sharp left" -> "TURN_SHARP_LEFT"
            "sharp right" -> "TURN_SHARP_RIGHT"
            "uturn" -> "UTURN_LEFT"
            "straight" -> "STRAIGHT"
            else -> if (type == "new name" || type == "continue") "STRAIGHT" else null
        }
    }

    /** Builds a short English instruction, e.g. "Turn left onto Main Street". */
    fun instruction(type: String?, modifier: String?, name: String?, exit: String? = null): String {
        val road = name?.takeIf { it.isNotBlank() }
        val direction = modifier?.replaceFirstChar { it.uppercase() }
        val onto = road?.let { " onto $it" }.orEmpty()
        return when (type) {
            "depart" -> "Head off" + (road?.let { " on $it" }.orEmpty())
            "arrive" -> "Arrive at your destination"
            "roundabout", "rotary" -> "At the roundabout, take " + (exit?.let { "exit $it" } ?: "the exit") + onto
            "exit roundabout", "exit rotary" -> "Leave the roundabout$onto"
            "merge" -> "Merge$onto"
            "fork" -> "Keep ${modifier ?: "straight"} at the fork$onto"
            "on ramp", "off ramp" -> "Take the ramp$onto"
            "end of road" -> "At the end of the road, turn ${modifier ?: "ahead"}$onto"
            "new name", "continue" -> if (modifier == null || modifier == "straight") "Continue$onto" else "Continue $modifier$onto"
            else -> when {
                modifier == "uturn" -> "Make a U-turn$onto"
                modifier == "straight" -> "Go straight$onto"
                direction != null -> "Turn ${modifier}$onto"
                else -> "Continue$onto"
            }
        }
    }

    private fun JsonObject.string(key: String) = this[key]?.jsonPrimitive?.contentOrNull
    private fun JsonObject.double(key: String) = this[key]?.jsonPrimitive?.doubleOrNull
    private fun JsonArray?.orEmpty() = this ?: JsonArray(emptyList())
}
