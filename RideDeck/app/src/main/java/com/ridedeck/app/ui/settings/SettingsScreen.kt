package com.ridedeck.app.ui.settings

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.animateContentSize
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.outlined.BatteryAlert
import androidx.compose.material.icons.outlined.BatteryFull
import androidx.compose.material.icons.outlined.Dashboard
import androidx.compose.material.icons.outlined.Map
import androidx.compose.material.icons.outlined.MusicNote
import androidx.compose.material.icons.outlined.Navigation
import androidx.compose.material.icons.outlined.NotificationsActive
import androidx.compose.material.icons.outlined.Palette
import androidx.compose.material.icons.outlined.Shield
import androidx.compose.material.icons.outlined.Speed
import androidx.compose.material.icons.outlined.Timeline
import androidx.compose.material3.Surface
import androidx.compose.material3.IconButton
import androidx.compose.material.icons.filled.KeyboardArrowDown
import androidx.compose.material.icons.filled.KeyboardArrowUp
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.style.TextAlign
import com.ridedeck.app.ui.components.ScreenTitle
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.selection.toggleable
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.SegmentedButton
import androidx.compose.material3.SegmentedButtonDefaults
import androidx.compose.material3.SingleChoiceSegmentedButtonRow
import androidx.compose.material3.Slider
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.unit.dp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.ridedeck.app.BuildConfig
import com.ridedeck.app.data.prefs.MapStyle
import com.ridedeck.app.ui.startup.RideDeckLogo
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Check
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Icon
import com.ridedeck.app.data.prefs.RideStat
import com.ridedeck.app.data.prefs.RideView
import com.ridedeck.app.data.prefs.ThemeMode
import com.ridedeck.app.ui.components.GloveTouchTarget
import com.ridedeck.app.ui.components.LinkText
import com.ridedeck.app.ui.theme.GoGreen
import androidx.lifecycle.compose.LifecycleResumeEffect
import com.ridedeck.core.format.Units
import com.ridedeck.core.model.SpeedUnit
import kotlin.math.roundToInt

