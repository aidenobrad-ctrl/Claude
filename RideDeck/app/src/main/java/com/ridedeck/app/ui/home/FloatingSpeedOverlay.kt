package com.ridedeck.app.ui.home

import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.widthIn
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.CustomAccessibilityAction
import androidx.compose.ui.semantics.customActions
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.unit.dp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.ridedeck.app.data.prefs.OverlayPosition
import kotlin.math.roundToInt

/**
 * The speedometer card floating over the map. Drag it anywhere; it remembers where it was left,
 * as a fraction of the free space so the spot survives rotation and different screen sizes.
 * Until it is moved (or after a double-tap on its handle) it docks centred just above the
 * controls at the bottom of the map, [bottomReservedPx] tall.
 *
 * Position changes only re-place the card (no recomposition), so dragging stays smooth at 120 Hz.
 */
@Composable
fun FloatingSpeedOverlay(
    rideLocked: Boolean,
    onOpenPermissions: () -> Unit,
    area: IntSize,
    bottomReservedPx: Int,
    modifier: Modifier = Modifier,
    viewModel: HomeViewModel = hiltViewModel(),
) {
    val state by viewModel.uiState.collectAsStateWithLifecycle()
    val density = LocalDensity.current
    val marginPx = with(density) { 12.dp.roundToPx() }
    val maxCardWidth = with(density) { (area.width - 2 * marginPx).coerceAtLeast(0).toDp() }

    var cardSize by remember { mutableStateOf(IntSize.Zero) }
    // Shown position: follows the saved one, but updates at once when a drag ends.
    var placed by remember { mutableStateOf(state.position) }
    LaunchedEffect(state.position) { placed = state.position }
    var dragging by remember { mutableStateOf<Offset?>(null) }

    val currentArea by rememberUpdatedState(area)
    val currentBottom by rememberUpdatedState(bottomReservedPx)

    fun minX() = marginPx.toFloat()
    fun maxX() = (currentArea.width - cardSize.width - marginPx).toFloat().coerceAtLeast(minX())
    fun minY() = marginPx.toFloat()
    fun maxY() = (currentArea.height - cardSize.height - marginPx).toFloat().coerceAtLeast(minY())

    fun resolved(): Offset {
        dragging?.let { return it }
        val p = placed
        return if (p == null) {
            Offset(
                x = (currentArea.width - cardSize.width) / 2f,
                y = (currentArea.height - currentBottom - cardSize.height).toFloat().coerceIn(minY(), maxY()),
            )
        } else {
            Offset(minX() + p.x * (maxX() - minX()), minY() + p.y * (maxY() - minY()))
        }
    }

    fun commit(offset: Offset) {
        val fx = if (maxX() > minX()) (offset.x - minX()) / (maxX() - minX()) else 0.5f
        val fy = if (maxY() > minY()) (offset.y - minY()) / (maxY() - minY()) else 1f
        val position = OverlayPosition(fx.coerceIn(0f, 1f), fy.coerceIn(0f, 1f))
        placed = position
        viewModel.setPosition(position)
    }

    fun place(position: OverlayPosition?) {
        placed = position
        viewModel.setPosition(position)
    }

    Box(
        modifier
            .offset { resolved().let { IntOffset(it.x.roundToInt(), it.y.roundToInt()) } }
            .onSizeChanged { cardSize = it }
            .pointerInput(Unit) {
                detectDragGestures(
                    onDragStart = { dragging = resolved() },
                    onDragEnd = {
                        dragging?.let(::commit)
                        dragging = null
                    },
                    onDragCancel = {
                        dragging?.let(::commit)
                        dragging = null
                    },
                ) { change, amount ->
                    change.consume()
                    val from = dragging ?: resolved()
                    dragging = Offset(
                        (from.x + amount.x).coerceIn(minX(), maxX()),
                        (from.y + amount.y).coerceIn(minY(), maxY()),
                    )
                }
            }
            .semantics {
                customActions = listOf(
                    CustomAccessibilityAction("Move speedometer to the top") { place(OverlayPosition(0.5f, 0f)); true },
                    CustomAccessibilityAction("Move speedometer to the left") { place(OverlayPosition(0f, placed?.y ?: 1f)); true },
                    CustomAccessibilityAction("Move speedometer to the right") { place(OverlayPosition(1f, placed?.y ?: 1f)); true },
                    CustomAccessibilityAction("Dock speedometer at the bottom") { place(null); true },
                )
            },
    ) {
        SpeedOverlay(
            rideLocked = rideLocked,
            onOpenPermissions = onOpenPermissions,
            onHandleDoubleTap = { place(null) },
            modifier = Modifier.widthIn(max = maxCardWidth),
            viewModel = viewModel,
        )
    }
}
