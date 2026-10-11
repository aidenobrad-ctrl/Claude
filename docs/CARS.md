# Cars

Twelve cars, one per class. Every make and model is fictional.

Every number here is measured by the automated suite (`tests/physics.test.ts`) on flat dry asphalt with default aids:

- 0–100 and 0–200 km/h use launch control and TCS, without rollout. 0–200 is listed only for cars whose top speed is comfortably above 200 km/h.
- 100–0 km/h braking uses ABS.
- The skidpad has a 40 m radius, with ESC off.

The C# port in `unity/Assets/Halcyon/Core` replays the same tests bit-exactly (`dotnet/Halcyon.Core.Tests`). To regenerate this table, run `node tools/run-sim-tests.mjs physics --grep validation` and then `node tools/run-ts.mjs tools/dev/cars-table.ts`.

Each car is checked against published figures for a comparable real car of its class, and every measured value must be within 10% of its reference. Without slicks or real downforce, lateral grip must stay below about 1.3 g; every car does.

| Car | Class | Drive | Power | Mass | 0–100 | 0–200 | ¼ mile | Top speed | 100–0 | Skidpad |
|---|---|---|---|---|---|---|---|---|---|---|
| Halden Aster GT | Sports | RWD | 286 kW | 1490 kg | 4.74 s | 14.76 s | 12.87 s @ 186 km/h | 297.6 km/h | 31.0 m | 0.948 g |
| Aurelle Pico | Compact | FWD | 85 kW | 1090 kg | 9.49 s | — | 16.97 s @ 135 km/h | 195.4 km/h | 37.4 m | 0.887 g |
| Aurelle Vif R | Hot hatch | FWD | 235 kW | 1400 kg | 5.66 s | 18.34 s | 13.90 s @ 176 km/h | 275.1 km/h | 31.5 m | 1.003 g |
| Hoshida Kiri RS | Rally | AWD | 221 kW | 1540 kg | 5.17 s | 20.37 s | 13.59 s @ 169 km/h | 261.5 km/h | 38.8 m | 0.842 g |
| Halden 280 Sprint | Classic | RWD | 127 kW | 1150 kg | 8.17 s | — | 15.96 s @ 146 km/h | 201.6 km/h | 42.4 m | 0.775 g |
| Stallworth Ironside 454 | Muscle | RWD | 282 kW | 1800 kg | 6.32 s | — | 14.48 s @ 168 km/h | 203.1 km/h | 44.7 m | 0.765 g |
| Nordvik Kyst Sport | GT | AWD | 440 kW | 2110 kg | 3.75 s | 12.65 s | 11.85 s @ 194 km/h | 313.1 km/h | 33.0 m | 0.906 g |
| Orsini Saetta | Super | RWD | 530 kW | 1450 kg | 3.10 s | 7.88 s | 10.41 s @ 233 km/h | 349.1 km/h | 28.0 m | 1.080 g |
| Arcline Vanta | Hyper | AWD | 1418 kW | 2150 kg | 2.17 s | 4.63 s | 8.49 s @ 290 km/h | 408.3 km/h | 29.7 m | 1.076 g |
| Ironbark Ridgeback | Off-road | AWD | 213 kW | 2230 kg | 8.74 s | — | 16.45 s @ 141 km/h | 187.2 km/h | 45.8 m | 0.714 g |
| Stallworth Prairie | Truck | AWD | 313 kW | 2480 kg | 6.36 s | — | 14.59 s @ 153 km/h | 206.2 km/h | 45.3 m | 0.765 g |
| Nordvik Varg | SUV | AWD | 470 kW | 2230 kg | 3.73 s | 12.65 s | 11.84 s @ 194 km/h | 310.6 km/h | 33.5 m | 0.942 g |

## Reference values

Deviations are measured minus reference, rounded. Every value is within 10%.

**Halden Aster GT.** A front-engined, rear-drive coupe with a twin-turbo six: about 290 kW, 1.5 t, on summer tires.

| | Reference | Measured | Deviation |
|---|---|---|---|
| 0–100 | 4.4 s | 4.74 s | +8% |
| 0–200 | 15 s | 14.76 s | −2% |
| 100–0 | 33 m | 31.0 m | −6% |
| Skidpad | 1.0 g | 0.948 g | −5% |
| Top speed | 290 km/h | 297.6 km/h | +3% |

**Aurelle Pico.** A three-cylinder turbo city hatch: 85 kW, 1.1 t, seven-speed twin-clutch.

| | Reference | Measured | Deviation |
|---|---|---|---|
| 0–100 | 9.3 s | 9.49 s | +2% |
| 100–0 | 38 m | 37.4 m | −2% |
| Skidpad | 0.85 g | 0.887 g | +4% |
| Top speed | 195 km/h | 195.4 km/h | 0% |

**Aurelle Vif R.** A front-drive hot hatch: 2.0 turbo, about 235 kW, 1.4 t, limited-slip differential, summer tires.

| | Reference | Measured | Deviation |
|---|---|---|---|
| 0–100 | 5.7 s | 5.66 s | −1% |
| 0–200 | 18 s | 18.34 s | +2% |
| 100–0 | 33 m | 31.5 m | −4% |
| Skidpad | 1.0 g | 1.003 g | 0% |
| Top speed | 275 km/h | 275.1 km/h | 0% |

