package com.ridedeck.app.data.local

import androidx.room.Database
import androidx.room.RoomDatabase

@Database(
    entities = [SessionEntity::class, RoutePointEntity::class, FavouriteTrackEntity::class],
    version = 1,
    exportSchema = true,
)
abstract class RideDeckDatabase : RoomDatabase() {
    abstract fun sessionDao(): SessionDao
    abstract fun musicDao(): MusicDao
}
