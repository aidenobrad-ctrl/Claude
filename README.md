# Duskline

A 3D open-world racing game that runs in the browser. Everything is in a single `index.html` file, built on [three.js](https://threejs.org/) r128 loaded from a CDN. There is no build step.

## Play

Open `index.html` in a desktop or mobile browser. You can also serve the folder:

```sh
python3 -m http.server 8000   # then visit http://localhost:8000
```

## Driving model

The car runs on a single-track ("bicycle") vehicle model, integrated at 240 Hz:

- **Tires.** A Pacejka-style lateral grip curve with slip angles and a friction circle per axle. Grip also depends on load, so more weight on an axle adds less than proportional grip.
- **Weight transfer.** Load shifts front to back under braking and acceleration, and the body pitches and rolls on springs.
- **Engine and gearbox.** A torque curve (peak 540 Nm), clutch slip when pulling away, a rev limiter, and a 6-speed automatic with shift times. Reverse engages when you hold the brake at a standstill.
- **Aero and resistance.** Aerodynamic drag, downforce and rolling resistance, so top speed comes from power against drag rather than a fixed cap.
- **Driver aids.** Traction control, ABS and stability control, as a modern sports car has. The handbrake locks the rear wheels and bypasses stability control, so you can drift.
- **Surfaces and terrain.** Asphalt, grass/dirt and water each have their own grip and drag. Gravity pulls along slopes, and the car takes real ballistic jumps off crests and ramps.
- **Rivals.** The AI cars use the same power, drag and slope physics, and corner at about 94% of the car's measured grip limit.

Measured in headless tests: 0–100 km/h in about 3.4–3.8 s, and about 1.1 g of lateral grip on a skidpad.

## World

- **Terrain.** A 2.4 km × 2.4 km valley with lakes, ridges, snowcaps and mountains around the edge. The terrain is textured, and the water has animated ripples.
- **Circuit.** A 4.8 km loop with a grade capped at 7.5% and smoothed crests and dips. It has lane markings, worn wheel tracks, gravel shoulders, red/white curbs on the corners, W-beam guardrails at drops and bends, and bridges on concrete piers where it crosses a lake.
- **Start/finish.** A grandstand with a cheering crowd, pit garages, sponsor boards and F1-style start lights.
- **Scenery.** Pine, oak and birch trees, bushes, boulders, grass and wildflowers around the car, farms with barns, silos and fences, wind turbines, street lamps and pink sunset clouds.
- **Cars.** Each car has an extruded body with wheel arches and a fastback glasshouse, clear-coat paint with sky reflections, and a wing or ducktail. Details include multi-spoke rims, brake discs and colored calipers, headlights with running lights, a full-width tail-light bar, brake lights, a diffuser, quad exhausts, mirrors and number plates.
- **Effects.** Skid marks, tire smoke, dust and spray, nitro flames, a sound engine with tire screech and wind noise, and a headlight beam.

## Modes

- **Free roam.** Drive anywhere, find 24 glowing tokens, and jump the 12 ramps.
- **Race.** Three laps against Vega, Okoro and Lindqvist, with checkpoints, a guide arrow, live position, lap times, a wrong-way warning and a results table. Your best lap is saved in your browser.

## Controls

| Keyboard | Action |
| --- | --- |
| W / ↑ | Throttle |
| S / ↓ | Brake, then reverse when stopped |
| A D / ← → | Steer |
| Space | Handbrake |
| Shift | Nitro |
| C | Cycle camera (chase, far chase, bonnet) |
| R | Back to the nearest road |
| Enter / Esc | Start a race / back to free roam |
| M / P / H | Mute / pause / hide help |

**Touch:**
- Slide your thumb along the pad at the bottom left to steer (analog).
- Hold GAS and BRAKE on the right. Hold BRAKE at a standstill to reverse.
- HANDBRAKE and NITRO buttons sit above the pedals.
- CAM, ROAD, RACE and pause buttons sit under the minimap in portrait, or along the top in landscape.
- Phones vibrate on impacts, landings and button presses, where the browser allows it.
- Phones and low-core devices automatically get a lighter quality setting.
