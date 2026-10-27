package com.ridedeck.app.ui.privacy

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
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Block
import androidx.compose.material.icons.filled.CloudOff
import androidx.compose.material.icons.filled.MoneyOff
import androidx.compose.material.icons.filled.PhoneAndroid
import androidx.compose.material.icons.filled.VisibilityOff
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.ridedeck.app.ui.components.GloveTouchTarget

private data class Promise(val icon: ImageVector, val title: String, val text: String)

private data class Section(val title: String, val paragraphs: List<String>)

private val promises = listOf(
    Promise(Icons.Default.VisibilityOff, "We can't see your data", "There is no RideDeck server and no account. Nothing you record or play is ever sent to us."),
    Promise(Icons.Default.MoneyOff, "We will never sell it", "Not to advertisers, data brokers or anyone else. There is nothing to sell, because we never have it."),
    Promise(Icons.Default.Block, "No ads, no tracking", "No advertising, no analytics, no crash reporting services and no third-party tracking code."),
    Promise(Icons.Default.PhoneAndroid, "It stays on your phone", "Your rides, routes, favourites and settings live only on this device, and you can delete them at any time."),
    Promise(Icons.Default.CloudOff, "Works without an account", "You never sign up, log in or give us your name, email or phone number."),
)

/** Kept in step with PRIVACY.md, the version published for the Play Store. */
private val sections = listOf(
    Section(
        "Who we are",
        listOf(
            "RideDeck is an independent cycling app: a speedometer and ride recorder, a cycling map with directions, and a music player. " +
                "This policy explains exactly what happens to your information when you use it. In short: it stays with you.",
        ),
    ),
    Section(
        "What we collect",
        listOf(
            "Nothing. The people who make RideDeck do not collect, receive, store or have any way to look at your information. " +
                "RideDeck has no user accounts, no backend server, no analytics and no advertising.",
        ),
    ),
    Section(
        "What stays on your phone",
        listOf(
            "Location. RideDeck uses your precise location to show your speed, put you on the map, give directions and record rides. " +
                "Recorded rides, including their GPS tracks, are saved only in RideDeck's private storage on this phone.",
            "Music. The built-in player reads the music files already on your phone so it can play them. Your favourites are saved on this phone. " +
                "Your music is never uploaded anywhere.",
            "Settings, such as your units, theme and which views you like, are saved on this phone.",
            "If you use Android's own backup, Android may include RideDeck's rides and settings in your personal backup to your Google account, " +
                "or copy them when you move to a new phone. " +
                "That backup is controlled by you and Google, not by us.",
        ),
    ),
    Section(
        "We will never sell or share your data",
        listOf(
            "We do not sell, rent, trade or give your information to anyone, including advertisers, data brokers, insurers or employers. " +
                "We never have your information in the first place, so there is nothing we could sell or hand over.",
        ),
    ),
    Section(
        "Services RideDeck talks to",
        listOf(
            "To draw maps and find routes, RideDeck has to ask some open services for data. These requests go straight from your phone to the service. " +
                "They never include your name, an account or your recorded rides, and they only happen while you use the feature.",
            "Map tiles (OpenStreetMap and CyclOSM): the area of the map you're looking at.",
            "Search (Photon by komoot): what you type in the search box, and your rough position so nearby places come first.",
            "Directions (OSRM on routing.openstreetmap.de): your starting point and destination.",
            "Lyrics (LRCLIB): the title, artist, album and length of the song that is playing.",
            "Google map (only if you pick it in Settings): Google's Maps SDK loads the map under Google's own privacy policy.",
            "Like any website you visit, these services can see your phone's IP address when it connects to them. Each handles requests under its own privacy policy.",
        ),
    ),
    Section(
        "Optional extras",
        listOf(
            "Spotify control. If you turn it on and allow notification access, RideDeck can see what Spotify is playing on this phone and send play, pause, skip and seek to it. " +
                "It does not read your other notifications, and nothing about your listening leaves the phone.",
            "Google Maps navigation. Tapping Start in Google Maps hands your destination to the Google Maps app, which then works under Google's policy.",
        ),
    ),
    Section(
        "Permissions and why we ask",
        listOf(
            "Precise location: speed, the map and ride recording. Recording with the screen off runs as a visible foreground service, so you always see a notification while it's on.",
            "Notifications: the ride notification with pause and stop buttons.",
            "Music and audio: playing the songs stored on your phone.",
            "Notification access: only for the optional Spotify control.",
            "You can turn any of these off at any time in Android settings. RideDeck keeps working without the ones you don't need.",
        ),
    ),
    Section(
        "Your control",
        listOf(
            "Delete any ride from History. Clear RideDeck's storage in Android settings to remove everything, or simply uninstall the app: all of its data is deleted with it.",
            "Because we hold no data about you, there is nothing for us to export, correct or delete on our side. That is the point.",
        ),
    ),
    Section(
        "Security",
        listOf(
            "Everything RideDeck sends over the internet uses encrypted HTTPS connections. Data on your phone is protected by Android's app sandbox and your screen lock.",
        ),
    ),
    Section(
        "Children",
        listOf("RideDeck is not aimed at children under 13 and doesn't knowingly handle any information about them."),
    ),
    Section(
        "Changes and contact",
        listOf(
            "If this policy ever changes, the new version will be in the app and on the RideDeck Play Store page before it takes effect. " +
                "We will never change it to allow selling your data.",
            "Questions? Write to the contact email on RideDeck's Play Store page.",
        ),
    ),
)

