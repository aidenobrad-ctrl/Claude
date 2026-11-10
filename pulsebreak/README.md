# Pulsebreak

A fast, competitive multiplayer FPS for PC, built in Unity 6. "Pulsebreak" is a working title.

Two phases are built so far, both offline: **movement and a firing range**, and **the weapon system with 30 weapons and 21 attachments**. The plan for everything after is in [docs/ROADMAP.md](docs/ROADMAP.md), and how the code is organised is in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

> **Not yet run in Unity.** This was written without access to a Unity Editor. The C# was compiled against Unity's reference assemblies, and the simulation logic and the weapon data were tested outside the engine, but nothing has been opened or played in Unity itself. Expect to report a few first-run issues; [Troubleshooting](#troubleshooting) covers the likely ones.

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
| Left mouse button | Fire (hold to charge on charge weapons) |
| R | Reload |
| 1 / 2 / 3 or mouse wheel | Switch to primary / secondary / melee |
| Esc | Pause: armory and settings |
| F5 | Reset session stats |
| F1 | Hide or show the controls panel |

## The armory

Press **Esc** in play. The armory on the left of the pause menu lets you:

- pick the weapon for each of the three slots (primary, secondary, melee),
- cycle through the attachments that fit it, slot by slot,
- see what each attachment does, as text and as green or red changes on the stat bars.

Changes apply at once. Press Esc again and the new build is in your hands with a full magazine.

## What to try

The range has three areas. You spawn at the firing line facing downrange.

- **Centre: shooting lanes.** Targets at 10, 20, 35, 50 and 75 m, two of them moving, and a tight group of three off to the right for explosives and piercing shots. Each target has head, body and limb hitboxes, 100 health, and stands back up after it drops. The panel at the top right tracks accuracy, headshot rate and the time your last kill took.
- **West: movement course.** Crates to mantle (0.9 m, 1.5 m, then a 2.6 m platform that needs a jump first), a ramp to slide down, a tunnel too low to walk through, and two jump gaps, the second of which needs a sprint.
- **East: the perch.** A raised platform reached by stairs, for long shots.

Things worth a look with the new weapons:

- The **Breaker 12** loads shell by shell; fire mid-reload to cut it short.
- The **Arc Lance** charges while you hold the trigger and goes through several targets in a line.
- The **Carom Blaster**'s shots bounce and hit harder each time; try them off the floor or the cover walls.
- The **Pitcher GL** bounces grenades that burst on a fuse; the **Kiln RL** flies straight and bursts on contact.
- The **Aphelion** has a full-screen scope. Fit the 3x Optic in the armory to swap it for an open sight.
- Fit a **Suppressor** and the shot goes quiet and loses its muzzle flash.

## Weapons

| Class | Weapons |
| --- | --- |
| Assault rifles | Hadron AR, Corvane HR, Quill AR, Triad BR (burst) |
| Submachine guns | Flit, Sprocket, Whisp SD, Brisk PDW |
| Light machine guns | Bulwark LMG, Dynamo |
| Shotguns | Breaker 12 (pump), Hailstorm (automatic), Twinfang (double barrel) |
| Sniper rifles | Aphelion (bolt), Solstice (semi-automatic) |
| Marksman rifles | Verdict DMR, Cadence (two-round burst) |
| Pistols | Notch, Magnate (revolver), Gnat MP (automatic), Trey (burst) |
| Launchers | Kiln RL (rocket), Pitcher GL (grenades) |
| Special | Volley Crossbow, Carom Blaster, Arc Lance, Stinger Repeater, Flicker Knives |
| Melee | Field Knife, Crescent |

Attachments come in six slots: sights (Red Dot, Holo, 3x Optic, 6x Scope, 10x Scope), barrels (Suppressor, Long Barrel, Compensator, Muzzle Brake, Choke), magazines (Extended, Drum, Quick), grips (Vertical, Angled, Stubby), stocks (Light, Heavy, None) and lasers (Laser Sight, Tactical Laser). Each weapon accepts a subset.

The grapple gun from the original brief is not here yet. It is a movement tool more than a weapon, so it is planned alongside abilities.

## Changing and adding content

Everything is data. Select an asset in the Project window and edit it in the Inspector.

- **Movement:** `Assets/_Project/Content/Movement/DefaultMovement.asset`.
- **Weapons:** `Assets/_Project/Content/Weapons/WPN_*.asset`. The `Stats` section holds the numbers; `View` and `Audio` shape the placeholder model and sound.
- **Attachments:** `Assets/_Project/Content/Attachments/ATT_*.asset`. An attachment is a list of stat changes.

To add a weapon:

1. Right-click in the Project window and choose **Create ▸ Pulsebreak ▸ Weapon Definition**.
2. Fill it in. Give it a unique lower-case `weaponId`.
3. Drag the attachments it should accept into its **Attachments** list.
4. Add it to the **Weapons** list on `Content/Weapons/WeaponCatalog.asset`. It now appears in the armory.
5. Run **Pulsebreak ▸ Validate Weapons** to catch mistakes.

To add an attachment: **Create ▸ Pulsebreak ▸ Attachment Definition**, give it a slot and its stat changes, add it to the catalog's **Attachments** list, then to each weapon that should accept it.

A weapon needs code only if it behaves in a way none of these do. The existing behaviours are: automatic, semi-automatic, burst and charge fire; instant bullets with optional piercing; multi-pellet blasts; projectiles with optional gravity, bounces, fuse and explosion; and melee.

## Tests

Open **Window ▸ General ▸ Test Runner**, select **EditMode** and run all. The tests cover fire rate and timing, every fire mode, both reload styles, weapon switching, attachment maths, damage, the movement maths, and the real weapon and attachment assets in the catalog.

## Troubleshooting

**Everything is pink, or the scene looks flat and unlit.** The render pipeline did not get assigned. Run **Pulsebreak ▸ Run Project Doctor** and read the Console. If it reports that the URP asset could not be loaded, create one with **Assets ▸ Create ▸ Rendering ▸ URP Asset (with Universal Renderer)** and assign it under **Project Settings ▸ Graphics**. The game is built to run without it too, on the Built-in pipeline.

**Pressing Play shows an empty scene.** Use **Pulsebreak ▸ Rebuild Firing Range Scene**.

**The armory lists only three "Fallback" weapons.** The scene lost its link to the weapon catalog. Run **Pulsebreak ▸ Run Project Doctor**, which reconnects it.

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
        ├── Shared/    Gameplay rules: movement, weapons, projectiles, damage. Will also run on the server.
        ├── Client/    Input, camera, weapon models, effects, audio, HUD, armory, the firing range.
        ├── Editor/    Project Doctor.
        ├── Content/   Weapon, attachment and movement assets.
        └── Tests/     Unit tests.
```
