package com.ridedeck.core.routing

import com.ridedeck.core.geo.Geo
import com.ridedeck.core.model.ActivityMode
import com.ridedeck.core.model.GeoPoint

data class RouteStep(
    val instruction: String,
    val maneuver: String?,
    val distanceM: Int,
    val durationSec: Long,
    val polyline: List<GeoPoint>,
)

data class Route(
    val mode: ActivityMode,
    val distanceM: Int,
    val durationSec: Long,
    val polyline: List<GeoPoint>,
    val steps: List<RouteStep>,
) {
    /** Cumulative end distance of each step, measured along [polyline]. */
    val stepEndsAlongM: List<Double> by lazy {
        var acc = 0.0
        steps.map { step ->
            acc += Geo.polylineLengthMeters(step.polyline).takeIf { it > 0 } ?: step.distanceM.toDouble()
            acc
        }
    }

    val polylineLengthM: Double by lazy { Geo.polylineLengthMeters(polyline) }
}

data class RouteProgress(
    val distanceFromRouteM: Double,
    val remainingDistanceM: Double,
    val remainingDurationSec: Long,
    val currentStepIndex: Int,
    val distanceToManeuverM: Double,
    val arrived: Boolean,
)

object RouteProgressCalculator {

    private const val ARRIVAL_RADIUS_M = 25.0

    fun progress(route: Route, location: GeoPoint): RouteProgress? {
        val projection = Geo.projectOntoPolyline(location, route.polyline) ?: return null
        val total = route.polylineLengthM
        val along = projection.alongM
        val remainingAlong = (total - along).coerceAtLeast(0.0)
        // The API's distance and polyline length differ slightly; scale to the API's figures.
        val fraction = if (total > 0) remainingAlong / total else 0.0
        val remainingDistance = route.distanceM * fraction
        val remainingDuration = (route.durationSec * fraction).toLong()

        val ends = route.stepEndsAlongM
        val stepTotal = ends.lastOrNull() ?: 0.0
        val alongForSteps = if (total > 0 && stepTotal > 0) along * (stepTotal / total) else along
        var index = ends.indexOfFirst { it > alongForSteps }
        if (index < 0) index = (route.steps.size - 1).coerceAtLeast(0)
        val toManeuver = if (ends.isEmpty()) remainingDistance else (ends[index] - alongForSteps).coerceAtLeast(0.0)

        val end = route.polyline.lastOrNull()
        val arrived = end != null && Geo.distanceMeters(location, end) <= ARRIVAL_RADIUS_M
        return RouteProgress(
            distanceFromRouteM = projection.distanceM,
            remainingDistanceM = remainingDistance,
            remainingDurationSec = remainingDuration,
            currentStepIndex = index,
            distanceToManeuverM = toManeuver,
            arrived = arrived,
        )
    }
}

/**
 * Declares the user off-route after several consecutive fixes beyond the threshold, so a single
 * bad GPS fix never triggers a reroute (each reroute costs a Routes API call).
 */
class OffRouteDetector(private val requiredConsecutive: Int = 3) {
    private var count = 0

    fun update(distanceFromRouteM: Double, accuracyM: Float, thresholdM: Double): Boolean {
        val effective = thresholdM + accuracyM.coerceAtMost(30f)
        count = if (distanceFromRouteM > effective) count + 1 else 0
        return count >= requiredConsecutive
    }

    fun reset() {
        count = 0
    }
}

enum class RerouteReason { MODE_CHANGED, OFF_ROUTE }

/** Decides when an active route must be recomputed. */
class ReroutePolicy(private val offRouteCooldownMs: Long = 20_000) {
    private var routedMode: ActivityMode? = null
    private var lastRouteAtMs: Long? = null

    fun onRouteComputed(mode: ActivityMode, nowMs: Long) {
        routedMode = mode
        lastRouteAtMs = nowMs
    }

    fun clear() {
        routedMode = null
        lastRouteAtMs = null
    }

    /** Returns why a reroute is needed now, or null. Mode changes always win and skip the cooldown. */
    fun evaluate(currentMode: ActivityMode, offRoute: Boolean, nowMs: Long): RerouteReason? {
        val mode = routedMode ?: return null
        if (mode != currentMode) return RerouteReason.MODE_CHANGED
        if (offRoute) {
            val last = lastRouteAtMs
            if (last == null || nowMs - last >= offRouteCooldownMs) return RerouteReason.OFF_ROUTE
        }
        return null
    }
}
