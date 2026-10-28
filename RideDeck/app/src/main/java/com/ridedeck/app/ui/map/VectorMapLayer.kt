package com.ridedeck.app.ui.map

import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.viewinterop.AndroidView
import com.ridedeck.app.data.maps.MapStyles
import com.ridedeck.core.geo.Geo
import com.ridedeck.core.model.GeoPoint
import org.maplibre.android.camera.CameraPosition
import org.maplibre.android.camera.CameraUpdateFactory
import org.maplibre.android.maps.MapLibreMap

/**
 * The Ride screen map: OpenFreeMap vector tiles drawn by MapLibre, with the route, destination
 * and rider. While following, the camera stays on the rider; while navigating it also tilts and
 * turns to the direction of travel, like a car navigation app.
 */
@Composable
fun VectorMapLayer(state: MapUiState, onUserGesture: () -> Unit) {
    val vm = rememberVectorMap()
    val gesture by rememberUpdatedState(onUserGesture)

    val dark = MaterialTheme.colorScheme.background.luminance() < 0.5f
    LaunchedEffect(state.mapStyle, dark) { vm.setStyleUrl(MapStyles.url(state.mapStyle, dark)) }

    LaunchedEffect(vm.map) {
        vm.map?.addOnCameraMoveStartedListener { reason ->
            if (reason == MapLibreMap.OnCameraMoveStartedListener.REASON_API_GESTURE) gesture()
        }
    }

    val accent = MaterialTheme.colorScheme.primary.toArgb()
    val casing = if (dark) 0xFF00344D.toInt() else 0xFF005A80.toInt()
    val destination = 0xFFFF5252.toInt()
    val style = vm.style
    LaunchedEffect(style, state.route, accent) { style?.let { VectorOverlays.setRoute(it, state.route?.polyline, accent, casing) } }
    LaunchedEffect(style, state.destination) { style?.let { VectorOverlays.setDestination(it, state.destination?.location, destination) } }
    LaunchedEffect(style, state.myLocation, state.hasLocationPermission) {
        style?.let { VectorOverlays.setMyLocation(it, state.myLocation.takeIf { state.hasLocationPermission }, accent) }
    }

    // Direction of travel from the last two positions far enough apart to be meaningful.
    var lastPoint by remember { mutableStateOf<GeoPoint?>(null) }
    var heading by remember { mutableStateOf(0.0) }
    LaunchedEffect(state.myLocation) {
        val loc = state.myLocation ?: return@LaunchedEffect
        val prev = lastPoint
        if (prev == null || Geo.distanceMeters(prev, loc) >= 6.0) {
            if (prev != null) heading = Geo.bearingDegrees(prev, loc)
            lastPoint = loc
        }
    }

    val navigating = state.route != null && !state.arrived
    LaunchedEffect(vm.map, state.myLocation, state.followUser, navigating, heading) {
        val map = vm.map ?: return@LaunchedEffect
        val loc = state.myLocation ?: return@LaunchedEffect
        if (!state.followUser) return@LaunchedEffect
        val current = map.cameraPosition
        val position = CameraPosition.Builder()
            .target(loc.toLatLng())
            .zoom(if (navigating) 17.0 else maxOf(current.zoom, 15.5))
            .tilt(if (navigating) 50.0 else 0.0)
            .bearing(if (navigating) heading else current.bearing)
            .build()
        map.animateCamera(CameraUpdateFactory.newCameraPosition(position), 700)
    }

    AndroidView(factory = { vm.view }, modifier = Modifier.fillMaxSize())
}
