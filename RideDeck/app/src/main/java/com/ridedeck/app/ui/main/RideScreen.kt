package com.ridedeck.app.ui.main

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Map
import androidx.compose.material.icons.filled.MusicNote
import androidx.compose.material.icons.filled.Speed
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.unit.dp
import com.ridedeck.app.data.prefs.RideView
import com.ridedeck.app.data.prefs.UserPreferences
import com.ridedeck.app.ui.home.SpeedOverlay
import com.ridedeck.app.ui.map.MapScreen
import com.ridedeck.app.ui.music.MusicPanel

/**
 * The Ride screen shows any combination of map, speedometer and music, picked with the chips at
 * the top. Map and speedometer together means the speedometer floats over the map; music always
 * takes the resizable bottom panel when something else is shown, or the whole screen on its own.
 */
@Composable
fun RideScreen(
    prefs: UserPreferences,
    rideLocked: Boolean,
    onToggleView: (RideView, Boolean) -> Unit,
    onMusicFraction: (Float) -> Unit,
    onMusicSource: (com.ridedeck.app.data.prefs.MusicSource) -> Unit,
    onOpenPermissions: () -> Unit,
) {
    val showMap = prefs.showMap
    val showSpeed = prefs.showSpeedometer
    val showMusic = prefs.musicPanelVisible

    Column(Modifier.fillMaxSize()) {
        Row(
            Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 4.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            ViewChip("Map", Icons.Default.Map, showMap) { onToggleView(RideView.MAP, !showMap) }
            ViewChip("Speed", Icons.Default.Speed, showSpeed) { onToggleView(RideView.SPEEDOMETER, !showSpeed) }
            ViewChip("Music", Icons.Default.MusicNote, showMusic) { onToggleView(RideView.MUSIC, !showMusic) }
        }

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

        Box(Modifier.weight(1f)) {
            if (top == null) {
                music()
            } else {
                SplitPanelLayout(
                    showPanel = showMusic,
                    fraction = prefs.musicPanelFraction,
                    onFractionCommitted = onMusicFraction,
                    onHidePanel = { onToggleView(RideView.MUSIC, false) },
                    top = top,
                    panel = music,
                )
            }
        }
    }
}

@Composable
private fun ViewChip(label: String, icon: ImageVector, selected: Boolean, onClick: () -> Unit) {
    FilterChip(
        selected = selected,
        onClick = onClick,
        label = { Text(label) },
        leadingIcon = { Icon(icon, contentDescription = null, modifier = Modifier.padding(0.dp)) },
        colors = FilterChipDefaults.filterChipColors(),
        modifier = Modifier.heightIn(min = 48.dp),
    )
}
