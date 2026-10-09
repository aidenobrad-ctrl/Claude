# Duskline

A 3D open-world racing game that runs in the browser. Everything is in a single `index.html` file, built on [three.js](https://threejs.org/) r128 loaded from a CDN. There is no build step.

## Play

Open `index.html` in a desktop or mobile browser. You can also serve the folder:

```sh
python3 -m http.server 8000   # then visit http://localhost:8000
```

## What's in it

- **Open valley.** A 2.4 km × 2.4 km procedural landscape with hills, lakes, rocky ridges and snowcaps, about 1,700 trees, rocks, and mountains around the edge. Light comes from a low sunset sun with real-time shadows.
- **Circuit.** A 4.8 km paved loop with lane markings, street lamps, 12 checkpoint gates and a start/finish arch. The road is cut into the terrain and becomes a causeway where it crosses lakes.
- **Free roam.** Drive anywhere. The surface changes the handling: asphalt is fast and grippy, grass and dirt are slower and slide more, and water nearly stops you. There are 24 glowing tokens to find, plus 12 ramps for jumps (three of them on the circuit).
- **Races.** Three laps against three AI rivals (Vega, Okoro and Lindqvist), with a countdown, live position, lap timer, a wrong-way warning, a guide arrow to the next gate, and a results table. Your best lap is saved in your browser.
- **Driving model.** Arcade physics with weight transfer feel, handbrake drifts, slope gravity, real airtime off crests and ramps, a 6-speed gearbox readout, and boost. Boost refills over time and refills faster when you drift, land jumps or collect tokens.
- **Extras.** Chase, far-chase and hood cameras, a minimap, a synthesized engine sound, dust and spray particles, and on-screen touch controls on phones.

## Controls

| Key | Action |
| --- | --- |
| W / ↑ | Throttle |
| S / ↓ | Brake, then reverse |
| A D / ← → | Steer |
| Space | Handbrake (drift) |
| Shift | Boost |
| C | Cycle camera |
| R | Reset to the nearest road |
| Enter | Start a race (or restart) |
| Esc | Back to free roam |
| M / P / H | Mute / pause / hide help |
