package com.ridedeck.app.data.maps

import org.osmdroid.tileprovider.tilesource.OnlineTileSourceBase
import org.osmdroid.tileprovider.tilesource.TileSourceFactory
import org.osmdroid.tileprovider.tilesource.XYTileSource

/** Map styles. Bike mode uses CyclOSM, an OpenStreetMap style that highlights cycling infrastructure. */
object MapTiles {
    val CYCLE: OnlineTileSourceBase = XYTileSource(
        "CyclOSM",
        0,
        20,
        256,
        ".png",
        arrayOf(
            "https://a.tile-cyclosm.openstreetmap.fr/cyclosm/",
            "https://b.tile-cyclosm.openstreetmap.fr/cyclosm/",
            "https://c.tile-cyclosm.openstreetmap.fr/cyclosm/",
        ),
        "© CyclOSM | © OpenStreetMap contributors",
    )

    val STANDARD: OnlineTileSourceBase = TileSourceFactory.MAPNIK

    const val STANDARD_ATTRIBUTION = "© OpenStreetMap contributors"
    const val CYCLE_ATTRIBUTION = "© CyclOSM · © OpenStreetMap contributors"
}
