package com.ridedeck.app.ui.home

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.ridedeck.app.ui.theme.AlertRed
import com.ridedeck.app.ui.theme.NumericStyle

/**
 * Large, high-contrast readout with a gauge arc. Digits are sized from the available width (not
 * the font scale) so they stay as large as possible without overflowing at any text size.
 */
@Composable
fun Speedometer(
    primaryValue: String,
    primaryUnit: String,
    secondaryText: String?,
    gaugeFraction: Float,
    limitFraction: Float?,
    overLimit: Boolean,
    spokenDescription: String,
    modifier: Modifier = Modifier,
) {
    val accent = if (overLimit) AlertRed else MaterialTheme.colorScheme.primary
    val track = MaterialTheme.colorScheme.surfaceContainerHighest
    val limitColor = MaterialTheme.colorScheme.error
    BoxWithConstraints(
        modifier
            .fillMaxWidth()
            .aspectRatio(1.35f)
            .clearAndSetSemantics { contentDescription = spokenDescription },
        contentAlignment = Alignment.Center,
    ) {
        val density = LocalDensity.current
        val digitsSize = with(density) { (maxWidth * 0.36f).toSp() }
        val unitSize = with(density) { (maxWidth * 0.07f).toSp() }
        val secondarySize = with(density) { (maxWidth * 0.055f).toSp() }

        Canvas(Modifier.matchParentSize()) {
            val stroke = size.width * 0.045f
            val diameter = minOf(size.width, size.height * 1.3f) - stroke
            val topLeft = Offset((size.width - diameter) / 2f, stroke / 2f + size.height * 0.04f)
            val arcSize = Size(diameter, diameter)
            drawArc(track, 150f, 240f, false, topLeft, arcSize, style = Stroke(stroke, cap = StrokeCap.Round))
            drawArc(
                accent,
                150f,
                240f * gaugeFraction.coerceIn(0f, 1f),
                false,
                topLeft,
                arcSize,
                style = Stroke(stroke, cap = StrokeCap.Round),
            )
            if (limitFraction != null) {
                drawArc(
                    limitColor,
                    150f + 240f * limitFraction.coerceIn(0f, 1f) - 1f,
                    2f,
                    false,
                    topLeft,
                    arcSize,
                    style = Stroke(stroke * 1.6f),
                )
            }
        }
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            Text(
                primaryValue,
                style = NumericStyle.copy(fontSize = digitsSize, lineHeight = digitsSize),
                color = if (overLimit) AlertRed else Color.Unspecified,
                textAlign = TextAlign.Center,
                maxLines = 1,
            )
            Text(primaryUnit, style = MaterialTheme.typography.titleLarge.copy(fontSize = unitSize), color = MaterialTheme.colorScheme.onSurfaceVariant)
            if (secondaryText != null) {
                Box(Modifier) {
                    Text(
                        secondaryText,
                        style = NumericStyle.copy(fontSize = secondarySize),
                        color = MaterialTheme.colorScheme.secondary,
                    )
                }
            }
        }
    }
}
