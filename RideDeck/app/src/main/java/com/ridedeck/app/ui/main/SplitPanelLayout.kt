package com.ridedeck.app.ui.main

import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.Orientation
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.draggable
import androidx.compose.foundation.gestures.rememberDraggableState
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.CustomAccessibilityAction
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.customActions
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.ridedeck.app.data.prefs.UserPreferences

/**
 * Splits the screen between [top] content (map or speedometer) and the music [panel].
 * The handle between them can be dragged to resize; dragging below the minimum hides the panel,
 * and double-tapping toggles between compact and large. TalkBack users get equivalent actions.
 */
@Composable
fun SplitPanelLayout(
    showPanel: Boolean,
    fraction: Float,
    onFractionCommitted: (Float) -> Unit,
    onHidePanel: () -> Unit,
    top: @Composable () -> Unit,
    panel: @Composable () -> Unit,
    modifier: Modifier = Modifier,
) {
    BoxWithConstraints(modifier.fillMaxSize()) {
        val totalPx = with(LocalDensity.current) { maxHeight.toPx() }.coerceAtLeast(1f)
        var liveFraction by remember { mutableFloatStateOf(fraction) }
        LaunchedEffect(fraction) { liveFraction = fraction }
        val commit by rememberUpdatedState(onFractionCommitted)
        val hide by rememberUpdatedState(onHidePanel)

        Column(Modifier.fillMaxSize()) {
            val panelFraction = if (showPanel) liveFraction else 0f
            Box(Modifier.weight(1f - panelFraction).fillMaxWidth()) { top() }
            if (showPanel) {
                val dragState = rememberDraggableState { delta ->
                    // Dragging up (negative delta) grows the panel.
                    liveFraction = (liveFraction - delta / totalPx).coerceIn(HIDE_THRESHOLD, UserPreferences.MAX_PANEL_FRACTION)
                }
                Box(
                    Modifier
                        .fillMaxWidth()
                        .height(28.dp)
                        .background(MaterialTheme.colorScheme.surfaceContainerHigh)
                        .draggable(
                            state = dragState,
                            orientation = Orientation.Vertical,
                            onDragStopped = {
                                if (liveFraction < UserPreferences.MIN_PANEL_FRACTION) {
                                    liveFraction = fraction
                                    hide()
                                } else {
                                    commit(liveFraction)
                                }
                            },
                        )
                        .pointerInput(Unit) {
                            detectTapGestures(onDoubleTap = {
                                val target = if (liveFraction < 0.45f) 0.6f else UserPreferences.DEFAULT_PANEL_FRACTION
                                liveFraction = target
                                commit(target)
                            })
                        }
                        .semantics {
                            contentDescription = "Music panel resize handle"
                            customActions = listOf(
                                CustomAccessibilityAction("Make music panel larger") {
                                    liveFraction = (liveFraction + 0.1f).coerceAtMost(UserPreferences.MAX_PANEL_FRACTION)
                                    commit(liveFraction)
                                    true
                                },
                                CustomAccessibilityAction("Make music panel smaller") {
                                    liveFraction = (liveFraction - 0.1f).coerceAtLeast(UserPreferences.MIN_PANEL_FRACTION)
                                    commit(liveFraction)
                                    true
                                },
                                CustomAccessibilityAction("Hide music panel") {
                                    hide()
                                    true
                                },
                            )
                        },
                    contentAlignment = Alignment.Center,
                ) {
                    Box(
                        Modifier
                            .size(width = 48.dp, height = 5.dp)
                            .background(MaterialTheme.colorScheme.onSurfaceVariant, RoundedCornerShape(50)),
                    )
                }
                Box(Modifier.weight(panelFraction.coerceAtLeast(0.01f)).fillMaxWidth()) { panel() }
            }
        }
    }
}

private const val HIDE_THRESHOLD = 0.08f
