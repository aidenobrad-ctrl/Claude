package com.ridedeck.app.ui.home

import androidx.compose.foundation.clickable
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.filled.DragIndicator
import androidx.compose.material3.LocalContentColor
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.TextUnit
import com.ridedeck.app.ui.components.AutoSizeText
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.ui.graphics.RectangleShape
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ExpandLess
import androidx.compose.material.icons.filled.ExpandMore
import androidx.compose.material.icons.filled.Pause
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.Stop
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.FilledIconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.IconButtonDefaults
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
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.LifecycleResumeEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.ridedeck.app.R
import com.ridedeck.app.data.prefs.RideStat
import com.ridedeck.app.ui.components.GloveTouchTarget
import com.ridedeck.app.ui.theme.AlertRed
import com.ridedeck.app.ui.theme.GoGreen
import com.ridedeck.app.ui.theme.NumericStyle
import com.ridedeck.core.format.Formatters
import com.ridedeck.core.format.Units
import com.ridedeck.core.tracking.TripStatus

/**
 * The speedometer. Over the map it floats as a card that can shrink to a small speed pill; with
 * [fullScreen] it fills its own view (speedometer alone, or above the music panel).
 */
@Composable
fun SpeedOverlay(
    rideLocked: Boolean,
    onOpenPermissions: () -> Unit,
    modifier: Modifier = Modifier,
    fullScreen: Boolean = false,
    /** Floating over the map: shows a grab handle, and double-tapping it calls this (docks the card). */
    onHandleDoubleTap: (() -> Unit)? = null,
    viewModel: HomeViewModel = hiltViewModel(),
) {
    val state by viewModel.uiState.collectAsStateWithLifecycle()
    var confirmStop by rememberSaveable { mutableStateOf(false) }
    val haptics = LocalHapticFeedback.current

    LifecycleResumeEffect(Unit) {
        viewModel.refreshPermissions()
        onPauseOrDispose { }
    }

    val unit = state.unit
    val snap = state.snapshot
    val speedMps = state.displaySpeedMps
    val speedText = Formatters.speed(speedMps, unit)
    val overLimit = state.speedAlertEnabled && speedMps > state.speedAlertLimitMps
    val speedColor = if (overLimit) AlertRed else MaterialTheme.colorScheme.onSurface
    val spokenSpeed = "Speed $speedText ${Units.speedLabel(unit)}" + if (overLimit) ", over your speed limit" else ""

    Surface(
        shape = if (fullScreen) RectangleShape else MaterialTheme.shapes.extraLarge,
        color = if (fullScreen) MaterialTheme.colorScheme.background else MaterialTheme.colorScheme.surface.copy(alpha = 0.92f),
        tonalElevation = if (fullScreen) 0.dp else 3.dp,
        shadowElevation = if (fullScreen) 0.dp else 6.dp,
        modifier = if (fullScreen) modifier.fillMaxSize() else modifier.widthIn(max = 480.dp),
    ) {
        BoxWithConstraints {
            // Size the speed to the space it has, so it fills a phone held either way, a tablet, or a
            // split-screen window, instead of a fixed size that clips at large font settings.
            val density = LocalDensity.current
            val wide = fullScreen && maxWidth > maxHeight && maxWidth >= 560.dp
            val heroSize = with(density) {
                when {
                    !fullScreen -> minOf(maxWidth * 0.2f, 76.dp).toSp()
                    wide -> minOf(maxWidth * 0.22f, maxHeight * 0.45f).toSp()
                    else -> minOf(maxWidth * 0.34f, maxHeight * 0.24f, 200.dp).toSp()
                }
            }
            if (!fullScreen && !state.expanded) {
                Row(
                    Modifier
                        .clickable(onClickLabel = "Show speedometer") { viewModel.setExpanded(true) }
                        .padding(start = if (onHandleDoubleTap != null) 0.dp else 16.dp, end = 4.dp, top = 4.dp, bottom = 4.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    if (onHandleDoubleTap != null) SideHandle(onHandleDoubleTap)
                    Text(
                        speedText,
                        style = NumericStyle.copy(fontSize = 40.sp),
                        color = speedColor,
                        modifier = Modifier.semantics { contentDescription = spokenSpeed },
                    )
                    Spacer(Modifier.width(4.dp))
                    Text(Units.speedLabel(unit), style = MaterialTheme.typography.labelLarge)
                    if (state.sessionActive && RideStat.DISTANCE in state.visibleStats) {
                        Spacer(Modifier.width(12.dp))
                        Text(
                            "${Formatters.distance(snap.distanceM, unit)} ${Units.distanceLabel(unit)}",
                            style = MaterialTheme.typography.titleMedium,
                        )
                    }
                    Spacer(Modifier.width(4.dp))
                    PrimaryControl(snap.status, state, viewModel, onOpenPermissions)
                    Icon(Icons.Default.ExpandLess, contentDescription = null, modifier = Modifier.padding(horizontal = 8.dp))
                }
            } else {
                Column(
                    Modifier
                        .then(if (fullScreen) Modifier.verticalScroll(rememberScrollState()) else Modifier)
                        .padding(
                            start = if (fullScreen) 20.dp else 14.dp,
                            end = if (fullScreen) 20.dp else 14.dp,
                            top = if (onHandleDoubleTap != null) 0.dp else if (fullScreen) 20.dp else 14.dp,
                            bottom = if (fullScreen) 20.dp else 14.dp,
                        ),
                    verticalArrangement = Arrangement.spacedBy(if (fullScreen) 16.dp else 10.dp),
                ) {
                    if (onHandleDoubleTap != null) TopHandle(onHandleDoubleTap)
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Row(
                            Modifier.weight(1f).semantics(mergeDescendants = true) { contentDescription = spokenSpeed },
                            verticalAlignment = Alignment.Bottom,
                        ) {
                            Text(
                                speedText,
                                style = NumericStyle.copy(fontSize = heroSize, lineHeight = heroSize),
                                color = speedColor,
                                maxLines = 1,
                                softWrap = false,
                            )
                            Spacer(Modifier.width(6.dp))
                            TextButton(onClick = viewModel::toggleUnit, modifier = Modifier.heightIn(min = 48.dp)) {
                                Text(Units.speedLabel(unit), style = MaterialTheme.typography.titleLarge)
                            }
                        }
                        Column(horizontalAlignment = Alignment.End) {
                            StatusChip(snap.status, state.isNewRecord)
                            if (!fullScreen) {
                                IconButton(onClick = { viewModel.setExpanded(false) }, modifier = Modifier.size(48.dp)) {
                                    Icon(Icons.Default.ExpandMore, contentDescription = "Shrink speedometer")
                                }
                            }
                        }
                    }
                    val statSize = if (fullScreen) (if (wide) 30.sp else 26.sp) else 20.sp
                    // Only the figures switched on in Settings; the rest share the space.
                    val stats = state.visibleStats
                    if (stats.any { it != RideStat.ODOMETER }) {
                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            if (RideStat.DISTANCE in stats) {
                                MiniStat(stringResource(R.string.stat_distance), Formatters.distance(snap.distanceM, unit), Units.distanceLabel(unit), statSize, Modifier.weight(1f))
                            }
                            if (RideStat.TIME in stats) {
                                MiniStat(stringResource(R.string.stat_time), Formatters.duration(snap.movingTimeMs), null, statSize, Modifier.weight(1f))
                            }
                            if (RideStat.AVERAGE in stats) {
                                MiniStat(stringResource(R.string.stat_avg), Formatters.speed(snap.averageSpeedMps, unit, 1), null, statSize, Modifier.weight(1f))
                            }
                            if (RideStat.MAX in stats) {
                                MiniStat(stringResource(R.string.stat_max), Formatters.speed(snap.maxSpeedMps, unit, 1), null, statSize, Modifier.weight(1f))
                            }
                        }
                    }
                    if (!state.hasLocationPermission) {
                        OutlinedButton(
                            onClick = onOpenPermissions,
                            modifier = Modifier.fillMaxWidth().heightIn(min = GloveTouchTarget),
                        ) { Text("Precise location is off. Tap to allow it.") }
                    }
                    Controls(
                        status = snap.status,
                        onStart = {
                            haptics.performHapticFeedback(HapticFeedbackType.LongPress)
                            if (!viewModel.start()) onOpenPermissions()
                        },
                        onPause = {
                            haptics.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                            viewModel.pause()
                        },
                        onResume = {
                            haptics.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                            viewModel.resume()
                        },
                        onStop = { confirmStop = true },
                    )
                    val showOdometer = RideStat.ODOMETER in stats
                    val showResetMax = RideStat.MAX in stats && !rideLocked
                    if (state.sessionActive && (showOdometer || showResetMax)) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Text(
                                if (showOdometer) "Odometer ${Formatters.distance(state.odometerM, unit, 1)} ${Units.distanceLabel(unit)}" else "",
                                style = MaterialTheme.typography.bodyMedium,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                modifier = Modifier.weight(1f),
                            )
                            if (showResetMax) {
                                TextButton(onClick = viewModel::resetMax, modifier = Modifier.heightIn(min = 48.dp)) {
                                    Text(stringResource(R.string.reset_max))
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    if (confirmStop) {
        AlertDialog(
            onDismissRequest = { confirmStop = false },
            title = { Text("Finish this ride?") },
            text = { Text("It will be saved to your history.") },
            confirmButton = {
                Button(
                    onClick = {
                        confirmStop = false
                        haptics.performHapticFeedback(HapticFeedbackType.LongPress)
                        viewModel.stop()
                    },
                    colors = ButtonDefaults.buttonColors(containerColor = AlertRed),
                    modifier = Modifier.heightIn(min = GloveTouchTarget),
                ) { Text("Finish") }
            },
            dismissButton = {
                OutlinedButton(onClick = { confirmStop = false }, modifier = Modifier.heightIn(min = GloveTouchTarget)) {
                    Text("Keep going")
                }
            },
        )
    }
}

/** The one button shown on the collapsed pill: start, pause or resume. */
@Composable
private fun PrimaryControl(status: TripStatus, state: HomeUiState, viewModel: HomeViewModel, onOpenPermissions: () -> Unit) {
    val haptics = LocalHapticFeedback.current
    val (icon, label, color) = when (status) {
        TripStatus.IDLE -> Triple(Icons.Default.PlayArrow, "Start ride", GoGreen)
        TripStatus.PAUSED -> Triple(Icons.Default.PlayArrow, "Resume", GoGreen)
        else -> Triple(Icons.Default.Pause, "Pause", MaterialTheme.colorScheme.primary)
    }
    FilledIconButton(
        onClick = {
            haptics.performHapticFeedback(if (status == TripStatus.IDLE) HapticFeedbackType.LongPress else HapticFeedbackType.TextHandleMove)
            when (status) {
                TripStatus.IDLE -> if (!viewModel.start()) onOpenPermissions()
                TripStatus.PAUSED -> viewModel.resume()
                else -> viewModel.pause()
            }
        },
        colors = IconButtonDefaults.filledIconButtonColors(containerColor = color),
        modifier = Modifier.size(56.dp),
    ) {
        Icon(icon, contentDescription = label, modifier = Modifier.size(32.dp))
    }
}

/** One trip figure. The value shrinks to fit its column instead of clipping at large font sizes. */
@Composable
private fun MiniStat(label: String, value: String, unit: String?, size: TextUnit, modifier: Modifier = Modifier) {
    Column(modifier.semantics(mergeDescendants = true) { contentDescription = "$label: $value ${unit.orEmpty()}" }) {
        Text(
            label.uppercase(),
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        AutoSizeText(
            if (unit != null) "$value $unit" else value,
            style = NumericStyle.copy(color = MaterialTheme.colorScheme.onSurface),
            maxSize = size,
        )
    }
}

/** Grab handle along the top of the floating card; drag anywhere on the card to move it. */
@Composable
private fun TopHandle(onDoubleTap: () -> Unit) {
    Box(
        Modifier
            .fillMaxWidth()
            .height(20.dp)
            .pointerInput(onDoubleTap) { detectTapGestures(onDoubleTap = { onDoubleTap() }) }
            .semantics { contentDescription = "Drag to move the speedometer. Double-tap to dock it." },
        contentAlignment = Alignment.Center,
    ) {
        Box(
            Modifier
                .size(width = 40.dp, height = 4.dp)
                .background(MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.6f), RoundedCornerShape(50)),
        )
    }
}

/** Grab handle at the start of the collapsed speed pill. */
@Composable
private fun SideHandle(onDoubleTap: () -> Unit) {
    Icon(
        Icons.Default.DragIndicator,
        contentDescription = "Drag to move the speedometer. Double-tap to dock it.",
        tint = MaterialTheme.colorScheme.onSurfaceVariant,
        modifier = Modifier
            .padding(horizontal = 4.dp)
            .pointerInput(onDoubleTap) { detectTapGestures(onDoubleTap = { onDoubleTap() }) },
    )
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
        modifier = Modifier.semantics { liveRegion = LiveRegionMode.Polite },
    ) {
        Text(text, style = MaterialTheme.typography.labelLarge, modifier = Modifier.padding(horizontal = 10.dp, vertical = 6.dp))
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
    val buttonModifier = Modifier.heightIn(min = GloveTouchTarget)
    when (status) {
        TripStatus.IDLE -> Button(
            onClick = onStart,
            colors = ButtonDefaults.buttonColors(containerColor = GoGreen),
            modifier = buttonModifier.fillMaxWidth(),
        ) {
            Icon(Icons.Default.PlayArrow, contentDescription = null, modifier = Modifier.size(28.dp))
            Spacer(Modifier.width(8.dp))
            Text(stringResource(R.string.action_start), style = MaterialTheme.typography.titleLarge)
        }
        else -> Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            val paused = status == TripStatus.PAUSED
            Button(
                onClick = if (paused) onResume else onPause,
                colors = ButtonDefaults.buttonColors(containerColor = if (paused) GoGreen else MaterialTheme.colorScheme.primary),
                modifier = buttonModifier.weight(1f),
            ) {
                Icon(if (paused) Icons.Default.PlayArrow else Icons.Default.Pause, contentDescription = null, modifier = Modifier.size(28.dp))
                Spacer(Modifier.width(8.dp))
                AutoSizeText(
                    stringResource(if (paused) R.string.action_resume else R.string.action_pause),
                    style = MaterialTheme.typography.titleLarge.copy(color = LocalContentColor.current),
                )
            }
            Button(
                onClick = onStop,
                colors = ButtonDefaults.buttonColors(containerColor = AlertRed),
                modifier = buttonModifier.weight(1f),
            ) {
                Icon(Icons.Default.Stop, contentDescription = null, modifier = Modifier.size(28.dp))
                Spacer(Modifier.width(8.dp))
                AutoSizeText(
                    stringResource(R.string.action_stop),
                    style = MaterialTheme.typography.titleLarge.copy(color = LocalContentColor.current),
                )
            }
        }
    }
}
