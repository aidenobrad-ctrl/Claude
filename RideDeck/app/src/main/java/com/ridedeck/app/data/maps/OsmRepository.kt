package com.ridedeck.app.data.maps

import com.ridedeck.app.data.net.HttpException
import com.ridedeck.app.data.net.await
import com.ridedeck.app.data.net.suspendRunCatching
import com.ridedeck.core.model.ActivityMode
import com.ridedeck.core.model.GeoPoint
import com.ridedeck.core.osm.PhotonApi
import com.ridedeck.core.osm.PlaceSuggestion
import com.ridedeck.core.routing.OsrmApi
import com.ridedeck.core.routing.Route
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import java.io.IOException
import java.util.Locale
import javax.inject.Inject
import javax.inject.Singleton

class NoRouteException(message: String?) : IOException(message ?: "No route found for this mode")

/**
 * Search, routing and points of interest from free, open-source services built on
 * OpenStreetMap data. None of them needs an API key:
 * - Photon (komoot) for search-as-you-type
 * - OSRM on routing.openstreetmap.de (FOSSGIS) for bike and foot routing
 * These are community-run servers with fair-use policies, so calls are debounced and rate limited.
 */
@Singleton
class OsmRepository @Inject constructor(
    private val http: OkHttpClient,
) {
    suspend fun search(query: String, near: GeoPoint?): List<PlaceSuggestion> = withContext(Dispatchers.IO) {
        if (query.trim().length < 2) return@withContext emptyList()
        val request = Request.Builder().url(PhotonApi.searchUrl(query, near, Locale.getDefault().language)).build()
        http.newCall(request).await().use { response ->
            if (!response.isSuccessful) throw HttpException(response.code, "Search failed (${response.code})")
            PhotonApi.parse(response.body?.string().orEmpty())
        }
    }

    suspend fun route(origin: GeoPoint, destination: GeoPoint, mode: ActivityMode): Result<Route> = withContext(Dispatchers.IO) {
        suspendRunCatching {
            val request = Request.Builder().url(OsrmApi.routeUrl(origin, destination, mode)).build()
            http.newCall(request).await().use { response ->
                val text = response.body?.string().orEmpty()
                if (!response.isSuccessful && response.code != 400) {
                    throw HttpException(response.code, "Routing server error ${response.code}")
                }
                OsrmApi.parseResponse(text, mode)?.takeIf { it.polyline.isNotEmpty() }
                    ?: throw NoRouteException(OsrmApi.parseError(text))
            }
        }
    }
}
