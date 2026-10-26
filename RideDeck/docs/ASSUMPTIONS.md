# Assumptions and known limitations

## Verification status

- The `:core` module (all tracking, pace, smoothing, mode-switching, routing, search, lyrics and GBFS logic) was compiled and its **65 unit tests pass**.
- The `:app` module is compiled by the GitHub Actions workflow, which is the real compile check; the authoring environment could not reach Google's Maven repository. Its sources were syntax-checked with the Kotlin 2.1 compiler.
- The Room DAO test is an instrumented test (`connectedDebugAndroidTest`) and has not been run yet.
- Dependency versions are pinned to a mid-2025 set that is known to work together (AGP 8.10, Kotlin 2.1.21, Compose BOM 2025.05.01, Hilt 2.56.2, Room 2.7.1, osmdroid 6.1.20, Media3 1.6.1). Bump them together in `gradle/libs.versions.toml`.

## Product decisions

- **Bike only, one screen** (requested 2026-10-26). Walk mode was removed from the UI; the tracking engine still has the mode internally and rides saved as walks by earlier builds remain in History. The Ride tab is the map with a floating speedometer and the music panel below it.
- **Spotify and Google Maps are optional.** Spotify is controlled through its Android media session (needs notification access; on Android 13+ sideloaded apps must first get "Allow restricted settings" in App info). Liking songs in Spotify isn't possible this way. Google Maps is used by handing off the destination with the public `google.navigation:` intent in bicycle mode; its map isn't embedded, because embedding needs a paid API key.

1. **Open source only.** Google Maps, Places, Routes, Play services location and Spotify were replaced with osmdroid + OpenStreetMap tiles, OSRM (FOSSGIS servers at routing.openstreetmap.de), Photon (photon.komoot.io), the Overpass API, Android's own LocationManager, and a built-in Media3 ExoPlayer music player. No keys or accounts are needed.
2. **Map style.** Bike mode uses the CyclOSM style, which shows cycle lanes and bike infrastructure; Walk mode (or with "Cycling map" off) uses the standard OSM style. Dark themes invert the tiles. These are free community tile servers with fair-use policies; heavy use should move to a self-hosted or commercial tile server.
3. **Music** comes from the phone's own storage (MediaStore). Streaming services are not integrated. Favourites are stored in RideDeck's database and are separate from any other music app. Lyrics come from LRCLIB (free, community-sourced) behind `LyricsProvider`.
4. **Mid-session mode switch** keeps one session. Distance and max speed are split per mode (so odometers and records stay correct) and the session is listed under the mode that covered the most distance.
5. **Max speed** is taken from the smoothed speed, so single GPS spikes never become records. It can read slightly below a raw instantaneous peak. "Reset max" clears the session max but not the per-mode all-time record data.
6. **Very short sessions** (under 20 m and under 60 s moving) are discarded on Stop as accidental starts. Everything else is saved.
7. **Auto-pause** thresholds: Bike under 0.9 m/s for 6 s (resume at 1.6 m/s); Walk under 0.3 m/s for 15 s (resume at 0.6 m/s). Detection is GPS-only (no Google activity recognition).
8. **Speed alert** is Bike-only, fires when crossing the limit, repeats every 30 s while still over, and re-arms after dropping 0.5 m/s below. It uses the notification volume.
9. **Ride Mode** locks search, History and Settings while a session is active and you are moving (Bike ≥ 1.5 m/s, Walk ≥ 1.0 m/s). Session controls, the map, mode toggle and music stay usable.
10. **Live speed when not recording.** The Ride screen shows live speed while it is visible even before you press Start. GPS stops when you leave the screen.
11. **Background location is optional.** Recording runs as a while-in-use `location` foreground service. "Allow all the time" is only used to resume a recording if Android kills and restarts the app.
12. **Bike-share systems** come from MobilityData's public `systems.csv`, ranked by your country and city (via the Geocoder), or you can paste any `gbfs.json` URL. Feeds that need authentication aren't supported. Stations are refreshed every 2 minutes (respecting the feed TTL) and only the 150 nearest to the camera are drawn.
13. **Bike shops** are loaded on the first location fix and on "Shops in this area" (20 results within 3 km, rate limited) to respect the shared Overpass server.
14. **Navigation is visual** (instruction card, step list, ETA). There is no voice guidance.
15. **Units:** km/h goes with km and min/km; mph goes with miles and min/mi.
16. **Signing.** CI signs with a committed shared key so builds update each other; repository secrets can replace it (see SETUP.md).

## Known limitations

- The free OSRM, Photon and Overpass servers have no uptime guarantee and rate-limit heavy use; errors are shown and the app keeps working without them.
- Search results are in English, German or French (Photon's supported languages); other languages fall back to English.
- Phones without a GPS chip or with location off cannot record; network-only location is used just for the map dot.
- Off-route detection needs three consecutive bad fixes and re-routes are at most every 20 s, so in dense streets a reroute can take a few seconds.
- Process death during a session without background location: the session is closed at its last saved point (at most ~30 s or 20 points of route are lost), not resumed.
- The detail map sits inside a scrolling page; vertical drags on the map may scroll the page instead of panning.