@Composable
fun SettingsScreen(
    onOpenPermissions: () -> Unit,
    onOpenPrivacy: () -> Unit,
    viewModel: SettingsViewModel = hiltViewModel(),
) {
    val state by viewModel.uiState.collectAsStateWithLifecycle()
    // Setup steps finish in other apps, so re-check them whenever the screen comes back.
    LifecycleResumeEffect(Unit) {
        viewModel.refreshIntegrations()
        onPauseOrDispose { }
    }
    val p = state.prefs

    LazyColumn(
        Modifier.fillMaxSize(),
        contentPadding = androidx.compose.foundation.layout.PaddingValues(start = 16.dp, end = 16.dp, top = 12.dp, bottom = 32.dp),
        verticalArrangement = Arrangement.spacedBy(20.dp),
    ) {
        item { ScreenTitle("Settings") }

        // What the Ride screen shows. Turning the phone sideways switches to the landscape set,
        // so each orientation remembers its own combination.
        item {
            Section("Ride screen layout", Icons.Outlined.Dashboard) {
                ViewToggles(
                    label = "Portrait",
                    views = p.views(landscape = false),
                    onToggle = { view, on -> viewModel.setView(view, on, landscape = false) },
                )
                SectionDivider()
                ViewToggles(
                    label = "Landscape (remembered separately)",
                    views = p.views(landscape = true),
                    onToggle = { view, on -> viewModel.setView(view, on, landscape = true) },
                )
                SectionDivider()
                SwitchRow(
                    "Speedometer floats over the map",
                    p.floatingSpeedometer,
                    viewModel::setFloatingSpeedometer,
                    supporting = "Drag it anywhere on the map. Turn off to give the speedometer its own panel.",
                )
                SectionDivider()
                PanelOrder(p.panelOrder, onMove = viewModel::movePanel)
            }
        }

        item {
            Section("Speedometer stats", Icons.Outlined.Speed) {
                SwitchRow("Distance", p.showDistance, { viewModel.setStatVisible(RideStat.DISTANCE, it) })
                SwitchRow("Moving time", p.showTime, { viewModel.setStatVisible(RideStat.TIME, it) })
                SwitchRow("Average speed", p.showAverage, { viewModel.setStatVisible(RideStat.AVERAGE, it) })
                SwitchRow("Max speed", p.showMax, { viewModel.setStatVisible(RideStat.MAX, it) })
                SwitchRow("Odometer", p.showOdometer, { viewModel.setStatVisible(RideStat.ODOMETER, it) })
            }
        }

        item {
            Section("Display", Icons.Outlined.Palette) {
                Choice(
                    label = "Units",
                    options = listOf(SpeedUnit.MPH to "mph", SpeedUnit.KMH to "km/h"),
                    selected = p.speedUnit,
                    onSelect = viewModel::setUnit,
                )
                Choice(
                    label = "Theme",
                    options = listOf(ThemeMode.OLED to "OLED black", ThemeMode.DARK to "Dark", ThemeMode.SYSTEM to "System"),
                    selected = p.themeMode,
                    onSelect = viewModel::setTheme,
                )
            }
        }

        item {
            Section("Map and directions", Icons.Outlined.Map) {
                Choice(
                    label = "Map look",
                    options = listOf(
                        MapStyle.COLOR to "Color",
                        MapStyle.AUTO to "Auto",
                        MapStyle.CLEAN to "Clean",
                        MapStyle.DARK to "Dark",
                    ),
                    selected = p.mapStyle,
                    onSelect = viewModel::setMapStyle,
                )
                SectionDivider()
                SwitchRow(
                    "Spoken directions",
                    p.voiceDirections,
                    viewModel::setVoiceDirections,
                    supporting = "Announces each turn while you navigate. Music turns down while it speaks.",
                )
            }
        }

        item {
            Section("Tracking", Icons.Outlined.Timeline) {
                SwitchRow("Auto-pause when stopped", p.autoPauseEnabled, viewModel::setAutoPause)
                SwitchRow("Keep screen on during rides", p.keepScreenOn, viewModel::setKeepScreenOn)
                SwitchRow(
                    "Ride Mode",
                    p.rideModeLockEnabled,
                    viewModel::setRideModeLock,
                    supporting = "Locks search, history and settings while you're moving.",
                )
            }
        }

        item {
            Section("Speed alert", Icons.Outlined.NotificationsActive) {
                SwitchRow("Alert when over the limit", p.speedAlertEnabled, viewModel::setSpeedAlertEnabled)
                AnimatedVisibility(p.speedAlertEnabled) {
                    Column {
                        SpeedLimitSlider(p.speedAlertLimitMps, p.speedUnit, viewModel::setSpeedAlertLimit)
                        SwitchRow("Sound", p.speedAlertSound, viewModel::setSpeedAlertSound)
                        SwitchRow("Vibrate", p.speedAlertVibrate, viewModel::setSpeedAlertVibrate)
                    }
                }
            }
        }

        item {
            Section(
                if (state.batteryUnrestricted) "Battery: all set" else "Battery",
                if (state.batteryUnrestricted) Icons.Outlined.BatteryFull else Icons.Outlined.BatteryAlert,
            ) {
                SetupSteps(
                    buildList {
                        add(
                            SetupStep(
                                "Let RideDeck run without battery limits, so a ride keeps recording and music keeps playing with " +
                                    "the screen off. In the list, switch to All apps, pick RideDeck and choose Don't optimize.",
                                done = state.batteryUnrestricted,
                                action = if (state.batteryUnrestricted) null else "Open battery settings" to viewModel::openBatterySettings,
                            ),
                        )
                        if (state.isSamsung) {
                            add(
                                SetupStep(
                                    "On Samsung Galaxy phones, also open App info → Battery and choose Unrestricted. One UI can " +
                                        "otherwise put RideDeck to sleep during a long ride.",
                                    done = false,
                                    action = "Open App info" to viewModel::openAppInfo,
                                ),
                            )
                        }
                    },
                )
            }
        }

        item {
            Section("Music", Icons.Outlined.MusicNote) {
                SwitchRow("Show synced lyrics", p.lyricsEnabled, viewModel::setLyricsEnabled)
                Note(
                    "The built-in player plays the songs stored on your phone (MP3, FLAC, OGG, M4A, WAV). " +
                        "Copy music into the Music folder and it shows up in the library.",
                )
                SectionDivider()
                SwitchRow(
                    "Control Spotify from RideDeck",
                    p.spotifyEnabled,
                    viewModel::setSpotifyEnabled,
                    supporting = "Adds a Spotify tab to the music panel. No account linking or API key needed.",
                )
                AnimatedVisibility(p.spotifyEnabled) {
                    SetupSteps(
                        listOf(
                            SetupStep(
                                "Install Spotify and log in",
                                done = state.spotifyInstalled,
                                action = if (state.spotifyInstalled) null else "Get Spotify" to viewModel::openSpotify,
                            ),
                            SetupStep(
                                "Allow notification access for RideDeck. It lets RideDeck see what Spotify is playing and " +
                                    "send play, pause and skip. Find RideDeck in the list and switch it on.",
                                done = state.spotifyAccess,
                                action = if (state.spotifyAccess) null else "Allow access" to viewModel::openNotificationAccess,
                            ),
                            SetupStep(
                                "If Android says the setting is restricted (Android 13 and newer, for apps installed from an APK): " +
                                    "open App info for RideDeck, tap ⋮ at the top right, choose \"Allow restricted settings\", then repeat step 2.",
                                done = state.spotifyAccess,
                                action = if (state.spotifyAccess) null else "Open App info" to viewModel::openAppInfo,
                            ),
                            SetupStep(
                                "Play something in Spotify, open the music panel and tap the Spotify tab.",
                                done = false,
                            ),
                        ),
                    )
                }
            }
        }

        item {
            Section("Google Maps (optional)", Icons.Outlined.Navigation) {
                SwitchRow(
                    "Navigate with Google Maps",
                    p.googleMapsEnabled,
                    viewModel::setGoogleMapsEnabled,
                    supporting = "Adds a \"Start in Google Maps\" button to directions, for Google's own cycling navigation.",
                )
                AnimatedVisibility(p.googleMapsEnabled) {
                    SetupSteps(
                        listOf(
                            SetupStep(
                                "Install Google Maps",
                                done = state.googleMapsInstalled,
                                action = if (state.googleMapsInstalled) null else "Get Google Maps" to viewModel::openGoogleMapsStore,
                            ),
                            SetupStep("Search for a destination on the Ride screen and pick it.", done = false),
                            SetupStep(
                                "Tap \"Start in Google Maps\". Google Maps opens with spoken turn-by-turn steps for cycling. " +
                                    "RideDeck keeps recording your ride in the background; come back to it any time.",
                                done = false,
                            ),
                        ),
                    )
                }
            }
        }

        item {
            Section("Privacy", Icons.Outlined.Shield) {
                Column(Modifier.padding(horizontal = 16.dp, vertical = 4.dp)) {
                    LinkText("Privacy policy", onClick = onOpenPrivacy)
                    LinkText("Review permissions", onClick = onOpenPermissions)
                }
            }
        }

        item { About() }
    }


    state.message?.let { msg ->
        AlertDialog(
            onDismissRequest = viewModel::dismissMessage,
            text = { Text(msg) },
            confirmButton = { TextButton(onClick = viewModel::dismissMessage) { Text("OK") } },
        )
    }
}

