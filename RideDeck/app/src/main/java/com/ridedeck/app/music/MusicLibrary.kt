package com.ridedeck.app.music

import android.content.ContentUris
import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import android.os.Build
import android.provider.MediaStore
import android.util.Size
import com.ridedeck.app.data.local.FavouriteTrackEntity
import com.ridedeck.app.data.local.MusicDao
import com.ridedeck.app.ui.permissions.audioPermission
import com.ridedeck.app.ui.permissions.isGranted
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.withContext
import javax.inject.Inject
import javax.inject.Singleton

/** A song stored on the phone, as indexed by Android's MediaStore. */
data class Track(
    val id: Long,
    val uri: Uri,
    val title: String,
    val artist: String,
    val album: String?,
    val albumId: Long,
    val durationMs: Long,
)

/** Reads the phone's music library and keeps the rider's favourites. */
@Singleton
class MusicLibrary @Inject constructor(
    @ApplicationContext private val context: Context,
    private val dao: MusicDao,
) {
    fun hasPermission(): Boolean = context.isGranted(audioPermission())

    val favouriteIds: Flow<Set<Long>> = dao.favouriteIds().map { it.toSet() }

    suspend fun setFavourite(trackId: Long, favourite: Boolean) {
        if (favourite) dao.addFavourite(FavouriteTrackEntity(trackId, System.currentTimeMillis()))
        else dao.removeFavourite(trackId)
    }

    /** All music tracks (ringtones and notification sounds excluded), sorted by title. */
    suspend fun tracks(): List<Track> = withContext(Dispatchers.IO) {
        if (!hasPermission()) return@withContext emptyList()
        val collection = if (Build.VERSION.SDK_INT >= 29) {
            MediaStore.Audio.Media.getContentUri(MediaStore.VOLUME_EXTERNAL)
        } else {
            MediaStore.Audio.Media.EXTERNAL_CONTENT_URI
        }
        val projection = arrayOf(
            MediaStore.Audio.Media._ID,
            MediaStore.Audio.Media.TITLE,
            MediaStore.Audio.Media.ARTIST,
            MediaStore.Audio.Media.ALBUM,
            MediaStore.Audio.Media.ALBUM_ID,
            MediaStore.Audio.Media.DURATION,
        )
        val selection = "${MediaStore.Audio.Media.IS_MUSIC} != 0 AND ${MediaStore.Audio.Media.DURATION} >= 30000"
        val result = ArrayList<Track>()
        try {
            context.contentResolver.query(
                collection,
                projection,
                selection,
                null,
                "${MediaStore.Audio.Media.TITLE} COLLATE NOCASE ASC",
            )?.use { c ->
                val idCol = c.getColumnIndexOrThrow(MediaStore.Audio.Media._ID)
                val titleCol = c.getColumnIndexOrThrow(MediaStore.Audio.Media.TITLE)
                val artistCol = c.getColumnIndexOrThrow(MediaStore.Audio.Media.ARTIST)
                val albumCol = c.getColumnIndexOrThrow(MediaStore.Audio.Media.ALBUM)
                val albumIdCol = c.getColumnIndexOrThrow(MediaStore.Audio.Media.ALBUM_ID)
                val durationCol = c.getColumnIndexOrThrow(MediaStore.Audio.Media.DURATION)
                while (c.moveToNext()) {
                    val id = c.getLong(idCol)
                    val artist = c.getString(artistCol)
                    result += Track(
                        id = id,
                        uri = ContentUris.withAppendedId(collection, id),
                        title = c.getString(titleCol)?.takeIf { it.isNotBlank() } ?: "Unknown title",
                        artist = artist?.takeIf { it.isNotBlank() && it != MediaStore.UNKNOWN_STRING } ?: "Unknown artist",
                        album = c.getString(albumCol)?.takeIf { it.isNotBlank() && it != MediaStore.UNKNOWN_STRING },
                        albumId = c.getLong(albumIdCol),
                        durationMs = c.getLong(durationCol),
                    )
                }
            }
        } catch (e: SecurityException) {
            return@withContext emptyList()
        }
        result
    }

    /** Embedded or folder album art, scaled down for the panel. Null when the track has none. */
    suspend fun artwork(track: Track, sizePx: Int = 300): Bitmap? = withContext(Dispatchers.IO) {
        try {
            if (Build.VERSION.SDK_INT >= 29) {
                context.contentResolver.loadThumbnail(track.uri, Size(sizePx, sizePx), null)
            } else {
                val albumArt = ContentUris.withAppendedId(Uri.parse("content://media/external/audio/albumart"), track.albumId)
                context.contentResolver.openInputStream(albumArt)?.use { BitmapFactory.decodeStream(it) }
            }
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            null
        }
    }
}
