package com.ridedeck.app.tracking

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.SystemClock
import androidx.core.app.ServiceCompat
import androidx.core.content.ContextCompat
import androidx.lifecycle.LifecycleService
import androidx.lifecycle.lifecycleScope
import com.ridedeck.app.data.prefs.UserPreferencesRepository
import com.ridedeck.core.model.SpeedUnit
import com.ridedeck.core.tracking.TripStatus
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.launch
import javax.inject.Inject

/**
 * Foreground service (type `location`) that keeps a session recording with the screen off.
 * The tracking logic itself lives in [TrackingManager]; this class handles the Android service
 * lifecycle and the ongoing notification.
 */
@AndroidEntryPoint
class TrackingService : LifecycleService() {

    @Inject lateinit var manager: TrackingManager
    @Inject lateinit var notifications: TrackingNotifications
    @Inject lateinit var preferences: UserPreferencesRepository

    private var observing = false
    private var unit = SpeedUnit.KMH

    override fun onCreate() {
        super.onCreate()
        lifecycleScope.launch { preferences.preferences.collect { unit = it.speedUnit } }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        super.onStartCommand(intent, flags, startId)
        when (intent?.action) {
            ACTION_START -> handleStart()
            ACTION_PAUSE -> withSession { manager.pause() }
            ACTION_RESUME -> withSession { manager.resume() }
            ACTION_STOP -> withSession { manager.stop() }
            // A null intent means Android restarted us (START_STICKY) after killing the process.
            null -> handleRestart()
            else -> stopIfIdle()
        }
        return START_STICKY
    }

    override fun onDestroy() {
        // Destroyed by the system while still recording: save what we have.
        if (manager.hasSession) manager.stop()
        super.onDestroy()
    }

    private fun handleStart() {
        if (!enterForeground()) {
            stopSelf()
            return
        }
        lifecycleScope.launch {
            if (manager.startSession()) observe() else leave()
        }
    }

    private fun handleRestart() {
        if (!hasBackgroundLocation() || !enterForeground()) {
            lifecycleScope.launch {
                manager.cleanUpOrphans()
                leave()
            }
            return
        }
        lifecycleScope.launch {
            if (manager.recoverOpenSession()) observe() else leave()
        }
    }

    private inline fun withSession(action: () -> Unit) {
        if (manager.hasSession) {
            action()
            observe()
        } else {
            stopIfIdle()
        }
    }

    private fun stopIfIdle() {
        if (!manager.hasSession) leave()
    }

    private fun enterForeground(): Boolean {
        val type = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) ServiceInfo.FOREGROUND_SERVICE_TYPE_LOCATION else 0
        return try {
            ServiceCompat.startForeground(this, TrackingNotifications.NOTIFICATION_ID, notifications.build(manager.snapshot.value, unit), type)
            true
        } catch (e: SecurityException) {
            // Location permission missing (Android 14+ checks it when entering the foreground).
            false
        } catch (e: IllegalStateException) {
            // ForegroundServiceStartNotAllowedException: started from the background on Android 12+.
            false
        }
    }

    private fun observe() {
        if (observing) return
        observing = true
        lifecycleScope.launch {
            var seenActive = false
            var lastPostedMs = 0L
            var lastStatus: TripStatus? = null
            manager.snapshot.collect { snap ->
                if (snap.isActive) {
                    seenActive = true
                    val now = SystemClock.elapsedRealtime()
                    // Throttled: the notification only needs a glance-level refresh rate.
                    if (snap.status != lastStatus || now - lastPostedMs >= NOTIFICATION_INTERVAL_MS) {
                        notifications.notify(notifications.build(snap, unit))
                        lastPostedMs = now
                        lastStatus = snap.status
                    }
                } else if (seenActive) {
                    leave()
                }
            }
        }
    }

    private fun leave() {
        ServiceCompat.stopForeground(this, ServiceCompat.STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    private fun hasBackgroundLocation(): Boolean =
        Build.VERSION.SDK_INT < Build.VERSION_CODES.Q ||
            ContextCompat.checkSelfPermission(this, Manifest.permission.ACCESS_BACKGROUND_LOCATION) ==
            PackageManager.PERMISSION_GRANTED

    companion object {
        const val ACTION_START = "com.ridedeck.app.action.START"
        const val ACTION_PAUSE = "com.ridedeck.app.action.PAUSE"
        const val ACTION_RESUME = "com.ridedeck.app.action.RESUME"
        const val ACTION_STOP = "com.ridedeck.app.action.STOP"
        private const val NOTIFICATION_INTERVAL_MS = 3_000L

        /** Must be called while the app is in the foreground (Android 12+ restriction). */
        fun start(context: Context) {
            ContextCompat.startForegroundService(context, Intent(context, TrackingService::class.java).setAction(ACTION_START))
        }
    }
}
