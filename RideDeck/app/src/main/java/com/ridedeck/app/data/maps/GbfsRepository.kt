package com.ridedeck.app.data.maps

import android.content.Context
import android.location.Address
import android.location.Geocoder
import android.os.Build
import android.os.SystemClock
import com.ridedeck.app.data.net.HttpException
import com.ridedeck.app.data.net.await
import com.ridedeck.core.gbfs.BikeShareStation
import com.ridedeck.core.gbfs.GbfsParser
import com.ridedeck.core.gbfs.GbfsSystem
import com.ridedeck.core.gbfs.GbfsSystemsCsv
import com.ridedeck.core.model.GeoPoint
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import java.util.Locale
import javax.inject.Inject
import javax.inject.Singleton
import kotlin.coroutines.resume

data class AreaInfo(val countryCode: String?, val locality: String?)

/** Bike-share stations from public GBFS feeds, plus discovery of systems near the user. */
@Singleton
class GbfsRepository @Inject constructor(
    @ApplicationContext private val context: Context,
    private val http: OkHttpClient,
) {
    private val mutex = Mutex()
    private var systemsCache: List<GbfsSystem>? = null
    private var stationCache: Triple<String, Long, List<BikeShareStation>>? = null

    /** Loads stations for a system, honouring the feed's TTL (minimum 60 s) between network calls. */
    suspend fun stations(discoveryUrl: String): List<BikeShareStation> = mutex.withLock {
        val now = SystemClock.elapsedRealtime()
        val cached = stationCache?.takeIf { (url, expiresAt, _) -> url == discoveryUrl && now < expiresAt }
        cached?.third ?: withContext(Dispatchers.IO) {
            val lang = Locale.getDefault().language
            val discoveryBody = get(discoveryUrl)
            val feeds = GbfsParser.parseDiscovery(discoveryBody, lang)
            val infoUrl = feeds.stationInformationUrl ?: return@withContext emptyList<BikeShareStation>()
            val infoBody = get(infoUrl)
            val info = GbfsParser.parseStationInformation(infoBody, lang)
            val statusBody = feeds.stationStatusUrl?.let { get(it) }
            val status = statusBody?.let(GbfsParser::parseStationStatus).orEmpty()
            val ttlSec = statusBody?.let(GbfsParser::parseTtlSeconds) ?: GbfsParser.parseTtlSeconds(infoBody)
            val merged = GbfsParser.merge(info, status)
            stationCache = Triple(discoveryUrl, now + ttlSec.coerceAtLeast(60) * 1000L, merged)
            merged
        }
    }

    /** The MobilityData catalogue of public GBFS systems. Cached for the process lifetime. */
    suspend fun systems(): List<GbfsSystem> = mutex.withLock {
        systemsCache ?: withContext(Dispatchers.IO) {
            GbfsSystemsCsv.parse(get(SYSTEMS_CSV_URL)).also { systemsCache = it }
        }
    }

    suspend fun systemsNear(point: GeoPoint?): List<GbfsSystem> {
        val all = systems()
        val area = point?.let { areaFor(it) }
        return GbfsSystemsCsv.rankForArea(all, area?.countryCode, area?.locality)
    }

    suspend fun areaFor(point: GeoPoint): AreaInfo? {
        if (!Geocoder.isPresent()) return null
        val geocoder = Geocoder(context, Locale.getDefault())
        val address: Address? = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            suspendCancellableCoroutine { cont ->
                geocoder.getFromLocation(point.latitude, point.longitude, 1, object : Geocoder.GeocodeListener {
                    override fun onGeocode(addresses: MutableList<Address>) {
                        if (cont.isActive) cont.resume(addresses.firstOrNull())
                    }

                    override fun onError(errorMessage: String?) {
                        if (cont.isActive) cont.resume(null)
                    }
                })
            }
        } else {
            withContext(Dispatchers.IO) {
                @Suppress("DEPRECATION")
                runCatching { geocoder.getFromLocation(point.latitude, point.longitude, 1)?.firstOrNull() }.getOrNull()
            }
        }
        return address?.let { AreaInfo(it.countryCode, it.locality ?: it.subAdminArea ?: it.adminArea) }
    }

    private suspend fun get(url: String): String {
        val response = http.newCall(Request.Builder().url(url).build()).await()
        return response.use {
            if (!it.isSuccessful) throw HttpException(it.code, "GBFS request failed (${it.code})")
            it.body?.string().orEmpty()
        }
    }

    private companion object {
        const val SYSTEMS_CSV_URL = "https://raw.githubusercontent.com/MobilityData/gbfs/master/systems.csv"
    }
}