/** Map, Speed and Music on/off for one orientation; the last one on can't be turned off. */
@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun ViewToggles(label: String, views: Triple<Boolean, Boolean, Boolean>, onToggle: (RideView, Boolean) -> Unit) {
    val (map, speed, music) = views
    val onlyOne = listOf(map, speed, music).count { it } == 1
    Column(Modifier.padding(horizontal = 16.dp, vertical = 8.dp)) {
        Text(label, style = MaterialTheme.typography.bodyLarge, modifier = Modifier.padding(bottom = 6.dp))
        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            listOf(
                Triple(RideView.MAP, "Map", map),
                Triple(RideView.SPEEDOMETER, "Speedometer", speed),
                Triple(RideView.MUSIC, "Music", music),
            ).forEach { (view, name, on) ->
                FilterChip(
                    selected = on,
                    onClick = { onToggle(view, !on) },
                    enabled = !(onlyOne && on),
                    label = { Text(name, maxLines = 1) },
                    leadingIcon = if (on) {
                        { Icon(Icons.Default.Check, contentDescription = null, modifier = Modifier.size(FilterChipDefaults.IconSize)) }
                    } else {
                        null
                    },
                    modifier = Modifier.heightIn(min = 48.dp),
                )
            }
        }
    }
}

/** A titled group of settings on one rounded card. */
@Composable
private fun Section(title: String, icon: ImageVector, content: @Composable ColumnScope.() -> Unit) {
    Column {
        Row(
            Modifier.padding(start = 6.dp, bottom = 8.dp).semantics(mergeDescendants = true) { heading() },
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Icon(icon, contentDescription = null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(20.dp))
            Spacer(Modifier.width(8.dp))
            Text(title.uppercase(), style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.primary)
        }
        Surface(
            color = MaterialTheme.colorScheme.surfaceContainer,
            shape = MaterialTheme.shapes.large,
            border = BorderStroke(1.dp, MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.5f)),
            modifier = Modifier.fillMaxWidth(),
        ) {
            Column(Modifier.padding(vertical = 6.dp).animateContentSize(), content = content)
        }
    }
}

