package com.ridedeck.app

import android.app.Application
import com.ridedeck.app.tracking.TrackingNotifications
import dagger.hilt.android.HiltAndroidApp
import org.maplibre.android.MapLibre
import javax.inject.Inject

@HiltAndroidApp
class RideDeckApp : Application() {

    @Inject lateinit var notifications: TrackingNotifications

    override fun onCreate() {
        super.onCreate()
        notifications.createChannels()
        // MapLibre (open-source vector map renderer) must be set up before any map view is created.
        MapLibre.getInstance(this)
    }
}
