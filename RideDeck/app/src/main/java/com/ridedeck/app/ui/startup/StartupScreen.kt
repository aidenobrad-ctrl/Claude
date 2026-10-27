package com.ridedeck.app.ui.startup

import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.ridedeck.app.R
import com.ridedeck.app.ui.theme.AccentBlue
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlin.math.cos
import kotlin.math.sin

/** Background of the launcher icon and system splash, so the hand-off is invisible. */
val StartupBackground = Color(0xFF02060C)
private val GlowNavy = Color(0xFF0C3352)
private val AccentLight = Color(0xFF8CF7FF)

/**
 * Animated startup screen. It begins exactly where the system splash leaves off (the logo,
 * centred, at splash size), then "revs" the gauge, lifts the logo and brings in the name and
 * tagline. Tap to skip. [onFinished] runs once the animation has played.
 */
@Composable
fun StartupScreen(onFinished: () -> Unit, modifier: Modifier = Modifier) {
    val finished by rememberUpdatedState(onFinished)
    val gauge = remember { Animatable(1f) }
    val reveal = remember { Animatable(0f) }

    LaunchedEffect(Unit) {
        gauge.animateTo(0f, tween(durationMillis = 280, easing = FastOutSlowInEasing))
        launch { reveal.animateTo(1f, tween(durationMillis = 650, easing = FastOutSlowInEasing)) }
        gauge.animateTo(1f, spring(dampingRatio = 0.42f, stiffness = 90f))
        delay(250)
        finished()
    }

    Box(
        modifier
            .fillMaxSize()
            .background(StartupBackground)
            .drawBehind {
                // A soft glow that blooms in behind the logo.
                drawRect(
                    Brush.radialGradient(
                        colors = listOf(GlowNavy.copy(alpha = reveal.value), Color.Transparent),
                        center = Offset(size.width / 2, size.height * 0.42f),
                        radius = size.maxDimension * 0.6f,
                    ),
                )
            }
            .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) { finished() }
            .semantics { contentDescription = "RideDeck is starting. Tap to skip." },
        contentAlignment = Alignment.Center,
    ) {
        // 288 dp matches the system splash icon, so the first frame lines up with it.
        RideDeckLogo(
            gauge = gauge.value,
            modifier = Modifier
                .size(288.dp)
                .graphicsLayer {
                    val s = 1f - 0.12f * reveal.value
                    scaleX = s
                    scaleY = s
                    translationY = -56.dp.toPx() * reveal.value
                },
        )
        Column(
            Modifier
                .offset(y = 120.dp)
                .graphicsLayer {
                    alpha = reveal.value
                    translationY = 24.dp.toPx() * (1f - reveal.value)
                },
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Text(
                stringResource(R.string.app_name),
                style = TextStyle(fontSize = 44.sp, fontWeight = FontWeight.ExtraBold, letterSpacing = (-1).sp),
                color = Color.White,
            )
            Text(
                stringResource(R.string.app_tagline),
                style = TextStyle(fontSize = 18.sp, fontWeight = FontWeight.SemiBold),
                color = AccentBlue,
                modifier = Modifier.padding(top = 4.dp),
            )
        }
    }
}

/**
 * The RideDeck logo (speedometer gauge over a sound wave) drawn on the same 108-unit grid as the
 * launcher icon (store/logo.py). [gauge] 0..1 sweeps the needle and the lit arc; a little over 1
 * is allowed for the spring overshoot.
 */
@Composable
fun RideDeckLogo(gauge: Float, modifier: Modifier = Modifier) {
    Canvas(modifier) {
        val u = size.minDimension / 108f
        val origin = Offset((size.width - 108f * u) / 2f, (size.height - 108f * u) / 2f)
        drawLogo(u, origin, gauge.coerceIn(0f, 1.3f))
    }
}

private fun DrawScope.drawLogo(u: Float, origin: Offset, gauge: Float) {
    fun p(x: Float, y: Float) = Offset(origin.x + x * u, origin.y + y * u)
    val cx = 54f
    val cy = 50f
    val r = 24f
    val stroke = 8f * u
    val accent = Brush.linearGradient(listOf(AccentBlue, AccentLight), start = p(30f, 80f), end = p(78f, 28f))
    val arcTopLeft = p(cx - r, cy - r)
    val arcSize = Size(2 * r * u, 2 * r * u)

    // Track and lit arc: 270 degrees starting bottom-left; the logo rests at 183 degrees lit.
    drawArc(Color.White.copy(alpha = 0.13f), 135f, 270f, false, arcTopLeft, arcSize, style = Stroke(stroke, cap = StrokeCap.Round))
    val sweep = (183f * gauge).coerceIn(0.5f, 270f)
    drawArc(accent, 135f, sweep, false, arcTopLeft, arcSize, style = Stroke(stroke, cap = StrokeCap.Round))

    fun polar(deg: Float, radius: Float): Offset {
        val t = Math.toRadians(deg.toDouble())
        return p(cx + radius * cos(t).toFloat(), cy + radius * sin(t).toFloat())
    }
    for (deg in 135..405 step 45) {
        drawLine(Color.White.copy(alpha = 0.55f), polar(deg.toFloat(), r - 8.5f), polar(deg.toFloat(), r - 11.5f), 1.8f * u, StrokeCap.Round)
    }

    val a = 135f + sweep
    val needle = Path().apply {
        val l = polar(a + 90f, 3.6f)
        val tip = polar(a, 20f)
        val rg = polar(a - 90f, 3.6f)
        val back = polar(a + 180f, 5f)
        moveTo(l.x, l.y)
        lineTo(tip.x, tip.y)
        lineTo(rg.x, rg.y)
        lineTo(back.x, back.y)
        close()
    }
    drawPath(needle, Color.White)
    drawCircle(Color.White, 4.6f * u, p(cx, cy))
    drawCircle(AccentBlue, 1.9f * u, p(cx, cy))

    // Sound wave: bars bounce with the gauge.
    val xs = floatArrayOf(43f, 48.5f, 54f, 59.5f, 65f)
    val hs = floatArrayOf(4.5f, 10f, 13f, 8f, 4f)
    for (i in xs.indices) {
        val h = (hs[i] * (0.55f + 0.45f * gauge)).coerceAtLeast(3.6f)
        drawRoundRect(
            accent,
            topLeft = p(xs[i] - 1.8f, 76.5f - h / 2f),
            size = Size(3.6f * u, h * u),
            cornerRadius = CornerRadius(1.8f * u, 1.8f * u),
        )
    }
}
