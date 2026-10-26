package com.ridedeck.app.music

import android.content.ComponentName
import android.content.Context
import android.os.SystemClock
import androidx.media3.common.MediaItem
import androidx.media3.common.MediaMetadata
import androidx.media3.common.Player
import androidx.media3.session.MediaController
import androidx.media3.session.SessionToken
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.guava.await
import javax.inject.Inject
import javax.inject.Singleton

/** What the player is doing, sampled whenever the player reports a change. */
data class PlaybackState(
    val connected: Boolean = false,
    val trackId: Long? = null,
    val title: String = "",
    val artist: String = "",
    val album: String? = null,
    val durationMs: Long = 0,
    val isPlaying: Boolean = false,
    val shuffle: Boolean = false,
    val hasNext: Boolean = false,
    val positionMs: Long = 0,
    /** elapsedRealtime when [positionMs] was read, so the UI can extrapolate between updates. */
    val sampledAtMs: Long = 0,
) {
    fun positionAt(nowMs: Long): Long =
        if (isPlaying) (positionMs + (nowMs - sampledAtMs)).coerceIn(0, durationMs.coerceAtLeast(0)) else positionMs
}

/**
 * App-side handle on [PlaybackService]. Holds a Media3 [MediaController] while the music panel is
 * on screen; playback itself lives in the service and continues after [release].
 */
@Singleton
class MusicController @Inject constructor(
    @ApplicationContext private val context: Context,
) {
    private val _state = MutableStateFlow(PlaybackState())
    val state: StateFlow<PlaybackState> = _state.asStateFlow()

    private var controller: MediaController? = null

    private val listener = object : Player.Listener {
        override fun onEvents(player: Player, events: Player.Events) = sample(player)
    }

    suspend fun connect() {
        if (controller != null) return
        try {
            val token = SessionToken(context, ComponentName(context, PlaybackService::class.java))
            val c = MediaController.Builder(context, token).buildAsync().await()
            if (controller != null) {
                c.release()
                return
            }
            controller = c
            c.addListener(listener)
            sample(c)
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            _state.value = PlaybackState(connected = false)
        }
    }

    fun release() {
        controller?.run {
            removeListener(listener)
            release()
        }
        controller = null
        _state.value = _state.value.copy(connected = false)
    }

    /** Replaces the queue with [tracks] and starts at [startIndex]. */
    fun play(tracks: List<Track>, startIndex: Int, shuffle: Boolean = false) {
        val c = controller ?: return
        if (tracks.isEmpty()) return
        c.shuffleModeEnabled = shuffle
        c.setMediaItems(tracks.map { it.toMediaItem() }, startIndex.coerceIn(tracks.indices), 0L)
        c.prepare()
        c.play()
    }

    fun togglePlayPause() {
        val c = controller ?: return
        if (c.isPlaying) c.pause() else {
            if (c.playbackState == Player.STATE_IDLE) c.prepare()
            if (c.playbackState == Player.STATE_ENDED) c.seekToDefaultPosition(0)
            c.play()
        }
    }

    fun next() {
        controller?.takeIf { it.hasNextMediaItem() }?.seekToNextMediaItem()
    }

    /** Restarts the song when more than a few seconds in, like every music app. */
    fun previous() {
        controller?.seekToPrevious()
    }

    fun toggleShuffle() {
        controller?.let { it.shuffleModeEnabled = !it.shuffleModeEnabled }
    }

    private fun sample(player: Player) {
        val item = player.currentMediaItem
        val meta = item?.mediaMetadata
        _state.value = PlaybackState(
            connected = true,
            trackId = item?.mediaId?.toLongOrNull(),
            title = meta?.title?.toString().orEmpty(),
            artist = meta?.artist?.toString().orEmpty(),
            album = meta?.albumTitle?.toString(),
            durationMs = player.duration.takeIf { it > 0 } ?: meta?.durationMs ?: 0L,
            isPlaying = player.isPlaying,
            shuffle = player.shuffleModeEnabled,
            hasNext = player.hasNextMediaItem(),
            positionMs = player.currentPosition,
            sampledAtMs = SystemClock.elapsedRealtime(),
        )
    }
}

private fun Track.toMediaItem(): MediaItem = MediaItem.Builder()
    .setMediaId(id.toString())
    .setUri(uri)
    .setMediaMetadata(
        MediaMetadata.Builder()
            .setTitle(title)
            .setArtist(artist)
            .setAlbumTitle(album)
            .setDurationMs(durationMs)
            .setIsPlayable(true)
            .setIsBrowsable(false)
            .build(),
    )
    .build()
