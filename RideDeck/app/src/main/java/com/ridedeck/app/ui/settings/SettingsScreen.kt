package com.ridedeck.app.ui.settings

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
        contentPadding = androidx.compose.foundation.layout.PaddingValues(vertical = 12.dp),
    ) {
        item { Header("Settings", large = true) }

        // What the Ride screen shows. Turning the phone sideways switches to the landscape set,
        // so each orientation remembers its own combination.
        item { Header("Ride screen views") }
        item {
            ViewToggles(
                label = "Portrait",
                views = p.views(landscape = false),
                onToggle = { view, on -> viewModel.setView(view, on, landscape = false) },
            )
        }
        item {
            ViewToggles(
                label = "Landscape (remembered separately)",
                views = p.views(landscape = true),
                onToggle = { view, on -> viewModel.setView(view, on, landscape = true) },
            )
        }

        item { Header("Speedometer stats") }
        item { SwitchRow("Distance", p.showDistance, { viewModel.setStatVisible(RideStat.DISTANCE, it) }) }
        item { SwitchRow("Moving time", p.showTime, { viewModel.setStatVisible(RideStat.TIME, it) }) }
        item { SwitchRow("Average speed", p.showAverage, { viewModel.setStatVisible(RideStat.AVERAGE, it) }) }
        item { SwitchRow("Max speed", p.showMax, { viewModel.setStatVisible(RideStat.MAX, it) }) }
        item { SwitchRow("Odometer", p.showOdometer, { viewModel.setStatVisible(RideStat.ODOMETER, it) }) }

        item { Header("Display") }
        item {
            Choice(
                label = "Units",
                options = listOf(SpeedUnit.KMH to "km/h", SpeedUnit.MPH to "mph"),
                selected = p.speedUnit,
                onSelect = viewModel::setUnit,
            )
        }
        item {
            Choice(
                label = "Theme",
                options = listOf(ThemeMode.OLED to "OLED black", ThemeMode.DARK to "Dark", ThemeMode.SYSTEM to "System"),
                selected = p.themeMode,
                onSelect = viewModel::setTheme,
            )
        }

        item { Header("Tracking") }
        item { SwitchRow("Auto-pause when stopped", p.autoPauseEnabled, viewModel::setAutoPause) }
        item { SwitchRow("Keep screen on during sessions", p.keepScreenOn, viewModel::setKeepScreenOn) }
        item {
            SwitchRow(
                "Ride Mode",
                p.rideModeLockEnabled,
                viewModel::setRideModeLock,
                supporting = "Locks search, history and settings while you're moving.",
            )
        }

        item { Header("Battery") }
        item {
            SetupSteps(
                buildList {
                    add(
                        SetupStep(
                            "Let RideDeck run without battery limits, so a ride keeps recording and music keeps playing with " +
                                "the screen off. In the list, switch to All apps, pick RideDeck and choose Don't optimise.",
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
                title = if (state.batteryUnrestricted) "Battery: all set" else "Battery",
            )
        }

        item { Header("Speed alert") }
        item { SwitchRow("Alert when over the limit", p.speedAlertEnabled, viewModel::setSpeedAlertEnabled) }
        if (p.speedAlertEnabled) {
            item { SpeedLimitSlider(p.speedAlertLimitMps, p.speedUnit, viewModel::setSpeedAlertLimit) }
            item { SwitchRow("Sound", p.speedAlertSound, viewModel::setSpeedAlertSound) }
            item { SwitchRow("Vibrate", p.speedAlertVibrate, viewModel::setSpeedAlertVibrate) }
        }

        item { Header("Map") }
        item {
            Choice(
                label = "Map look",
                options = listOf(
                    MapStyle.AUTO to "Auto",
                    MapStyle.COLOURFUL to "Colour",
                    MapStyle.CLEAN to "Clean",
                    MapStyle.DARK to "Dark",
                ),
                selected = p.mapStyle,
                onSelect = viewModel::setMapStyle,
            )
        }
        item {
            SwitchRow(
                "Spoken directions",
                p.voiceDirections,
                viewModel::setVoiceDirections,
                supporting = "Announces each turn while you navigate. Music turns down while it speaks.",
            )
        }

        item { Header("Music") }
        item { SwitchRow("Show synced lyrics", p.lyricsEnabled, viewModel::setLyricsEnabled) }
        item {
            Text(
                "The built-in player plays the songs stored on your phone (MP3, FLAC, OGG, M4A, WAV). " +
                    "Copy music into the Music folder and it shows up in the library.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(horizontal = 20.dp, vertical = 8.dp),
            )
        }

        item { Header("Spotify (optional)") }
        item {
            SwitchRow(
                "Control Spotify from RideDeck",
                p.spotifyEnabled,
                viewModel::setSpotifyEnabled,
                supporting = "Adds a Spotify tab to the music panel. No account linking or API key needed.",
            )
        }
        if (p.spotifyEnabled) {
            item {
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

        item { Header("Google Maps (optional)") }
        item {
            SwitchRow(
                "Navigate with Google Maps",
                p.googleMapsEnabled,
                viewModel::setGoogleMapsEnabled,
                supporting = "Adds a \"Start in Google Maps\" button to directions, for Google's voice turn-by-turn cycling navigation.",
            )
        }
        if (p.googleMapsEnabled) {
            item {
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

        item { Header("Privacy") }
        item {
            LinkText("Privacy policy", onClick = onOpenPrivacy, modifier = Modifier.padding(horizontal = 20.dp))
        }
        item {
            TextButton(
                onClick = onOpenPermissions,
                modifier = Modifier.padding(horizontal = 12.dp).heightIn(min = GloveTouchTarget),
            ) { Text("Review permissions") }
        }

        item { Header("About") }
        item {
            Row(
                Modifier.padding(horizontal = 20.dp, vertical = 8.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                RideDeckLogo(gauge = 1f, modifier = Modifier.size(56.dp))
                Column(Modifier.padding(start = 12.dp)) {
                    Text("RideDeck", style = MaterialTheme.typography.titleLarge)
                    Text(
                        "Version ${BuildConfig.VERSION_NAME} (${BuildConfig.VERSION_CODE})",
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            }
        }
        item {
            Text(
                "RideDeck is built on open-source software and open data; Spotify and Google Maps are optional add-ons. " +
                    "Maps by OpenFreeMap and MapLibre; map data © OpenStreetMap contributors (ODbL), © OpenMapTiles. " +
                    "Routing by OSRM (FOSSGIS servers), search by Photon (komoot). Lyrics from LRCLIB. " +
                    "Music playback by AndroidX Media3 ExoPlayer.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(horizontal = 20.dp, vertical = 8.dp),
            )
        }
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
    Column(Modifier.padding(horizontal = 20.dp, vertical = 8.dp)) {
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

@Composable
private fun Header(text: String, large: Boolean = false) {
    Text(
        text,
        style = if (large) MaterialTheme.typography.headlineMedium else MaterialTheme.typography.titleSmall,
        color = if (large) MaterialTheme.colorScheme.onSurface else MaterialTheme.colorScheme.primary,
        modifier = Modifier
            .padding(start = 20.dp, end = 20.dp, top = if (large) 4.dp else 20.dp, bottom = 4.dp)
            .semantics { heading() },
    )
}

private data class SetupStep(
    val text: String,
    val done: Boolean,
    val action: Pair<String, () -> Unit>? = null,
)

/** Numbered setup instructions with a tick for each step that is already done. */
@Composable
private fun SetupSteps(steps: List<SetupStep>, title: String = "Setup") {
    Column(
        Modifier.padding(horizontal = 20.dp, vertical = 4.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Text(title, style = MaterialTheme.typography.titleSmall)
        steps.forEachIndexed { index, step ->
            Row(verticalAlignment = Alignment.Top) {
                Text(
                    if (step.done) "✓" else "${index + 1}.",
                    style = MaterialTheme.typography.titleMedium,
                    color = if (step.done) GoGreen else MaterialTheme.colorScheme.onSurface,
                    modifier = Modifier.padding(end = 10.dp),
                )
                Column(Modifier.weight(1f)) {
                    Text(step.text, style = MaterialTheme.typography.bodyMedium)
                    step.action?.let { (label, onClick) ->
                        OutlinedButton(
                            onClick = onClick,
                            modifier = Modifier.padding(top = 6.dp).heightIn(min = 48.dp),
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
            .padding(horizontal = 20.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(Modifier.weight(1f)) {
            Text(label, style = MaterialTheme.typography.bodyLarge)
            if (supporting != null) {
                Text(supporting, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
        // The whole row is the toggle; the switch is decorative for accessibility.
        Switch(checked = checked, onCheckedChange = null)
    }
}

@Composable
private fun <T> Choice(label: String, options: List<Pair<T, String>>, selected: T, onSelect: (T) -> Unit) {
    Column(Modifier.padding(horizontal = 20.dp, vertical = 8.dp)) {
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
    Column(Modifier.padding(horizontal = 20.dp, vertical = 4.dp)) {
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