/**
 * The order of the Ride screen panels, top to bottom (left to right in landscape), with arrows to
 * move each one. Swapping panels from the Ride screen's dividers changes the same order.
 */
@Composable
private fun PanelOrder(order: List<RideView>, onMove: (RideView, Int) -> Unit) {
    Column(Modifier.padding(horizontal = 16.dp, vertical = 8.dp)) {
        Text("Panel order", style = MaterialTheme.typography.bodyLarge)
        Text(
            "Top to bottom, or left to right when turned sideways. On the Ride screen you can also press and hold the divider between two panels, then drag, to swap them.",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(top = 2.dp, bottom = 8.dp),
        )
        order.forEachIndexed { index, view ->
            val name = when (view) {
                RideView.MAP -> "Map"
                RideView.SPEEDOMETER -> "Speedometer"
                RideView.MUSIC -> "Music"
            }
            Surface(
                color = MaterialTheme.colorScheme.surfaceContainerHigh,
                shape = MaterialTheme.shapes.medium,
                modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp).animateContentSize(),
            ) {
                Row(Modifier.padding(start = 14.dp, end = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                    Text(
                        "${index + 1}",
                        style = MaterialTheme.typography.titleMedium,
                        color = MaterialTheme.colorScheme.primary,
                        modifier = Modifier.width(24.dp),
                    )
                    Text(name, style = MaterialTheme.typography.titleMedium, modifier = Modifier.weight(1f))
                    IconButton(onClick = { onMove(view, -1) }, enabled = index > 0, modifier = Modifier.size(48.dp)) {
                        Icon(Icons.Default.KeyboardArrowUp, contentDescription = "Move $name up")
                    }
                    IconButton(onClick = { onMove(view, 1) }, enabled = index < order.lastIndex, modifier = Modifier.size(48.dp)) {
                        Icon(Icons.Default.KeyboardArrowDown, contentDescription = "Move $name down")
                    }
                }
            }
        }
    }
}

@Composable
private fun SectionDivider() {
    HorizontalDivider(
        Modifier.padding(horizontal = 16.dp, vertical = 4.dp),
        color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.6f),
    )
}

@Composable
private fun Note(text: String) {
    Text(
        text,
        style = MaterialTheme.typography.bodyMedium,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
        modifier = Modifier.padding(horizontal = 16.dp, vertical = 6.dp),
    )
}

