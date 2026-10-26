package com.ridedeck.app.tracking

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.ridedeck.app.MainActivity
import com.ridedeck.app.R
import com.ridedeck.core.format.Formatters
import com.ridedeck.core.format.Pace
import com.ridedeck.core.format.Units
import com.ridedeck.core.mode.ModeProfile
import com.ridedeck.core.model.ActivityMode
import com.ridedeck.core.model.SpeedUnit
import com.ridedeck.core.tracking.TripSnapshot
import com.ridedeck.core.tracking.TripStatus
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class TrackingNotifications @Inject constructor(
    @ApplicationContext private val context: Context,
) {
    fun createChannels() {
        val channel = NotificationChannel(
            CHANNEL_TRACKING,
            context.getString(R.string.notif_channel_tracking),
            NotificationManager.IMPORTANCE_LOW,
        ).apply {
            description = context.getString(R.string.notif_channel_tracking_desc)
            setShowBadge(false)
        }
        context.getSystemService(NotificationManager::class.java).createNotificationChannel(channel)
    }

    fun build(snapshot: TripSnapshot, unit: SpeedUnit): Notification {
        val paused = snapshot.status == TripStatus.PAUSED
        val title = when {
            paused -> context.getString(R.string.notif_title_paused)
            snapshot.mode == ActivityMode.BIKE -> context.getString(R.string.notif_title_bike)
            else -> context.getString(R.string.notif_title_walk)
        }
        val primary = if (ModeProfile.of(snapshot.mode).showPaceAsPrimary) {
            "${Pace.format(Pace.secondsPerUnit(snapshot.currentSpeedMps, unit))} ${Units.paceLabel(unit)}"
        } else {
            "${Formatters.speed(snapshot.currentSpeedMps, unit)} ${Units.speedLabel(unit)}"
        }
        val text = "$primary · ${Formatters.distance(snapshot.distanceM, unit)} ${Units.distanceLabel(unit)} · " +
            Formatters.duration(snapshot.movingTimeMs)

        val openApp = PendingIntent.getActivity(
            context,
            0,
            Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val builder = NotificationCompat.Builder(context, CHANNEL_TRACKING)
            .setSmallIcon(R.drawable.ic_stat_ride)
            .setContentTitle(title)
            .setContentText(text)
            .setContentIntent(openApp)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setSilent(true)
            .setCategory(NotificationCompat.CATEGORY_WORKOUT)
            .setVisibility(NotificationCompat.VISIBILITY_PUBLIC)
            .setForegroundServiceBehavior(NotificationCompat.FOREGROUND_SERVICE_IMMEDIATE)
        if (paused) {
            builder.addAction(0, context.getString(R.string.notif_action_resume), serviceIntent(TrackingService.ACTION_RESUME, 1))
        } else {
            builder.addAction(0, context.getString(R.string.notif_action_pause), serviceIntent(TrackingService.ACTION_PAUSE, 2))
        }
        builder.addAction(0, context.getString(R.string.notif_action_stop), serviceIntent(TrackingService.ACTION_STOP, 3))
        return builder.build()
    }

    fun notify(notification: Notification) {
        // Posting can be refused when POST_NOTIFICATIONS is denied; the service keeps running anyway.
        if (NotificationManagerCompat.from(context).areNotificationsEnabled()) {
            try {
                NotificationManagerCompat.from(context).notify(NOTIFICATION_ID, notification)
            } catch (e: SecurityException) {
                // Permission revoked between the check and the call.
            }
        }
    }

    private fun serviceIntent(action: String, requestCode: Int): PendingIntent = PendingIntent.getService(
        context,
        requestCode,
        Intent(context, TrackingService::class.java).setAction(action),
        PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
    )

    companion object {
        const val CHANNEL_TRACKING = "tracking"
        const val NOTIFICATION_ID = 1001
    }
}
