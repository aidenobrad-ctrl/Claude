package com.ridedeck.app

import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.core.splashscreen.SplashScreen.Companion.installSplashScreen
import com.ridedeck.app.data.location.LocationRepository
import com.ridedeck.app.tracking.TrackingService
import com.ridedeck.app.ui.main.RideDeckRoot
import dagger.hilt.android.AndroidEntryPoint
import javax.inject.Inject

@AndroidEntryPoint
class MainActivity : ComponentActivity() {

    @Inject lateinit var locationRepository: LocationRepository

    override fun onCreate(savedInstanceState: Bundle?) {
        // The system splash shows the logo; the animated startup screen in Compose takes over from it.
        installSplashScreen()
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent { RideDeckRoot() }
        if (savedInstanceState == null) handleShortcut(intent)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        handleShortcut(intent)
    }

    /** The "Start ride" launcher shortcut starts recording straight away when location is allowed. */
    private fun handleShortcut(intent: Intent?) {
        if (intent?.action != ACTION_START_RIDE) return
        if (locationRepository.hasPreciseLocationPermission()) TrackingService.start(this)
    }

    companion object {
        const val ACTION_START_RIDE = "com.ridedeck.app.action.START_RIDE"
    }
}
