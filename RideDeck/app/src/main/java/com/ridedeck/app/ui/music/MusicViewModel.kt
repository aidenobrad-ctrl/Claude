package com.ridedeck.app.ui.music

import android.graphics.Bitmap
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.ridedeck.app.data.lyrics.LyricsProvider
import com.ridedeck.app.data.lyrics.LyricsQuery
import com.ridedeck.app.data.lyrics.LyricsResult
import com.ridedeck.app.data.prefs.UserPreferencesRepository
import com.ridedeck.app.music.MusicController
import com.ridedeck.app.music.MusicLibrary
import com.ridedeck.app.music.PlaybackState
import com.ridedeck.app.music.Track
import com.ridedeck.core.lyrics.Lyrics
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

sealed interface LyricsState {
    data object Off : LyricsState
    data object Loading : LyricsState
    data class Ready(val lyrics: Lyrics, val source: String) : LyricsState
    data class Unavailable(val reason: String) : LyricsState
}

sealed interface LibraryState {
    data object Loading : LibraryState
    data object NeedsPermission : LibraryState
    data object Empty : LibraryState
    data class Ready(val tracks: List<Track>) : LibraryState
}

data class MusicUiState(
    val library: LibraryState = LibraryState.Loading,
    val playback: PlaybackState = PlaybackState(),
    val favourites: Set<Long> = emptySet(),
    val favouritesOnly: Boolean = false,
    val albumArt: Bitmap? = null,
    val lyrics: LyricsState = LyricsState.Off,
) {
    val isFavourite: Boolean get() = playback.trackId != null && playback.trackId in favourites

    /** The list shown in the library view, honouring the favourites filter. */
    val visibleTracks: List<Track>
        get() = (library as? LibraryState.Ready)?.tracks.orEmpty()
            .let { all -> if (favouritesOnly) all.filter { it.id in favourites } else all }
}

private data class TrackExtras(
    val albumArt: Bitmap? = null,
    val lyrics: LyricsState = LyricsState.Off,
)

@HiltViewModel
class MusicViewModel @Inject constructor(
    private val controller: MusicController,
    private val library: MusicLibrary,
    private val lyricsProvider: LyricsProvider,
    private val preferences: UserPreferencesRepository,
) : ViewModel() {

    private val libraryState = MutableStateFlow<LibraryState>(LibraryState.Loading)
    private val favouritesOnly = MutableStateFlow(false)
    private val extras = MutableStateFlow(TrackExtras())

    val uiState: StateFlow<MusicUiState> = combine(
        libraryState,
        controller.state,
        library.favouriteIds,
        favouritesOnly,
        extras,
    ) { lib, playback, favs, favOnly, ex ->
        MusicUiState(lib, playback, favs, favOnly, ex.albumArt, ex.lyrics)
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), MusicUiState())

    init {
        refreshLibrary()
        // On every track change (or lyrics toggle) load album art and lyrics.
        viewModelScope.launch {
            combine(
                controller.state.map { it.trackId }.distinctUntilChanged(),
                preferences.preferences.map { it.lyricsEnabled }.distinctUntilChanged(),
            ) { id, lyricsOn -> id to lyricsOn }
                .collectLatest { (id, lyricsOn) ->
                    val track = id?.let { findTrack(it) }
                    if (track == null) {
                        extras.value = TrackExtras()
                        return@collectLatest
                    }
                    extras.value = TrackExtras(lyrics = if (lyricsOn) LyricsState.Loading else LyricsState.Off)
                    launch { extras.value = extras.value.copy(albumArt = library.artwork(track)) }
                    if (lyricsOn) {
                        val result = lyricsProvider.lyricsFor(
                            LyricsQuery(track.uri.toString(), track.title, track.artist, track.album, track.durationMs),
                        )
                        extras.value = extras.value.copy(
                            lyrics = when (result) {
                                is LyricsResult.Found -> LyricsState.Ready(result.lyrics, result.source)
                                LyricsResult.NotFound -> LyricsState.Unavailable("Lyrics unavailable for this song")
                                is LyricsResult.Unavailable -> LyricsState.Unavailable(result.reason)
                            },
                        )
                    }
                }
        }
    }

    /** Called when the panel becomes visible, and after the audio permission is granted. */
    fun onVisible() {
        viewModelScope.launch { controller.connect() }
        if (libraryState.value !is LibraryState.Ready) refreshLibrary()
    }

    fun onHidden() = controller.release()

    fun refreshLibrary() {
        viewModelScope.launch {
            if (!library.hasPermission()) {
                libraryState.value = LibraryState.NeedsPermission
                return@launch
            }
            val tracks = library.tracks()
            libraryState.value = if (tracks.isEmpty()) LibraryState.Empty else LibraryState.Ready(tracks)
        }
    }

    fun playTrack(track: Track) {
        val list = uiState.value.visibleTracks
        val index = list.indexOfFirst { it.id == track.id }
        if (index >= 0) controller.play(list, index, shuffle = uiState.value.playback.shuffle)
    }

    fun shuffleAll() {
        val list = uiState.value.visibleTracks
        if (list.isNotEmpty()) controller.play(list, list.indices.random(), shuffle = true)
    }

    fun togglePlayPause() {
        if (uiState.value.playback.trackId == null) shuffleAll() else controller.togglePlayPause()
    }

    fun next() = controller.next()
    fun previous() = controller.previous()
    fun toggleShuffle() = controller.toggleShuffle()
    fun seekTo(positionMs: Long) = controller.seekTo(positionMs)
    fun setFavouritesOnly(value: Boolean) {
        favouritesOnly.value = value
    }

    fun toggleFavourite() {
        val id = uiState.value.playback.trackId ?: return
        val target = id !in uiState.value.favourites
        viewModelScope.launch { library.setFavourite(id, target) }
    }

    override fun onCleared() {
        controller.release()
    }

    private fun findTrack(id: Long): Track? =
        (libraryState.value as? LibraryState.Ready)?.tracks?.firstOrNull { it.id == id }
            ?: controller.state.value.takeIf { it.trackId == id }?.let { s ->
                // Library not loaded yet (e.g. playback started before this screen): build from metadata.
                Track(id, android.net.Uri.EMPTY, s.title, s.artist, s.album, 0, s.durationMs)
            }
}
