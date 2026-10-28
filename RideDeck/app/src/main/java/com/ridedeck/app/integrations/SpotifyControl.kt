package com.ridedeck.app.integrations

import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.media.MediaMetadata
import android.media.session.MediaController
import android.media.session.MediaSessionManager
import android.media.session.PlaybackState
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.provider.Settings
import android.service.notification.NotificationListenerService
import androidx.core.app.NotificationManagerCompat
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Required by Android to read other apps' media sessions. RideDeck does not read or store
 * notifications; being an enabled listener is only what unlocks [MediaSessionManager.getActiveSessions].
 */
class MediaAccessListenerService : NotificationListenerService()

/** What Spotify is playing, as published through its Android media session. */
data class SpotifyPlayback(
    val hasAccess: Boolean = false,
    /** True when Spotify has an active media session (it is open or was playing recently). */
    val active: Boolean = false,
    val title: String = "",
    val artist: String = "",
    val album: String? = null,
    val durationMs: Long = 0,
    val art: Bitmap? = null,
    val isPlaying: Boolean = false,
    val positionMs: Long = 0,
    /** elapsedRealtime when [positionMs] was valid, for extrapolating the playhead. */
    val sampledAtMs: Long = 0,
    val speed: Float = 1f,
) {
    fun positionAt(nowMs: Long): Long =
        if (isPlaying) (positionMs + ((nowMs - sampledAtMs) * speed).toLong()).coerceIn(0, durationMs.coerceAtLeast(0)) else positionMs
}

/**
 * Optional Spotify integration with no API key or developer account: it controls the Spotify app
 * through the standard Android media session that Spotify publishes for lock-screen controls.
 * The rider grants RideDeck notification access once (see the setup steps in Settings).
 */
@Singleton
class SpotifyControl @Inject constructor(
    @ApplicationContext private val context: Context,
) {
    private val _state = MutableStateFlow(SpotifyPlayback())
    val state: StateFlow<SpotifyPlayback> = _state.asStateFlow()

    private val sessionManager = context.getSystemService(MediaSessionManager::class.java)
    private val listenerComponent = ComponentName(context, MediaAccessListenerService::class.java)
    private val mainHandler = Handler(Looper.getMainLooper())
    private var controller: MediaController? = null
    private var watching = false

    private val sessionsListener = MediaSessionManager.OnActiveSessionsChangedListener { controllers ->
        attach(controllers?.firstOrNull { it.packageName == SPOTIFY_PACKAGE })
    }

    private val callback = object : MediaController.Callback() {
        override fun onMetadataChanged(metadata: MediaMetadata?) = publish()
        override fun onPlaybackStateChanged(state: PlaybackState?) = publish()
        override fun onSessionDestroyed() = attach(null)
    }

    fun hasAccess(): Boolean = NotificationManagerCompat.getEnabledListenerPackages(context).contains(context.packageName)

    fun isInstalled(): Boolean = context.packageManager.getLaunchIntentForPackage(SPOTIFY_PACKAGE) != null

    /** Starts following Spotify's session. Safe to call repeatedly; no-op without access. */
    fun start() {
        if (watching) return
        if (!hasAccess()) {
            _state.value = SpotifyPlayback(hasAccess = false)
            return
        }
        try {
            sessionManager.addOnActiveSessionsChangedListener(sessionsListener, listenerComponent, mainHandler)
            watching = true
            attach(sessionManager.getActiveSessions(listenerComponent).firstOrNull { it.packageName == SPOTIFY_PACKAGE })
        } catch (e: SecurityException) {
            _state.value = SpotifyPlayback(hasAccess = false)
        }
    }

    fun stop() {
        if (watching) sessionManager.removeOnActiveSessionsChangedListener(sessionsListener)
        watching = false
        attach(null)
    }

    fun playPause() {
        val c = controller ?: return
        if (c.playbackState?.state == PlaybackState.STATE_PLAYING) c.transportControls.pause() else c.transportControls.play()
    }

    fun next() = controller?.transportControls?.skipToNext()
    fun previous() = controller?.transportControls?.skipToPrevious()
    fun seekTo(positionMs: Long) = controller?.transportControls?.seekTo(positionMs.coerceAtLeast(0))

    fun openSpotify() {
        val launch = context.packageManager.getLaunchIntentForPackage(SPOTIFY_PACKAGE)
        if (launch != null) context.startActivity(launch.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
        else openStoreListing(context, SPOTIFY_PACKAGE)
    }

    fun openAccessSettings() {
        context.startActivity(Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
    }

    private fun attach(next: MediaController?) {
        if (controller?.sessionToken == next?.sessionToken && next != null) return
        controller?.unregisterCallback(callback)
        controller = next
        next?.registerCallback(callback, mainHandler)
        publish()
    }

    private fun publish() {
        val c = controller
        if (c == null) {
            _state.value = SpotifyPlayback(hasAccess = watching || hasAccess())
            return
        }
        val meta = c.metadata
        val playback = c.playbackState
        val now = SystemClock.elapsedRealtime()
        _state.value = SpotifyPlayback(
            hasAccess = true,
            active = true,
            title = meta?.getString(MediaMetadata.METADATA_KEY_TITLE).orEmpty(),
            artist = meta?.getString(MediaMetadata.METADATA_KEY_ARTIST).orEmpty(),
            album = meta?.getString(MediaMetadata.METADATA_KEY_ALBUM),
            durationMs = meta?.getLong(MediaMetadata.METADATA_KEY_DURATION) ?: 0L,
            art = meta?.getBitmap(MediaMetadata.METADATA_KEY_ALBUM_ART) ?: meta?.getBitmap(MediaMetadata.METADATA_KEY_ART),
            isPlaying = playback?.state == PlaybackState.STATE_PLAYING,
            positionMs = playback?.position ?: 0L,
            sampledAtMs = playback?.lastPositionUpdateTime?.takeIf { it > 0 } ?: now,
            speed = playback?.playbackSpeed?.takeIf { it > 0f } ?: 1f,
        )
    }

    companion object {
        const val SPOTIFY_PACKAGE = "com.spotify.music"
    }
}
