package com.ridedeck.core.geo

import com.ridedeck.core.model.GeoPoint
import kotlin.math.asin
import kotlin.math.atan2
import kotlin.math.cos
import kotlin.math.sin
import kotlin.math.sqrt

object Geo {
    const val EARTH_RADIUS_M = 6_371_008.8

    /** Great-circle distance using the haversine formula. */
    fun distanceMeters(lat1: Double, lon1: Double, lat2: Double, lon2: Double): Double {
        val dLat = Math.toRadians(lat2 - lat1)
        val dLon = Math.toRadians(lon2 - lon1)
        val a = sin(dLat / 2) * sin(dLat / 2) +
            cos(Math.toRadians(lat1)) * cos(Math.toRadians(lat2)) * sin(dLon / 2) * sin(dLon / 2)
        return 2 * EARTH_RADIUS_M * asin(sqrt(a.coerceIn(0.0, 1.0)))
    }

    fun distanceMeters(a: GeoPoint, b: GeoPoint): Double =
        distanceMeters(a.latitude, a.longitude, b.latitude, b.longitude)

    fun polylineLengthMeters(points: List<GeoPoint>): Double {
        var total = 0.0
        for (i in 1 until points.size) total += distanceMeters(points[i - 1], points[i])
        return total
    }

    fun bearingDegrees(from: GeoPoint, to: GeoPoint): Double {
        val lat1 = Math.toRadians(from.latitude)
        val lat2 = Math.toRadians(to.latitude)
        val dLon = Math.toRadians(to.longitude - from.longitude)
        val y = sin(dLon) * cos(lat2)
        val x = cos(lat1) * sin(lat2) - sin(lat1) * cos(lat2) * cos(dLon)
        return (Math.toDegrees(atan2(y, x)) + 360.0) % 360.0
    }

    /**
     * Projects [p] onto the polyline and returns the closest point information.
     * Uses a local equirectangular projection per segment, which is accurate to well under a metre
     * for the segment lengths a route contains.
     */
    fun projectOntoPolyline(p: GeoPoint, polyline: List<GeoPoint>): PolylineProjection? {
        if (polyline.isEmpty()) return null
        if (polyline.size == 1) {
            return PolylineProjection(distanceMeters(p, polyline[0]), 0, 0.0, polyline[0])
        }
        var best: PolylineProjection? = null
        var cumulative = 0.0
        for (i in 0 until polyline.size - 1) {
            val a = polyline[i]
            val b = polyline[i + 1]
            val segLen = distanceMeters(a, b)
            val (t, closest) = closestOnSegment(p, a, b)
            val d = distanceMeters(p, closest)
            if (best == null || d < best.distanceM) {
                best = PolylineProjection(d, i, cumulative + segLen * t, closest)
            }
            cumulative += segLen
        }
        return best
    }

    private fun closestOnSegment(p: GeoPoint, a: GeoPoint, b: GeoPoint): Pair<Double, GeoPoint> {
        val cosLat = cos(Math.toRadians((a.latitude + b.latitude) / 2))
        val ax = a.longitude * cosLat
        val ay = a.latitude
        val bx = b.longitude * cosLat
        val by = b.latitude
        val px = p.longitude * cosLat
        val py = p.latitude
        val dx = bx - ax
        val dy = by - ay
        val lenSq = dx * dx + dy * dy
        val t = if (lenSq == 0.0) 0.0 else (((px - ax) * dx + (py - ay) * dy) / lenSq).coerceIn(0.0, 1.0)
        val closest = GeoPoint(a.latitude + (b.latitude - a.latitude) * t, a.longitude + (b.longitude - a.longitude) * t)
        return t to closest
    }
}

data class PolylineProjection(
    /** Distance from the query point to the polyline. */
    val distanceM: Double,
    val segmentIndex: Int,
    /** Distance along the polyline from its start to the projected point. */
    val alongM: Double,
    val closestPoint: GeoPoint,
)

/** Google encoded polyline format (used by the Routes API). */
object PolylineCodec {

    fun decode(encoded: String): List<GeoPoint> {
        val result = ArrayList<GeoPoint>()
        var index = 0
        var lat = 0
        var lng = 0
        while (index < encoded.length) {
            var shift = 0
            var value = 0
            var b: Int
            do {
                b = encoded[index++].code - 63
                value = value or ((b and 0x1f) shl shift)
                shift += 5
            } while (b >= 0x20 && index < encoded.length)
            lat += if (value and 1 != 0) (value shr 1).inv() else value shr 1

            shift = 0
            value = 0
            if (index >= encoded.length) break
            do {
                b = encoded[index++].code - 63
                value = value or ((b and 0x1f) shl shift)
                shift += 5
            } while (b >= 0x20 && index < encoded.length)
            lng += if (value and 1 != 0) (value shr 1).inv() else value shr 1
            result.add(GeoPoint(lat / 1e5, lng / 1e5))
        }
        return result
    }

    fun encode(points: List<GeoPoint>): String {
        val sb = StringBuilder()
        var prevLat = 0
        var prevLng = 0
        for (p in points) {
            val lat = Math.round(p.latitude * 1e5).toInt()
            val lng = Math.round(p.longitude * 1e5).toInt()
            encodeValue(lat - prevLat, sb)
            encodeValue(lng - prevLng, sb)
            prevLat = lat
            prevLng = lng
        }
        return sb.toString()
    }

    private fun encodeValue(v: Int, sb: StringBuilder) {
        var value = if (v < 0) (v shl 1).inv() else v shl 1
        while (value >= 0x20) {
            sb.append(((0x20 or (value and 0x1f)) + 63).toChar())
            value = value shr 5
        }
        sb.append((value + 63).toChar())
    }
}
