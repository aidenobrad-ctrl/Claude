package com.ridedeck.core.lyrics

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.jsonPrimitive

data class LyricLine(val timeMs: Long, val text: String)

sealed interface Lyrics {
    /** Time-stamped lyrics; the panel highlights the current line. */
    data class Synced(val lines: List<LyricLine>) : Lyrics {
        /** Index of the line being sung at [positionMs], or -1 before the first line. */
        fun indexAt(positionMs: Long): Int {
            var lo = 0
            var hi = lines.size - 1
            var result = -1
            while (lo <= hi) {
                val mid = (lo + hi) ushr 1
                if (lines[mid].timeMs <= positionMs) {
                    result = mid
                    lo = mid + 1
                } else {
                    hi = mid - 1
                }
            }
            return result
        }
    }

    /** The provider only has untimed text. */
    data class Plain(val text: String) : Lyrics

    data object Instrumental : Lyrics
}

/** Parser for the LRC format: `[mm:ss.xx]line`, several stamps per line, and `[offset:±ms]`. */
object LrcParser {
    private val timeTag = Regex("""\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?]""")
    private val offsetTag = Regex("""\[offset:\s*([+-]?\d+)]""", RegexOption.IGNORE_CASE)

    fun parse(lrc: String): Lyrics.Synced {
        // Per the LRC spec a positive offset shifts lyrics earlier.
        val offset = offsetTag.find(lrc)?.groupValues?.get(1)?.toLongOrNull() ?: 0L
        val lines = mutableListOf<LyricLine>()
        lrc.lineSequence().forEach { raw ->
            val line = raw.trim()
            val stamps = timeTag.findAll(line).toList()
            if (stamps.isEmpty()) return@forEach
            val text = line.substring(stamps.last().range.last + 1).trim()
            stamps.forEach { m ->
                val min = m.groupValues[1].toLong()
                val sec = m.groupValues[2].toLong()
                val fraction = m.groupValues[3]
                val ms = when (fraction.length) {
                    0 -> 0L
                    1 -> fraction.toLong() * 100
                    2 -> fraction.toLong() * 10
                    else -> fraction.take(3).toLong()
                }
                val time = (min * 60_000 + sec * 1000 + ms - offset).coerceAtLeast(0)
                lines += LyricLine(time, text)
            }
        }
        return Lyrics.Synced(lines.sortedBy { it.timeMs })
    }
}

/** Maps an LRCLIB `/api/get` response to [Lyrics]. */
object LrcLibParser {
    private val json = Json { ignoreUnknownKeys = true }

    fun parse(body: String): Lyrics? {
        val obj = json.parseToJsonElement(body) as? JsonObject ?: return null
        if (obj["instrumental"]?.jsonPrimitive?.booleanOrNull == true) return Lyrics.Instrumental
        val synced = obj["syncedLyrics"]?.jsonPrimitive?.contentOrNull
        if (!synced.isNullOrBlank()) {
            val parsed = LrcParser.parse(synced)
            if (parsed.lines.isNotEmpty()) return parsed
        }
        val plain = obj["plainLyrics"]?.jsonPrimitive?.contentOrNull
        if (!plain.isNullOrBlank()) return Lyrics.Plain(plain)
        return null
    }
}
