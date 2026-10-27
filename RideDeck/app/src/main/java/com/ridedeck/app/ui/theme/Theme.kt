package com.ridedeck.app.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.ridedeck.app.data.prefs.ThemeMode

// Accent #03B7FF. On black it has a contrast ratio of about 9:1 (WCAG AAA for text).
val AccentBlue = Color(0xFF03B7FF)
val AccentBlueLight = Color(0xFF8AD8FF)
val AlertRed = Color(0xFFFF5252)
val GoGreen = Color(0xFF00E676)

private val OledColors = darkColorScheme(
    primary = AccentBlue,
    onPrimary = Color.Black,
    primaryContainer = Color(0xFF00344D),
    onPrimaryContainer = AccentBlueLight,
    secondary = AccentBlueLight,
    onSecondary = Color.Black,
    secondaryContainer = Color(0xFF002A3D),
    onSecondaryContainer = AccentBlueLight,
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
    primary = AccentBlue,
    onPrimary = Color.Black,
    primaryContainer = Color(0xFFCDEEFF),
    onPrimaryContainer = Color(0xFF00344D),
    secondary = Color(0xFF00658F),
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

/**
 * Slightly larger body and label sizes than Material defaults, for reading at a glance, and
 * heavier, tighter headings so screens have a clear hierarchy.
 */
val RideDeckTypography = base.copy(
    headlineLarge = base.headlineLarge.copy(fontWeight = FontWeight.ExtraBold, letterSpacing = (-0.5).sp),
    headlineMedium = base.headlineMedium.copy(fontWeight = FontWeight.ExtraBold, letterSpacing = (-0.5).sp),
    headlineSmall = base.headlineSmall.copy(fontWeight = FontWeight.Bold),
    titleLarge = base.titleLarge.copy(fontWeight = FontWeight.Bold),
    titleMedium = base.titleMedium.copy(fontWeight = FontWeight.SemiBold),
    titleSmall = base.titleSmall.copy(fontWeight = FontWeight.Bold, letterSpacing = 0.6.sp),
    bodyLarge = base.bodyLarge.copy(fontSize = 18.sp),
    bodyMedium = base.bodyMedium.copy(fontSize = 16.sp),
    labelLarge = base.labelLarge.copy(fontSize = 18.sp, fontWeight = FontWeight.SemiBold),
)

/** Softer, rounder corners throughout, so cards and sheets read as one family. */
val RideDeckShapes = Shapes(
    extraSmall = RoundedCornerShape(8.dp),
    small = RoundedCornerShape(12.dp),
    medium = RoundedCornerShape(18.dp),
    large = RoundedCornerShape(24.dp),
    extraLarge = RoundedCornerShape(32.dp),
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
    MaterialTheme(colorScheme = colors, typography = RideDeckTypography, shapes = RideDeckShapes, content = content)
}

@Composable
fun isDarkTheme(themeMode: ThemeMode): Boolean =
    themeMode != ThemeMode.SYSTEM || isSystemInDarkTheme()
