package com.ridedeck.app.ui.map

import android.os.SystemClock
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.ridedeck.app.data.location.LocationAccuracy
import com.ridedeck.app.data.location.LocationRepository
import com.ridedeck.app.data.maps.NoRouteException
import com.ridedeck.app.data.maps.OsmRepository
import com.ridedeck.app.data.prefs.MapStyle
import com.ridedeck.app.navigation.TurnAnnouncer
import com.ridedeck.app.navigation.VoiceGuide
import com.ridedeck.core.format.Formatters
import com.ridedeck.app.data.prefs.UserPreferences
import com.ridedeck.app.data.prefs.UserPreferencesRepository
import com.ridedeck.core.mode.ModeProfile
import com.ridedeck.core.model.GeoPoint
import com.ridedeck.core.model.LocationFix
import com.ridedeck.core.model.SpeedUnit
import com.ridedeck.core.osm.PlaceSuggestion
import com.ridedeck.core.routing.OffRouteDetector
import com.ridedeck.core.routing.Route
import com.ridedeck.core.routing.RouteProgress
import com.ridedeck.core.routing.RouteProgressCalculator
import com.ridedeck.core.routing.ReroutePolicy
import com.ridedeck.core.routing.RerouteReason
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.catch
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.onEach
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import javax.inject.Inject

data class MapUiState(
    val unit: SpeedUnit = SpeedUnit.KMH,
    val hasLocationPermission: Boolean = false,
    val myLocation: GeoPoint? = null,
    val followUser: Boolean = true,
    val query: String = "",
    val suggestions: List<PlaceSuggestion> = emptyList(),
    val searching: Boolean = false,
    val destination: PlaceSuggestion? = null,
    val route: Route? = null,
    val progress: RouteProgress? = null,
    val routing: Boolean = false,
    val rerouteReason: RerouteReason? = null,
    val arrived: Boolean = false,
    val message: String? = null,
    val mapStyle: MapStyle = MapStyle.AUTO,
    val googleMapsHandOff: Boolean = false,
)

private data class NavState(
    val query: String = "",
    val suggestions: List<PlaceSuggestion> = emptyList(),
    val searching: Boolean = false,
    val destination: PlaceSuggestion? = null,
    val route: Route? = null,
    val progress: RouteProgress? = null,
    val routing: Boolean = false,
    val rerouteReason: RerouteReason? = null,
    val arrived: Boolean = false,
    val message: String? = null,
    val followUser: Boolean = true,
)

