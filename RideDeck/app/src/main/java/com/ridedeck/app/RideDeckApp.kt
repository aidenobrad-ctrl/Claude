package com.ridedeck.app

import android.app.Application
import com.ridedeck.app.tracking.TrackingNotifications
import dagger.hilt.android.HiltAndroidApp
import org.osmdroid.config.Configuration
import java.io.File
import javax.inject.Inject

@HiltAndroidApp
class RideDeckApp : Application() {

    @Inject lateinit var notifications: TrackingNotifications

    override fun onCreate() {
        super.onCreate()
        notifications.createChannels()
        // OpenStreetMap's tile policy requires an identifying User-Agent; tiles are cached in app storage.
        Configuration.getInstance().apply {
            userAgentValue = "RideDeck/${BuildConfig.VERSION_NAME} (${BuildConfig.APPLICATION_ID})"
            osmdroidBasePath = File(filesDir, "osmdroid")
            osmdroidTileCache = File(cacheDir, "osmdroid-tiles")
            tileFileSystemCacheMaxBytes = 200L * 1024 * 1024
        }
    }
}
