package com.ridedeck.app.ui.main

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.material3.VerticalDivider
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
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

    // The last view left on can't be turned off, so its chip is shown as locked on.
    val onlyOne = listOf(showMap, showSpeed, showMusic).count { it } == 1

    Column(Modifier.fillMaxSize()) {
        Row(
            Modifier
                .fillMaxWidth()
                .horizontalScroll(rememberScrollState())
                .padding(horizontal = 12.dp, vertical = 4.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            ViewChip("Map", Icons.Default.Map, showMap, enabled = !(onlyOne && showMap)) { onToggleView(RideView.MAP, !showMap) }
            ViewChip("Speed", Icons.Default.Speed, showSpeed, enabled = !(onlyOne && showSpeed)) { onToggleView(RideView.SPEEDOMETER, !showSpeed) }
            ViewChip("Music", Icons.Default.MusicNote, showMusic, enabled = !(onlyOne && showMusic)) { onToggleView(RideView.MUSIC, !showMusic) }
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

        BoxWithConstraints(Modifier.weight(1f)) {
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
                    onHidePanel = { onToggleView(RideView.MUSIC, false) },
                    top = top,
                    panel = music,
                )
            }
        }
    }
}

@Composable
private fun ViewChip(label: String, icon: ImageVector, selected: Boolean, enabled: Boolean, onClick: () -> Unit) {
    FilterChip(
        selected = selected,
        onClick = onClick,
        enabled = enabled,
        label = { Text(label, maxLines = 1) },
        leadingIcon = { Icon(icon, contentDescription = null, modifier = Modifier.size(FilterChipDefaults.IconSize)) },
        modifier = Modifier
            .heightIn(min = 48.dp)
            .semantics { if (!enabled) stateDescription = "On. At least one view stays on." },
    )
}
