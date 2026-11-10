# Pulsebreak

A fast, competitive multiplayer FPS for PC, built in Unity 6. "Pulsebreak" is a working title.

Three phases are built so far, all offline: **movement and a firing range**, **the weapon system with 30 weapons and 21 attachments**, and **loadouts saved on your machine**. The plan for everything after is in [docs/ROADMAP.md](docs/ROADMAP.md), and how the code is organised is in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

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
| G / F | Throw utility item 1 / 2 |
| B | Quick swap: pick another loadout by number |
| Esc | Pause: loadouts and settings |
| F5 | Reset session stats |
| F1 | Hide or show the controls panel |

## Loadouts

A loadout is a primary weapon, a secondary, a melee weapon, two utility items and a perk set. Press **Esc** in play to open the loadout screen.

- **Pick one to use it.** The list on the left has six default loadouts (Assault, Rusher, Marksman, Breacher, Sharpshooter, Demolition) and, below them, your own. Clicking a loadout equips it.
- **Make your own.** Press **New** to copy the selected loadout, or just change something in a default: defaults cannot be altered, so the game makes your own copy and edits that. You can keep up to ten.
- **Edit it.** The tabs across the top are the six slots. Choose a weapon and cycle its attachments, choose each utility item, choose a perk set. The stat bars show what attachments and perks do to the weapon.
- **Rename it** by typing in the name box and pressing Enter. **Delete** removes it.
- **Nothing to save.** Every change is written straight away and is there the next time you start the game.

Press **B** in play for the quick swap list: every loadout with a number beside it. Press the number and you are carrying it, with full ammunition. This stands in for choosing a loadout between rounds and on respawn until there are rounds.

Loadouts are kept in a file called `loadouts.json` in the game's save folder; **Pulsebreak ▸ Open Saved Loadouts Folder** in the Editor takes you to it. Delete the file to start over. If it ever becomes unreadable, the game keeps it under another name and starts from the defaults.

This phase is local only, as asked. Loadouts do not follow you to another machine until accounts and the backend exist.

## What to try

The range has three areas. You spawn at the firing line facing downrange.

- **Centre: shooting lanes.** Targets at 10, 20, 35, 50 and 75 m, two of them moving, and a tight group of three off to the right for explosives and piercing shots. Each target has head, body and limb hitboxes, 100 health, and stands back up after it drops. The panel at the top right tracks accuracy, headshot rate and the time your last kill took.
- **West: movement course.** Crates to mantle (0.9 m, 1.5 m, then a 2.6 m platform that needs a jump first), a ramp to slide down, a tunnel too low to walk through, and two jump gaps, the second of which needs a sprint.
- **East: the perch.** A raised platform reached by stairs, for long shots.

Utility and perks:

- **Frag Grenade** bounces and bursts on a fuse; **Impact Grenade** bursts on contact; **Shock Charge** is a wide, weak blast; **Ricochet Disc** skips off surfaces and hits harder each bounce.
- On the range, used utility items come back after a few seconds.
- Perk sets are passive: try **Skirmisher** and watch the speed readout, or **Demolitionist** and count your grenades.

Things worth a look among the weapons:

- The **Breaker 12** loads shell by shell; fire mid-reload to cut it short.
- The **Arc Lance** charges while you hold the trigger and goes through several targets in a line.
- The **Carom Blaster**'s shots bounce and hit harder each time; try them off the floor or the cover walls.
- The **Pitcher GL** bounces grenades that burst on a fuse; the **Kiln RL** flies straight and bursts on contact.
- The **Aphelion** has a full-screen scope. Fit the 3x Optic on the loadout screen to swap it for an open sight.
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

Everything is data. Select an asset in the Project window and edit it in the Inspector. All of it lives under `Assets/_Project/Content/`.

| Folder | Assets | What they are |
| --- | --- | --- |
| `Movement/` | `DefaultMovement` | Speeds, jump, slide, mantle, lean |
| `Weapons/` | `WPN_*`, `WeaponCatalog` | One per weapon; the catalog lists weapons and attachments |
| `Attachments/` | `ATT_*` | A slot and a list of stat changes |
| `Utilities/` | `UTL_*` | A throwable: how many, how it is thrown, what the thrown object does |
| `Perks/` | `PRK_*` | A perk set: weapon stat changes, move speed, utility bonuses |
| `Loadouts/` | `LDT_*`, `LoadoutCatalog` | Default loadouts; the catalog lists utility, perks and defaults |

To add something, create the asset from **Assets ▸ Create ▸ Pulsebreak**, give it a unique lower-case id, and add it to the right catalog list:

- **Weapon:** add to `WeaponCatalog` ▸ Weapons, and drag the attachments it accepts into its own Attachments list.
- **Attachment:** add to `WeaponCatalog` ▸ Attachments, then to each weapon that should accept it.
- **Utility item, perk set or default loadout:** add to the matching list on `LoadoutCatalog`.

Then run **Pulsebreak ▸ Validate Content**, which checks all of it and logs anything wrong.

A weapon or utility item needs code only if it behaves in a way none of the existing ones do. The existing behaviours are: automatic, semi-automatic, burst and charge fire; instant bullets with optional piercing; multi-pellet blasts; projectiles with optional gravity, bounces, fuse and explosion; and melee.

Removing or renaming content is safe for saved loadouts: anything a save refers to that no longer exists is swapped for a default when it loads.

## Tests

Open **Window ▸ General ▸ Test Runner**, select **EditMode** and run all. The tests cover fire rate and timing, every fire mode, both reload styles, weapon switching, attachment maths, damage, the movement maths, loadout rules, the utility belt, creating, editing and saving loadouts, and the real content assets.

## Troubleshooting

**Everything is pink, or the scene looks flat and unlit.** The render pipeline did not get assigned. Run **Pulsebreak ▸ Run Project Doctor** and read the Console. If it reports that the URP asset could not be loaded, create one with **Assets ▸ Create ▸ Rendering ▸ URP Asset (with Universal Renderer)** and assign it under **Project Settings ▸ Graphics**. The game is built to run without it too, on the Built-in pipeline.

**Pressing Play shows an empty scene.** Use **Pulsebreak ▸ Rebuild Firing Range Scene**.

**There is only one loadout, called "Fallback".** The scene lost its link to the loadout catalog. Run **Pulsebreak ▸ Run Project Doctor**, which reconnects it.

**A loadout changed by itself.** Something it used was removed or no longer fits, and it was repaired on load. That is by design; pick the item again.

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
        ├── Shared/    Gameplay rules: movement, weapons, projectiles, damage, loadouts. Will also run on the server.
        ├── Client/    Input, camera, weapon models, effects, audio, HUD, loadout screen and saves, the firing range.
        ├── Editor/    Project Doctor.
        ├── Content/   Weapons, attachments, utility items, perk sets, default loadouts, movement.
        └── Tests/     Unit tests.
```
