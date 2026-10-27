package com.ridedeck.app.ui.music

import android.os.SystemClock
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Favorite
import androidx.compose.material.icons.filled.FavoriteBorder
import androidx.compose.material.icons.filled.MusicNote
import androidx.compose.material.icons.filled.Pause
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.Shuffle
import androidx.compose.material.icons.filled.SkipNext
import androidx.compose.material.icons.filled.SkipPrevious
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.FilledIconButton
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.IconButtonDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.TabRow
import androidx.compose.material3.Surface
import androidx.compose.material3.Tab
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.ridedeck.app.data.prefs.MusicSource
import com.ridedeck.app.music.PlaybackState
import com.ridedeck.app.music.Track
import com.ridedeck.app.ui.permissions.audioPermission
import com.ridedeck.core.format.Formatters
import com.ridedeck.core.lyrics.Lyrics
import kotlinx.coroutines.delay

/**
 * Built-in music player shown under the map or speedometer. Plays the music stored on the phone
 * through Media3 ExoPlayer; playback continues in the background with lock-screen controls.
 */
@Composable
fun MusicPanel(
    lyricsEnabled: Boolean,
    spotifyEnabled: Boolean,
    source: MusicSource,
    onSourceChange: (MusicSource) -> Unit,
) {
    Surface(color = MaterialTheme.colorScheme.surfaceContainer, modifier = Modifier.fillMaxSize()) {
        Column {
            if (spotifyEnabled) {
                Row(
                    Modifier.padding(start = 12.dp, end = 12.dp, top = 4.dp),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    FilterChip(
                        selected = source == MusicSource.PHONE,
                        onClick = { onSourceChange(MusicSource.PHONE) },
                        label = { Text("On this phone") },
                    )
                    FilterChip(
                        selected = source == MusicSource.SPOTIFY,
                        onClick = { onSourceChange(MusicSource.SPOTIFY) },
                        label = { Text("Spotify") },
                    )
                }
            }
            Box(Modifier.weight(1f)) {
                if (spotifyEnabled && source == MusicSource.SPOTIFY) SpotifyPanel(lyricsEnabled) else PhoneMusicPanel(lyricsEnabled)
            }
        }
    }
}

