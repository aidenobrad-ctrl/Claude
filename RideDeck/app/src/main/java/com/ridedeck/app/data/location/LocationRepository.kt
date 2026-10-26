package com.ridedeck.app.data.location

import android.Manifest
import android.annotation.SuppressLint
import android.content.Context
import android.content.pm.PackageManager
import android.location.Location
import android.location.LocationManager
import androidx.core.content.ContextCompat
import androidx.core.location.LocationListenerCompat
import androidx.core.location.LocationManagerCompat
import androidx.core.location.LocationRequestCompat
import com.ridedeck.core.model.LocationFix
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.callbackFlow
import javax.inject.Inject
import javax.inject.Singleton

/** How precise (and power hungry) a location subscription needs to be. */
enum class LocationAccuracy { HIGH, BALANCED }

/**
 * Location from Android's own LocationManager (GPS, with the network provider as a low-power
 * option). No Google Play services needed, so it works on de-Googled phones and LineageOS/GrapheneOS.
 */
@Singleton
class LocationRepository @Inject constructor(
    @ApplicationContext private val context: Context,
) {
    private val manager: LocationManager = context.getSystemService(LocationManager::class.java)

    fun hasLocationPermission(): Boolean =
        ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_FINE_LOCATION) ==
            PackageManager.PERMISSION_GRANTED ||
            ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_COARSE_LOCATION) ==
            PackageManager.PERMISSION_GRANTED

    /** Recording needs precise location; approximate fixes are kilometres off and get filtered out. */
    fun hasPreciseLocationPermission(): Boolean =
        ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_FINE_LOCATION) ==
            PackageManager.PERMISSION_GRANTED

    fun isGpsEnabled(): Boolean = manager.isProviderEnabled(LocationManager.GPS_PROVIDER)

    /**
     * Location updates as a cold flow; updates stop as soon as the collector is cancelled, which is
     * what keeps GPS off when nothing needs it. Fails with [SecurityException] without permission.
     *
     * HIGH uses the GPS provider. BALANCED prefers the network provider and falls back to GPS
     * (some phones without Google services have no network provider).
     */
    @SuppressLint("MissingPermission")
    fun updates(intervalMs: Long, accuracy: LocationAccuracy = LocationAccuracy.HIGH): Flow<LocationFix> = callbackFlow {
        if (!hasLocationPermission()) {
            close(SecurityException("Location permission not granted"))
            return@callbackFlow
        }
        val providers = manager.getProviders(true)
        val provider = when {
            accuracy == LocationAccuracy.HIGH && LocationManager.GPS_PROVIDER in providers -> LocationManager.GPS_PROVIDER
            accuracy == LocationAccuracy.BALANCED && LocationManager.NETWORK_PROVIDER in providers -> LocationManager.NETWORK_PROVIDER
            LocationManager.GPS_PROVIDER in providers -> LocationManager.GPS_PROVIDER
            LocationManager.NETWORK_PROVIDER in providers -> LocationManager.NETWORK_PROVIDER
            else -> {
                close(IllegalStateException("Location is turned off"))
                return@callbackFlow
            }
        }
        val request = LocationRequestCompat.Builder(intervalMs)
            .setQuality(
                if (accuracy == LocationAccuracy.HIGH) LocationRequestCompat.QUALITY_HIGH_ACCURACY
                else LocationRequestCompat.QUALITY_BALANCED_POWER_ACCURACY,
            )
            .setMinUpdateIntervalMillis(intervalMs / 2)
            .build()
        val listener = LocationListenerCompat { location -> trySend(location.toFix()) }
        try {
            // Seed with the last fix so the map can centre immediately.
            manager.getLastKnownLocation(provider)?.let { trySend(it.toFix()) }
            LocationManagerCompat.requestLocationUpdates(
                manager,
                provider,
                request,
                ContextCompat.getMainExecutor(context),
                listener,
            )
        } catch (e: SecurityException) {
            close(e)
        }
        awaitClose { LocationManagerCompat.removeUpdates(manager, listener) }
    }

    @SuppressLint("MissingPermission")
    fun lastKnown(): LocationFix? {
        if (!hasLocationPermission()) return null
        return try {
            listOf(LocationManager.GPS_PROVIDER, LocationManager.NETWORK_PROVIDER, LocationManager.PASSIVE_PROVIDER)
                .filter { manager.allProviders.contains(it) }
                .mapNotNull { manager.getLastKnownLocation(it) }
                .maxByOrNull { it.elapsedRealtimeNanos }
                ?.toFix()
        } catch (e: SecurityException) {
            null
        }
    }
}

fun Location.toFix(): LocationFix = LocationFix(
    latitude = latitude,
    longitude = longitude,
    accuracyM = if (hasAccuracy()) accuracy else Float.MAX_VALUE,
    speedMps = if (hasSpeed()) speed else null,
    speedAccuracyMps = if (hasSpeedAccuracy()) speedAccuracyMetersPerSecond else null,
    altitudeM = if (hasAltitude()) altitude else null,
    elapsedRealtimeMs = elapsedRealtimeNanos / 1_000_000,
    wallTimeMs = time,
)
