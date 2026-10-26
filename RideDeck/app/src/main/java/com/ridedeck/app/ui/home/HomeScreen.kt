package com.ridedeck.app.ui.home

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Pause
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.Stop
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.LifecycleResumeEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.ridedeck.app.R
import com.ridedeck.app.ui.components.BigActionButton
import com.ridedeck.app.ui.components.GloveTouchTarget
import com.ridedeck.app.ui.components.ModeToggle
import com.ridedeck.app.ui.components.StatTile
import com.ridedeck.app.ui.theme.AlertRed
import com.ridedeck.app.ui.theme.GoGreen
import com.ridedeck.core.format.Formatters
import com.ridedeck.core.format.Pace
import com.ridedeck.core.format.Units
import com.ridedeck.core.model.ActivityMode
import com.ridedeck.core.tracking.TripStatus

@Composable
fun HomeScreen(
    rideLocked: Boolean,
    onOpenPermissions: () -> Unit,
    viewModel: HomeViewModel = hiltViewModel(),
) {
    val state by viewModel.uiState.collectAsStateWithLifecycle()
    var confirmStop by rememberSaveable { mutableStateOf(false) }

    LifecycleResumeEffect(Unit) {
        viewModel.refreshPermissions()
        onPauseOrDispose { }
    }

    val unit = state.unit
    val snap = state.snapshot
    val speedMps = state.displaySpeedMps
    val pacePrimary = state.profile.showPaceAsPrimary
    val speedText = Formatters.speed(speedMps, unit, decimals = if (pacePrimary) 1 else 0)
    val paceText = Pace.format(Pace.secondsPerUnit(speedMps, unit))
    val overLimit = state.mode == ActivityMode.BIKE && state.speedAlertEnabled && speedMps > state.speedAlertLimitMps
    val gaugeMaxMps = if (state.mode == ActivityMode.BIKE) 60.0 / 3.6 else 8.0 / 3.6

    Column(
        Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 16.dp, vertical = 12.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        ModeToggle(mode = state.mode, onModeChange = viewModel::setMode)

        Row(verticalAlignment = Alignment.CenterVertically) {
            StatusChip(snap.status, state.isNewRecord)
            Row(Modifier.weight(1f), horizontalArrangement = Arrangement.End) {
                TextButton(
                    onClick = viewModel::toggleUnit,
                    modifier = Modifier.heightIn(min = 48.dp),
                ) {
                    Text("Units: ${Units.speedLabel(unit)}", style = MaterialTheme.typography.labelLarge)
                }
            }
        }

        Speedometer(
            primaryValue = if (pacePrimary) paceText else speedText,
            primaryUnit = if (pacePrimary) Units.paceLabel(unit) else Units.speedLabel(unit),
            secondaryText = if (pacePrimary) "$speedText ${Units.speedLabel(unit)}" else null,
            gaugeFraction = (speedMps / gaugeMaxMps).toFloat(),
            limitFraction = if (state.mode == ActivityMode.BIKE && state.speedAlertEnabled) {
                (state.speedAlertLimitMps / gaugeMaxMps).toFloat()
            } else {
                null
            },
            overLimit = overLimit,
            spokenDescription = if (pacePrimary) {
                "Pace $paceText ${Units.paceLabel(unit)}, speed $speedText ${Units.speedLabel(unit)}"
            } else {
                "Speed $speedText ${Units.speedLabel(unit)}" + if (overLimit) ", over your speed limit" else ""
            },
        )

        if (!state.hasLocationPermission) {
            OutlinedButton(
                onClick = onOpenPermissions,
                modifier = Modifier.fillMaxWidth().heightIn(min = GloveTouchTarget),
            ) { Text("Precise location is off. Tap to allow it.") }
        }

        Controls(
            status = snap.status,
            onStart = { if (!viewModel.start()) onOpenPermissions() },
            onPause = viewModel::pause,
            onResume = viewModel::resume,
            onStop = { confirmStop = true },
        )

        StatsGrid(state)

        if (state.sessionActive && !rideLocked) {
            TextButton(onClick = viewModel::resetMax, modifier = Modifier.heightIn(min = 48.dp)) {
                Text(stringResource(R.string.reset_max))
            }
        }
    }

    if (confirmStop) {
        AlertDialog(
            onDismissRequest = { confirmStop = false },
            title = { Text("Finish this session?") },
            text = { Text("It will be saved to your history.") },
            confirmButton = {
                Button(
                    onClick = {
                        confirmStop = false
                        viewModel.stop()
                    },
                    colors = ButtonDefaults.buttonColors(containerColor = AlertRed),
                    modifier = Modifier.heightIn(min = GloveTouchTarget),
                ) { Text("Finish") }
            },
            dismissButton = {
                OutlinedButton(
                    onClick = { confirmStop = false },
                    modifier = Modifier.heightIn(min = GloveTouchTarget),
                ) { Text("Keep going") }
            },
        )
    }
}