**Hoshida Kiri RS.** A late-2000s rally-homologation sedan: 2.0 turbo four, about 220 kW, 1.55 t, all-wheel drive. Braking and grip are for gravel-capable tires; the same cars on summer tires stop in about 36 m and pull about 0.93 g.

| | Reference | Measured | Deviation |
|---|---|---|---|
| 0–100 | 5.0 s | 5.17 s | +3% |
| 0–200 | 20 s | 20.37 s | +2% |
| 100–0 | 38 m | 38.8 m | +2% |
| Skidpad | 0.85 g | 0.842 g | −1% |
| Top speed | 250 km/h | 261.5 km/h | +5% |

**Halden 280 Sprint.** A 1971 2.8-litre straight-six roadster: about 125 kW, 1.15 t, four-speed, period radials.

| | Reference | Measured | Deviation |
|---|---|---|---|
| 0–100 | 8 s | 8.17 s | +2% |
| 100–0 | 42 m | 42.4 m | +1% |
| Skidpad | 0.8 g | 0.775 g | −3% |
| Top speed | 200 km/h | 201.6 km/h | +1% |

**Stallworth Ironside 454.** A 1970 7.4-litre big-block coupe: about 280 kW net, 1.8 t, four-speed, rear drum brakes.

| | Reference | Measured | Deviation |
|---|---|---|---|
| 0–100 | 6.2 s | 6.32 s | +2% |
| 100–0 | 45 m | 44.7 m | −1% |
| Skidpad | 0.75 g | 0.765 g | +2% |
| Top speed | 205 km/h | 203.1 km/h | −1% |

**Nordvik Kyst Sport.** A twin-turbo V8 all-wheel-drive performance estate: 441 kW, 2.1 t. Top speed is unrestricted.

| | Reference | Measured | Deviation |
|---|---|---|---|
| 0–100 | 3.6 s | 3.75 s | +4% |
| 0–200 | 12 s | 12.65 s | +5% |
| 100–0 | 34 m | 33.0 m | −3% |
| Skidpad | 0.95 g | 0.906 g | −5% |
| Top speed | 305 km/h | 313.1 km/h | +3% |

**Orsini Saetta.** A mid-engined twin-turbo V8 supercar: 530 kW, 1.45 t, seven-speed twin-clutch.

| | Reference | Measured | Deviation |
|---|---|---|---|
| 0–100 | 2.9 s | 3.10 s | +7% |
| 0–200 | 7.8 s | 7.88 s | +1% |
| 100–0 | 30 m | 28.0 m | −7% |
| Skidpad | 1.1 g | 1.080 g | −2% |
| Top speed | 340 km/h | 349.1 km/h | +3% |

**Arcline Vanta.** A four-motor electric hypercar: about 1.4 MW, 2.15 t, single-speed drive.

| | Reference | Measured | Deviation |
|---|---|---|---|
| 0–100 | 2.05 s | 2.17 s | +6% |
| 0–200 | 4.7 s | 4.63 s | −2% |
| 100–0 | 30 m | 29.7 m | −1% |
| Skidpad | 1.05 g | 1.076 g | +2% |
| Top speed | 400 km/h | 408.3 km/h | +2% |

**Ironbark Ridgeback.** A body-on-frame off-roader: 213 kW naturally aspirated V6, 2.2 t, all-terrain tires. The reference top speed is the maker-limited figure.

| | Reference | Measured | Deviation |
|---|---|---|---|
| 0–100 | 8.5 s | 8.74 s | +3% |
| 100–0 | 44 m | 45.8 m | +4% |
| Skidpad | 0.70 g | 0.714 g | +2% |
| Top speed | 180 km/h | 187.2 km/h | +4% |

**Stallworth Prairie.** A full-size pickup: 313 kW 6.2-litre V8, ten-speed automatic, 2.5 t, all-terrain tires. The reference top speed is the maker-limited figure; the simulation has no limiter.

| | Reference | Measured | Deviation |
|---|---|---|---|
| 0–100 | 6.3 s | 6.36 s | +1% |
| 100–0 | 43 m | 45.3 m | +5% |
| Skidpad | 0.75 g | 0.765 g | +2% |
| Top speed | 190 km/h | 206.2 km/h | +9% |

**Nordvik Varg.** A twin-turbo V8 performance SUV: 470 kW, 2.2 t, summer tires.

| | Reference | Measured | Deviation |
|---|---|---|---|
| 0–100 | 3.6 s | 3.73 s | +4% |
| 0–200 | 12.5 s | 12.65 s | +1% |
| 100–0 | 34 m | 33.5 m | −2% |
| Skidpad | 0.98 g | 0.942 g | −4% |
| Top speed | 300 km/h | 310.6 km/h | +4% |

## Models

Every car has a Blender model made by `blender/build_car.py` (see `docs/UNITY.md`). Each model has three LODs, and each LOD keeps cleanly cut glass, lights, grilles and panel gaps. Review sheets with six views per car are in `blender/renders/`.
