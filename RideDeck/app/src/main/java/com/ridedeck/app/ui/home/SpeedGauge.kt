package com.ridedeck.app.ui.home

import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxScope
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.rotate
import com.ridedeck.app.ui.theme.AccentBlue
import com.ridedeck.app.ui.theme.AccentBlueLight
import com.ridedeck.app.ui.theme.AlertRed
import kotlin.math.cos
import kotlin.math.sin

private const val START_DEG = 135f
private const val SWEEP_DEG = 270f

/**
 * A 270-degree speed dial in the style of the RideDeck logo: a faint track, ticks, and a glowing
 * accent arc that springs to [fraction] (0..1) with a bright tip. It turns red when [alert] is
 * on. [content] (the speed figures) sits in the middle.
 */
@Composable
fun SpeedGauge(
    fraction: Float,
    alert: Boolean,
    modifier: Modifier = Modifier,
    content: @Composable BoxScope.() -> Unit,
) {
    val sweep by animateFloatAsState(
        targetValue = fraction.coerceIn(0f, 1f),
        animationSpec = spring(dampingRatio = 0.8f, stiffness = Spring.StiffnessLow),
        label = "gauge",
    )
    val glow by animateFloatAsState(if (alert) 1f else 0f, label = "alert")
    Box(modifier, contentAlignment = Alignment.Center) {
        Canvas(Modifier.fillMaxSize()) {
            val stroke = size.minDimension * 0.065f
            val inset = stroke * 1.6f
            val arcSize = Size(size.minDimension - 2 * inset, size.minDimension - 2 * inset)
            val topLeft = Offset((size.width - arcSize.width) / 2f, (size.height - arcSize.height) / 2f)
            val center = Offset(size.width / 2f, size.height / 2f)
            val radius = arcSize.width / 2f
            val start = lerp(AccentBlue, AlertRed, glow)
            val end = lerp(AccentBlueLight, Color(0xFFFF9E80), glow)

            drawArc(Color.White.copy(alpha = 0.08f), START_DEG, SWEEP_DEG, false, topLeft, arcSize, style = Stroke(stroke, cap = StrokeCap.Round))

            // Ticks every tenth of the dial, longer at each quarter.
            for (i in 0..20) {
                val deg = START_DEG + SWEEP_DEG * i / 20f
                val t = Math.toRadians(deg.toDouble())
                val major = i % 5 == 0
                val outer = radius - stroke * 1.2f
                val inner = outer - if (major) stroke * 0.9f else stroke * 0.45f
                drawLine(
                    Color.White.copy(alpha = if (major) 0.5f else 0.22f),
                    Offset(center.x + outer * cos(t).toFloat(), center.y + outer * sin(t).toFloat()),
                    Offset(center.x + inner * cos(t).toFloat(), center.y + inner * sin(t).toFloat()),
                    strokeWidth = stroke * if (major) 0.22f else 0.14f,
                    cap = StrokeCap.Round,
                )
            }

            val lit = (SWEEP_DEG * sweep).coerceAtLeast(0.5f)
            // The brush runs around the dial so the arc brightens towards its tip.
            val brush = Brush.sweepGradient(
                0f to start,
                (SWEEP_DEG / 360f) to end,
                1f to start,
                center = center,
            )
            rotate(START_DEG, center) {
                // Soft glow under the arc, then the arc itself.
                drawArc(start.copy(alpha = 0.16f), 0f, lit, false, topLeft, arcSize, style = Stroke(stroke * 2.6f, cap = StrokeCap.Round))
                drawArc(brush, 0f, lit, false, topLeft, arcSize, style = Stroke(stroke, cap = StrokeCap.Round))
            }
            val tip = Math.toRadians((START_DEG + lit).toDouble())
            val tipCenter = Offset(center.x + radius * cos(tip).toFloat(), center.y + radius * sin(tip).toFloat())
            drawCircle(end.copy(alpha = 0.35f), stroke * 1.1f, tipCenter)
            drawCircle(Color.White, stroke * 0.42f, tipCenter)
        }
        content()
    }
}

private fun lerp(a: Color, b: Color, t: Float): Color = Color(
    red = a.red + (b.red - a.red) * t,
    green = a.green + (b.green - a.green) * t,
    blue = a.blue + (b.blue - a.blue) * t,
    alpha = a.alpha + (b.alpha - a.alpha) * t,
)

/** Dial full-scale in the rider's unit: 40 mph or 60 km/h, stepping up by 10 if they go faster. */
fun gaugeFullScale(speedInUnit: Double, maxInUnit: Double, metric: Boolean): Double {
    val base = if (metric) 60.0 else 40.0
    val top = maxOf(speedInUnit, maxInUnit)
    return if (top <= base) base else (kotlin.math.ceil(top / 10.0) * 10.0)
}
