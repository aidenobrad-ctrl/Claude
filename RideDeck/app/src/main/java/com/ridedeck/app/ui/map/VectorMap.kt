package com.ridedeck.app.ui.map

import android.view.Gravity
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.ridedeck.app.data.maps.MapStyles
import com.ridedeck.core.model.GeoPoint
import org.maplibre.android.geometry.LatLng
import org.maplibre.android.maps.MapLibreMap
import org.maplibre.android.maps.MapView
import org.maplibre.android.maps.Style
import org.maplibre.android.style.layers.CircleLayer
import org.maplibre.android.style.layers.LineLayer
import org.maplibre.android.style.layers.Property
import org.maplibre.android.style.layers.PropertyFactory
import org.maplibre.android.style.sources.GeoJsonSource
import org.maplibre.geojson.FeatureCollection
import org.maplibre.geojson.LineString
import org.maplibre.geojson.MultiLineString
import org.maplibre.geojson.Point

/**
 * A MapLibre map view (open-source vector renderer) tied to the composition's lifecycle, plus the
 * map and loaded style once they are ready. [style] is null while a style loads; anything drawn on
 * the map has to be added again when it changes.
 */
class VectorMapState(val view: MapView) {
    var map: MapLibreMap? by mutableStateOf(null)
        private set
    var style: Style? by mutableStateOf(null)
        private set
    private var requestedUrl: String? = null

    init {
        view.getMapAsync { m ->
            m.uiSettings.apply {
                // RideDeck shows the required OpenFreeMap / OpenStreetMap credit as text itself.
                setLogoEnabled(false)
                setAttributionEnabled(false)
                setCompassGravity(Gravity.TOP or Gravity.END)
            }
            m.setMinZoomPreference(2.0)
            m.setMaxZoomPreference(19.5)
            map = m
            requestedUrl?.let(::load)
        }
        // A style that fails to load (offline on first start, or a renamed style) falls back once.
        view.addOnDidFailLoadingMapListener {
            if (requestedUrl != MapStyles.FALLBACK) setStyleUrl(MapStyles.FALLBACK)
        }
    }

    fun setStyleUrl(url: String) {
        if (url == requestedUrl) return
        requestedUrl = url
        if (map != null) load(url)
    }

    private fun load(url: String) {
        style = null
        map?.setStyle(Style.Builder().fromUri(url)) { loaded -> style = loaded }
    }
}

@Composable
fun rememberVectorMap(): VectorMapState {
    val context = LocalContext.current
    val state = remember { VectorMapState(MapView(context).apply { onCreate(null) }) }
    val lifecycleOwner = LocalLifecycleOwner.current
    DisposableEffect(lifecycleOwner, state) {
        val view = state.view
        val observer = LifecycleEventObserver { _, event ->
            when (event) {
                Lifecycle.Event.ON_START -> view.onStart()
                Lifecycle.Event.ON_RESUME -> view.onResume()
                Lifecycle.Event.ON_PAUSE -> view.onPause()
                Lifecycle.Event.ON_STOP -> view.onStop()
                else -> Unit
            }
        }
        lifecycleOwner.lifecycle.addObserver(observer)
        val current = lifecycleOwner.lifecycle.currentState
        if (current.isAtLeast(Lifecycle.State.STARTED)) view.onStart()
        if (current.isAtLeast(Lifecycle.State.RESUMED)) view.onResume()
        onDispose {
            lifecycleOwner.lifecycle.removeObserver(observer)
            view.onPause()
            view.onStop()
            view.onDestroy()
        }
    }
    return state
}

fun GeoPoint.toLatLng(): LatLng = LatLng(latitude, longitude)

private fun GeoPoint.toPoint(): Point = Point.fromLngLat(longitude, latitude)

/** Route, destination, rider and track layers RideDeck draws over the base map. */
/** Credit required by OpenFreeMap, OpenMapTiles and OpenStreetMap; shown on every map. */
const val MAP_ATTRIBUTION = "© OpenFreeMap © OpenMapTiles © OpenStreetMap contributors"

object VectorOverlays {
    private const val ROUTE = "rd-route"
    private const val DEST = "rd-dest"
    private const val ME = "rd-me"
    private const val TRACK = "rd-track"
    private const val ENDS = "rd-ends"

    private fun Style.source(id: String): GeoJsonSource =
        getSourceAs<GeoJsonSource>(id) ?: GeoJsonSource(id).also { addSource(it) }

