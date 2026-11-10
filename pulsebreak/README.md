# Pulsebreak

A fast, competitive multiplayer FPS for PC, built in Unity 6. "Pulsebreak" is a working title.

This is **Phase 1**: movement, one weapon and a firing range, offline. The plan for everything after it is in [docs/ROADMAP.md](docs/ROADMAP.md), and how the code is organised is in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

> **Not yet run in Unity.** This phase was written without access to a Unity Editor. The C# was compiled against Unity's reference assemblies and the simulation logic was unit tested outside the engine, but nothing has been opened or played in Unity itself. Expect to report a few first-run issues; [Troubleshooting](#troubleshooting) covers the likely ones.

## Open and play

1. Install **Unity 6** (6000.0 LTS or newer) through Unity Hub.
2. In Unity Hub choose **Add ▸ Add project from disk** and pick the `pulsebreak/game` folder. If Hub says the project's Editor version is not installed, pick the Unity 6 version you have.
3. Let the first import finish. The firing range scene opens by itself. If it does not, use **Pulsebreak ▸ Open Firing Range** in the menu bar.
4. Press **Play** and click inside the Game view to capture the mouse.

On first open Unity creates several settings files and one asset (`Resources/UI/HudPanel.asset`). Commit them; they are part of the project.

## Controls

| Input | Action |
| --- | --- |
| W A S D | Move |
| Mouse | Look |
| Shift | Sprint |
| Space | Jump |
| Ctrl or C | Crouch |
| Crouch while sprinting | Slide |
| Hold Space + W at a ledge | Mantle |
| Q / E | Lean left / right |
| Right mouse button | Aim down sights |
| Left mouse button | Fire |
| R | Reload |
| F5 | Reset session stats |
| F1 | Hide or show the controls panel |
| Esc | Pause menu: sensitivity, aim sensitivity, field of view, volume, damage numbers |

## What to try

The range has three areas. You spawn at the firing line facing downrange.

- **Centre: shooting lanes.** Targets at 10, 20, 35, 50 and 75 m, two of them moving. Each has head, body and limb hitboxes, 100 health, and stands back up after it drops. Damage starts falling off past 28 m. The panel at the top right tracks accuracy, headshot rate and the time your last kill took.
- **West: movement course.** Crates to mantle (0.9 m, 1.5 m, then a 2.6 m platform that needs a jump first), a ramp to slide down, a tunnel too low to walk through, and two jump gaps, the second of which needs a sprint.
- **East: the perch.** A raised platform reached by stairs, for long shots.

The readout at the bottom left shows your speed and movement state, which helps when tuning.

## Tuning

Feel is driven by two assets. Select one in the Project window and edit it in the Inspector, including while the game is running.

- `Assets/_Project/Content/Movement/DefaultMovement.asset` holds speeds, acceleration, friction, gravity, jump height, slide, mantle and lean.
- `Assets/_Project/Content/Weapons/AR_Hadron.asset` holds fire rate, damage and falloff, magazine and reload, ADS, spread, and the recoil pattern.

To add a weapon, right-click in the Project window, choose **Create ▸ Pulsebreak ▸ Weapon Definition**, fill it in, and assign it to the **Weapon** slot on the `FiringRange` object in the scene.

## Tests

Open **Window ▸ General ▸ Test Runner**, select **EditMode** and run all. The tests cover fire rate and timing, fire modes, ammo and reload, spread, recoil, damage falloff and the movement maths.

## Troubleshooting

**Everything is pink, or the scene looks flat and unlit.** The render pipeline did not get assigned. Run **Pulsebreak ▸ Run Project Doctor** and read the Console. If it reports that the URP asset could not be loaded, create one with **Assets ▸ Create ▸ Rendering ▸ URP Asset (with Universal Renderer)** and assign it under **Project Settings ▸ Graphics**. The game is built to run without it too, on the Built-in pipeline.

**Pressing Play shows an empty scene.** Use **Pulsebreak ▸ Rebuild Firing Range Scene**.

**Keys and mouse do nothing.** Go to **Project Settings ▸ Player ▸ Other Settings ▸ Active Input Handling**, set it to **Both**, and let the Editor restart.

**No HUD, or HUD text is missing.** Check the Console for a message starting "Pulsebreak HUD" or "Pulsebreak Project Doctor"; it names the file that failed to load.

**A package fails to resolve.** Open **Window ▸ Package Manager** and update the package it names to the version your Editor offers.

**Compile errors.** Please send them to me as they appear in the Console. A single error stops the whole project from running, and they are quick to fix once seen.

## Layout

```
pulsebreak/
├── docs/        Architecture and roadmap
└── game/        The Unity project
    └── Assets/_Project/
        ├── Shared/    Gameplay rules: movement, weapons, damage. Will also run on the server.
        ├── Client/    Input, camera, weapon model, effects, audio, HUD, the firing range.
        ├── Editor/    Project Doctor.
        ├── Content/   Weapon and movement tuning assets.
        └── Tests/     Unit tests.
```
