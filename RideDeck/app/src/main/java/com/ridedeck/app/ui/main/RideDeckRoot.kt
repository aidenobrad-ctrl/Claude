package com.ridedeck.app.ui.main

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.History
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.Map
import androidx.compose.material.icons.filled.MusicNote
import androidx.compose.material.icons.filled.MusicOff
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.Speed
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.SnackbarResult
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.res.stringResource
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.navigation.NavGraph.Companion.findStartDestination
import androidx.navigation.NavHostController
import androidx.navigation.NavType
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.currentBackStackEntryAsState
import androidx.navigation.compose.rememberNavController
import androidx.navigation.navArgument
import com.ridedeck.app.R
import com.ridedeck.app.tracking.TrackingEvent
import com.ridedeck.app.ui.components.RideModeBanner
import com.ridedeck.app.ui.history.HistoryScreen
import com.ridedeck.app.ui.history.SessionDetailScreen
import com.ridedeck.app.ui.home.HomeScreen
import com.ridedeck.app.ui.map.MapScreen
import com.ridedeck.app.ui.music.MusicPanel
import com.ridedeck.app.ui.permissions.PermissionsScreen
import com.ridedeck.app.ui.settings.SettingsScreen
import com.ridedeck.app.ui.theme.RideDeckTheme

object Routes {
    const val PERMISSIONS = "permissions"
    const val RIDE = "ride"
    const val MAP = "map"
    const val HISTORY = "history"
    const val SESSION = "history/{sessionId}"
    const val SETTINGS = "settings"

    fun session(id: Long) = "history/$id"
}

private data class TopLevel(val route: String, val label: Int, val icon: ImageVector, val lockable: Boolean)

private val topLevels = listOf(
    TopLevel(Routes.RIDE, R.string.nav_ride, Icons.Default.Speed, lockable = false),
    TopLevel(Routes.MAP, R.string.nav_map, Icons.Default.Map, lockable = false),
    TopLevel(Routes.HISTORY, R.string.nav_history, Icons.Default.History, lockable = true),
    TopLevel(Routes.SETTINGS, R.string.nav_settings, Icons.Default.Settings, lockable = true),
)

@Composable
fun RideDeckRoot(viewModel: MainViewModel = hiltViewModel()) {
    val state by viewModel.uiState.collectAsStateWithLifecycle()
    RideDeckTheme(themeMode = state.preferences.themeMode) {
        if (!state.loaded) {
            Box(Modifier.fillMaxSize())
            return@RideDeckTheme
        }
        KeepScreenOn(state.keepScreenOn)

        val navController = rememberNavController()
        val snackbarHostState = remember { SnackbarHostState() }

        LaunchedEffect(Unit) {
            if (viewModel.needsPermissionOnboarding(state.preferences)) navController.navigate(Routes.PERMISSIONS)
        }

        LaunchedEffect(Unit) {
            viewModel.trackingEvents.collect { event ->
                when (event) {
                    is TrackingEvent.SessionSaved -> {
                        val result = snackbarHostState.showSnackbar("Session saved", actionLabel = "View")
                        if (result == SnackbarResult.ActionPerformed) navController.navigate(Routes.session(event.sessionId))
                    }
                    TrackingEvent.SessionDiscarded -> snackbarHostState.showSnackbar("Session too short to save")
                    is TrackingEvent.Error -> snackbarHostState.showSnackbar(event.message)
                }
            }
        }

        val backStack by navController.currentBackStackEntryAsState()
        val currentRoute = backStack?.destination?.route

        // Ride Mode: leave complex screens once the rider starts moving.
        LaunchedEffect(state.rideLocked, currentRoute) {
            if (state.rideLocked && (currentRoute == Routes.SETTINGS || currentRoute == Routes.HISTORY || currentRoute == Routes.SESSION)) {
                navController.navigateTopLevel(Routes.RIDE)
            }
        }

        val showChrome = currentRoute != Routes.PERMISSIONS
        Scaffold(
            snackbarHost = { SnackbarHost(snackbarHostState) },
            bottomBar = {
                if (showChrome) {
                    NavigationBar {
                        topLevels.forEach { item ->
                            val locked = item.lockable && state.rideLocked
                            NavigationBarItem(
                                selected = currentRoute == item.route ||
                                    (item.route == Routes.HISTORY && currentRoute == Routes.SESSION),
                                onClick = { navController.navigateTopLevel(item.route) },
                                enabled = !locked,
                                icon = { Icon(if (locked) Icons.Default.Lock else item.icon, contentDescription = null) },
                                label = { Text(stringResource(item.label)) },
                            )
                        }
                        val panelOn = state.preferences.musicPanelVisible
                        NavigationBarItem(
                            selected = panelOn,
                            onClick = { viewModel.setMusicPanelVisible(!panelOn) },
                            icon = { Icon(if (panelOn) Icons.Default.MusicNote else Icons.Default.MusicOff, contentDescription = null) },
                            label = { Text("Music") },
                        )
                    }
                }
            },
        ) { padding ->
            Column(Modifier.padding(padding).fillMaxSize()) {
                if (state.rideLocked) RideModeBanner()
                SplitPanelLayout(
                    showPanel = showChrome && state.preferences.musicPanelVisible,
                    fraction = state.preferences.musicPanelFraction,
                    onFractionCommitted = viewModel::setMusicPanelFraction,
                    onHidePanel = { viewModel.setMusicPanelVisible(false) },
                    top = {
                        NavHost(navController = navController, startDestination = Routes.RIDE) {
                            composable(Routes.PERMISSIONS) {
                                PermissionsScreen(onDone = { navController.popBackStack() })
                            }
                            composable(Routes.RIDE) {
                                HomeScreen(
                                    rideLocked = state.rideLocked,
                                    onOpenPermissions = { navController.navigate(Routes.PERMISSIONS) },
                                )
                            }
                            composable(Routes.MAP) { MapScreen(rideLocked = state.rideLocked) }
                            composable(Routes.HISTORY) {
                                HistoryScreen(onOpenSession = { navController.navigate(Routes.session(it)) })
                            }
                            composable(
                                Routes.SESSION,
                                arguments = listOf(navArgument("sessionId") { type = NavType.LongType }),
                            ) {
                                SessionDetailScreen(onBack = { navController.popBackStack() })
                            }
                            composable(Routes.SETTINGS) {
                                SettingsScreen(onOpenPermissions = { navController.navigate(Routes.PERMISSIONS) })
                            }
                        }
                    },
                    panel = { MusicPanel(lyricsEnabled = state.preferences.lyricsEnabled) },
                )
            }
        }
    }
}

private fun NavHostController.navigateTopLevel(route: String) {
    navigate(route) {
        popUpTo(graph.findStartDestination().id) { saveState = true }
        launchSingleTop = true
        restoreState = true
    }
}

/** Keeps the display on during a session (FLAG_KEEP_SCREEN_ON via the root view). */
@Composable
private fun KeepScreenOn(enabled: Boolean) {
    val view = LocalView.current
    DisposableEffect(enabled) {
        view.keepScreenOn = enabled
        onDispose { view.keepScreenOn = false }
    }
}
