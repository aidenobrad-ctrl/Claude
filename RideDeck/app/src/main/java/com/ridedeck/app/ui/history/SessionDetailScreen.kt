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
import com.ridedeck.app.data.maps.MapStyles
import com.ridedeck.app.data.prefs.MapStyle
import com.ridedeck.app.ui.map.MAP_ATTRIBUTION
import com.ridedeck.app.ui.map.VectorOverlays
import com.ridedeck.app.ui.map.rememberVectorMap
import com.ridedeck.app.ui.map.toLatLng
import com.ridedeck.core.model.GeoPoint
import org.maplibre.android.camera.CameraUpdateFactory
import org.maplibre.android.geometry.LatLngBounds
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.viewinterop.AndroidView
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
    val vectorMap = rememberVectorMap()
    val segments = remember(state.segments) {
        state.segments.map { seg -> seg.map { GeoPoint(it.latitude, it.longitude) } }
    }
    val allPoints = remember(segments) { segments.flatten() }

    LaunchedEffect(dark) { vectorMap.setStyleUrl(MapStyles.url(MapStyle.AUTO, dark)) }
    LaunchedEffect(vectorMap.style, segments, routeColor) {
        val style = vectorMap.style ?: return@LaunchedEffect
        VectorOverlays.setTrack(style, segments, routeColor, start = 0xFF2EB85C.toInt(), finish = 0xFFE53935.toInt())
    }
    LaunchedEffect(vectorMap.map, allPoints) {
        val map = vectorMap.map ?: return@LaunchedEffect
        when {
            allPoints.isEmpty() -> Unit
            allPoints.size == 1 -> map.moveCamera(CameraUpdateFactory.newLatLngZoom(allPoints.first().toLatLng(), 16.0))
            // Fitting bounds needs the view laid out; post runs after the first layout pass.
            else -> vectorMap.view.post {
                val bounds = LatLngBounds.Builder().includes(allPoints.map { it.toLatLng() }).build()
                map.moveCamera(CameraUpdateFactory.newLatLngBounds(bounds, 64))
            }
        }
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
                // A fixed 320 dp would fill the whole screen in landscape; scale with the window.
                .height(minOf(320.dp, (LocalConfiguration.current.screenHeightDp * 0.45f).dp))
                .semantics { contentDescription = "Map of the recorded route" },
        ) {
            AndroidView(factory = { vectorMap.view }, modifier = Modifier.fillMaxSize())
            Text(
                MAP_ATTRIBUTION,
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
