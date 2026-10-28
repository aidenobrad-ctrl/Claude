package com.ridedeck.app.ui.main

import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.spring
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.Orientation
import androidx.compose.foundation.gestures.draggable
import androidx.compose.foundation.gestures.rememberDraggableState
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.SwapHoriz
import androidx.compose.material.icons.filled.SwapVert
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.semantics.CustomAccessibilityAction
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.customActions
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.ridedeck.app.data.prefs.RideView
import com.ridedeck.app.data.prefs.UserPreferences
import kotlin.math.sign

/** One Ride screen panel: which view it is, and what it shows. */
class Panel(val view: RideView, val content: @Composable () -> Unit)

private val RideView.title: String
    get() = when (this) {
        RideView.MAP -> "map"
        RideView.SPEEDOMETER -> "speedometer"
        RideView.MUSIC -> "music"
    }

/**
 * Stacks the Ride screen [panels] top to bottom ([vertical]) or side by side. Each handle between
 * two panels can be dragged to resize them and has a swap button that trades their places; the
 * panels glide into their new spots. Panels are keyed by view, so the map keeps its camera and
 * the music keeps playing when they move. The last panel's share of a two-panel stack is saved.
 */
@Composable
fun PanelStack(
    panels: List<Panel>,
    vertical: Boolean,
    lastFraction: Float,
    onLastFractionCommitted: (Float) -> Unit,
    onSwap: (RideView, RideView) -> Unit,
    modifier: Modifier = Modifier,
) {
    val count = panels.size
    // Sizes belong to positions, not views, so whatever is on top keeps the top's size.
    val weights = remember(count, vertical) {
        mutableStateListOf<Float>().apply {
            when (count) {
                1 -> add(1f)
                2 -> { add(1f - lastFraction); add(lastFraction) }
                else -> {
                    val rest = 1f - lastFraction
                    add(rest * 0.6f); add(rest * 0.4f); add(lastFraction)
                }
            }
        }
    }
    val commit by rememberUpdatedState(onLastFractionCommitted)
    val haptics = LocalHapticFeedback.current

    BoxWithConstraints(modifier.fillMaxSize()) {
        val totalPx = with(LocalDensity.current) { (if (vertical) maxHeight else maxWidth).toPx() }.coerceAtLeast(1f)

        @Composable
        fun Slots(slotModifier: (Float) -> Modifier) {
            panels.forEachIndexed { index, panel ->
                if (index > 0) {
                    val before = panels[index - 1]
                    Handle(
                        vertical = vertical,
                        swapLabel = "Swap ${before.view.title} and ${panel.view.title}",
                        onDrag = { deltaPx ->
                            val d = deltaPx / totalPx
                            val a = weights[index - 1] + d
                            val b = weights[index] - d
                            if (a >= MIN_SHARE && b >= MIN_SHARE) {
                                weights[index - 1] = a
                                weights[index] = b
                            }
                        },
                        onDragStopped = { if (index == count - 1) commit(weights[index].coerceIn(UserPreferences.MIN_PANEL_FRACTION, UserPreferences.MAX_PANEL_FRACTION)) },
                        onSwap = {
                            haptics.performHapticFeedback(HapticFeedbackType.LongPress)
                            onSwap(before.view, panel.view)
                        },
                    )
                }
                key(panel.view) {
                    val share = weights.getOrElse(index) { 1f / count }
                    MovingSlot(index = index, vertical = vertical, modifier = slotModifier(share)) { panel.content() }
                }
            }
        }

        if (vertical) {
            Column(Modifier.fillMaxSize()) { Slots { share -> Modifier.weight(share).fillMaxWidth() } }
        } else {
            Row(Modifier.fillMaxSize()) { Slots { share -> Modifier.weight(share).fillMaxHeight() } }
        }
    }
}

/** Plays a short glide whenever the panel's position in the stack changes. */
@Composable
private fun MovingSlot(index: Int, vertical: Boolean, modifier: Modifier, content: @Composable () -> Unit) {
    val progress = remember { Animatable(1f) }
    var lastIndex by remember { mutableIntStateOf(index) }
    var direction by remember { mutableIntStateOf(0) }
    LaunchedEffect(index) {
        if (index != lastIndex) {
            direction = (index - lastIndex).sign
            lastIndex = index
            progress.snapTo(0f)
            progress.animateTo(1f, spring(dampingRatio = 0.72f, stiffness = Spring.StiffnessMediumLow))
        }
    }
    val density = LocalDensity.current
    Box(
        modifier.graphicsLayer {
            val t = progress.value
            val shift = with(density) { 56.dp.toPx() } * (1f - t) * -direction
            if (vertical) translationY = shift else translationX = shift
            alpha = 0.35f + 0.65f * t
            val s = 0.94f + 0.06f * t
            scaleX = s
            scaleY = s
            clip = true
        },
    ) { content() }
}

@Composable
private fun Handle(
    vertical: Boolean,
    swapLabel: String,
    onDrag: (Float) -> Unit,
    onDragStopped: () -> Unit,
    onSwap: () -> Unit,
) {
    val dragState = rememberDraggableState(onDrag)
    val bar = MaterialTheme.colorScheme.surfaceContainer
    val edge = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.5f)
    Box(
        Modifier
            .then(if (vertical) Modifier.fillMaxWidth().height(40.dp) else Modifier.fillMaxHeight().width(40.dp))
            .background(
                if (vertical) Brush.verticalGradient(listOf(edge, bar, bar, edge)) else Brush.horizontalGradient(listOf(edge, bar, bar, edge)),
            )
            .draggable(
                state = dragState,
                orientation = if (vertical) Orientation.Vertical else Orientation.Horizontal,
                onDragStopped = { onDragStopped() },
            )
            .semantics {
                contentDescription = "Panel divider. Drag to resize."
                customActions = listOf(CustomAccessibilityAction(swapLabel) { onSwap(); true })
            },
        contentAlignment = Alignment.Center,
    ) {
        Box(
            Modifier
                .size(width = if (vertical) 44.dp else 5.dp, height = if (vertical) 5.dp else 44.dp)
                .background(MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.7f), RoundedCornerShape(50)),
        )
        IconButton(
            onClick = onSwap,
            modifier = Modifier
                .align(if (vertical) Alignment.CenterEnd else Alignment.BottomCenter)
                .size(40.dp),
        ) {
            Icon(
                if (vertical) Icons.Default.SwapVert else Icons.Default.SwapHoriz,
                contentDescription = swapLabel,
                tint = MaterialTheme.colorScheme.primary,
            )
        }
    }
}

private const val MIN_SHARE = 0.14f
