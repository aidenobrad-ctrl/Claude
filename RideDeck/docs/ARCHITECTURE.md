# RideDeck architecture

## Modules and packages

| Module | Type | Purpose |
| --- | --- | --- |
| `:core` | Pure Kotlin/JVM | All domain logic with no Android dependency: geo maths, speed smoothing, trip state machine, pace/units, Bike/Walk mode rules, OSRM response mapping, Photon and Overpass parsing, route progress and off-route detection, LRC lyrics parsing, GBFS parsing. Fully unit-tested on the JVM. |
| `:app` | Android application | Compose UI, Hilt DI, Room, DataStore, foreground tracking service, osmdroid map, OSM web services, Media3 music player, lyrics provider. |

`app` packages (`com.ridedeck.app`):

- `data/local` Room entities, DAOs (sessions, route points, favourite tracks), database
- `data/prefs` DataStore-backed user preferences (mode, units, alerts, layers, panel size…)
- `data/session` session repository (create, append points, finalize, totals)
- `data/location` Android `LocationManager` (via `LocationManagerCompat`) wrapped as a cold `Flow<LocationFix>`
- `data/maps` `OsmRepository` (Photon search, OSRM routing, Overpass bike shops), GBFS, tile sources
- `music` MediaStore library, `PlaybackService` (Media3 `MediaSessionService` + ExoPlayer), `MusicController` (MediaController wrapper)
- `data/lyrics` `LyricsProvider` interface + LRCLIB implementation
- `tracking` `TrackingManager` (session engine), `TrackingService` (foreground service), notification, speed alert
- `ui/*` one package per screen (home/speedometer, map, history, settings, permissions, music) plus theme, shared components and the root scaffold with the split music panel
- `di` Hilt modules

Pattern: MVVM. Each screen has a `@HiltViewModel` exposing one `StateFlow<UiState>` built with `combine(...).stateIn(WhileSubscribed(5s))`, so GPS and network work stops a few seconds after a screen leaves the foreground.

## Data model

```
sessions                                   route_points
-----------------------------------       ---------------------------------
id               PK                        id         PK
mode             BIKE|WALK (dominant)      sessionId  FK -> sessions.id (CASCADE)
startedAtMs      wall clock                segment    route segment (breaks at pauses/GPS gaps)
endedAtMs        NULL while recording      latitude, longitude
distanceM                                  timeMs, speedMps, accuracyM, altitudeM
bikeDistanceM / walkDistanceM   per-mode split (mid-session switches)
movingTimeMs / elapsedTimeMs
maxSpeedMps                     session max (user can reset)
bikeMaxSpeedMps / walkMaxSpeedMps   per-mode max, feeds all-time records
avgSpeedMps, lastSegment
```

Lifetime odometers and all-time records are **derived** with one aggregate query over finished sessions (`SessionDao.observeTotals`), so they can never drift from the history and deleting a session corrects them automatically. The Home screen adds the live session on top.

Preferences (DataStore): mode, speed unit, theme, auto-pause, keep-screen-on, Ride Mode lock, speed alert (enabled/limit/sound/vibrate), map layers, bike-share system, music panel visibility and size, lyrics toggle, onboarding flag. Favourite tracks are a Room table keyed by MediaStore id.

## Tracking service design

```
UI (Start) ──startForegroundService──► TrackingService (type=location, START_STICKY)
                                            │ startForeground(notification)
                                            ▼
                                   TrackingManager (@Singleton, single-thread dispatcher)
     LocationManager GPS 1 Hz ───►  TripAccumulator (core)  ──► StateFlow<TripSnapshot> ──► UI, notification
     DataStore prefs ────────────►  mode switch / auto-pause toggle
                                    SpeedAlertMonitor (core) ──► SpeedAlertPlayer (tone + vibration)
                                    batch insert route points (20 pts or 30 s) + running totals ──► Room
```

