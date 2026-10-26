package com.ridedeck.app.ui.music

import android.graphics.Bitmap
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.OpenInNew
import androidx.compose.material.icons.filled.MusicNote
import androidx.compose.material.icons.filled.Pause
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.SkipNext
import androidx.compose.material.icons.filled.SkipPrevious
import androidx.compose.material3.FilledIconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.IconButtonDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.ViewModel
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewModelScope
import com.ridedeck.app.data.lyrics.LyricsProvider
import com.ridedeck.app.data.lyrics.LyricsQuery
import com.ridedeck.app.data.lyrics.LyricsResult
import com.ridedeck.app.integrations.SpotifyControl
import com.ridedeck.app.integrations.SpotifyPlayback
import com.ridedeck.app.music.PlaybackState
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import javax.inject.Inject

data class SpotifyUiState(
    val playback: SpotifyPlayback = SpotifyPlayback(),
    val installed: Boolean = true,
    val lyrics: LyricsState = LyricsState.Off,
)

@HiltViewModel
class SpotifyViewModel @Inject constructor(
    private val spotify: SpotifyControl,
    private val lyricsProvider: LyricsProvider,
) : ViewModel() {

    private val lyrics = MutableStateFlow<LyricsState>(LyricsState.Off)
    private val installed = MutableStateFlow(spotify.isInstalled())

    val uiState: StateFlow<SpotifyUiState> = combine(spotify.state, installed, lyrics) { p, i, l ->
        SpotifyUiState(p, i, l)
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), SpotifyUiState())

    init {
        // Look up synced lyrics for each new song, the same way as for music on the phone.
        viewModelScope.launch {
            spotify.state
                .map { Triple(it.title, it.artist, it.durationMs) }
                .distinctUntilChanged()
                .collectLatest { (title, artist, duration) ->
                    if (title.isBlank()) {
                        lyrics.value = LyricsState.Off
                        return@collectLatest
                    }
                    lyrics.value = LyricsState.Loading
                    val album = spotify.state.value.album
                    lyrics.value = when (val r = lyricsProvider.lyricsFor(LyricsQuery("spotify:$artist:$title", title, artist, album, duration))) {
                        is LyricsResult.Found -> LyricsState.Ready(r.lyrics, r.source)
                        LyricsResult.NotFound -> LyricsState.Unavailable("Lyrics unavailable for this song")
                        is LyricsResult.Unavailable -> LyricsState.Unavailable(r.reason)
                    }
                }
        }
    }

    fun onVisible() {
        installed.value = spotify.isInstalled()
        spotify.start()
    }

    fun onHidden() = spotify.stop()
    fun playPause() = spotify.playPause()
    fun next() = spotify.next()
    fun previous() = spotify.previous()
    fun openSpotify() = spotify.openSpotify()
    fun openAccessSettings() = spotify.openAccessSettings()

    override fun onCleared() = spotify.stop()
}

/**
 * Controls the Spotify app through its Android media session. Needs notification access, which
 * the rider grants once; the steps are in Settings → Spotify.
 */
