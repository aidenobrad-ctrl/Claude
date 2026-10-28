package com.ridedeck.app.data.maps

import com.ridedeck.app.data.prefs.MapStyle

/**
 * Vector map styles from OpenFreeMap: free, open-source OpenStreetMap vector tiles with no API
 * key and no usage limits. MapLibre (also open source) renders them, so the map is sharp at any
 * zoom, rotates and tilts. The required attribution comes with each style and MapLibre shows it.
 */
object MapStyles {
    private const val BASE = "https://tiles.openfreemap.org/styles/"
    const val COLOR = BASE + "liberty"
    const val CLEAN = BASE + "positron"
    const val DARK = BASE + "dark"

    /** Used if a style can't be loaded (for example a renamed style). */
    const val FALLBACK = COLOR

    fun url(style: MapStyle, darkTheme: Boolean): String = when (style) {
        MapStyle.AUTO -> if (darkTheme) DARK else COLOR
        MapStyle.COLOR -> COLOR
        MapStyle.CLEAN -> CLEAN
        MapStyle.DARK -> DARK
    }
}
