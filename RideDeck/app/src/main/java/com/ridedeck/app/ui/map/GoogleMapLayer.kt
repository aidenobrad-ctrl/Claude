package com.ridedeck.app.ui.map

import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.luminance
import com.google.android.gms.maps.CameraUpdateFactory
import com.google.android.gms.maps.model.CameraPosition
import com.google.android.gms.maps.model.JointType
import com.google.android.gms.maps.model.LatLng
import com.google.android.gms.maps.model.MapStyleOptions
import com.google.android.gms.maps.model.RoundCap
import com.google.maps.android.compose.CameraMoveStartedReason
import com.google.maps.android.compose.GoogleMap
import com.google.maps.android.compose.MapProperties
import com.google.maps.android.compose.MapUiSettings
import com.google.maps.android.compose.Marker
import com.google.maps.android.compose.MarkerState
import com.google.maps.android.compose.Polyline
import com.google.maps.android.compose.rememberCameraPositionState

/**
 * Built-in Google map (Maps SDK for Android). Only used when the rider picks Google in Settings
 * and the APK was built with a Maps API key. Routing and search still come from OSRM and Photon.
 */
@Composable
fun GoogleMapLayer(state: MapUiState, onUserGesture: () -> Unit) {
    val gesture by rememberUpdatedState(onUserGesture)
    val cameraPositionState = rememberCameraPositionState {
        position = CameraPosition.fromLatLngZoom(LatLng(20.0, 0.0), 2f)
    }

    LaunchedEffect(state.myLocation, state.followUser) {
        val loc = state.myLocation ?: return@LaunchedEffect
        if (state.followUser) {
            val zoom = cameraPositionState.position.zoom.coerceAtLeast(16f)
            cameraPositionState.animate(CameraUpdateFactory.newLatLngZoom(LatLng(loc.latitude, loc.longitude), zoom), 600)
        }
    }
    LaunchedEffect(cameraPositionState.isMoving) {
        if (cameraPositionState.isMoving && cameraPositionState.cameraMoveStartedReason == CameraMoveStartedReason.GESTURE) {
            gesture()
        }
    }

    val dark = MaterialTheme.colorScheme.background.luminance() < 0.5f
    val style = remember(dark) { if (dark) MapStyleOptions(DARK_STYLE) else null }
    val routeColor = MaterialTheme.colorScheme.secondary

    GoogleMap(
        modifier = Modifier.fillMaxSize(),
        cameraPositionState = cameraPositionState,
        properties = MapProperties(isMyLocationEnabled = state.hasLocationPermission, mapStyleOptions = style),
        uiSettings = MapUiSettings(myLocationButtonEnabled = false, zoomControlsEnabled = false, mapToolbarEnabled = false),
    ) {
        state.route?.let { route ->
            Polyline(
                points = route.polyline.map { LatLng(it.latitude, it.longitude) },
                color = routeColor,
                width = 18f,
                jointType = JointType.ROUND,
                startCap = RoundCap(),
                endCap = RoundCap(),
            )
        }
        state.destination?.let { dest ->
            val markerState = remember(dest.id) { MarkerState(LatLng(dest.location.latitude, dest.location.longitude)) }
            Marker(state = markerState, title = dest.name, snippet = dest.detail)
        }
    }
}

/** A compact night style so the map doesn't glare in the dark theme. */
private const val DARK_STYLE = """
[
  {"elementType":"geometry","stylers":[{"color":"#1d2c4d"}]},
  {"elementType":"labels.text.fill","stylers":[{"color":"#8ec3b9"}]},
  {"elementType":"labels.text.stroke","stylers":[{"color":"#1a3646"}]},
  {"featureType":"poi","elementType":"geometry","stylers":[{"color":"#283d6a"}]},
  {"featureType":"poi.park","elementType":"geometry.fill","stylers":[{"color":"#023e58"}]},
  {"featureType":"road","elementType":"geometry","stylers":[{"color":"#304a7d"}]},
  {"featureType":"road","elementType":"labels.text.fill","stylers":[{"color":"#98a5be"}]},
  {"featureType":"road.highway","elementType":"geometry","stylers":[{"color":"#2c6675"}]},
  {"featureType":"transit","elementType":"geometry","stylers":[{"color":"#2f3948"}]},
  {"featureType":"water","elementType":"geometry","stylers":[{"color":"#0e1626"}]}
]
"""