    /** The planned route: a wide accent line with a darker casing so it reads on any style. */
    fun setRoute(style: Style, points: List<GeoPoint>?, color: Int, casing: Int) {
        val source = style.source(ROUTE)
        if (style.getLayer("$ROUTE-casing") == null) {
            style.addLayer(
                LineLayer("$ROUTE-casing", ROUTE).withProperties(
                    PropertyFactory.lineColor(casing),
                    PropertyFactory.lineWidth(11f),
                    PropertyFactory.lineCap(Property.LINE_CAP_ROUND),
                    PropertyFactory.lineJoin(Property.LINE_JOIN_ROUND),
                ),
            )
            style.addLayer(
                LineLayer(ROUTE, ROUTE).withProperties(
                    PropertyFactory.lineColor(color),
                    PropertyFactory.lineWidth(7f),
                    PropertyFactory.lineCap(Property.LINE_CAP_ROUND),
                    PropertyFactory.lineJoin(Property.LINE_JOIN_ROUND),
                ),
            )
        }
        if (points != null && points.size >= 2) {
            source.setGeoJson(LineString.fromLngLats(points.map { it.toPoint() }))
        } else {
            source.setGeoJson(FeatureCollection.fromFeatures(emptyList()))
        }
    }

    fun setDestination(style: Style, point: GeoPoint?, color: Int) {
        val source = style.source(DEST)
        if (style.getLayer(DEST) == null) {
            style.addLayer(
                CircleLayer(DEST, DEST).withProperties(
                    PropertyFactory.circleRadius(9f),
                    PropertyFactory.circleColor(color),
                    PropertyFactory.circleStrokeColor(android.graphics.Color.WHITE),
                    PropertyFactory.circleStrokeWidth(3f),
                ),
            )
        }
        if (point != null) source.setGeoJson(point.toPoint()) else source.setGeoJson(FeatureCollection.fromFeatures(emptyList()))
    }

    /** The rider: an accent dot with a white ring and a soft halo, always drawn on top. */
    fun setMyLocation(style: Style, point: GeoPoint?, color: Int) {
        val source = style.source(ME)
        if (style.getLayer(ME) == null) {
            style.addLayer(
                CircleLayer("$ME-halo", ME).withProperties(
                    PropertyFactory.circleRadius(20f),
                    PropertyFactory.circleColor(color),
                    PropertyFactory.circleOpacity(0.18f),
                ),
            )
            style.addLayer(
                CircleLayer(ME, ME).withProperties(
                    PropertyFactory.circleRadius(8f),
                    PropertyFactory.circleColor(color),
                    PropertyFactory.circleStrokeColor(android.graphics.Color.WHITE),
                    PropertyFactory.circleStrokeWidth(3f),
                ),
            )
        }
        if (point != null) source.setGeoJson(point.toPoint()) else source.setGeoJson(FeatureCollection.fromFeatures(emptyList()))
    }

    /** A recorded ride (split into segments at pauses) with start and finish dots. */
    fun setTrack(style: Style, segments: List<List<GeoPoint>>, color: Int, start: Int, finish: Int) {
        val track = style.source(TRACK)
        if (style.getLayer(TRACK) == null) {
            style.addLayer(
                LineLayer(TRACK, TRACK).withProperties(
                    PropertyFactory.lineColor(color),
                    PropertyFactory.lineWidth(6f),
                    PropertyFactory.lineCap(Property.LINE_CAP_ROUND),
                    PropertyFactory.lineJoin(Property.LINE_JOIN_ROUND),
                ),
            )
        }
        val lines = segments.filter { it.size >= 2 }.map { seg -> seg.map { it.toPoint() } }
        track.setGeoJson(MultiLineString.fromLngLats(lines))

        val all = segments.flatten()
        listOf("$ENDS-start" to all.firstOrNull(), "$ENDS-finish" to all.lastOrNull()?.takeIf { all.size > 1 })
            .forEach { (id, point) ->
                val source = style.source(id)
                if (style.getLayer(id) == null) {
                    style.addLayer(
                        CircleLayer(id, id).withProperties(
                            PropertyFactory.circleRadius(8f),
                            PropertyFactory.circleColor(if (id.endsWith("start")) start else finish),
                            PropertyFactory.circleStrokeColor(android.graphics.Color.WHITE),
                            PropertyFactory.circleStrokeWidth(3f),
                        ),
                    )
                }
                if (point != null) source.setGeoJson(point.toPoint()) else source.setGeoJson(FeatureCollection.fromFeatures(emptyList()))
            }
    }
}
