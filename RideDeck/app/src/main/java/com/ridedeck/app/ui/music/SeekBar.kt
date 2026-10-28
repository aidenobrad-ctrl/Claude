package com.ridedeck.app.ui.music

import android.os.SystemClock
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Slider
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.ridedeck.app.music.PlaybackState
import com.ridedeck.core.format.Formatters
import kotlinx.coroutines.delay

/** Song progress with a draggable thumb; the new position is sent when the drag ends. */
@Composable
fun SeekBar(playback: PlaybackState, onSeek: (Long) -> Unit, modifier: Modifier = Modifier) {
    val duration = playback.durationMs.coerceAtLeast(0)
    val position by produceState(playback.positionAt(SystemClock.elapsedRealtime()), playback) {
        while (true) {
            value = playback.positionAt(SystemClock.elapsedRealtime())
            if (!playback.isPlaying) break
            delay(500)
        }
    }
    var dragging by remember { mutableStateOf<Float?>(null) }
    val shownMs = dragging?.let { (it * duration).toLong() } ?: position
    Row(modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Text(Formatters.duration(shownMs), style = MaterialTheme.typography.labelMedium)
        Slider(
            value = if (duration > 0) (shownMs.toFloat() / duration).coerceIn(0f, 1f) else 0f,
            onValueChange = { dragging = it },
            onValueChangeFinished = {
                dragging?.let { onSeek((it * duration).toLong()) }
                dragging = null
            },
            enabled = duration > 0,
            modifier = Modifier
                .weight(1f)
                .padding(horizontal = 8.dp)
                .semantics { contentDescription = "Song position" },
        )
        Text(Formatters.duration(duration), style = MaterialTheme.typography.labelMedium)
    }
}