@Composable
private fun PhoneMusicPanel(
    lyricsEnabled: Boolean,
    viewModel: MusicViewModel = hiltViewModel(),
) {
    val state by viewModel.uiState.collectAsStateWithLifecycle()
    val lifecycleOwner = LocalLifecycleOwner.current

    DisposableEffect(lifecycleOwner) {
        val observer = LifecycleEventObserver { _, event ->
            when (event) {
                Lifecycle.Event.ON_START -> viewModel.onVisible()
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

    val permissionLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) {
        viewModel.refreshLibrary()
    }

    Box(Modifier.fillMaxSize()) {
        when (state.library) {
            LibraryState.Loading -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                CircularProgressIndicator()
            }
            LibraryState.NeedsPermission -> Prompt(
                "Allow access to the music on your phone to play it here.",
                "Allow music access",
            ) { permissionLauncher.launch(audioPermission()) }
            LibraryState.Empty -> Prompt(
                "No music found. Copy MP3, FLAC, OGG or M4A files to the Music folder on your phone.",
                "Scan again",
                viewModel::refreshLibrary,
            )
            is LibraryState.Ready -> Player(state, lyricsEnabled, viewModel)
        }
    }
}

@Composable
private fun Player(state: MusicUiState, lyricsEnabled: Boolean, viewModel: MusicViewModel) {
    val playback = state.playback
    BoxWithConstraints(Modifier.fillMaxSize().padding(horizontal = 12.dp, vertical = 8.dp)) {
        // Breakpoints grow with the font size setting so large text never gets squeezed.
        val fontScale = LocalDensity.current.fontScale.coerceAtLeast(1f)
        val compact = maxHeight < 150.dp * fontScale
        val roomForList = maxHeight >= 240.dp * fontScale
        // Music on its own on a tall screen gets big album art instead of a thumbnail.
        val artSize = when {
            compact -> 56.dp
            maxHeight >= 520.dp -> minOf(maxWidth * 0.34f, maxHeight * 0.22f, 180.dp)
            else -> 88.dp
        }
        // 0 = lyrics, 1 = library. Start on the library until something is playing.
        var tab by rememberSaveable { mutableIntStateOf(if (playback.trackId == null || !lyricsEnabled) 1 else 0) }
        Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                AlbumArt(state, artSize)
                Spacer(Modifier.width(12.dp))
                Column(Modifier.weight(1f)) {
                    Text(
                        playback.title.ifBlank { "Nothing playing" },
                        style = MaterialTheme.typography.titleMedium,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                    Text(
                        if (playback.trackId == null) "Tap play to shuffle your music" else playback.artist,
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                    if (!compact && playback.album != null) {
                        Text(
                            playback.album,
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                }
                if (compact) Controls(state, viewModel, small = true)
            }
            if (playback.trackId != null) SeekBar(playback, onSeek = viewModel::seekTo)
            if (!compact) Controls(state, viewModel, small = false)
            if (roomForList) {
                if (lyricsEnabled) {
                    TabRow(selectedTabIndex = tab) {
                        Tab(selected = tab == 0, onClick = { tab = 0 }, text = { Text("Lyrics") })
                        Tab(selected = tab == 1, onClick = { tab = 1 }, text = { Text("Library") })
                    }
                }
                Box(Modifier.weight(1f)) {
                    if (lyricsEnabled && tab == 0) LyricsView(state.lyrics, playback, Modifier.fillMaxSize())
                    else LibraryList(state, viewModel)
                }
            }
        }
    }
}

@Composable
private fun LibraryList(state: MusicUiState, viewModel: MusicViewModel) {
    val tracks = state.visibleTracks
    Column {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            FilterChip(
                selected = !state.favouritesOnly,
                onClick = { viewModel.setFavouritesOnly(false) },
                label = { Text("All songs") },
            )
            FilterChip(
                selected = state.favouritesOnly,
                onClick = { viewModel.setFavouritesOnly(true) },
                label = { Text("Favorites") },
            )
            Spacer(Modifier.weight(1f))
            IconButton(onClick = viewModel::shuffleAll, enabled = tracks.isNotEmpty()) {
                Icon(Icons.Default.Shuffle, contentDescription = "Shuffle all")
            }
        }
        if (tracks.isEmpty()) {
            Text(
                "No favorites yet. Tap the heart while a song plays.",
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(8.dp),
            )
        } else {
            LazyColumn(Modifier.fillMaxSize()) {
                items(tracks, key = { it.id }) { track -> TrackRow(track, track.id == state.playback.trackId) { viewModel.playTrack(track) } }
            }
        }
    }
}

@Composable
private fun TrackRow(track: Track, current: Boolean, onClick: () -> Unit) {
    Row(
        Modifier
            .fillMaxWidth()
            .heightIn(min = 56.dp)
            .clickable(onClick = onClick)
            .padding(horizontal = 4.dp, vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(Modifier.weight(1f)) {
            Text(
                track.title,
                style = MaterialTheme.typography.bodyLarge,
                fontWeight = if (current) FontWeight.Bold else null,
                color = if (current) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurface,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                track.artist,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        Text(
            Formatters.duration(track.durationMs),
            style = MaterialTheme.typography.labelMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}

@Composable
private fun AlbumArt(state: MusicUiState, size: Dp) {
    val art = state.albumArt
    val modifier = Modifier.size(size).clip(RoundedCornerShape(8.dp))
    if (art != null) {
        Image(
            bitmap = art.asImageBitmap(),
            contentDescription = "Album art",
            contentScale = ContentScale.Crop,
            modifier = modifier,
        )
    } else {
        Box(modifier.background(MaterialTheme.colorScheme.surfaceContainerHighest), contentAlignment = Alignment.Center) {
            Icon(Icons.Default.MusicNote, contentDescription = null)
        }
    }
}

@Composable
private fun Controls(state: MusicUiState, viewModel: MusicViewModel, small: Boolean) {
    val playback = state.playback
    val buttonSize = if (small) 52.dp else 64.dp
    val iconSize = if (small) 28.dp else 36.dp
    Row(
        horizontalArrangement = if (small) Arrangement.spacedBy(2.dp) else Arrangement.SpaceEvenly,
        verticalAlignment = Alignment.CenterVertically,
        modifier = if (small) Modifier else Modifier.fillMaxWidth(),
    ) {
        if (!small) {
            IconButton(onClick = viewModel::toggleShuffle, modifier = Modifier.size(buttonSize)) {
                Icon(
                    Icons.Default.Shuffle,
                    contentDescription = if (playback.shuffle) "Shuffle on" else "Shuffle off",
                    tint = if (playback.shuffle) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.size(iconSize),
                )
            }
            IconButton(onClick = viewModel::previous, enabled = playback.trackId != null, modifier = Modifier.size(buttonSize)) {
                Icon(Icons.Default.SkipPrevious, contentDescription = "Previous track", modifier = Modifier.size(iconSize))
            }
        }
        FilledIconButton(
            onClick = viewModel::togglePlayPause,
            enabled = playback.connected,
            modifier = Modifier.size(buttonSize),
            colors = IconButtonDefaults.filledIconButtonColors(containerColor = MaterialTheme.colorScheme.primary),
        ) {
            Icon(
                if (playback.isPlaying) Icons.Default.Pause else Icons.Default.PlayArrow,
                contentDescription = if (playback.isPlaying) "Pause" else "Play",
                modifier = Modifier.size(iconSize),
            )
        }
        IconButton(onClick = viewModel::next, enabled = playback.hasNext, modifier = Modifier.size(buttonSize)) {
            Icon(Icons.Default.SkipNext, contentDescription = "Next track", modifier = Modifier.size(iconSize))
        }
        if (playback.trackId != null) {
            IconButton(onClick = viewModel::toggleFavourite, modifier = Modifier.size(buttonSize)) {
                Icon(
                    if (state.isFavourite) Icons.Default.Favorite else Icons.Default.FavoriteBorder,
                    contentDescription = if (state.isFavourite) "Remove from favorites" else "Add to favorites",
                    tint = if (state.isFavourite) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurface,
                    modifier = Modifier.size(iconSize),
                )
            }
        }
    }
}

@Composable
internal fun LyricsView(lyrics: LyricsState, playback: PlaybackState, modifier: Modifier) {
    Box(modifier.fillMaxWidth()) {
        when (lyrics) {
            LyricsState.Off -> Text("Play a song to see its lyrics.", color = MaterialTheme.colorScheme.onSurfaceVariant)
            LyricsState.Loading -> Text("Loading lyrics…", color = MaterialTheme.colorScheme.onSurfaceVariant)
            is LyricsState.Unavailable -> Text(lyrics.reason, color = MaterialTheme.colorScheme.onSurfaceVariant)
            is LyricsState.Ready -> when (val l = lyrics.lyrics) {
                is Lyrics.Synced -> SyncedLyrics(l, playback, lyrics.source)
                is Lyrics.Plain -> Column(Modifier.verticalScroll(rememberScrollState())) {
                    Text(l.text, style = MaterialTheme.typography.bodyLarge)
                    Source(lyrics.source)
                }
                Lyrics.Instrumental -> Text("Instrumental", color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
    }
}

@Composable
private fun SyncedLyrics(lyrics: Lyrics.Synced, playback: PlaybackState, source: String) {
    // Extrapolate the playhead between player updates; ticks only while playing.
    val position by produceState(playback.positionAt(SystemClock.elapsedRealtime()), playback) {
        while (true) {
            value = playback.positionAt(SystemClock.elapsedRealtime())
            if (!playback.isPlaying) break
            delay(250)
        }
    }
    val current = lyrics.indexAt(position)
    val listState = rememberLazyListState()
    LaunchedEffect(current) {
        if (current >= 0) listState.animateScrollToItem((current - 1).coerceAtLeast(0))
    }
    LazyColumn(state = listState, modifier = Modifier.fillMaxSize().semantics { contentDescription = "Synced lyrics" }) {
        itemsIndexed(lyrics.lines) { index, line ->
            val active = index == current
            Text(
                line.text.ifBlank { "♪" },
                style = if (active) MaterialTheme.typography.titleLarge.copy(fontWeight = FontWeight.Bold) else MaterialTheme.typography.bodyLarge,
                color = if (active) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(vertical = 3.dp),
            )
        }
        item { Source(source) }
    }
}

@Composable
private fun Source(source: String) {
    Text(
        "Lyrics from $source",
        style = MaterialTheme.typography.labelSmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
        modifier = Modifier.padding(top = 8.dp),
    )
}

@Composable
internal fun Prompt(message: String, action: String?, onAction: (() -> Unit)?) {
    Column(
        Modifier.fillMaxSize().padding(16.dp),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text(message, style = MaterialTheme.typography.bodyLarge)
        if (action != null && onAction != null) {
            Button(onClick = onAction, modifier = Modifier.padding(top = 12.dp).heightIn(min = 56.dp)) { Text(action) }
        }
    }
}
