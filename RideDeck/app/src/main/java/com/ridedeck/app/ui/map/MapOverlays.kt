package com.ridedeck.app.ui.map

import android.graphics.Color
import android.graphics.Paint
import com.ridedeck.core.gbfs.BikeShareStation
import com.ridedeck.core.model.GeoPoint
import com.ridedeck.core.osm.BikeShop
import com.ridedeck.core.osm.PlaceSuggestion
import org.osmdroid.views.MapView
import org.osmdroid.views.overlay.FolderOverlay
import org.osmdroid.views.overlay.Marker
import org.osmdroid.views.overlay.Polyline

/**
 * The live map's overlays, kept as long-lived objects and updated in place so recomposition does
 * not rebuild the whole overlay stack. Draw order: route, shops, stations, destination, me.
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
    private val shops = FolderOverlay()
    private val stations = FolderOverlay()
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
        map.overlays.addAll(listOf(route, shops, stations, destination, me))
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

    fun setShops(list: List<BikeShop>) {
        shops.items.forEach { (it as? Marker)?.closeInfoWindow() }
        shops.items.clear()
        val icon = dotIcon(context, Color.rgb(0x00, 0x96, 0xD6), 20)
        list.forEach { shop ->
            shops.add(
                Marker(map).apply {
                    position = shop.location.toOsm()
                    title = shop.name
                    snippet = listOfNotNull(shop.address, shop.openingHours).joinToString("\n")
                    this.icon = icon
                    setAnchor(Marker.ANCHOR_CENTER, Marker.ANCHOR_CENTER)
                },
            )
        }
        map.invalidate()
    }

    fun setStations(list: List<BikeShareStation>) {
        stations.items.forEach { (it as? Marker)?.closeInfoWindow() }
        stations.items.clear()
        val available = dotIcon(context, Color.rgb(0x2E, 0xB8, 0x5C), 18)
        val empty = dotIcon(context, Color.rgb(0xF5, 0x8A, 0x1F), 18)
        list.forEach { station ->
            val bikes = station.bikesAvailable
            stations.add(
                Marker(map).apply {
                    position = org.osmdroid.util.GeoPoint(station.latitude, station.longitude)
                    title = station.name
                    snippet = "Bikes: ${bikes ?: "?"} · Docks: ${station.docksAvailable ?: "?"}"
                    icon = if ((bikes ?: 0) > 0 && station.isRenting) available else empty
                    alpha = if (station.isRenting) 1f else 0.5f
                    setAnchor(Marker.ANCHOR_CENTER, Marker.ANCHOR_CENTER)
                },
            )
        }
        map.invalidate()
    }

    fun setMyLocation(point: GeoPoint?) {
        if (point != null) me.position = point.toOsm()
        me.isEnabled = point != null
        map.invalidate()
    }
}
