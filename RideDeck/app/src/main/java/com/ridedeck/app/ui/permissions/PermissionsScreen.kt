package com.ridedeck.app.ui.permissions

import android.Manifest
import android.app.Activity
import android.content.Context
import android.content.ContextWrapper
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.LibraryMusic
import androidx.compose.material.icons.filled.LocationOn
import androidx.compose.material.icons.filled.MyLocation
import androidx.compose.material.icons.filled.Notifications
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.ViewModel
import androidx.lifecycle.compose.LifecycleResumeEffect
import androidx.lifecycle.viewModelScope
import com.ridedeck.app.data.prefs.UserPreferencesRepository
import com.ridedeck.app.ui.components.GloveTouchTarget
import com.ridedeck.app.ui.components.LinkText
import com.ridedeck.app.ui.theme.GoGreen
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.launch
import javax.inject.Inject

@HiltViewModel
class PermissionsViewModel @Inject constructor(
    private val preferences: UserPreferencesRepository,
) : ViewModel() {
    fun markOnboardingDone() = viewModelScope.launch { preferences.setOnboardingDone() }
}

/**
 * Explains each permission before the system dialog appears, requests them one at a time, and
 * offers a shortcut to app settings when a permission was permanently denied.
 */
@Composable
fun PermissionsScreen(
    onDone: () -> Unit,
    onOpenPrivacy: () -> Unit,
    viewModel: PermissionsViewModel = hiltViewModel(),
) {
    val context = LocalContext.current
    val activity = remember(context) { context.findActivity() }
    // Bumped on resume and after each request so statuses are re-read (e.g. after visiting settings).
    var refresh by remember { mutableIntStateOf(0) }
    LifecycleResumeEffect(Unit) {
        refresh++
        onPauseOrDispose { }
    }
    var asked by remember { mutableStateOf(setOf<String>()) }

    val single = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { refresh++ }
    val multiple = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { refresh++ }

    val granted = remember(refresh) { PermissionStatus.read(context) }
    val locationGranted = granted.location
    val notificationsGranted = granted.notifications
    val musicGranted = granted.music

    fun permanentlyDenied(permission: String, granted: Boolean) =
        !granted && permission in asked && activity != null &&
            !ActivityCompat.shouldShowRequestPermissionRationale(activity, permission)

    Column(
        Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(20.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        Text(
            "Before you ride",
            style = MaterialTheme.typography.headlineMedium,
            modifier = Modifier.semantics { heading() },
        )
        Text(
            "RideDeck asks for a few permissions. Each one is explained below and only location is required.",
            style = MaterialTheme.typography.bodyLarge,
        )

        PermissionCard(
            icon = Icons.Default.LocationOn,
            title = "Precise location (required)",
            rationale = "Drives the speedometer, measures your distance and shows you on the map. " +
                "Precise location is needed because approximate location is only accurate to a few miles.",
            granted = locationGranted,
            permanentlyDenied = permanentlyDenied(Manifest.permission.ACCESS_FINE_LOCATION, locationGranted),
            onRequest = {
                asked = asked + Manifest.permission.ACCESS_FINE_LOCATION
                multiple.launch(arrayOf(Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION))
            },
            onOpenSettings = { context.openAppSettings() },
        )

        if (Build.VERSION.SDK_INT >= 33) {
            PermissionCard(
                icon = Icons.Default.Notifications,
                title = "Notifications",
                rationale = "Shows your live speed and distance while recording with the screen off, with " +
                    "Pause and Stop buttons. Without it, recording still works but the controls are hidden.",
                granted = notificationsGranted,
                permanentlyDenied = permanentlyDenied(Manifest.permission.POST_NOTIFICATIONS, notificationsGranted),
                onRequest = {
                    asked = asked + Manifest.permission.POST_NOTIFICATIONS
                    single.launch(Manifest.permission.POST_NOTIFICATIONS)
                },
                onOpenSettings = { context.openAppSettings() },
            )
        }

        PermissionCard(
            icon = Icons.Default.LibraryMusic,
            title = "Music on this phone",
            rationale = "Lets the built-in music player list and play the songs stored on your phone. " +
                "Optional, and nothing leaves the device.",
            granted = musicGranted,
            permanentlyDenied = permanentlyDenied(audioPermission(), musicGranted),
            onRequest = {
                asked = asked + audioPermission()
                single.launch(audioPermission())
            },
            onOpenSettings = { context.openAppSettings() },
        )

        LinkText("Privacy policy", onClick = onOpenPrivacy)
        Spacer(Modifier.size(8.dp))
        Button(
            onClick = {
                viewModel.markOnboardingDone()
                onDone()
            },
            modifier = Modifier.fillMaxWidth().heightIn(min = GloveTouchTarget),
        ) {
            Text(if (locationGranted) "Continue" else "Continue without location")
        }
    }
}

@Composable
private fun PermissionCard(
    icon: ImageVector,
    title: String,
    rationale: String,
    granted: Boolean,
    permanentlyDenied: Boolean,
    onRequest: () -> Unit,
    onOpenSettings: () -> Unit,
    enabled: Boolean = true,
    disabledHint: String? = null,
) {
    Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainerHigh)) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(icon, contentDescription = null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(28.dp))
                Spacer(Modifier.width(12.dp))
                Text(title, style = MaterialTheme.typography.titleMedium, modifier = Modifier.weight(1f))
                if (granted) {
                    Icon(Icons.Default.CheckCircle, contentDescription = "Granted", tint = GoGreen)
                }
            }
            Text(rationale, style = MaterialTheme.typography.bodyMedium)
            when {
                granted -> Text("Granted", color = GoGreen, style = MaterialTheme.typography.labelLarge)
                !enabled -> Text(disabledHint.orEmpty(), style = MaterialTheme.typography.labelLarge)
                permanentlyDenied -> OutlinedButton(
                    onClick = onOpenSettings,
                    modifier = Modifier.fillMaxWidth().heightIn(min = GloveTouchTarget),
                ) { Text("Open app settings") }
                else -> Button(
                    onClick = onRequest,
                    modifier = Modifier.fillMaxWidth().heightIn(min = GloveTouchTarget),
                ) { Text("Allow") }
            }
        }
    }
}

private data class PermissionStatus(
    val location: Boolean,
    val notifications: Boolean,
    val music: Boolean,
) {
    companion object {
        fun read(context: Context) = PermissionStatus(
            location = context.isGranted(Manifest.permission.ACCESS_FINE_LOCATION),
            notifications = Build.VERSION.SDK_INT < 33 || context.isGranted(Manifest.permission.POST_NOTIFICATIONS),
            music = context.isGranted(audioPermission()),
        )
    }
}

/** Reading the music library needs READ_MEDIA_AUDIO on Android 13+ and storage access before that. */
fun audioPermission(): String =
    if (Build.VERSION.SDK_INT >= 33) Manifest.permission.READ_MEDIA_AUDIO else Manifest.permission.READ_EXTERNAL_STORAGE

fun Context.isGranted(permission: String): Boolean =
    ContextCompat.checkSelfPermission(this, permission) == PackageManager.PERMISSION_GRANTED

fun Context.openAppSettings() {
    startActivity(
        Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.fromParts("package", packageName, null))
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
    )
}

tailrec fun Context.findActivity(): Activity? = when (this) {
    is Activity -> this
    is ContextWrapper -> baseContext.findActivity()
    else -> null
}