@Composable
fun SpotifyPanel(lyricsEnabled: Boolean, viewModel: SpotifyViewModel = hiltViewModel()) {
    val state by viewModel.uiState.collectAsStateWithLifecycle()
    val lifecycleOwner = LocalLifecycleOwner.current
    DisposableEffect(lifecycleOwner) {
        val observer = LifecycleEventObserver { _, event ->
            when (event) {
                // Re-checked on resume, so returning from the access screen connects straight away.
                Lifecycle.Event.ON_RESUME -> viewModel.onVisible()
                Lifecycle.Event.ON_STOP -> viewModel.onHidden()
                else -> Unit
            }
        }
        lifecycleOwner.lifecycle.addObserver(observer)
        if (lifecycleOwner.lifecycle.currentState.isAtLeast(Lifecycle.State.STARTED)) viewModel.onVisible()
        onDispose {
            lifecycleOwner.lifecycle.removeObserver(observer)
            viewModel.onHidden()
        }
    }

    val p = state.playback
    when {
        !state.installed -> Prompt("Spotify isn't installed on this phone.", "Get Spotify", viewModel::openSpotify)
        !p.hasAccess -> Prompt(
            "Allow notification access so RideDeck can control Spotify. Find RideDeck in the list and switch it on.",
            "Allow access",
            viewModel::openAccessSettings,
        )
        !p.active || p.title.isBlank() -> Prompt("Start a song in Spotify, then control it here.", "Open Spotify", viewModel::openSpotify)
        else -> BoxWithConstraints(Modifier.fillMaxSize().padding(horizontal = 12.dp, vertical = 8.dp)) {
            val compact = maxHeight < 150.dp
            val showLyrics = lyricsEnabled && maxHeight >= 240.dp
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Art(p.art, if (compact) 56.dp else 88.dp)
                    Spacer(Modifier.width(12.dp))
                    Column(Modifier.weight(1f)) {
                        Text(p.title, style = MaterialTheme.typography.titleMedium, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        Text(
                            p.artist,
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                    if (compact) Transport(p.isPlaying, viewModel, small = true)
                }
                if (!compact) Transport(p.isPlaying, viewModel, small = false)
                if (showLyrics) {
                    LyricsView(
                        state.lyrics,
                        PlaybackState(
                            connected = true,
                            trackId = (p.artist + p.title).hashCode().toLong(),
                            title = p.title,
                            artist = p.artist,
                            durationMs = p.durationMs,
                            isPlaying = p.isPlaying,
                            positionMs = p.positionMs,
                            sampledAtMs = p.sampledAtMs,
                        ),
                        Modifier.weight(1f),
                    )
                }
            }
        }
    }
}

@Composable
private fun Art(art: Bitmap?, size: Dp) {
    val modifier = Modifier.size(size).clip(RoundedCornerShape(8.dp))
    if (art != null) {
        Image(art.asImageBitmap(), contentDescription = "Album art", contentScale = ContentScale.Crop, modifier = modifier)
    } else {
        Box(modifier.background(MaterialTheme.colorScheme.surfaceContainerHighest), contentAlignment = Alignment.Center) {
            Icon(Icons.Default.MusicNote, contentDescription = null)
        }
    }
}

@Composable
private fun Transport(isPlaying: Boolean, viewModel: SpotifyViewModel, small: Boolean) {
    val buttonSize = if (small) 52.dp else 64.dp
    val iconSize = if (small) 28.dp else 36.dp
    Row(
        horizontalArrangement = if (small) Arrangement.spacedBy(2.dp) else Arrangement.SpaceEvenly,
        verticalAlignment = Alignment.CenterVertically,
        modifier = if (small) Modifier else Modifier.fillMaxWidth(),
    ) {
        if (!small) {
            IconButton(onClick = viewModel::previous, modifier = Modifier.size(buttonSize)) {
                Icon(Icons.Default.SkipPrevious, contentDescription = "Previous track", modifier = Modifier.size(iconSize))
            }
        }
        FilledIconButton(
            onClick = viewModel::playPause,
            modifier = Modifier.size(buttonSize),
            colors = IconButtonDefaults.filledIconButtonColors(containerColor = MaterialTheme.colorScheme.primary),
        ) {
            Icon(
                if (isPlaying) Icons.Default.Pause else Icons.Default.PlayArrow,
                contentDescription = if (isPlaying) "Pause" else "Play",
                modifier = Modifier.size(iconSize),
            )
        }
        IconButton(onClick = viewModel::next, modifier = Modifier.size(buttonSize)) {
            Icon(Icons.Default.SkipNext, contentDescription = "Next track", modifier = Modifier.size(iconSize))
        }
        if (!small) {
            IconButton(onClick = viewModel::openSpotify, modifier = Modifier.size(buttonSize)) {
                Icon(Icons.AutoMirrored.Filled.OpenInNew, contentDescription = "Open Spotify", modifier = Modifier.size(iconSize))
            }
        }
    }
}
