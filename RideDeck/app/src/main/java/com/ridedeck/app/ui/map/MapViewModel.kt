package com.ridedeck.app.ui.map

import android.os.SystemClock
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.ridedeck.app.data.location.LocationAccuracy
import com.ridedeck.app.data.location.LocationRepository
import com.ridedeck.app.data.maps.GbfsRepository
import com.ridedeck.app.data.maps.NoRouteException
import com.ridedeck.app.data.maps.OsmRepository
import com.ridedeck.app.data.prefs.UserPreferences
import com.ridedeck.app.data.prefs.UserPreferencesRepository
import com.ridedeck.core.gbfs.BikeShareStation
import com.ridedeck.core.mode.MapLayerPolicy
import com.ridedeck.core.mode.ModeProfile
import com.ridedeck.core.model.ActivityMode
import com.ridedeck.core.model.GeoPoint
import com.ridedeck.core.model.LocationFix
import com.ridedeck.core.model.SpeedUnit
import com.ridedeck.core.osm.BikeShop
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
import kotlinx.coroutines.flow.drop
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.onEach
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import javax.inject.Inject

data class MapUiState(
    val mode: ActivityMode = ActivityMode.BIKE,
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
    val bikeShops: List<BikeShop> = emptyList(),
    val loadingShops: Boolean = false,
    val stations: List<BikeShareStation> = emptyList(),
    val showShops: Boolean = true,
    val showStations: Boolean = false,
    val showCycleLanes: Boolean = false,
    val cycleLanesEnabled: Boolean = true,
    val bikeShareLayerEnabled: Boolean = true,
    val bikeShareInWalk: Boolean = false,
    val onlyStationsWithBikes: Boolean = false,
    val bikeShareConfigured: Boolean = false,
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
    val bikeShops: List<BikeShop> = emptyList(),
    val loadingShops: Boolean = false,
    val followUser: Boolean = true,
)