- All mutable tracking state is confined to `Dispatchers.Default.limitedParallelism(1)`, so there are no locks.
- GPS cadence follows the trip state: 1 Hz while moving, 0.5 Hz while auto-paused, **off** during a manual pause.
- Speed: Doppler speed from the fix when its accuracy is good, otherwise derived from position deltas; then the `SpeedSmoother` clamps implausible values and accelerations and applies a time-aware EMA. Readouts snap to 0 under a noise floor.
- Distance: only counted once the user has moved further than a jitter threshold scaled by GPS accuracy, and never while auto-paused.
- Auto-pause: speed below the mode's threshold for N seconds (6 s bike, 15 s walk); resumes after two fixes above the resume speed.
- Mode can be switched mid-session; distance and max speed are attributed per mode and the session is saved under the dominant mode.
- Crash safety: running totals are written with every flush. If the process dies, the session is either resumed (when Android restarts the sticky service and background location is granted) or closed at its last point on the next launch.
- Notification: ongoing, silent, updates at most every 3 s, with Pause/Resume and Stop actions.

## Maps and navigation

- osmdroid `MapView` inside Compose (`AndroidView`), tied to the lifecycle. CyclOSM tiles in Bike mode, standard OSM tiles otherwise, inverted for dark themes, cached on disk (200 MB). Attribution is always visible.
- Overlays (`MapOverlays`) are long-lived objects updated in place: route polyline, bike shops, bike-share stations, destination and the rider's position.
- Search: Photon with a 300 ms debounce, biased to the rider's position.
- Directions: OSRM `route/v1` on `routed-bike` or `routed-foot` (from the mode) with full polyline geometry and steps; maneuvers are mapped to icons and English instructions in `:core`.
- Turn-by-turn: the rider is projected onto the polyline for the current step, distance to the next manoeuvre, remaining distance and ETA.
- Re-routing: switching mode re-routes immediately; going off route (3 consecutive fixes beyond the mode threshold plus GPS accuracy) re-routes with a 20 s cooldown.
- Layers: bike shops (Overpass `shop=bicycle` within 3 km, on the first fix or "Shops in this area", rate limited), bike-share stations (GBFS v2/v3; hidden in Walk mode unless opted in; "has bikes" filter).

## Music player

- A draggable, resizable split below the map or speedometer.
- `MusicLibrary` lists music from MediaStore (needs `READ_MEDIA_AUDIO`, or storage access before Android 13) and loads album art thumbnails.
- `PlaybackService` owns an ExoPlayer with audio focus (pauses for calls, ducks under alerts) and "becoming noisy" handling. Its media session provides the playback notification, lock-screen and headset/Bluetooth controls, and keeps music playing in the background.
- The panel binds a `MediaController` only while visible; playback is unaffected when it disconnects.
- Lyrics: `LyricsProvider` interface; `LrcLibLyricsProvider` fetches synced LRC, cached per track. The current line is highlighted from the extrapolated playhead.

## Key risks and mitigations

| Risk | Mitigation |
| --- | --- |
| **Background location**: Android 10+ restricts it, Android 12+ blocks starting foreground services from the background, and Android 14 requires a service type and permission. | Tracking always starts from a visible Activity as a `location` foreground service, which needs only while-in-use permission. Background location is optional and only used to resume a session after the system restarts the service; every `startForeground` failure is caught and the session is safely closed. |
| **Battery** | GPS is collected only while a session runs or a map/speedometer screen is visible; cadence drops when auto-paused and stops when paused; DB writes are batched; notification updates are throttled; the music controller disconnects when the panel is hidden. |
| **Fair use of free servers** (OSM tiles, OSRM, Photon, Overpass) | Identifying User-Agent, disk tile cache, search debounce, Overpass rate limit, reroute cooldown. Endpoints are constants in `:core` and easy to point at self-hosted instances. |
| **No Google Play services** | Location uses the platform `LocationManager`, so the app behaves the same on phones without Google. |
| **GPS noise** | Accuracy gating, teleport rejection, gap segmentation, smoothing, and an accuracy-scaled jitter threshold. All of this is in `:core` and unit-tested. |
