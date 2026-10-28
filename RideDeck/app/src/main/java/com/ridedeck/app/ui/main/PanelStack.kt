package com.ridedeck.app.ui.main

import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.spring
import androidx.compose.foundation.background
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.material3.Text
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.input.pointer.PointerInputChange
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.zIndex
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
 * Stacks the Ride screen [panels] top to bottom ([vertical]) or side by side. Each divider
 * between two panels can be dragged to resize them; press and hold it, then drag, to swap the two
 * panels, which glide into their new spots. While resizing, only the divider moves and the panels
 * take their new size when it is let go, so the map isn't re-laid out on every frame.
 * Panels are keyed by view, so the map keeps its camera and the music keeps playing when they
 * move. The last panel's share of a two-panel stack is saved.
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
    val swap by rememberUpdatedState(onSwap)
    val haptics = LocalHapticFeedback.current
    // The divider being held for a swap: the panels either side of it lift slightly.
    var armedDivider by remember { mutableIntStateOf(-1) }

    BoxWithConstraints(modifier.fillMaxSize()) {
        val totalPx = with(LocalDensity.current) { (if (vertical) maxHeight else maxWidth).toPx() }.coerceAtLeast(1f)
        val swapDistancePx = with(LocalDensity.current) { SWAP_DISTANCE.toPx() }

        @Composable
        fun Slots(slotModifier: (Float) -> Modifier) {
            panels.forEachIndexed { index, panel ->
                if (index > 0) {
                    val before = panels[index - 1]
                    Divider(
                        vertical = vertical,
                        swapDistancePx = swapDistancePx,
                        swapLabel = "Swap ${before.view.title} and ${panel.view.title}",
                        limits = {
                            // How far the divider may travel before either panel gets too small.
                            val a = (weights[index - 1] - MIN_SHARE) * totalPx
                            val b = (weights[index] - MIN_SHARE) * totalPx
                            -a.coerceAtLeast(0f)..b.coerceAtLeast(0f)
                        },
                        onResized = { travelPx ->
                            val d = travelPx / totalPx
                            weights[index - 1] = weights[index - 1] + d
                            weights[index] = weights[index] - d
                            if (index == count - 1) {
                                commit(weights[index].coerceIn(UserPreferences.MIN_PANEL_FRACTION, UserPreferences.MAX_PANEL_FRACTION))
                            }
                        },
                        onSwapArmed = { armed ->
                            if (armed) haptics.performHapticFeedback(HapticFeedbackType.LongPress)
                            armedDivider = if (armed) index else -1
                        },
                        onSwap = {
                            haptics.performHapticFeedback(HapticFeedbackType.LongPress)
                            armedDivider = -1
                            swap(before.view, panel.view)
                        },
                    )
                }
                key(panel.view) {
                    val share = weights.getOrElse(index) { 1f / count }
                    val lifted = armedDivider == index || armedDivider == index + 1
                    MovingSlot(index = index, vertical = vertical, lifted = lifted, modifier = slotModifier(share)) { panel.content() }
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

/**
 * Plays a short glide whenever the panel's position in the stack changes, and shrinks it a
 * touch while it is [lifted] for a swap. Only movement and scale are animated (no fading), which
 * the map's surface follows smoothly.
 */
@Composable
private fun MovingSlot(index: Int, vertical: Boolean, lifted: Boolean, modifier: Modifier, content: @Composable () -> Unit) {
    val progress = remember { Animatable(1f) }
    var lastIndex by remember { mutableIntStateOf(index) }
    var direction by remember { mutableIntStateOf(0) }
    LaunchedEffect(index) {
        if (index != lastIndex) {
            direction = (index - lastIndex).sign
            lastIndex = index
            progress.snapTo(0f)
            progress.animateTo(1f, spring(dampingRatio = 0.75f, stiffness = Spring.StiffnessMediumLow))
        }
    }
    val lift = animateFloatAsState(if (lifted) 1f else 0f, spring(stiffness = Spring.StiffnessMedium), label = "lift")
    val density = LocalDensity.current
    Box(
        modifier.graphicsLayer {
            val t = progress.value
            val shift = with(density) { 56.dp.toPx() } * (1f - t) * -direction
            if (vertical) translationY = shift else translationX = shift
            val s = (0.95f + 0.05f * t) * (1f - 0.03f * lift.value)
            scaleX = s
            scaleY = s
            clip = true
        },
    ) { content() }
}

/**
 * The divider between two panels. Drag to resize: a preview line follows the finger and the
 * panels resize on release. Press and hold, then drag at least [swapDistancePx] either way and let
 * go, to swap the panels.
 */
@Composable
private fun Divider(
    vertical: Boolean,
    swapDistancePx: Float,
    swapLabel: String,
    limits: () -> ClosedFloatingPointRange<Float>,
    onResized: (Float) -> Unit,
    onSwapArmed: (Boolean) -> Unit,
    onSwap: () -> Unit,
) {
    val currentLimits by rememberUpdatedState(limits)
    val resized by rememberUpdatedState(onResized)
    val armedChanged by rememberUpdatedState(onSwapArmed)
    val swapped by rememberUpdatedState(onSwap)

    // Read only in the graphics layer, so following the finger costs no recomposition.
    val travel = remember { mutableFloatStateOf(0f) }
    var mode by remember { mutableStateOf(DividerMode.IDLE) }
    val farEnough = mode == DividerMode.SWAP && kotlin.math.abs(travel.floatValue) >= swapDistancePx

    val accent = MaterialTheme.colorScheme.primary
    val bar = MaterialTheme.colorScheme.surfaceContainer
    val edge = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.5f)
    Box(
        Modifier
            .zIndex(1f)
            .then(if (vertical) Modifier.fillMaxWidth().height(DIVIDER) else Modifier.fillMaxHeight().width(DIVIDER))
            .graphicsLayer {
                // Resizing moves the divider itself as a preview; a held swap only lifts it.
                if (mode == DividerMode.RESIZE) {
                    if (vertical) translationY = travel.floatValue else translationX = travel.floatValue
                }
            }
            .background(
                when (mode) {
                    DividerMode.IDLE -> if (vertical) Brush.verticalGradient(listOf(edge, bar, bar, edge)) else Brush.horizontalGradient(listOf(edge, bar, bar, edge))
                    else -> Brush.linearGradient(listOf(accent.copy(alpha = 0.35f), accent.copy(alpha = 0.35f)))
                },
            )
            .pointerInput(vertical, swapDistancePx) {
                awaitEachGesture {
                    val down = awaitFirstDown()
                    fun along(c: PointerInputChange) = if (vertical) c.position.y - down.position.y else c.position.x - down.position.x
                    val slop = viewConfiguration.touchSlop
                    // Moving before the long-press timeout means resize; holding still means swap.
                    val decided = withTimeoutOrNull(viewConfiguration.longPressTimeoutMillis) {
                        while (true) {
                            val c = awaitPointerEvent().changes.firstOrNull { it.id == down.id } ?: return@withTimeoutOrNull DividerMode.IDLE
                            if (!c.pressed) return@withTimeoutOrNull DividerMode.IDLE
                            if (kotlin.math.abs(along(c)) > slop) {
                                c.consume()
                                return@withTimeoutOrNull DividerMode.RESIZE
                            }
                        }
                        @Suppress("UNREACHABLE_CODE")
                        DividerMode.IDLE
                    } ?: DividerMode.SWAP
                    if (decided == DividerMode.IDLE) return@awaitEachGesture
                    mode = decided
                    if (decided == DividerMode.SWAP) armedChanged(true)
                    val range = currentLimits()
                    while (true) {
                        val c = awaitPointerEvent().changes.firstOrNull { it.id == down.id } ?: break
                        if (!c.pressed) break
                        c.consume()
                        val raw = along(c)
                        travel.floatValue = if (decided == DividerMode.RESIZE) raw.coerceIn(range) else raw
                    }
                    val moved = travel.floatValue
                    if (decided == DividerMode.RESIZE) {
                        if (moved != 0f) resized(moved)
                    } else {
                        armedChanged(false)
                        if (kotlin.math.abs(moved) >= swapDistancePx) swapped()
                    }
                    travel.floatValue = 0f
                    mode = DividerMode.IDLE
                }
            }
            .semantics {
                contentDescription = "Panel divider. Drag to resize. Press and hold, then drag, to swap the panels."
                customActions = listOf(CustomAccessibilityAction(swapLabel) { swapped(); true })
            },
        contentAlignment = Alignment.Center,
    ) {
        if (mode == DividerMode.SWAP) {
            Text(
                if (farEnough) "Let go to swap" else "Drag to swap",
                style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.onSurface,
            )
        } else {
            Box(
                Modifier
                    .size(width = if (vertical) 44.dp else 5.dp, height = if (vertical) 5.dp else 44.dp)
                    .background(
                        if (mode == DividerMode.RESIZE) accent else MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.7f),
                        RoundedCornerShape(50),
                    ),
            )
        }
    }
}

private enum class DividerMode { IDLE, RESIZE, SWAP }

private val DIVIDER = 32.dp
private val SWAP_DISTANCE = 48.dp
private const val MIN_SHARE = 0.14f
