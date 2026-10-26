package com.ridedeck.core.mode

import com.ridedeck.core.model.ActivityMode

/**
 * Every behaviour that differs between Bike and Walk lives here, so switching mode is a single
 * lookup and the rules are unit-testable without Android.
 */
data class ModeProfile(
    val mode: ActivityMode,
    /** Fixes with a worse horizontal accuracy than this are ignored. */
    val maxAcceptedAccuracyM: Float,
    /** Anything faster than this is treated as a GPS glitch. */
    val maxPlausibleSpeedMps: Double,
    /** Speed changes faster than this are clamped by the smoother. */
    val maxAccelerationMps2: Double,
    /** Smoothing time constant for the speed filter. */
    val smoothingTauSec: Double,
    val autoPauseSpeedMps: Double,
    val autoPauseDelayMs: Long,
    val autoResumeSpeedMps: Double,
    /** Distance from the route polyline before a fix counts as off-route. */
    val offRouteThresholdM: Double,
    val showPaceAsPrimary: Boolean,
    val speedAlertSupported: Boolean,
    /** OSRM server for this mode (FOSSGIS public instances, OpenStreetMap data). */
    val osrmBaseUrl: String,
    /** Bike mode uses the CyclOSM cycling map style instead of the standard OSM style. */
    val showBicyclingLayer: Boolean,
    val showBikeShareByDefault: Boolean,
    /** Speed above which Ride Mode locks complex menus. */
    val rideModeLockSpeedMps: Double,
) {
    companion object {
        val BIKE = ModeProfile(
            mode = ActivityMode.BIKE,
            maxAcceptedAccuracyM = 25f,
            maxPlausibleSpeedMps = 25.0,
            maxAccelerationMps2 = 4.0,
            smoothingTauSec = 1.5,
            autoPauseSpeedMps = 0.9,
            autoPauseDelayMs = 6_000,
            autoResumeSpeedMps = 1.6,
            offRouteThresholdM = 40.0,
            showPaceAsPrimary = false,
            speedAlertSupported = true,
            osrmBaseUrl = "https://routing.openstreetmap.de/routed-bike",
            showBicyclingLayer = true,
            showBikeShareByDefault = true,
            rideModeLockSpeedMps = 1.5,
        )

        val WALK = ModeProfile(
            mode = ActivityMode.WALK,
            maxAcceptedAccuracyM = 20f,
            maxPlausibleSpeedMps = 7.0,
            maxAccelerationMps2 = 2.5,
            smoothingTauSec = 3.0,
            autoPauseSpeedMps = 0.3,
            autoPauseDelayMs = 15_000,
            autoResumeSpeedMps = 0.6,
            offRouteThresholdM = 30.0,
            showPaceAsPrimary = true,
            speedAlertSupported = false,
            osrmBaseUrl = "https://routing.openstreetmap.de/routed-foot",
            showBicyclingLayer = false,
            showBikeShareByDefault = false,
            rideModeLockSpeedMps = 1.0,
        )

        fun of(mode: ActivityMode): ModeProfile = when (mode) {
            ActivityMode.BIKE -> BIKE
            ActivityMode.WALK -> WALK
        }
    }
}

/** Rules for which map layers are visible in each mode. */
object MapLayerPolicy {

    /**
     * Bike-share stations show in Bike mode whenever the layer is enabled. In Walk mode they stay
     * hidden unless the user explicitly opted in.
     */
    fun showBikeShare(mode: ActivityMode, layerEnabled: Boolean, showInWalkMode: Boolean): Boolean =
        layerEnabled && (ModeProfile.of(mode).showBikeShareByDefault || showInWalkMode)

    fun showBicyclingLayer(mode: ActivityMode): Boolean = ModeProfile.of(mode).showBicyclingLayer

    fun showBikeShops(layerEnabled: Boolean): Boolean = layerEnabled
}

/** Ride Mode hides complex menus while the user is moving. */
object RideModePolicy {
    fun isLocked(
        enabled: Boolean,
        sessionActive: Boolean,
        mode: ActivityMode,
        currentSpeedMps: Double,
    ): Boolean = enabled && sessionActive && currentSpeedMps >= ModeProfile.of(mode).rideModeLockSpeedMps
}
