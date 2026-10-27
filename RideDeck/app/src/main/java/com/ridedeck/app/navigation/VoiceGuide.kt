package com.ridedeck.app.navigation

import android.content.Context
import android.media.AudioAttributes
import android.media.AudioFocusRequest
import android.media.AudioManager
import android.speech.tts.TextToSpeech
import android.speech.tts.UtteranceProgressListener
import dagger.hilt.android.qualifiers.ApplicationContext
import java.util.Locale
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Speaks navigation prompts with Android's built-in text-to-speech (works offline). Music ducks
 * under each prompt and comes back afterwards, like in a car navigation app.
 */
@Singleton
class VoiceGuide @Inject constructor(
    @ApplicationContext private val context: Context,
) {
    private var tts: TextToSpeech? = null
    private var ready = false
    private val pending = ArrayDeque<String>()
    private var nextId = 0

    private val audioManager = context.getSystemService(AudioManager::class.java)
    private val attributes = AudioAttributes.Builder()
        .setUsage(AudioAttributes.USAGE_ASSISTANCE_NAVIGATION_GUIDANCE)
        .setContentType(AudioAttributes.CONTENT_TYPE_SPEECH)
        .build()
    private val focus = AudioFocusRequest.Builder(AudioManager.AUDIOFOCUS_GAIN_TRANSIENT_MAY_DUCK)
        .setAudioAttributes(attributes)
        .build()

    fun speak(text: String) {
        val engine = tts ?: TextToSpeech(context) { status -> onInit(status) }.also { tts = it }
        if (!ready) {
            pending.addLast(text)
            return
        }
        audioManager?.requestAudioFocus(focus)
        engine.speak(text, TextToSpeech.QUEUE_ADD, null, "rd-${nextId++}")
    }

    /** Releases the speech engine, for example when navigation ends. */
    fun shutdown() {
        tts?.stop()
        tts?.shutdown()
        tts = null
        ready = false
        pending.clear()
        audioManager?.abandonAudioFocusRequest(focus)
    }

    private fun onInit(status: Int) {
        val engine = tts ?: return
        if (status != TextToSpeech.SUCCESS) return
        // Instructions are written in English, so use an English voice.
        val locale = Locale.getDefault().takeIf { it.language == "en" } ?: Locale.US
        engine.setLanguage(locale)
        engine.setAudioAttributes(attributes)
        engine.setOnUtteranceProgressListener(object : UtteranceProgressListener() {
            override fun onStart(utteranceId: String?) = Unit
            override fun onDone(utteranceId: String?) {
                if (tts?.isSpeaking != true) audioManager?.abandonAudioFocusRequest(focus)
            }
            @Deprecated("Deprecated in Java")
            override fun onError(utteranceId: String?) {
                audioManager?.abandonAudioFocusRequest(focus)
            }
        })
        ready = true
        while (pending.isNotEmpty()) speak(pending.removeFirst())
    }
}

/**
 * Decides when to speak each turn: once as a heads-up at a distance, and once again just before
 * the turn. [onProgress] is called on every location update.
 */
class TurnAnnouncer(private val speak: (String) -> Unit) {
    private val spoken = mutableSetOf<String>()

    fun reset() = spoken.clear()

    fun onNewRoute(firstInstruction: String?, summary: String, rerouted: Boolean) {
        spoken.clear()
        speak(if (rerouted) "New route. $summary" else summary)
        firstInstruction?.let { speak(it) }
    }

    /** [upcoming] is the instruction at the end of the current step; [distanceM] how far away it is. */
    fun onProgress(stepIndex: Int, upcoming: String?, distanceM: Double, spokenDistance: String, arrived: Boolean) {
        if (arrived) {
            once("arrived") { speak("You have arrived.") }
            return
        }
        if (upcoming == null) return
        when {
            distanceM <= NOW_M -> once("now-$stepIndex") { speak(upcoming) }
            distanceM <= HEADS_UP_M && distanceM > NOW_M * 2 -> once("soon-$stepIndex") { speak("In $spokenDistance, ${upcoming.replaceFirstChar { it.lowercase() }}") }
        }
    }

    private inline fun once(key: String, action: () -> Unit) {
        if (spoken.add(key)) action()
    }

    companion object {
        private const val HEADS_UP_M = 250.0
        private const val NOW_M = 35.0

        /** "250 m" reads as "250 metres", and so on, for the speech engine. */
        fun spoken(distance: String): String = distance
            .replace(Regex("(\\d) m$"), "$1 metres")
            .replace(Regex("(\\d) km$"), "$1 kilometres")
            .replace(Regex("(\\d) ft$"), "$1 feet")
            .replace(Regex("(\\d) mi$"), "$1 miles")
    }
}