@OptIn(ExperimentalCoroutinesApi::class)
@HiltViewModel
class MapViewModel @Inject constructor(
    private val locationRepository: LocationRepository,
    private val osmRepository: OsmRepository,
    private val gbfsRepository: GbfsRepository,
    private val preferencesRepository: UserPreferencesRepository,
) : ViewModel() {

    private val nav = MutableStateFlow(NavState())
    private val permissionTick = MutableStateFlow(0)
    private var prefs = UserPreferences()
    private var lastFix: LocationFix? = null
    private var searchJob: Job? = null
    private var routeJob: Job? = null
    private var shopsLoadedOnce = false
    private var lastShopSearchMs = 0L
    private val offRouteDetector = OffRouteDetector()
    private val reroutePolicy = ReroutePolicy()

    /**
     * Location only flows while the map is on screen. High accuracy at 1 Hz while navigating,
     * balanced power every 3 s otherwise (the blue dot comes from the map's own location overlay).
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

    /** Bike-share stations refresh every two minutes while visible (GBFS TTL is respected too). */
    private val stations: Flow<List<BikeShareStation>> = preferencesRepository.preferences
        .map { p ->
            val url = p.gbfsDiscoveryUrl
            if (url != null && MapLayerPolicy.showBikeShare(p.mode, p.showBikeShare, p.showBikeShareInWalkMode)) url else null
        }
        .distinctUntilChanged()
        .flatMapLatest { url ->
            if (url == null) {
                flowOf(emptyList())
            } else {
                flow {
                    while (true) {
                        emit(gbfsRepository.stations(url))
                        delay(STATION_REFRESH_MS)
                    }
                }.catch {
                    nav.update { s -> s.copy(message = "Couldn't load bike-share stations") }
                    emit(emptyList())
                }
            }
        }

    val uiState: StateFlow<MapUiState> = combine(
        nav,
        preferencesRepository.preferences,
        location,
        stations,
        permissionTick,
    ) { n, p, fix, stationList, _ ->
        prefs = p
        MapUiState(
            mode = p.mode,
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
            bikeShops = n.bikeShops,
            loadingShops = n.loadingShops,
            stations = if (p.onlyStationsWithBikes) stationList.filter { (it.bikesAvailable ?: 0) > 0 } else stationList,
            showShops = MapLayerPolicy.showBikeShops(p.showBikeShops),
            showStations = MapLayerPolicy.showBikeShare(p.mode, p.showBikeShare, p.showBikeShareInWalkMode),
            showCycleLanes = p.showCycleLanes && MapLayerPolicy.showBicyclingLayer(p.mode),
            cycleLanesEnabled = p.showCycleLanes,
            bikeShareLayerEnabled = p.showBikeShare,
            bikeShareInWalk = p.showBikeShareInWalkMode,
            onlyStationsWithBikes = p.onlyStationsWithBikes,
            bikeShareConfigured = p.gbfsDiscoveryUrl != null,
        )
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), MapUiState())

    init {
        // Re-route as soon as the activity mode changes while a destination is set.
        viewModelScope.launch {
            preferencesRepository.mode.drop(1).collect { mode ->
                val reason = reroutePolicy.evaluate(mode, offRoute = false, nowMs = SystemClock.elapsedRealtime())
                if (reason != null && nav.value.destination != null) computeRoute(reason)
            }
        }
    }

    fun refreshPermissions() {
        permissionTick.value++
    }

    fun setMode(mode: ActivityMode) = viewModelScope.launch { preferencesRepository.setMode(mode) }

    fun setFollowUser(follow: Boolean) = nav.update { it.copy(followUser = follow) }

    fun dismissMessage() = nav.update { it.copy(message = null) }

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

    fun clearRoute() {
        routeJob?.cancel()
        reroutePolicy.clear()
        offRouteDetector.reset()
        nav.update {
            it.copy(destination = null, route = null, progress = null, routing = false, rerouteReason = null, arrived = false, query = "")
        }
    }

    fun searchShopsHere(center: GeoPoint) = loadShops(center, force = true)

    private fun onLocation(fix: LocationFix) {
        lastFix = fix
        if (!shopsLoadedOnce && prefs.showBikeShops) {
            shopsLoadedOnce = true
            loadShops(fix.point, force = false)
        }
        val n = nav.value
        val route = n.route ?: return
        val progress = RouteProgressCalculator.progress(route, fix.point) ?: return
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
                        is NoRouteException -> "No ${if (mode == ActivityMode.BIKE) "cycling" else "walking"} route found"
                        else -> "Directions failed: ${e.message ?: "check your connection"}"
                    }
                    nav.update { it.copy(routing = false, rerouteReason = null, message = text) }
                }
        }
    }

    private fun loadShops(center: GeoPoint, force: Boolean) {
        val now = SystemClock.elapsedRealtime()
        // Overpass is a shared community server; never query it more than twice a minute.
        if (!force && now - lastShopSearchMs < SHOP_SEARCH_MIN_INTERVAL_MS) return
        if (force && now - lastShopSearchMs < SHOP_SEARCH_MIN_INTERVAL_MS / 6) return
        lastShopSearchMs = now
        viewModelScope.launch {
            nav.update { it.copy(loadingShops = true) }
            try {
                val shops = osmRepository.bikeShops(center)
                nav.update { it.copy(bikeShops = shops, loadingShops = false) }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                nav.update { it.copy(loadingShops = false, message = "Couldn't load bike shops") }
            }
        }
    }

    fun setShowShops(show: Boolean) = viewModelScope.launch { preferencesRepository.setShowBikeShops(show) }

    fun setShowStations(show: Boolean) = viewModelScope.launch {
        if (prefs.mode == ActivityMode.WALK) {
            // In Walk mode the chip is the explicit opt-in; it also turns the layer on.
            preferencesRepository.setShowBikeShareInWalkMode(show)
            if (show) preferencesRepository.setShowBikeShare(true)
        } else {
            preferencesRepository.setShowBikeShare(show)
        }
    }

    fun setShowCycleLanes(show: Boolean) = viewModelScope.launch { preferencesRepository.setShowCycleLanes(show) }

    fun setOnlyWithBikes(only: Boolean) = viewModelScope.launch { preferencesRepository.setOnlyStationsWithBikes(only) }

    private companion object {
        const val SEARCH_DEBOUNCE_MS = 300L
        const val STATION_REFRESH_MS = 120_000L
        const val SHOP_SEARCH_MIN_INTERVAL_MS = 30_000L
    }
}
