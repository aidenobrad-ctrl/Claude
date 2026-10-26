package com.ridedeck.app.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp
import com.ridedeck.app.data.prefs.ThemeMode

// High-contrast palette: signal yellow on black passes WCAG AAA (contrast ratio > 15:1).
val SignalYellow = Color(0xFFFFD600)
val SignalCyan = Color(0xFF00E5FF)
val AlertRed = Color(0xFFFF5252)
val GoGreen = Color(0xFF00E676)

private val OledColors = darkColorScheme(
    primary = SignalYellow,
    onPrimary = Color.Black,
    primaryContainer = Color(0xFF3A3000),
    onPrimaryContainer = SignalYellow,
    secondary = SignalCyan,
    onSecondary = Color.Black,
    secondaryContainer = Color(0xFF00363D),
    onSecondaryContainer = SignalCyan,
    tertiary = GoGreen,
    onTertiary = Color.Black,
    error = AlertRed,
    onError = Color.Black,
    background = Color.Black,
    onBackground = Color.White,
    surface = Color.Black,
    onSurface = Color.White,
    surfaceVariant = Color(0xFF1A1A1A),
    onSurfaceVariant = Color(0xFFE0E0E0),
    surfaceContainer = Color(0xFF0D0D0D),
    surfaceContainerHigh = Color(0xFF161616),
    surfaceContainerHighest = Color(0xFF202020),
    surfaceContainerLow = Color(0xFF080808),
    surfaceContainerLowest = Color.Black,
    outline = Color(0xFF9E9E9E),
    outlineVariant = Color(0xFF424242),
)

private val DarkColors = OledColors.copy(
    background = Color(0xFF121212),
    surface = Color(0xFF121212),
    surfaceContainer = Color(0xFF1E1E1E),
    surfaceContainerLow = Color(0xFF1A1A1A),
    surfaceContainerLowest = Color(0xFF121212),
    surfaceContainerHigh = Color(0xFF242424),
    surfaceContainerHighest = Color(0xFF2C2C2C),
    surfaceVariant = Color(0xFF2A2A2A),
)

private val LightColors = lightColorScheme(
    primary = Color(0xFF5C4B00),
    onPrimary = Color.White,
    primaryContainer = SignalYellow,
    onPrimaryContainer = Color.Black,
    secondary = Color(0xFF00616B),
    onSecondary = Color.White,
    tertiary = Color(0xFF006D35),
    error = Color(0xFFB3261E),
    background = Color.White,
    onBackground = Color.Black,
    surface = Color.White,
    onSurface = Color.Black,
    onSurfaceVariant = Color(0xFF303030),
)

private val base = Typography()

/** Slightly larger body and label sizes than Material defaults, for reading at a glance. */
val RideDeckTypography = base.copy(
    bodyLarge = base.bodyLarge.copy(fontSize = 18.sp),
    bodyMedium = base.bodyMedium.copy(fontSize = 16.sp),
    labelLarge = base.labelLarge.copy(fontSize = 18.sp, fontWeight = FontWeight.SemiBold),
    titleLarge = base.titleLarge.copy(fontWeight = FontWeight.Bold),
)

/** Tabular figures so digits do not shift width as the speed changes. */
val NumericStyle = TextStyle(
    fontFamily = FontFamily.Default,
    fontWeight = FontWeight.Bold,
    fontFeatureSettings = "tnum",
)

@Composable
fun RideDeckTheme(themeMode: ThemeMode, content: @Composable () -> Unit) {
    val colors = when (themeMode) {
        ThemeMode.OLED -> OledColors
        ThemeMode.DARK -> DarkColors
        ThemeMode.SYSTEM -> if (isSystemInDarkTheme()) DarkColors else LightColors
    }
    MaterialTheme(colorScheme = colors, typography = RideDeckTypography, content = content)
}

@Composable
fun isDarkTheme(themeMode: ThemeMode): Boolean =
    themeMode != ThemeMode.SYSTEM || isSystemInDarkTheme()