@Composable
private fun StatusChip(status: TripStatus, newRecord: Boolean) {
    val text = when {
        newRecord -> stringResource(R.string.status_new_record)
        status == TripStatus.AUTO_PAUSED -> stringResource(R.string.status_auto_paused)
        status == TripStatus.PAUSED -> stringResource(R.string.status_paused)
        status == TripStatus.RUNNING -> "Recording"
        else -> "Ready"
    }
    val highlight = newRecord || status == TripStatus.AUTO_PAUSED || status == TripStatus.PAUSED
    Surface(
        shape = MaterialTheme.shapes.small,
        color = if (highlight) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.surfaceContainerHighest,
        contentColor = if (highlight) MaterialTheme.colorScheme.onPrimary else MaterialTheme.colorScheme.onSurface,
    ) {
        Text(text, style = MaterialTheme.typography.labelLarge, modifier = Modifier.padding(horizontal = 12.dp, vertical = 8.dp))
    }
}

@Composable
private fun Controls(
    status: TripStatus,
    onStart: () -> Unit,
    onPause: () -> Unit,
    onResume: () -> Unit,
    onStop: () -> Unit,
) {
    when (status) {
        TripStatus.IDLE -> BigActionButton(
            text = stringResource(R.string.action_start),
            icon = Icons.Default.PlayArrow,
            onClick = onStart,
            containerColor = GoGreen,
            modifier = Modifier.fillMaxWidth(),
        )
        else -> Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            if (status == TripStatus.PAUSED) {
                BigActionButton(
                    text = stringResource(R.string.action_resume),
                    icon = Icons.Default.PlayArrow,
                    onClick = onResume,
                    containerColor = GoGreen,
                    modifier = Modifier.weight(1f),
                )
            } else {
                BigActionButton(
                    text = stringResource(R.string.action_pause),
                    icon = Icons.Default.Pause,
                    onClick = onPause,
                    modifier = Modifier.weight(1f),
                )
            }
            BigActionButton(
                text = stringResource(R.string.action_stop),
                icon = Icons.Default.Stop,
                onClick = onStop,
                containerColor = AlertRed,
                modifier = Modifier.weight(1f),
            )
        }
    }
}

@Composable
private fun StatsGrid(state: HomeUiState) {
    val unit = state.unit
    val snap = state.snapshot
    val walk = state.mode == ActivityMode.WALK
    val tiles = listOf(
        Tile(
            stringResource(R.string.stat_max),
            Formatters.speed(snap.maxSpeedMps, unit, if (walk) 1 else 0),
            Units.speedLabel(unit),
        ),
        if (walk) {
            Tile(stringResource(R.string.stat_avg), Pace.format(Pace.averageSecondsPerUnit(snap.distanceM, snap.movingTimeMs, unit)), Units.paceLabel(unit))
        } else {
            Tile(stringResource(R.string.stat_avg), Formatters.speed(snap.averageSpeedMps, unit, 1), Units.speedLabel(unit))
        },
        Tile(stringResource(R.string.stat_distance), Formatters.distance(snap.distanceM, unit), Units.distanceLabel(unit)),
        Tile(
            stringResource(R.string.stat_time),
            Formatters.duration(snap.movingTimeMs),
            null,
            spoken = Formatters.spokenDuration(snap.movingTimeMs / 1000),
        ),
        Tile(
            "${stringResource(R.string.stat_odometer)} (${if (walk) "walk" else "bike"})",
            Formatters.distance(state.odometerM, unit, 1),
            Units.distanceLabel(unit),
        ),
        Tile(
            "${stringResource(R.string.stat_record)} (${if (walk) "walk" else "bike"})",
            Formatters.speed(state.recordMps, unit, 1),
            Units.speedLabel(unit),
            highlight = state.isNewRecord,
        ),
    )
    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        tiles.chunked(2).forEach { row ->
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                row.forEach { t ->
                    StatTile(
                        label = t.label,
                        value = t.value,
                        unit = t.unit,
                        spokenValue = t.spoken ?: listOfNotNull(t.value, t.unit).joinToString(" "),
                        highlight = t.highlight,
                        modifier = Modifier.weight(1f),
                    )
                }
            }
        }
        if (state.sessionActive && snap.status != TripStatus.IDLE) {
            Text(
                "${stringResource(R.string.stat_elapsed)}: ${Formatters.duration(snap.elapsedTimeMs)}",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

private data class Tile(
    val label: String,
    val value: String,
    val unit: String?,
    val spoken: String? = null,
    val highlight: Boolean = false,
)
