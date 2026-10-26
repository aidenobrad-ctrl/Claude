package com.ridedeck.app.data.lyrics

import com.ridedeck.core.lyrics.Lyrics

data class LyricsQuery(
    val trackUri: String,
    val title: String,
    val artist: String,
    val album: String?,
    val durationMs: Long,
)

sealed interface LyricsResult {
    data class Found(val lyrics: Lyrics, val source: String) : LyricsResult
    data object NotFound : LyricsResult
    data class Unavailable(val reason: String) : LyricsResult
}

/**
 * Synced-lyrics source. The default is LRCLIB, a free and open lyrics database; swap the binding in
 * `BindingsModule` to change provider (for example to read .lrc files stored next to the music).
 */
interface LyricsProvider {
    val displayName: String
    suspend fun lyricsFor(query: LyricsQuery): LyricsResult
}