/** The full privacy policy, readable offline inside the app. */
@Composable
fun PrivacyPolicyScreen(onBack: () -> Unit) {
    LazyColumn(
        Modifier.fillMaxSize(),
        contentPadding = androidx.compose.foundation.layout.PaddingValues(bottom = 32.dp),
    ) {
        item {
            Row(Modifier.padding(start = 4.dp, top = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                IconButton(onClick = onBack, modifier = Modifier.size(GloveTouchTarget)) {
                    Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Back")
                }
                Text("Privacy policy", style = MaterialTheme.typography.titleLarge)
            }
        }
        item {
            Column(Modifier.padding(horizontal = 20.dp, vertical = 12.dp)) {
                Text(
                    "Your data is yours.",
                    style = MaterialTheme.typography.displaySmall.copy(fontWeight = FontWeight.ExtraBold),
                    modifier = Modifier.semantics { heading() },
                )
                Text(
                    "We don't have access to it, and we will never sell it.",
                    style = MaterialTheme.typography.headlineSmall,
                    color = MaterialTheme.colorScheme.primary,
                    modifier = Modifier.padding(top = 8.dp),
                )
            }
        }
        items(promises) { p ->
            Surface(
                color = MaterialTheme.colorScheme.surfaceContainerHigh,
                shape = MaterialTheme.shapes.large,
                modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 6.dp),
            ) {
                Row(Modifier.padding(16.dp).heightIn(min = 48.dp), verticalAlignment = Alignment.CenterVertically) {
                    Icon(p.icon, contentDescription = null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(36.dp))
                    Spacer(Modifier.width(16.dp))
                    Column {
                        Text(p.title, style = MaterialTheme.typography.titleLarge)
                        Text(p.text, style = MaterialTheme.typography.bodyLarge, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                }
            }
        }
        items(sections) { s ->
            Column(
                Modifier.padding(horizontal = 20.dp).padding(top = 24.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                Text(
                    s.title,
                    style = MaterialTheme.typography.titleLarge,
                    color = MaterialTheme.colorScheme.primary,
                    modifier = Modifier.semantics { heading() },
                )
                s.paragraphs.forEach { Text(it, style = MaterialTheme.typography.bodyLarge) }
            }
        }
        item {
            Text(
                "Last updated 27 October 2026",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(horizontal = 20.dp, vertical = 24.dp),
            )
        }
    }
}
