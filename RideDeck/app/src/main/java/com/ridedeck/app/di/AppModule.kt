package com.ridedeck.app.di

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.PreferenceDataStoreFactory
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.preferencesDataStoreFile
import androidx.room.Room
import com.ridedeck.app.BuildConfig
import com.ridedeck.app.data.local.MusicDao
import com.ridedeck.app.data.local.RideDeckDatabase
import com.ridedeck.app.data.local.SessionDao
import com.ridedeck.app.data.lyrics.LrcLibLyricsProvider
import com.ridedeck.app.data.lyrics.LyricsProvider
import dagger.Binds
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import okhttp3.Cache
import okhttp3.OkHttpClient
import java.io.File
import java.util.concurrent.TimeUnit
import javax.inject.Qualifier
import javax.inject.Singleton

@Qualifier
@Retention(AnnotationRetention.BINARY)
annotation class ApplicationScope

@Qualifier
@Retention(AnnotationRetention.BINARY)
annotation class SettingsStore

@Module
@InstallIn(SingletonComponent::class)
object AppModule {

    @Provides
    @Singleton
    @ApplicationScope
    fun provideApplicationScope(): CoroutineScope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    @Provides
    @Singleton
    fun provideOkHttp(@ApplicationContext context: Context): OkHttpClient = OkHttpClient.Builder()
        .connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(20, TimeUnit.SECONDS)
        // GBFS feeds and lyrics are cacheable; this keeps data usage down on rides.
        .cache(Cache(File(context.cacheDir, "http"), 50L * 1024 * 1024))
        .addInterceptor { chain ->
            val request = chain.request()
            val withUa = if (request.header("User-Agent") == null) {
                request.newBuilder().header("User-Agent", "RideDeck/${BuildConfig.VERSION_NAME} (Android)").build()
            } else {
                request
            }
            chain.proceed(withUa)
        }
        .build()

    @Provides
    @Singleton
    @SettingsStore
    fun provideSettingsDataStore(@ApplicationContext context: Context): DataStore<Preferences> =
        PreferenceDataStoreFactory.create { context.preferencesDataStoreFile("settings") }

    @Provides
    @Singleton
    fun provideDatabase(@ApplicationContext context: Context): RideDeckDatabase =
        Room.databaseBuilder(context, RideDeckDatabase::class.java, "ridedeck.db").build()

    @Provides
    fun provideSessionDao(db: RideDeckDatabase): SessionDao = db.sessionDao()

    @Provides
    fun provideMusicDao(db: RideDeckDatabase): MusicDao = db.musicDao()
}

@Module
@InstallIn(SingletonComponent::class)
abstract class BindingsModule {
    @Binds
    abstract fun bindLyricsProvider(impl: LrcLibLyricsProvider): LyricsProvider
}
