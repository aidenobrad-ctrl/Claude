package com.ridedeck.app.ui.history

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import android.graphics.Color
import android.graphics.Paint
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.viewinterop.AndroidView
import com.ridedeck.app.data.maps.MapTiles
import com.ridedeck.app.ui.map.applyStyle
import com.ridedeck.app.ui.map.dotIcon
import com.ridedeck.app.ui.map.rememberOsmMapView
import org.osmdroid.util.BoundingBox
import org.osmdroid.views.overlay.Marker
import org.osmdroid.views.overlay.Polyline
import com.ridedeck.app.ui.components.GloveTouchTarget
import com.ridedeck.app.ui.components.StatTile
import com.ridedeck.app.ui.theme.AlertRed
import com.ridedeck.core.format.Formatters
import com.ridedeck.core.format.Pace
import com.ridedeck.core.format.Units
import com.ridedeck.core.model.ActivityMode

@Composable
fun SessionDetailScreen(
    onBack: () -> Unit,
    viewModel: SessionDetailViewModel = hiltViewModel(),
) {
    val state by viewModel.uiState.collectAsStateWithLifecycle()
    var confirmDelete by rememberSaveable { mutableStateOf(false) }

    LaunchedEffect(state.deleted) { if (state.deleted) onBack() }

    val session = state.session
    val unit = state.unit
    val routeColor = MaterialTheme.colorScheme.primary.toArgb()
    val dark = MaterialTheme.colorScheme.background.luminance() < 0.5f
    val mapView = rememberOsmMapView()
    val allPoints = remember(state.segments) {
        state.segments.flatten().map { org.osmdroid.util.GeoPoint(it.latitude, it.longitude) }
    }

    LaunchedEffect(dark) { mapView.applyStyle(MapTiles.STANDARD, dark) }
    LaunchedEffect(state.segments, routeColor) {
        mapView.overlays.clear()
        val density = mapView.context.resources.displayMetrics.density
        state.segments.forEach { segment ->
            mapView.overlays.add(
                Polyline(mapView).apply {
                    setPoints(segment.map { org.osmdroid.util.GeoPoint(it.latitude, it.longitude) })
                    outlinePaint.color = routeColor
                    outlinePaint.strokeWidth = 6f * density
                    outlinePaint.strokeCap = Paint.Cap.ROUND
                    outlinePaint.strokeJoin = Paint.Join.ROUND
                    infoWindow = null
                },
            )
        }
        allPoints.firstOrNull()?.let { start ->
            mapView.overlays.add(
                Marker(mapView).apply {
                    position = start
                    title = "Start"
                    icon = dotIcon(mapView.context, Color.rgb(0x2E, 0xB8, 0x5C))
                    setAnchor(Marker.ANCHOR_CENTER, Marker.ANCHOR_CENTER)
                },
            )
        }
        allPoints.lastOrNull()?.takeIf { allPoints.size > 1 }?.let { end ->
            mapView.overlays.add(
                Marker(mapView).apply {
                    position = end
                    title = "Finish"
                    icon = dotIcon(mapView.context, Color.rgb(0xE5, 0x39, 0x35))
                    setAnchor(Marker.ANCHOR_CENTER, Marker.ANCHOR_CENTER)
                },
            )
        }
        when {
            allPoints.isEmpty() -> Unit
            allPoints.size == 1 -> {
                mapView.controller.setZoom(16.0)
                mapView.controller.setCenter(allPoints.first())
            }
            // Zooming to bounds needs the view laid out; post runs after the first layout pass.
            else -> mapView.post {
                mapView.zoomToBoundingBox(BoundingBox.fromGeoPointsSafe(allPoints).increaseByScale(1.2f), false, 48)
            }
        }
        mapView.invalidate()
    }

    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState())) {
        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(4.dp)) {
            IconButton(onClick = onBack, modifier = Modifier.heightIn(min = GloveTouchTarget)) {
                Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Back")
            }
            Text(
                session?.let { formatDateTime(it.startedAtMs) } ?: "",
                style = MaterialTheme.typography.titleLarge,
                modifier = Modifier.weight(1f),
            )
            IconButton(onClick = { confirmDelete = true }, modifier = Modifier.heightIn(min = GloveTouchTarget)) {
                Icon(Icons.Default.Delete, contentDescription = "Delete session")
            }
        }

        Box(
            Modifier
                .fillMaxWidth()
                .height(320.dp)
                .semantics { contentDescription = "Map of the recorded route" },
        ) {
            AndroidView(factory = { mapView }, modifier = Modifier.fillMaxSize())
            Text(
                MapTiles.STANDARD_ATTRIBUTION,
                style = MaterialTheme.typography.labelSmall,
                modifier = Modifier
                    .align(Alignment.BottomStart)
                    .background(MaterialTheme.colorScheme.surface.copy(alpha = 0.8f))
                    .padding(4.dp),
            )
            if (state.segments.isEmpty()) {
                Text(
                    "No route recorded",
                    modifier = Modifier.align(Alignment.Center),
                    style = MaterialTheme.typography.bodyLarge,
                )
            }
        }

        if (session != null) {
            val walk = session.mode == ActivityMode.WALK
            val stats = buildList {
                add(Triple("Distance", Formatters.distance(session.distanceM, unit), Units.distanceLabel(unit)))
                add(Triple("Moving time", Formatters.duration(session.movingTimeMs), null))
                add(Triple("Elapsed", Formatters.duration(session.elapsedTimeMs), null))
                add(Triple("Max speed", Formatters.speed(session.maxSpeedMps, unit, 1), Units.speedLabel(unit)))
                add(Triple("Avg speed", Formatters.speed(session.avgSpeedMps, unit, 1), Units.speedLabel(unit)))
                if (walk) {
                    add(Triple("Avg pace", Pace.format(Pace.averageSecondsPerUnit(session.distanceM, session.movingTimeMs, unit)), Units.paceLabel(unit)))
                }
                if (session.bikeDistanceM > 0 && session.walkDistanceM > 0) {
                    add(Triple("Biked", Formatters.distance(session.bikeDistanceM, unit), Units.distanceLabel(unit)))
                    add(Triple("Walked", Formatters.distance(session.walkDistanceM, unit), Units.distanceLabel(unit)))
                }
            }
            Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Text(
                    if (walk) "Walk" else "Bike ride",
                    style = MaterialTheme.typography.headlineSmall,
                )
                stats.chunked(2).forEach { row ->
                    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        row.forEach { (label, value, u) -> StatTile(label, value, u, Modifier.weight(1f)) }
                        if (row.size == 1) Box(Modifier.weight(1f))
                    }
                }
            }
        }
    }

    if (confirmDelete) {
        AlertDialog(
            onDismissRequest = { confirmDelete = false },
            title = { Text("Delete this session?") },
            text = { Text("Its distance will also be removed from your odometer. This can't be undone.") },
            confirmButton = {
                Button(
                    onClick = {
                        confirmDelete = false
                        viewModel.delete()
                    },
                    colors = ButtonDefaults.buttonColors(containerColor = AlertRed),
                    modifier = Modifier.heightIn(min = GloveTouchTarget),
                ) { Text("Delete") }
            },
            dismissButton = {
                OutlinedButton(onClick = { confirmDelete = false }, modifier = Modifier.heightIn(min = GloveTouchTarget)) {
                    Text("Cancel")
                }
            },
        )
    }
}
