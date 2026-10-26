package com.ridedeck.app.tracking

import android.content.Context
import android.media.AudioManager
import android.media.ToneGenerator
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton

/** Plays the over-speed alert: a short beep pattern and/or a vibration. */
@Singleton
class SpeedAlertPlayer @Inject constructor(
    @ApplicationContext private val context: Context,
) {
    private val mainHandler = Handler(Looper.getMainLooper())

    fun play(sound: Boolean, vibrate: Boolean) {
        if (sound) beep()
        if (vibrate) vibrate()
    }

    private fun beep() {
        mainHandler.post {
            val tone = try {
                ToneGenerator(AudioManager.STREAM_NOTIFICATION, ToneGenerator.MAX_VOLUME)
            } catch (e: RuntimeException) {
                // ToneGenerator throws if the audio system is unavailable.
                return@post
            }
            tone.startTone(ToneGenerator.TONE_PROP_BEEP2, BEEP_MS)
            mainHandler.postDelayed({ tone.release() }, BEEP_MS + 100L)
        }
    }

    private fun vibrate() {
        val vibrator: Vibrator? = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            context.getSystemService(VibratorManager::class.java)?.defaultVibrator
        } else {
            @Suppress("DEPRECATION")
            context.getSystemService(Context.VIBRATOR_SERVICE) as? Vibrator
        }
        if (vibrator?.hasVibrator() != true) return
        vibrator.vibrate(VibrationEffect.createWaveform(longArrayOf(0, 250, 120, 250, 120, 400), -1))
    }

    private companion object {
        const val BEEP_MS = 600
    }
}
