package com.ridedeck.app.ui.map

import android.graphics.Color
import android.graphics.Paint
import com.ridedeck.core.model.GeoPoint
import com.ridedeck.core.osm.PlaceSuggestion
import org.osmdroid.views.MapView
import org.osmdroid.views.overlay.Marker
import org.osmdroid.views.overlay.Polyline

/**
 * The live map's overlays, kept as long-lived objects and updated in place so recomposition does
 * not rebuild the whole overlay stack. Draw order: route, destination, me.
 */
class MapOverlays(private val map: MapView) {
    private val context = map.context
    private val route = Polyline(map).apply {
        outlinePaint.strokeWidth = 14f * context.resources.displayMetrics.density / 2f
        outlinePaint.strokeCap = Paint.Cap.ROUND
        outlinePaint.strokeJoin = Paint.Join.ROUND
        isEnabled = false
        infoWindow = null
    }
    private val destination = Marker(map).apply {
        setAnchor(Marker.ANCHOR_CENTER, Marker.ANCHOR_BOTTOM)
        isEnabled = false
    }
    private val me = Marker(map).apply {
        icon = dotIcon(context, Color.rgb(0x29, 0x79, 0xFF), 20)
        setAnchor(Marker.ANCHOR_CENTER, Marker.ANCHOR_CENTER)
        setInfoWindow(null)
        isEnabled = false
        title = "You"
    }

    init {
        map.overlays.addAll(listOf(route, destination, me))
    }

    fun setRoute(points: List<GeoPoint>?, color: Int) {
        route.outlinePaint.color = color
        route.setPoints(points.orEmpty().map { it.toOsm() })
        route.isEnabled = !points.isNullOrEmpty()
        map.invalidate()
    }

    fun setDestination(place: PlaceSuggestion?, color: Int) {
        if (place != null) {
            destination.position = place.location.toOsm()
            destination.title = place.name
            destination.snippet = place.detail
            destination.icon = dotIcon(context, color, 26)
        } else {
            destination.closeInfoWindow()
        }
        destination.isEnabled = place != null
        map.invalidate()
    }

    fun setMyLocation(point: GeoPoint?) {
        if (point != null) me.position = point.toOsm()
        me.isEnabled = point != null
        map.invalidate()
    }
}
