package com.ridedeck.app.data.local

import androidx.room.Dao
import androidx.room.Entity
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.PrimaryKey
import androidx.room.Query
import kotlinx.coroutines.flow.Flow

/** A track the rider hearted. Keyed by its MediaStore id, which is stable while the file exists. */
@Entity(tableName = "favourite_tracks")
data class FavouriteTrackEntity(
    @PrimaryKey val mediaId: Long,
    val addedAtMs: Long,
)

@Dao
interface MusicDao {
    @Query("SELECT mediaId FROM favourite_tracks")
    fun favouriteIds(): Flow<List<Long>>

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun addFavourite(entity: FavouriteTrackEntity)

    @Query("DELETE FROM favourite_tracks WHERE mediaId = :mediaId")
    suspend fun removeFavourite(mediaId: Long)
}
