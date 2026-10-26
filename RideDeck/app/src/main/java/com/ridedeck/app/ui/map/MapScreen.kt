package com.ridedeck.app.ui.map

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.rememberScrollState
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Flag
import androidx.compose.material.icons.filled.MyLocation
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextField
import androidx.compose.material3.TextFieldDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.LifecycleResumeEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import android.annotation.SuppressLint
import android.view.MotionEvent
import androidx.compose.runtime.mutableDoubleStateOf
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.viewinterop.AndroidView
import com.ridedeck.app.data.maps.MapTiles
import com.ridedeck.app.ui.components.GloveTouchTarget
import com.ridedeck.app.integrations.GoogleMapsLauncher
import com.ridedeck.app.ui.home.SpeedOverlay
import com.ridedeck.core.format.Formatters
import com.ridedeck.core.geo.Geo
import com.ridedeck.core.model.ActivityMode
import com.ridedeck.core.model.GeoPoint
import com.ridedeck.core.routing.RerouteReason
import java.time.LocalTime
import java.time.format.DateTimeFormatter
import java.time.format.FormatStyle

@Composable
fun MapScreen(
    rideLocked: Boolean,
    onOpenPermissions: () -> Unit,
    viewModel: MapViewModel = hiltViewModel(),
) {
    val context = LocalContext.current
    val state by viewModel.uiState.collectAsStateWithLifecycle()
    val focusManager = LocalFocusManager.current
    var showSteps by rememberSaveable { mutableStateOf(false) }

    LifecycleResumeEffect(Unit) {
        viewModel.refreshPermissions()
        onPauseOrDispose { }
    }

    val mapView = rememberOsmMapView()
    // Camera centre, updated as the map moves; drives station filtering and "Shops in this area".
    var centerLat by remember { mutableDoubleStateOf(20.0) }
    var centerLon by remember { mutableDoubleStateOf(0.0) }

    val dark = MaterialTheme.colorScheme.background.luminance() < 0.5f
    val cycleStyle = state.showCycleLanes
    LaunchedEffect(cycleStyle, dark) {
        mapView.applyStyle(if (cycleStyle) MapTiles.CYCLE else MapTiles.STANDARD, dark)
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
    val visibleStations = remember(state.stations, centerLat.toFloat(), centerLon.toFloat()) {
        // Only draw stations near the camera; thousands of markers would make the map sluggish.
        val center = GeoPoint(centerLat, centerLon)
        state.stations
            .asSequence()
            .map { it to Geo.distanceMeters(center, GeoPoint(it.latitude, it.longitude)) }
            .filter { it.second < STATION_RADIUS_M }
            .sortedBy { it.second }
            .take(MAX_STATION_MARKERS)
            .map { it.first }
            .toList()
    }
    val overlays = remember(mapView) { MapOverlays(mapView) }
    LaunchedEffect(state.route, routeColor) { overlays.setRoute(state.route?.polyline, routeColor) }
    LaunchedEffect(state.destination) { overlays.setDestination(state.destination, primary) }
    LaunchedEffect(state.bikeShops, state.showShops) { overlays.setShops(if (state.showShops) state.bikeShops else emptyList()) }
    LaunchedEffect(visibleStations, state.showStations) { overlays.setStations(if (state.showStations) visibleStations else emptyList()) }
    LaunchedEffect(state.myLocation, state.hasLocationPermission) {
        overlays.setMyLocation(state.myLocation.takeIf { state.hasLocationPermission })
    }

    Box(Modifier.fillMaxSize()) {
        AndroidView(
            factory = {
                mapView.apply {
                    addMapListener(
                        org.osmdroid.events.DelayedMapListener(
                            object : org.osmdroid.events.MapListener {
                                override fun onScroll(event: org.osmdroid.events.ScrollEvent?): Boolean {
                                    centerLat = mapCenter.latitude
                                    centerLon = mapCenter.longitude
                                    return false
                                }

                                override fun onZoom(event: org.osmdroid.events.ZoomEvent?): Boolean = onScroll(null)
                            },
                            250,
                        ),
                    )
                    setOnTouchListenerCompat { viewModel.setFollowUser(false) }
                }
            },
            modifier = Modifier.fillMaxSize(),
        )

        // Top overlay: mode toggle, search and layer filters.
        Column(
            Modifier
                .fillMaxWidth()
                .padding(12.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            SearchField(
                query = state.query,
                enabled = !rideLocked,
                searching = state.searching,
                onQueryChange = viewModel::onQueryChange,
                onClear = {
                    viewModel.onQueryChange("")
                    if (state.destination != null) viewModel.clearRoute()
                },
            )
            if (state.suggestions.isNotEmpty() && !rideLocked) {
                Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainerHigh)) {
                    Column {
                        state.suggestions.take(5).forEach { s ->
                            Column(
                                Modifier
                                    .fillMaxWidth()
                                    .heightIn(min = GloveTouchTarget)
                                    .clickable {
                                        focusManager.clearFocus()
                                        viewModel.selectSuggestion(s)
                                    }
                                    .padding(horizontal = 16.dp, vertical = 10.dp),
                            ) {
                                Text(s.name, style = MaterialTheme.typography.titleMedium, maxLines = 1, overflow = TextOverflow.Ellipsis)
                                val secondary = listOfNotNull(
                                    s.detail.takeIf { it.isNotBlank() },
                                    state.myLocation?.let { Formatters.navDistance(Geo.distanceMeters(it, s.location), state.unit) },
                                ).joinToString(" · ")
                                if (secondary.isNotEmpty()) {
                                    Text(secondary, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1)
                                }
                            }
                            HorizontalDivider()
                        }
                        Text(
                            "Search by Photon · © OpenStreetMap contributors",
                            style = MaterialTheme.typography.labelSmall,
                            modifier = Modifier.padding(8.dp).align(Alignment.End),
                        )
                    }
                }
            }
            LayerChips(state = state, rideLocked = rideLocked, viewModel = viewModel, searchHere = {
                viewModel.searchShopsHere(GeoPoint(centerLat, centerLon))
            })
        }

        // Bottom overlay: recentre button, attribution and the navigation card.
        Column(
            Modifier
                .align(Alignment.BottomCenter)
                .fillMaxWidth()
                .padding(12.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            SpeedOverlay(rideLocked = rideLocked, onOpenPermissions = onOpenPermissions)
            Row(verticalAlignment = Alignment.Bottom) {
                // OpenStreetMap's licence requires visible attribution.
                Box(Modifier.weight(1f)) {
                    Text(
                        if (state.showCycleLanes) MapTiles.CYCLE_ATTRIBUTION else MapTiles.STANDARD_ATTRIBUTION,
                        style = MaterialTheme.typography.labelSmall,
                        modifier = Modifier
                            .background(MaterialTheme.colorScheme.surface.copy(alpha = 0.8f))
                            .padding(4.dp),
                    )
                }
                if (!state.followUser && state.myLocation != null) {
                    FloatingActionButton(
                        onClick = { viewModel.setFollowUser(true) },
                        modifier = Modifier.size(GloveTouchTarget),
                    ) {
                        Icon(Icons.Default.MyLocation, contentDescription = "Recentre on my location")
                    }
                }
            }
            if (state.destination != null) {
                NavigationCard(
                    state = state,
                    onGoogleMaps = { state.destination?.let { GoogleMapsLauncher.navigate(context, it.location) } },
                    showSteps = showSteps,
                    onToggleSteps = { showSteps = !showSteps },
                    onEnd = {
                        showSteps = false
                        viewModel.clearRoute()
                    },
                )
            }
            state.message?.let { msg ->
                Surface(
                    color = MaterialTheme.colorScheme.errorContainer,
                    contentColor = MaterialTheme.colorScheme.onErrorContainer,
                    shape = MaterialTheme.shapes.medium,
                    modifier = Modifier.fillMaxWidth().semantics { liveRegion = LiveRegionMode.Polite },
                ) {
                    Row(Modifier.padding(start = 16.dp), verticalAlignment = Alignment.CenterVertically) {
                        Text(msg, modifier = Modifier.weight(1f))
                        IconButton(onClick = viewModel::dismissMessage, modifier = Modifier.size(GloveTouchTarget)) {
                            Icon(Icons.Default.Close, contentDescription = "Dismiss")
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun SearchField(
    query: String,
    enabled: Boolean,
    searching: Boolean,
    onQueryChange: (String) -> Unit,
    onClear: () -> Unit,
) {
    TextField(
        value = query,
        onValueChange = onQueryChange,
        enabled = enabled,
        singleLine = true,
        placeholder = { Text(if (enabled) "Search destination" else "Search locked while moving") },
        leadingIcon = { Icon(Icons.Default.Search, contentDescription = null) },
        trailingIcon = {
            when {
                searching -> CircularProgressIndicator(Modifier.size(24.dp), strokeWidth = 2.dp)
                query.isNotEmpty() -> IconButton(onClick = onClear, modifier = Modifier.size(GloveTouchTarget)) {
                    Icon(Icons.Default.Close, contentDescription = "Clear search and route")
                }
            }
        },
        shape = MaterialTheme.shapes.large,
        colors = TextFieldDefaults.colors(
            focusedIndicatorColor = MaterialTheme.colorScheme.primary,
            unfocusedIndicatorColor = MaterialTheme.colorScheme.outlineVariant,
        ),
        modifier = Modifier
            .fillMaxWidth()
            .heightIn(min = GloveTouchTarget)
            .semantics { contentDescription = "Destination search" },
    )
}

@Composable
private fun LayerChips(
    state: MapUiState,
    rideLocked: Boolean,
    viewModel: MapViewModel,
    searchHere: () -> Unit,
) {
    Row(
        Modifier.horizontalScroll(rememberScrollState()),
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        FilterChip(
            selected = state.showShops,
            onClick = { viewModel.setShowShops(!state.showShops) },
            label = { Text("Bike shops") },
            modifier = Modifier.heightIn(min = 48.dp),
        )
        FilterChip(
            selected = state.showStations,
            onClick = { viewModel.setShowStations(!state.showStations) },
            label = { Text("Bike share") },
            modifier = Modifier.heightIn(min = 48.dp),
        )
        FilterChip(
            selected = state.cycleLanesEnabled,
            onClick = { viewModel.setShowCycleLanes(!state.cycleLanesEnabled) },
            label = { Text("Cycling map") },
            modifier = Modifier.heightIn(min = 48.dp),
        )
        if (state.showStations) {
            FilterChip(
                selected = state.onlyStationsWithBikes,
                onClick = { viewModel.setOnlyWithBikes(!state.onlyStationsWithBikes) },
                label = { Text("Has bikes") },
                modifier = Modifier.heightIn(min = 48.dp),
            )
        }
        if (state.showShops && !rideLocked) {
            FilterChip(
                selected = false,
                onClick = searchHere,
                label = { Text(if (state.loadingShops) "Searching…" else "Shops in this area") },
                modifier = Modifier.heightIn(min = 48.dp),
            )
        }
    }
    if (state.showStations && !state.bikeShareConfigured) {
        Surface(shape = MaterialTheme.shapes.medium, color = MaterialTheme.colorScheme.surfaceContainerHigh) {
            Text(
                "Choose your bike-share system in Settings to see stations.",
                modifier = Modifier.padding(10.dp),
                style = MaterialTheme.typography.bodyMedium,
            )
        }
    }
}

@Composable
private fun NavigationCard(
    state: MapUiState,
    onGoogleMaps: () -> Unit,
    showSteps: Boolean,
    onToggleSteps: () -> Unit,
    onEnd: () -> Unit,
) {
    val route = state.route
    val progress = state.progress
    Card(
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainerHigh),
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            when {
                state.arrived -> Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(Icons.Default.Flag, contentDescription = null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(36.dp))
                    Spacer(Modifier.width(12.dp))
                    Text("You have arrived", style = MaterialTheme.typography.headlineSmall)
                }
                state.routing || route == null -> Row(verticalAlignment = Alignment.CenterVertically) {
                    CircularProgressIndicator(Modifier.size(28.dp))
                    Spacer(Modifier.width(12.dp))
                    Text(
                        when (state.rerouteReason) {
                            RerouteReason.MODE_CHANGED, RerouteReason.OFF_ROUTE -> "Off route, re-routing…"
                            null -> "Finding a cycling route…"
                        },
                        style = MaterialTheme.typography.titleMedium,
                        modifier = Modifier.semantics { liveRegion = LiveRegionMode.Polite },
                    )
                }
                else -> {
                    val stepIndex = progress?.currentStepIndex ?: 0
                    val step = route.steps.getOrNull(stepIndex)
                    if (step != null) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Icon(
                                maneuverIcon(step.maneuver),
                                contentDescription = null,
                                tint = MaterialTheme.colorScheme.primary,
                                modifier = Modifier.size(48.dp),
                            )
                            Spacer(Modifier.width(12.dp))
                            Column(Modifier.weight(1f)) {
                                progress?.let {
                                    Text(
                                        Formatters.navDistance(it.distanceToManeuverM, state.unit),
                                        style = MaterialTheme.typography.headlineSmall,
                                    )
                                }
                                Text(step.instruction, style = MaterialTheme.typography.titleMedium, maxLines = 2, overflow = TextOverflow.Ellipsis)
                            }
                        }
                    }
                    val remainingSec = progress?.remainingDurationSec ?: route.durationSec
                    val remainingM = progress?.remainingDistanceM ?: route.distanceM.toDouble()
                    val eta = LocalTime.now().plusSeconds(remainingSec).format(DateTimeFormatter.ofLocalizedTime(FormatStyle.SHORT))
                    Text(
                        "Arrive $eta · ${remainingSec / 60} min · ${Formatters.navDistance(remainingM, state.unit)}",
                        style = MaterialTheme.typography.bodyLarge,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.semantics {
                            contentDescription = "Arrival at $eta, ${Formatters.spokenDuration(remainingSec)} remaining, " +
                                Formatters.navDistance(remainingM, state.unit)
                        },
                    )
                }
            }
            if (state.googleMapsEnabled && !state.arrived) {
                Button(
                    onClick = onGoogleMaps,
                    modifier = Modifier.fillMaxWidth().heightIn(min = GloveTouchTarget),
                ) { Text("Start in Google Maps") }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                if (route != null && !state.arrived) {
                    OutlinedButton(onClick = onToggleSteps, modifier = Modifier.weight(1f).heightIn(min = GloveTouchTarget)) {
                        Text(if (showSteps) "Hide steps" else "Steps")
                    }
                }
                Button(onClick = onEnd, modifier = Modifier.weight(1f).heightIn(min = GloveTouchTarget)) {
                    Text("End")
                }
            }
            if (showSteps && route != null) {
                LazyColumn(Modifier.heightIn(max = 220.dp)) {
                    itemsIndexed(route.steps) { index, step ->
                        val current = index == (progress?.currentStepIndex ?: 0)
                        Row(
                            Modifier
                                .fillMaxWidth()
                                .padding(vertical = 6.dp),
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Icon(
                                maneuverIcon(step.maneuver),
                                contentDescription = null,
                                tint = if (current) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                            Spacer(Modifier.width(12.dp))
                            Text(
                                step.instruction,
                                modifier = Modifier.weight(1f),
                                style = if (current) MaterialTheme.typography.titleSmall else MaterialTheme.typography.bodyMedium,
                            )
                            Text(Formatters.navDistance(step.distanceM.toDouble(), state.unit), style = MaterialTheme.typography.bodyMedium)
                        }
                    }
                }
            }
        }
    }
}

/** Any drag or pinch on the map means the rider is exploring, so stop following their position. */
@SuppressLint("ClickableViewAccessibility")
private fun org.osmdroid.views.MapView.setOnTouchListenerCompat(onUserGesture: () -> Unit) {
    setOnTouchListener { _, event ->
        if (event.actionMasked == MotionEvent.ACTION_MOVE) onUserGesture()
        false
    }
}

private const val STATION_RADIUS_M = 2_500.0
private const val MAX_STATION_MARKERS = 150
