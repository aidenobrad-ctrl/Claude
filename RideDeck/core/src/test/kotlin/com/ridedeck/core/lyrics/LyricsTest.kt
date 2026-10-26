package com.ridedeck.core.lyrics

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class LyricsTest {

    @Test
    fun `parses timestamps, repeated stamps and offset`() {
        val lrc = """
            [ar:Someone]
            [offset:+500]
            [00:12.00]First line
            [00:15.5][01:15.50]Chorus
            [00:20.123]Third
        """.trimIndent()
        val lyrics = LrcParser.parse(lrc)
        assertEquals(listOf(11_500L, 15_000L, 19_623L, 75_000L), lyrics.lines.map { it.timeMs })
        assertEquals("Chorus", lyrics.lines[1].text)
        assertEquals("Chorus", lyrics.lines[3].text)
    }

    @Test
    fun `index lookup finds the active line`() {
        val lyrics = LrcParser.parse("[00:01.00]a\n[00:05.00]b\n[00:09.00]c")
        assertEquals(-1, lyrics.indexAt(500))
        assertEquals(0, lyrics.indexAt(1_000))
        assertEquals(1, lyrics.indexAt(8_999))
        assertEquals(2, lyrics.indexAt(60_000))
    }

    @Test
    fun `lrclib prefers synced, falls back to plain, flags instrumental`() {
        val synced = LrcLibParser.parse("""{"syncedLyrics":"[00:01.00]hi","plainLyrics":"hi"}""")
        assertTrue(synced is Lyrics.Synced)
        val plain = LrcLibParser.parse("""{"syncedLyrics":null,"plainLyrics":"just text"}""")
        assertEquals(Lyrics.Plain("just text"), plain)
        assertEquals(Lyrics.Instrumental, LrcLibParser.parse("""{"instrumental":true}"""))
        assertNull(LrcLibParser.parse("""{"syncedLyrics":null,"plainLyrics":null}"""))
    }
}
