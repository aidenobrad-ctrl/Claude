package com.ridedeck.app.ui.map

import android.annotation.SuppressLint
import android.view.MotionEvent
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.viewinterop.AndroidView
import com.ridedeck.app.data.maps.MapTiles

/** OpenStreetMap (osmdroid) map with the route, destination and rider position. */
@Composable
fun OsmMapLayer(state: MapUiState, onUserGesture: () -> Unit) {
    val mapView = rememberOsmMapView()
    val gesture by rememberUpdatedState(onUserGesture)

    val dark = MaterialTheme.colorScheme.background.luminance() < 0.5f
    LaunchedEffect(state.cycleMapStyle, dark) {
        mapView.applyStyle(if (state.cycleMapStyle) MapTiles.CYCLE else MapTiles.STANDARD, dark)
    }

    // Follow the rider until they pan the map themselves.
    LaunchedEffect(state.myLocation, state.followUser) {
        val loc = state.myLocation ?: return@LaunchedEffect
        if (state.followUser) {
            val zoom = mapView.zoomLevelDouble.coerceAtLeast(16.0)
            mapView.controller.animateTo(loc.toOsm(), zoom, 600L)
        }
    }

    val routeColor = MaterialTheme.colorScheme.secondary.toArgb()
    val primary = MaterialTheme.colorScheme.primary.toArgb()
    val overlays = remember(mapView) { MapOverlays(mapView) }
    LaunchedEffect(state.route, routeColor) { overlays.setRoute(state.route?.polyline, routeColor) }
    LaunchedEffect(state.destination) { overlays.setDestination(state.destination, primary) }
    LaunchedEffect(state.myLocation, state.hasLocationPermission) {
        overlays.setMyLocation(state.myLocation.takeIf { state.hasLocationPermission })
    }

    AndroidView(
        factory = { mapView.apply { setOnTouchListenerCompat { gesture() } } },
        modifier = Modifier.fillMaxSize(),
    )
}

/** Any drag or pinch on the map means the rider is exploring, so stop following their position. */
@SuppressLint("ClickableViewAccessibility")
private fun org.osmdroid.views.MapView.setOnTouchListenerCompat(onUserGesture: () -> Unit) {
    setOnTouchListener { _, event ->
        if (event.actionMasked == MotionEvent.ACTION_MOVE) onUserGesture()
        false
    }
}
