package com.ridedeck.app.ui.history

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
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
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.DirectionsBike
import androidx.compose.material.icons.automirrored.filled.DirectionsWalk
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.ridedeck.app.data.local.SessionEntity
import com.ridedeck.app.ui.components.StatTile
import com.ridedeck.core.format.Formatters
import com.ridedeck.core.format.Pace
import com.ridedeck.core.format.Units
import com.ridedeck.core.model.ActivityMode
import com.ridedeck.core.model.SpeedUnit
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.time.format.FormatStyle

@Composable
fun HistoryScreen(
    onOpenSession: (Long) -> Unit,
    viewModel: HistoryViewModel = hiltViewModel(),
) {
    val state by viewModel.uiState.collectAsStateWithLifecycle()
    val unit = state.unit

    LazyColumn(
        Modifier.fillMaxSize(),
        contentPadding = androidx.compose.foundation.layout.PaddingValues(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        item {
            Text("History", style = MaterialTheme.typography.headlineMedium, modifier = Modifier.semantics { heading() })
        }
        item {
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                StatTile(
                    "Odometer",
                    Formatters.distance(state.totals.bikeDistanceM, unit, 1),
                    Units.distanceLabel(unit),
                    Modifier.weight(1f),
                )
                StatTile(
                    "Top speed",
                    Formatters.speed(state.totals.bikeMaxSpeedMps, unit, 1),
                    Units.speedLabel(unit),
                    Modifier.weight(1f),
                )
            }
        }
        if (!state.loading && state.sessions.isEmpty()) {
            item {
                Text(
                    "No rides yet. Tap Start on the speedometer to record one.",
                    style = MaterialTheme.typography.bodyLarge,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
        items(state.sessions, key = { it.id }) { session ->
            SessionRow(session, unit, onClick = { onOpenSession(session.id) })
        }
    }
}

@Composable
private fun SessionRow(session: SessionEntity, unit: SpeedUnit, onClick: () -> Unit) {
    val bike = session.mode == ActivityMode.BIKE
    val date = formatDateTime(session.startedAtMs)
    val distance = "${Formatters.distance(session.distanceM, unit)} ${Units.distanceLabel(unit)}"
    val secondary = if (bike) {
        "avg ${Formatters.speed(session.avgSpeedMps, unit, 1)} · max ${Formatters.speed(session.maxSpeedMps, unit, 1)} ${Units.speedLabel(unit)}"
    } else {
        "pace ${Pace.format(Pace.averageSecondsPerUnit(session.distanceM, session.movingTimeMs, unit))} ${Units.paceLabel(unit)}"
    }
    Card(
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainerHigh),
        modifier = Modifier
            .fillMaxWidth()
            .clickable(role = Role.Button, onClickLabel = "Open session details", onClick = onClick),
    ) {
        Row(Modifier.padding(16.dp).heightIn(min = 56.dp), verticalAlignment = Alignment.CenterVertically) {
            Icon(
                if (bike) Icons.AutoMirrored.Filled.DirectionsBike else Icons.AutoMirrored.Filled.DirectionsWalk,
                contentDescription = if (bike) "Bike ride" else "Walk",
                tint = MaterialTheme.colorScheme.primary,
                modifier = Modifier.size(32.dp),
            )
            Spacer(Modifier.width(14.dp))
            Column(Modifier.weight(1f)) {
                Text(date, style = MaterialTheme.typography.titleMedium)
                Text(secondary, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            Column(horizontalAlignment = Alignment.End) {
                Text(distance, style = MaterialTheme.typography.titleMedium)
                Text(Formatters.duration(session.movingTimeMs), style = MaterialTheme.typography.bodyMedium)
            }
        }
    }
}

fun formatDateTime(epochMs: Long): String =
    DateTimeFormatter.ofLocalizedDateTime(FormatStyle.MEDIUM, FormatStyle.SHORT)
        .format(Instant.ofEpochMilli(epochMs).atZone(ZoneId.systemDefault()))
