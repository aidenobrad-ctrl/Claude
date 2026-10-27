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
import androidx.compose.material.icons.filled.KeyboardArrowDown
import androidx.compose.material.icons.filled.KeyboardArrowUp
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
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.unit.IntSize
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.LifecycleResumeEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.ridedeck.app.integrations.GoogleMapsLauncher
import com.ridedeck.app.ui.components.GloveTouchTarget
import com.ridedeck.app.ui.home.FloatingSpeedOverlay
import com.ridedeck.core.format.Formatters
import com.ridedeck.core.geo.Geo
import com.ridedeck.core.routing.RerouteReason
import java.time.LocalTime
import java.time.format.DateTimeFormatter
import java.time.format.FormatStyle

/**
 * The map view: search, directions and the rider's position on either OpenStreetMap or the
 * built-in Google map. The speedometer floats over it when [showSpeedOverlay] is on.
 */
@Composable
fun MapScreen(
    rideLocked: Boolean,
    showSpeedOverlay: Boolean,
    onOpenPermissions: () -> Unit,
    viewModel: MapViewModel = hiltViewModel(),
) {
    val context = LocalContext.current
    val state by viewModel.uiState.collectAsStateWithLifecycle()
    val focusManager = LocalFocusManager.current
    var showSteps by rememberSaveable { mutableStateOf(false) }
    var navCollapsed by rememberSaveable { mutableStateOf(false) }

    LifecycleResumeEffect(Unit) {
        viewModel.refreshPermissions()
        onPauseOrDispose { }
    }

    var area by remember { mutableStateOf(IntSize.Zero) }
    var bottomHeight by remember { mutableIntStateOf(0) }

    Box(Modifier.fillMaxSize().onSizeChanged { area = it }) {
        VectorMapLayer(state, onUserGesture = { viewModel.setFollowUser(false) })

        // Bottom overlay: recentre button, attribution and the navigation card.
        Column(
            Modifier
                .align(Alignment.BottomCenter)
                .fillMaxWidth()
                .onSizeChanged { bottomHeight = it.height }
                .padding(horizontal = 12.dp, vertical = 8.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Row(verticalAlignment = Alignment.Bottom) {
                Box(Modifier.weight(1f)) {
                    // The map data licences require visible attribution.
                    Text(
                        MAP_ATTRIBUTION,
                        style = MaterialTheme.typography.labelSmall,
                        modifier = Modifier
                            .background(MaterialTheme.colorScheme.surface.copy(alpha = 0.8f), MaterialTheme.shapes.extraSmall)
                            .padding(horizontal = 6.dp, vertical = 2.dp),
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
                    onRetry = viewModel::retryRoute,
                    showSteps = showSteps,
                    onToggleSteps = { showSteps = !showSteps },
                    collapsed = navCollapsed,
                    onToggleCollapsed = { navCollapsed = !navCollapsed },
                    onEnd = {
                        showSteps = false
                        navCollapsed = false
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

        // The speedometer floats above the map and bottom controls; drag it anywhere.
        if (showSpeedOverlay) {
            FloatingSpeedOverlay(
                rideLocked = rideLocked,
                onOpenPermissions = onOpenPermissions,
                area = area,
                bottomReservedPx = bottomHeight,
            )
        }

        // Top overlay: search and map style.
        Column(
            Modifier
                .fillMaxWidth()
                .padding(horizontal = 12.dp, vertical = 8.dp),
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
private fun NavigationCard(
    state: MapUiState,
    onGoogleMaps: () -> Unit,
    onRetry: () -> Unit,
    showSteps: Boolean,
    onToggleSteps: () -> Unit,
    collapsed: Boolean,
    onToggleCollapsed: () -> Unit,
    onEnd: () -> Unit,
) {
    val route = state.route
    val progress = state.progress
    // Collapsing keeps only the next turn and arrival; the buttons and steps come back on expand.
    val canCollapse = route != null && !state.arrived && !state.routing
    val compact = collapsed && canCollapse
    Card(
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainerHigh),
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(verticalAlignment = Alignment.Top) {
                Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    when {
                        state.arrived -> Row(verticalAlignment = Alignment.CenterVertically) {
                            Icon(Icons.Default.Flag, contentDescription = null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(36.dp))
                            Spacer(Modifier.width(12.dp))
                            Text("You have arrived", style = MaterialTheme.typography.headlineSmall)
                        }
                        route == null && !state.routing -> Row(verticalAlignment = Alignment.CenterVertically) {
                            Text(
                                "No route yet.",
                                style = MaterialTheme.typography.titleMedium,
                                modifier = Modifier.weight(1f),
                            )
                            OutlinedButton(onClick = onRetry, modifier = Modifier.heightIn(min = GloveTouchTarget)) { Text("Try again") }
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
                            // Each OSRM step's instruction happens at its start, so the turn ahead is the
                            // next step's (the last step is the arrival).
                            val stepIndex = progress?.currentStepIndex ?: 0
                            val step = route.steps.getOrNull(stepIndex + 1) ?: route.steps.getOrNull(stepIndex)
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
                }
                if (canCollapse) {
                    IconButton(onClick = onToggleCollapsed, modifier = Modifier.size(GloveTouchTarget)) {
                        Icon(
                            if (compact) Icons.Default.KeyboardArrowUp else Icons.Default.KeyboardArrowDown,
                            contentDescription = if (compact) "Show navigation buttons" else "Hide navigation buttons",
                        )
                    }
                }
            }
            if (compact) return@Column
            if (state.googleMapsHandOff && !state.arrived) {
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
                        val current = index == (progress?.currentStepIndex ?: 0) + 1
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
