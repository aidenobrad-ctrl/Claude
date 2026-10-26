package com.ridedeck.app.data.lyrics

import android.util.LruCache
import com.ridedeck.app.data.net.await
import com.ridedeck.core.lyrics.LrcLibParser
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.HttpUrl.Companion.toHttpUrl
import okhttp3.OkHttpClient
import okhttp3.Request
import javax.inject.Inject
import javax.inject.Singleton

/** LRCLIB (https://lrclib.net): free synced lyrics, no API key. */
@Singleton
class LrcLibLyricsProvider @Inject constructor(
    private val http: OkHttpClient,
) : LyricsProvider {

    override val displayName: String = "LRCLIB"

    private val cache = LruCache<String, LyricsResult>(40)

    override suspend fun lyricsFor(query: LyricsQuery): LyricsResult {
        cache.get(query.trackUri)?.let { return it }
        val result = withContext(Dispatchers.IO) {
            try {
                val url = "https://lrclib.net/api/get".toHttpUrl().newBuilder()
                    .addQueryParameter("track_name", query.title)
                    .addQueryParameter("artist_name", query.artist)
                    .apply { query.album?.takeIf { it.isNotBlank() }?.let { addQueryParameter("album_name", it) } }
                    .addQueryParameter("duration", (query.durationMs / 1000).toString())
                    .build()
                http.newCall(Request.Builder().url(url).build()).await().use { response ->
                    when {
                        response.code == 404 -> LyricsResult.NotFound
                        !response.isSuccessful -> LyricsResult.Unavailable("Lyrics service error (${response.code})")
                        else -> LrcLibParser.parse(response.body?.string().orEmpty())
                            ?.let { LyricsResult.Found(it, displayName) }
                            ?: LyricsResult.NotFound
                    }
                }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                LyricsResult.Unavailable("Couldn't reach the lyrics service")
            }
        }
        // Transient failures are not cached so they can be retried on the next track change.
        if (result !is LyricsResult.Unavailable) cache.put(query.trackUri, result)
        return result
    }
}