@OptIn(ExperimentalCoroutinesApi::class)
@HiltViewModel
class MapViewModel @Inject constructor(
    private val locationRepository: LocationRepository,
    private val osmRepository: OsmRepository,
    private val preferencesRepository: UserPreferencesRepository,
    private val voiceGuide: VoiceGuide,
) : ViewModel() {

    private val announcer = TurnAnnouncer { text -> if (prefs.voiceDirections) voiceGuide.speak(text) }

    private val nav = MutableStateFlow(NavState())
    private val permissionTick = MutableStateFlow(0)
    private var prefs = UserPreferences()
    private var lastFix: LocationFix? = null
    private var searchJob: Job? = null
    private var routeJob: Job? = null
    private val offRouteDetector = OffRouteDetector()
    private val reroutePolicy = ReroutePolicy()

    /**
     * Location only flows while the map is on screen. High accuracy at 1 Hz while navigating,
     * balanced power every 3 s otherwise.
     */
    private val location: Flow<LocationFix?> = combine(
        nav.map { it.destination != null }.distinctUntilChanged(),
        permissionTick,
    ) { navigating, _ -> navigating }
        .flatMapLatest { navigating ->
            if (!locationRepository.hasLocationPermission()) {
                flowOf(null)
            } else {
                locationRepository.updates(
                    intervalMs = if (navigating) 1_000 else 3_000,
                    accuracy = if (navigating) LocationAccuracy.HIGH else LocationAccuracy.BALANCED,
                ).map<LocationFix, LocationFix?> { it }.catch { emit(null) }
            }
        }
        .onEach { fix -> if (fix != null) onLocation(fix) }

    val uiState: StateFlow<MapUiState> = combine(
        nav,
        preferencesRepository.preferences,
        location,
        permissionTick,
    ) { n, p, fix, _ ->
        prefs = p
        MapUiState(
            unit = p.speedUnit,
            hasLocationPermission = locationRepository.hasLocationPermission(),
            myLocation = fix?.point ?: lastFix?.point,
            followUser = n.followUser,
            query = n.query,
            suggestions = n.suggestions,
            searching = n.searching,
            destination = n.destination,
            route = n.route,
            progress = n.progress,
            routing = n.routing,
            rerouteReason = n.rerouteReason,
            arrived = n.arrived,
            message = n.message,
            mapStyle = p.mapStyle,
            googleMapsHandOff = p.googleMapsEnabled,
        )
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), MapUiState())

    fun refreshPermissions() {
        permissionTick.value++
    }

    fun setFollowUser(follow: Boolean) = nav.update { it.copy(followUser = follow) }

    fun dismissMessage() = nav.update { it.copy(message = null) }

    fun setMapStyle(style: MapStyle) = viewModelScope.launch { preferencesRepository.setMapStyle(style) }

    fun onQueryChange(query: String) {
        nav.update { it.copy(query = query) }
        searchJob?.cancel()
        if (query.trim().length < 2) {
            nav.update { it.copy(suggestions = emptyList(), searching = false) }
            return
        }
        searchJob = viewModelScope.launch {
            delay(SEARCH_DEBOUNCE_MS)
            nav.update { it.copy(searching = true) }
            try {
                val results = osmRepository.search(query, lastFix?.point)
                nav.update { it.copy(suggestions = results, searching = false) }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                nav.update { it.copy(searching = false, message = "Search failed: ${e.message ?: "check your connection"}") }
            }
        }
    }

    fun selectSuggestion(suggestion: PlaceSuggestion) {
        searchJob?.cancel()
        nav.update {
            it.copy(suggestions = emptyList(), query = suggestion.name, destination = suggestion, arrived = false, followUser = true)
        }
        computeRoute(reason = null)
    }

    fun retryRoute() = computeRoute(reason = null)

    fun clearRoute() {
        routeJob?.cancel()
        announcer.reset()
        voiceGuide.shutdown()
        reroutePolicy.clear()
        offRouteDetector.reset()
        nav.update {
            it.copy(destination = null, route = null, progress = null, routing = false, rerouteReason = null, arrived = false, query = "")
        }
    }

    private fun onLocation(fix: LocationFix) {
        lastFix = fix
        val n = nav.value
        val route = n.route ?: return
        val progress = RouteProgressCalculator.progress(route, fix.point) ?: return
        announce(route, progress)
        if (progress.arrived) {
            nav.update { it.copy(progress = progress, arrived = true) }
            return
        }
        val offRoute = offRouteDetector.update(
            distanceFromRouteM = progress.distanceFromRouteM,
            accuracyM = fix.accuracyM,
            thresholdM = ModeProfile.of(prefs.mode).offRouteThresholdM,
        )
        nav.update { it.copy(progress = progress) }
        val reason = reroutePolicy.evaluate(prefs.mode, offRoute, SystemClock.elapsedRealtime())
        if (reason != null && !n.routing) computeRoute(reason)
    }

    private fun computeRoute(reason: RerouteReason?) {
        val destination = nav.value.destination ?: return
        routeJob?.cancel()
        routeJob = viewModelScope.launch {
            val origin = lastFix?.point ?: locationRepository.lastKnown()?.point
            if (origin == null) {
                nav.update { it.copy(routing = false, message = "Waiting for your location…") }
                return@launch
            }
            val mode = preferencesRepository.current().mode
            nav.update { it.copy(routing = true, rerouteReason = reason) }
            osmRepository.route(origin, destination.location, mode)
                .onSuccess { route ->
                    reroutePolicy.onRouteComputed(mode, SystemClock.elapsedRealtime())
                    offRouteDetector.reset()
                    val unit = prefs.speedUnit
                    announcer.onNewRoute(
                        firstInstruction = route.steps.firstOrNull()?.instruction,
                        summary = "${TurnAnnouncer.spoken(Formatters.navDistance(route.distanceM.toDouble(), unit))}, " +
                            "about ${Formatters.spokenDuration(route.durationSec)}.",
                        rerouted = reason != null,
                    )
                    nav.update {
                        it.copy(
                            route = route,
                            progress = RouteProgressCalculator.progress(route, origin),
                            routing = false,
                            rerouteReason = null,
                        )
                    }
                }
                .onFailure { e ->
                    // Still mark the attempt so off-route retries respect the cooldown.
                    reroutePolicy.onRouteComputed(mode, SystemClock.elapsedRealtime())
                    val text = when (e) {
                        is NoRouteException -> "No cycling route found"
                        else -> "Directions failed: ${e.message ?: "check your connection"}"
                    }
                    nav.update { it.copy(routing = false, rerouteReason = null, message = text) }
                }
        }
    }

    /** Speaks the turn at the end of the current step (OSRM puts each instruction at its step's start). */
    private fun announce(route: Route, progress: RouteProgress) {
        val upcoming = route.steps.getOrNull(progress.currentStepIndex + 1)?.instruction
        announcer.onProgress(
            stepIndex = progress.currentStepIndex,
            upcoming = upcoming,
            distanceM = progress.distanceToManeuverM,
            spokenDistance = TurnAnnouncer.spoken(Formatters.navDistance(progress.distanceToManeuverM, prefs.speedUnit)),
            arrived = progress.arrived,
        )
    }

    override fun onCleared() {
        voiceGuide.shutdown()
        super.onCleared()
    }

    companion object {
        private const val SEARCH_DEBOUNCE_MS = 300L
    }
}
