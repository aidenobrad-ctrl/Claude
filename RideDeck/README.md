# RideDeck

An Android app for bike rides and walks: a glove-friendly speedometer and trip recorder, OpenStreetMap navigation for cycling and walking, bike shops and bike-share stations, and a built-in music player with synced lyrics.

RideDeck is built only on open-source libraries and open data. It needs **no API keys, no Google Play services and no accounts**, so it also runs on de-Googled phones (LineageOS, GrapheneOS, /e/OS).

**Stack:** Kotlin · Jetpack Compose · Material 3 · MVVM · Hilt · Room · DataStore · Coroutines/Flow · osmdroid · Media3 ExoPlayer · minSdk 26 / targetSdk 35

## Install the APK

1. Open the repository's **Releases** page on your phone and download `RideDeck.apk` from the **RideDeck (latest build)** release (or from the latest "RideDeck APK" run under **Actions → Artifacts**).
2. Open the file. Android asks you to allow installs from your browser or file manager once; allow it.
3. Launch RideDeck and grant precise location. Music access is optional.

Every push that changes RideDeck builds a new signed APK, and each one installs as an update over the previous one.

## Features

- **Bike / Walk mode** on the Ride and Map screens, switchable mid-session. Mode drives routing, speed vs pace display, alerts, the saved session type, history filters and map style.
- **Speedometer and trip tracking**: smoothed GPS speed (km/h or mph), pace in Walk mode, max/average speed, distance, moving time, lifetime odometers and max-speed records per mode, a Bike-mode speed alert, auto-pause, and foreground recording with the screen off.
- **History** with mode filters and a route map per session.
- **Maps** (OpenStreetMap via osmdroid; CyclOSM cycling style in Bike mode): search (Photon), cycling and walking directions with steps and ETA (OSRM), automatic re-routing, bike shops (Overpass API) and GBFS bike-share stations.
- **Music player**: plays the songs stored on the phone (MP3, FLAC, OGG, M4A, WAV) with Media3 ExoPlayer. Library, shuffle, favourites, album art, lock-screen and headset controls, background playback, and synced lyrics from LRCLIB.
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
