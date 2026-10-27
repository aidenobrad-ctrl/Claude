package com.ridedeck.app.ui.main

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.displayCutout
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.systemBars
import androidx.compose.foundation.layout.union
import androidx.compose.foundation.layout.widthIn
import androidx.compose.material3.NavigationRail
import androidx.compose.material3.NavigationRailItem
import androidx.compose.ui.Alignment
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.unit.dp
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.History
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.Map
import androidx.compose.material.icons.filled.Settings
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
import com.ridedeck.app.ui.permissions.PermissionsScreen
import com.ridedeck.app.ui.settings.SettingsScreen
import com.ridedeck.app.ui.theme.RideDeckTheme

object Routes {
    const val PERMISSIONS = "permissions"
    const val RIDE = "ride"
    const val HISTORY = "history"
    const val SESSION = "history/{sessionId}"
    const val SETTINGS = "settings"

    fun session(id: Long) = "history/$id"
}

private data class TopLevel(val route: String, val label: Int, val icon: ImageVector, val lockable: Boolean)

private val topLevels = listOf(
    // Map, speedometer overlay and music panel all live on the Ride screen.
    TopLevel(Routes.RIDE, R.string.nav_ride, Icons.Default.Map, lockable = false),
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
        // Landscape or any wide window: a side rail instead of a bottom bar, which keeps the
        // map and speedometer as tall as possible.
        val config = LocalConfiguration.current
        val useRail = config.screenWidthDp > config.screenHeightDp && config.screenWidthDp >= 600

        val navItems = topLevels.map { item ->
            NavEntry(
                item = item,
                locked = item.lockable && state.rideLocked,
                selected = currentRoute == item.route ||
                    (item.route == Routes.HISTORY && currentRoute == Routes.SESSION),
            )
        }

        Scaffold(
            snackbarHost = { SnackbarHost(snackbarHostState) },
            // System bars plus the camera cutout, which sits at the side in landscape.
            contentWindowInsets = WindowInsets.systemBars.union(WindowInsets.displayCutout),
            bottomBar = {
                if (showChrome && !useRail) {
                    NavigationBar {
                        navItems.forEach { e ->
                            NavigationBarItem(
                                selected = e.selected,
                                onClick = { navController.navigateTopLevel(e.item.route) },
                                enabled = !e.locked,
                                icon = { NavIcon(e) },
                                label = { Text(stringResource(e.item.label), maxLines = 1) },
                            )
                        }
                    }
                }
            },
        ) { padding ->
            Row(Modifier.padding(padding).fillMaxSize()) {
                if (showChrome && useRail) {
                    NavigationRail(windowInsets = WindowInsets(0)) {
                        Spacer(Modifier.weight(1f))
                        navItems.forEach { e ->
                            NavigationRailItem(
                                selected = e.selected,
                                onClick = { navController.navigateTopLevel(e.item.route) },
                                enabled = !e.locked,
                                icon = { NavIcon(e) },
                                label = { Text(stringResource(e.item.label), maxLines = 1) },
                            )
                        }
                        Spacer(Modifier.weight(1f))
                    }
                }
                Column(Modifier.weight(1f).fillMaxHeight()) {
                    if (state.rideLocked) RideModeBanner()
                    NavHost(navController = navController, startDestination = Routes.RIDE, modifier = Modifier.weight(1f)) {
                        composable(Routes.PERMISSIONS) {
                            Readable { PermissionsScreen(onDone = { navController.popBackStack() }) }
                        }
                        composable(Routes.RIDE) {
                            RideScreen(
                                prefs = state.preferences,
                                rideLocked = state.rideLocked,
                                onMusicFraction = viewModel::setMusicPanelFraction,
                                onMusicSource = viewModel::setMusicSource,
                                onOpenPermissions = { navController.navigate(Routes.PERMISSIONS) },
                            )
                        }
                        composable(Routes.HISTORY) {
                            Readable { HistoryScreen(onOpenSession = { navController.navigate(Routes.session(it)) }) }
                        }
                        composable(
                            Routes.SESSION,
                            arguments = listOf(navArgument("sessionId") { type = NavType.LongType }),
                        ) {
                            Readable { SessionDetailScreen(onBack = { navController.popBackStack() }) }
                        }
                        composable(Routes.SETTINGS) {
                            Readable { SettingsScreen(onOpenPermissions = { navController.navigate(Routes.PERMISSIONS) }) }
                        }
                    }
                }
            }
        }
    }
}

private data class NavEntry(val item: TopLevel, val locked: Boolean, val selected: Boolean)

@Composable
private fun NavIcon(e: NavEntry) {
    Icon(if (e.locked) Icons.Default.Lock else e.item.icon, contentDescription = null)
}

/** Lists and forms stay a comfortable reading width when the window is wide (landscape, DeX, tablets). */
@Composable
private fun Readable(content: @Composable () -> Unit) {
    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.TopCenter) {
        Box(Modifier.widthIn(max = 720.dp).fillMaxSize()) { content() }
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