@Composable
private fun About() {
    Column(
        Modifier.fillMaxWidth().padding(top = 8.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        RideDeckLogo(gauge = 1f, modifier = Modifier.size(72.dp))
        Text("RideDeck", style = MaterialTheme.typography.titleLarge, modifier = Modifier.padding(top = 6.dp))
        Text(
            "Version ${BuildConfig.VERSION_NAME} (${BuildConfig.VERSION_CODE})",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Text(
            "Built on open-source software and open data; Spotify and Google Maps are optional add-ons. " +
                "Maps by OpenFreeMap and MapLibre; map data © OpenStreetMap contributors (ODbL), © OpenMapTiles. " +
                "Routing by OSRM (FOSSGIS servers), search by Photon (komoot). Lyrics from LRCLIB. " +
                "Music playback by AndroidX Media3 ExoPlayer.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            textAlign = TextAlign.Center,
            modifier = Modifier.padding(horizontal = 12.dp, vertical = 10.dp),
        )
    }
}

private data class SetupStep(
    val text: String,
    val done: Boolean,
    val action: Pair<String, () -> Unit>? = null,
)

/** Numbered setup instructions with a tick for each step that is already done. */
@Composable
private fun SetupSteps(steps: List<SetupStep>) {
    Column(
        Modifier.padding(horizontal = 16.dp, vertical = 8.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        steps.forEachIndexed { index, step ->
            Row(verticalAlignment = Alignment.Top) {
                // A numbered badge that turns into a green tick once the step is done.
                Box(
                    Modifier
                        .padding(end = 12.dp)
                        .size(28.dp)
                        .background(
                            if (step.done) GoGreen.copy(alpha = 0.18f) else MaterialTheme.colorScheme.primaryContainer,
                            CircleShape,
                        ),
                    contentAlignment = Alignment.Center,
                ) {
                    if (step.done) {
                        Icon(Icons.Default.Check, contentDescription = "Done", tint = GoGreen, modifier = Modifier.size(18.dp))
                    } else {
                        Text(
                            "${index + 1}",
                            style = MaterialTheme.typography.labelLarge.copy(fontSize = MaterialTheme.typography.labelMedium.fontSize),
                            color = MaterialTheme.colorScheme.onPrimaryContainer,
                        )
                    }
                }
                Column(Modifier.weight(1f)) {
                    Text(step.text, style = MaterialTheme.typography.bodyMedium)
                    step.action?.let { (label, onClick) ->
                        OutlinedButton(
                            onClick = onClick,
                            modifier = Modifier.padding(top = 8.dp).heightIn(min = 48.dp),
                        ) { Text(label) }
                    }
                }
            }
        }
    }
}

@Composable
private fun SwitchRow(label: String, checked: Boolean, onChange: (Boolean) -> Unit, supporting: String? = null) {
    Row(
        Modifier
            .fillMaxWidth()
            .heightIn(min = GloveTouchTarget)
            .toggleable(value = checked, role = Role.Switch, onValueChange = onChange)
            .padding(horizontal = 16.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(Modifier.weight(1f).padding(end = 12.dp)) {
            Text(label, style = MaterialTheme.typography.bodyLarge)
            if (supporting != null) {
                Text(
                    supporting,
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(top = 2.dp),
                )
            }
        }
        // The whole row is the toggle; the switch is decorative for accessibility.
        Switch(checked = checked, onCheckedChange = null)
    }
}

@Composable
private fun <T> Choice(label: String, options: List<Pair<T, String>>, selected: T, onSelect: (T) -> Unit) {
    Column(Modifier.padding(horizontal = 16.dp, vertical = 8.dp)) {
        Text(label, style = MaterialTheme.typography.bodyLarge, modifier = Modifier.padding(bottom = 6.dp))
        SingleChoiceSegmentedButtonRow(Modifier.fillMaxWidth()) {
            options.forEachIndexed { index, (value, text) ->
                SegmentedButton(
                    selected = value == selected,
                    onClick = { onSelect(value) },
                    shape = SegmentedButtonDefaults.itemShape(index, options.size),
                    modifier = Modifier.heightIn(min = 56.dp),
                ) { Text(text) }
            }
        }
    }
}

@Composable
private fun SpeedLimitSlider(limitMps: Double, unit: SpeedUnit, onChange: (Float, SpeedUnit) -> Unit) {
    val range = if (unit == SpeedUnit.KMH) 10f..80f else 6f..50f
    val stored = Units.speed(limitMps, unit).toFloat().coerceIn(range)
    var value by remember(unit) { mutableFloatStateOf(stored) }
    LaunchedEffect(stored) { value = stored }
    Column(Modifier.padding(horizontal = 16.dp, vertical = 4.dp)) {
        Text("Limit: ${value.roundToInt()} ${Units.speedLabel(unit)}", style = MaterialTheme.typography.bodyLarge)
        Slider(
            value = value,
            onValueChange = { value = it.roundToInt().toFloat() },
            onValueChangeFinished = { onChange(value, unit) },
            valueRange = range,
            modifier = Modifier
                .heightIn(min = GloveTouchTarget)
                .semantics { stateDescription = "${value.roundToInt()} ${Units.speedLabel(unit)}" },
        )
    }
}

