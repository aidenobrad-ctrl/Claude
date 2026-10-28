package com.ridedeck.app.ui.main

import android.content.res.Configuration
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.unit.dp
import com.ridedeck.app.data.prefs.MusicSource
import com.ridedeck.app.data.prefs.RideView
import com.ridedeck.app.data.prefs.UserPreferences
import com.ridedeck.app.ui.home.SpeedOverlay
import com.ridedeck.app.ui.map.MapScreen
import com.ridedeck.app.ui.music.MusicPanel

/**
 * The Ride screen shows any combination of map, speedometer and music, chosen in Settings (with
 * separate choices for portrait and landscape), as panels in the order the rider picked. The
 * speedometer either floats over the map, where it can be dragged anywhere, or takes a panel of
 * its own. Panels stack top to bottom, or side by side in landscape on a wide screen, and can be
 * resized and swapped from the handles between them.
 */
@Composable
fun RideScreen(
    prefs: UserPreferences,
    rideLocked: Boolean,
    onMusicFraction: (Float) -> Unit,
    onMusicSource: (MusicSource) -> Unit,
    onSwapPanels: (RideView, RideView) -> Unit,
    onOpenPermissions: () -> Unit,
) {
    val landscape = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE
    val (showMap, showSpeed, showMusic) = prefs.views(landscape)
    val floating = showMap && showSpeed && prefs.floatingSpeedometer

    val panels = prefs.panelOrder
        .filter {
            when (it) {
                RideView.MAP -> showMap
                RideView.SPEEDOMETER -> showSpeed && !floating
                RideView.MUSIC -> showMusic
            }
        }
        .map { view ->
            Panel(view) {
                when (view) {
                    RideView.MAP -> MapScreen(rideLocked = rideLocked, showSpeedOverlay = floating, onOpenPermissions = onOpenPermissions)
                    RideView.SPEEDOMETER -> SpeedOverlay(rideLocked = rideLocked, onOpenPermissions = onOpenPermissions, fullScreen = true)
                    RideView.MUSIC -> MusicPanel(
                        lyricsEnabled = prefs.lyricsEnabled,
                        spotifyEnabled = prefs.spotifyEnabled,
                        source = prefs.musicSource,
                        onSourceChange = onMusicSource,
                    )
                }
            }
        }

    BoxWithConstraints(Modifier.fillMaxSize()) {
        // Landscape on a phone, a tablet or a wide window: panels sit side by side instead of
        // squeezing into short strips.
        val sideBySide = maxWidth > maxHeight && maxWidth >= 600.dp
        if (panels.size == 1) {
            panels.single().content()
        } else {
            PanelStack(
                panels = panels,
                vertical = !sideBySide,
                lastFraction = if (sideBySide) 0.42f else prefs.musicPanelFraction,
                onLastFractionCommitted = { if (!sideBySide) onMusicFraction(it) },
                onSwap = onSwapPanels,
            )
        }
    }
}
