package com.ridedeck.app.ui.main

import android.content.res.Configuration
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.VerticalDivider
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.unit.dp
import com.ridedeck.app.data.prefs.MusicSource
import com.ridedeck.app.data.prefs.UserPreferences
import com.ridedeck.app.ui.home.SpeedOverlay
import com.ridedeck.app.ui.map.MapScreen
import com.ridedeck.app.ui.music.MusicPanel

/**
 * The Ride screen shows any combination of map, speedometer and music, chosen in Settings (with
 * separate choices for portrait and landscape). Map and speedometer together means the
 * speedometer floats over the map; music takes the resizable bottom panel when something else is
 * shown, or the whole screen on its own.
 */
@Composable
fun RideScreen(
    prefs: UserPreferences,
    rideLocked: Boolean,
    onMusicFraction: (Float) -> Unit,
    onMusicSource: (MusicSource) -> Unit,
    onOpenPermissions: () -> Unit,
) {
    val landscape = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE
    val (showMap, showSpeed, showMusic) = prefs.views(landscape)

    val music: @Composable () -> Unit = {
        MusicPanel(
            lyricsEnabled = prefs.lyricsEnabled,
            spotifyEnabled = prefs.spotifyEnabled,
            source = prefs.musicSource,
            onSourceChange = onMusicSource,
        )
    }
    val top: (@Composable () -> Unit)? = when {
        showMap -> {
            { MapScreen(rideLocked = rideLocked, showSpeedOverlay = showSpeed, onOpenPermissions = onOpenPermissions) }
        }
        showSpeed -> {
            { SpeedOverlay(rideLocked = rideLocked, onOpenPermissions = onOpenPermissions, fullScreen = true) }
        }
        else -> null
    }

    BoxWithConstraints(Modifier.fillMaxSize()) {
        // Landscape on a phone, a tablet or a wide window: music sits beside the map or
        // speedometer instead of squeezing both into a short strip.
        val sideBySide = maxWidth > maxHeight && maxWidth >= 600.dp
        if (top == null) {
            music()
        } else if (sideBySide && showMusic) {
            Row(Modifier.fillMaxSize()) {
                Box(Modifier.weight(0.58f).fillMaxHeight()) { top() }
                VerticalDivider()
                Box(Modifier.weight(0.42f).fillMaxHeight()) { music() }
            }
        } else {
            SplitPanelLayout(
                showPanel = showMusic,
                fraction = prefs.musicPanelFraction,
                onFractionCommitted = onMusicFraction,
                top = top,
                panel = music,
            )
        }
    }
}
