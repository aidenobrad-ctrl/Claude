# RideDeck

An Android app for bike rides: a map with cycling directions, a speedometer and trip recorder, and a music player with synced lyrics. Show any one of them on its own or combine them on one screen.

RideDeck is built on open-source libraries and open data. It needs **no API keys, no Google Play services and no accounts**, so it also runs on de-Googled phones (LineageOS, GrapheneOS, /e/OS). Spotify control and Google Maps navigation are optional switches in Settings, each with setup steps in the app.

**Stack:** Kotlin · Jetpack Compose · Material 3 · MVVM · Hilt · Room · DataStore · Coroutines/Flow · MapLibre · Media3 ExoPlayer · minSdk 26 / targetSdk 36

## Install the APK

1. Open the repository's **Releases** page on your phone and download `RideDeck.apk` from the **RideDeck (latest build)** release (or from the latest "RideDeck APK" run under **Actions → Artifacts**).
2. Open the file. Android asks you to allow installs from your browser or file manager once; allow it.
3. Launch RideDeck and grant precise location. Music access is optional.

Every push that changes RideDeck builds a new signed APK, and each one installs as an update over the previous one.

## Publishing on Google Play

The app targets Android 16 (API 36) and CI builds a Play-ready app bundle once your upload key is added as secrets. Follow [docs/PLAY_STORE.md](docs/PLAY_STORE.md); listing text and graphics are in [store/](store/), the privacy policy is [PRIVACY.md](PRIVACY.md).

## Features

- **Pick your views**: the first section of Settings switches Map, Speed and Music on or off in any combination. Portrait and landscape each remember their own combination, and Settings → Speedometer stats hides any of distance, time, average, max and odometer. With the map and speedometer together, the speedometer floats over the map: drag it anywhere and it stays there, double-tap its handle to dock it again, or tap to shrink it to a speed pill; music takes a resizable panel below, or the whole screen on its own.
- **Speedometer and trip tracking**: smoothed GPS speed (km/h or mph), max/average speed, distance, moving time, lifetime odometer and top speed, a speed alert, auto-pause, and foreground recording with the screen off.
- **History** with mode filters and a route map per session.
- **Fits any screen**: the speedometer, stats and album art size themselves to the window, text shrinks instead of clipping at large font sizes, and landscape puts music beside the map with a side navigation rail. Tuned for the Galaxy S26 Ultra (QHD+ tile cache, edge-to-edge with cutout insets, smooth 120 Hz dragging, and a Settings step that stops One UI from putting rides to sleep).
- **Maps**: smooth vector maps (MapLibre with OpenFreeMap's OpenStreetMap tiles; no key, no limits) that rotate and tilt, in Color (default), Auto, Clean or Dark looks. Search (Photon), cycling directions with turn-by-turn steps, ETA and spoken turns (Android text-to-speech, music ducks while it speaks), automatic re-routing, and an optional hand-off to the Google Maps app.
- **Optional Google Maps**: a "Start in Google Maps" button hands the destination to the Google Maps app for voice turn-by-turn cycling navigation.
- **Optional Spotify**: a Spotify tab in the music panel shows what Spotify is playing, with play/pause/skip, a seek bar and synced lyrics. It uses Android's media controls (notification access), so no Spotify account linking or API key is needed.
- **Music player**: plays the songs stored on the phone (MP3, FLAC, OGG, M4A, WAV) with Media3 ExoPlayer. Library, seek bar, shuffle, favourites, album art, lock-screen and headset controls, background playback, and synced lyrics from LRCLIB.
- **Safety and UX**: large touch targets, OLED black theme, Ride Mode that locks complex menus while moving, keep-screen-on, permission explanations and TalkBack labels.

## Docs

- [docs/SETUP.md](docs/SETUP.md): build from source, CI and signing, troubleshooting
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): modules, data model, services, risks
- [docs/ASSUMPTIONS.md](docs/ASSUMPTIONS.md): decisions, verification status and known limitations

## Project layout

```
RideDeck/
├── ci/ridedeck-ci.jks                shared signing key for CI builds
├── core/            pure Kotlin domain logic + JVM unit tests
│   └── src/main/kotlin/com/ridedeck/core/
│       ├── format/      units, pace, formatting
│       ├── gbfs/        GBFS + systems.csv parsing
│       ├── geo/         haversine, polyline projection, polyline codec
│       ├── lyrics/      LRC parser, LRCLIB response mapping
│       ├── mode/        ModeProfile (Bike/Walk rules), map layer and Ride Mode policies
│       ├── model/       ActivityMode, SpeedUnit, LocationFix
│       ├── osm/         Photon search and Overpass bike-shop parsing
│       ├── routing/     OSRM mapping, route progress, off-route + reroute policy
│       └── tracking/    SpeedSmoother, TripAccumulator, SpeedAlertMonitor
└── app/             Android app
    └── src/main/java/com/ridedeck/app/
        ├── data/        local (Room) · prefs · session · location · maps · lyrics · net
        ├── di/          Hilt modules
        ├── music/       MediaStore library, PlaybackService (Media3), MusicController
        ├── tracking/    TrackingService, TrackingManager, notification, alerts
        └── ui/          main · home · map · history · settings · permissions · music · theme · components
```
