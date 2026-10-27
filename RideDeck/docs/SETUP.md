# RideDeck setup guide

RideDeck needs no API keys or accounts. Everything it talks to is free and open: OpenFreeMap vector map tiles (OpenStreetMap data), OSRM routing, Photon search and LRCLIB.

## Getting the APK without building

The GitHub Actions workflow `.github/workflows/ridedeck-apk.yml` (at the repository root, building the `RideDeck/` folder) runs on every push to `main` or `ridedeck` that touches RideDeck. It runs the `:core` unit tests, builds a signed release APK, attaches it to the workflow run as the `RideDeck-apk` artifact, and replaces the `ridedeck-latest` GitHub Release with the new `RideDeck.apk`. Pushing a tag such as `ridedeck-v1.0.0` also creates a versioned release.

To install: download `RideDeck.apk` on the phone, open it, and allow installs from that app when Android asks.

## Signing

- By default CI signs with `ci/ridedeck-ci.jks` (password and alias `ridedeck`). It is committed on purpose so every build is signed with the same key and installs as an update. Anyone with the repo can sign APKs with it, so it is only suitable for personal or test builds.
- For your own private key, add these repository secrets (Settings → Secrets and variables → Actions) and CI uses them instead:
  - `RIDEDECK_KEYSTORE_BASE64`: `base64 -w0 your.jks`
  - `RIDEDECK_KEYSTORE_PASSWORD`, `RIDEDECK_KEY_ALIAS`, `RIDEDECK_KEY_PASSWORD`
- Switching keys means uninstalling the old build once, because Android refuses updates signed with a different key.

## Building locally

You need Android Studio (or the Android SDK command-line tools) with JDK 17+.

```bash
./gradlew :core:test                     # JVM unit tests (distance, pace, smoothing, mode switching, routing, search, lyrics, GBFS)
./gradlew assembleDebug                  # debug APK in app/build/outputs/apk/debug/
./gradlew assembleRelease                # signed release APK
./gradlew connectedDebugAndroidTest      # Room DAO test (needs a device or emulator)
```

## Using the app

On first launch RideDeck explains and requests permissions. Only precise location is required.

- **Ride tab**: tap Start to record. Recording continues with the screen off; use the notification to pause or stop.
- **Ride screen**: choose what's on screen (map, speedometer, music) at the top of Settings. Search a destination on the map for cycling directions.
- **Music** (bottom bar) opens the player. Copy music files into the phone's Music folder; they appear in the library. Tap a song to play, the heart to add it to favourites, and use the Lyrics tab for synced lyrics. Drag the panel's handle to resize it.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| "App not installed" when updating | The installed build was signed with a different key. Uninstall it once, then install the new APK. |
| Blank grey map | No internet, or the tile server is busy. Tiles you have already seen are cached for offline use. |
| "No cycling route found" | OSRM could not connect the points by bike (for example across water). Try Walk mode or a nearby destination. |
| Music panel says "No music found" | The files must be in shared storage (for example `Music/`) and at least 30 seconds long. Tap "Scan again". |
| Music stops when the screen locks | Some phones kill background apps aggressively; set RideDeck's battery usage to "Unrestricted". |
