# Pulsebreak

A fast, competitive multiplayer FPS for PC, built in Unity 6. "Pulsebreak" is a working title.

Four phases are built so far: **movement and a firing range**, **the weapon system with 30 weapons and 21 attachments**, **loadouts saved on your machine**, and **online 1v1 duels** on a server that owns the match. The plan for everything after is in [docs/ROADMAP.md](docs/ROADMAP.md), and how the code is organised is in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

> **Not yet run in Unity.** This was written without access to a Unity Editor. The C# was compiled against Unity's reference assemblies. The simulation, the weapon data and the netcode were tested outside the engine, including whole matches between a real server and two real clients on a simulated bad connection. But nothing has been opened or played in Unity itself, so the menus, the HUD and everything else you look at are unverified. Expect to report a few first-run issues; [Troubleshooting](#troubleshooting) covers the likely ones.

## Open and play

1. Install **Unity 6** (6000.0 LTS or newer) through Unity Hub.
2. In Unity Hub choose **Add ▸ Add project from disk** and pick the `pulsebreak/game` folder. If Hub says the project's Editor version is not installed, pick the Unity 6 version you have.
3. Let the first import finish. The firing range scene opens by itself. If it does not, use **Pulsebreak ▸ Open Firing Range** in the menu bar.
4. Press **Play** and click inside the Game view to capture the mouse.

That is the offline range. The main menu and online play are in a second scene: **Pulsebreak ▸ Open Duel (Main Menu)**. See [Play online](#play-online).

On first open Unity creates several settings files and one asset (`Resources/UI/HudPanel.asset`). Commit them; they are part of the project.

## Play online

A duel is two players, first to win three rounds. One copy of the game runs the server; it can be one of the two players (hosting) or a third copy running nothing else (a dedicated server).

### The quickest test: one computer

You need two copies of the game running. The Editor is one; a build is the other.

1. Make a build: **File ▸ Build Profiles** (Build Settings in older versions), check that **Duel** is the first scene in the list and **FiringRange** the second, and press **Build**. If the list is wrong, run **Pulsebreak ▸ Run Project Doctor** first.
2. In the Editor, open the duel scene (**Pulsebreak ▸ Open Duel (Main Menu)**), press **Play**, type a name and choose **Host a match**. You are placed in the arena for a warm-up: move and shoot freely.
3. Start the build, leave the address as `127.0.0.1` and choose **Join**.
4. The match starts as soon as the second player is in: a three second countdown, then the round.

Both copies keep running when their window is not in front, so you can click between them.

### Two computers on the same network

1. Both run a build. One chooses **Host a match**; the banner shows the address others should use, something like `192.168.1.20`.
2. The other types that address and chooses **Join**.

If the second computer gets "No answer from the server", the host's firewall is the usual cause: allow the game through it, or allow UDP port 7777.

Over the internet the host also has to forward UDP port 7777 on their router to their computer and give the other player their public address. That goes away when matches run on dedicated servers found through matchmaking.

### A dedicated server

Start a build with no window and no player:

```
Pulsebreak.exe -batchmode -nographics -server
```

It logs "Listening on UDP port 7777". Both players then **Join** its address. Add `-logFile server.log` to choose where the log goes.

### In a match

- **Warm-up** while you wait for an opponent. Ammunition and utility never run out.
- **Countdown.** You can look around and swap weapons; movement and fire unlock when it ends.
- **Round.** Eliminate the other player. If 90 seconds run out, whoever has more health takes it. Equal health is a draw.
- **Between rounds** you swap ends. Press **B** (or Esc for the full loadout screen) to pick another loadout; it is used from the next round.
- **First to three rounds wins.** A new match starts by itself a few seconds later. Leaving a match forfeits it.

On screen: score and round clock at the top, your health at the bottom, eliminations at the top right, and at the top left your ping, packet loss and a count of corrections (**F3** hides it). **Esc ▸ Leave match** returns to the menu.

### Trying it with a bad connection

To feel the game at a given ping, make your own connection worse on purpose. Each value applies in both directions, so 60 ms of delay adds about 120 ms to your ping.

- In the Editor: select the **Duel** object in the duel scene and set **Testing ▸ Simulated Connection** before pressing Play.
- In a build: start it with, for example, `-lag 60 -jitter 10 -loss 2`.

### Command line

| Option | Effect |
| --- | --- |
| `-server` | Run only the server. `-batchmode` implies it |
| `-port N` | Port to host on. Default 7777 |
| `-host` | Go straight to hosting a match |
| `-connect ADDRESS` | Go straight to joining, for example `-connect 192.168.1.20` |
| `-name NAME` | Player name, instead of the one in the menu |
| `-lag MS`, `-jitter MS`, `-loss PERCENT` | Make this copy's connection worse on purpose |

### What I would most like to hear about

- **The corrections counter.** It should jump a little when a round starts and then sit still while you run around. If it climbs steadily, Unity's physics is giving the client and the server different answers, and I need to know how fast it climbs and whether you can feel it.
- **Hits.** Whether shots that looked on target count, especially on a moving opponent with simulated lag on.
- **The other player.** Smooth, or jittery? Do their shots come from their weapon?
- **Anything on screen** that overlaps, is missing or reads wrong.

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
| Esc | Pause: loadouts and settings; in a match, also where you leave it |
| F5 | Reset session stats (firing range) |
| F1 | Hide or show the controls panel (firing range) |
| F3 | Hide or show the connection readout (in a match) |

## Loadouts

A loadout is a primary weapon, a secondary, a melee weapon, two utility items and a perk set. Press **Esc** in play to open the loadout screen.

- **Pick one to use it.** The list on the left has six default loadouts (Assault, Rusher, Marksman, Breacher, Sharpshooter, Demolition) and, below them, your own. Clicking a loadout equips it.
- **Make your own.** Press **New** to copy the selected loadout, or just change something in a default: defaults cannot be altered, so the game makes your own copy and edits that. You can keep up to ten.
- **Edit it.** The tabs across the top are the six slots. Choose a weapon and cycle its attachments, choose each utility item, choose a perk set. The stat bars show what attachments and perks do to the weapon.
- **Rename it** by typing in the name box and pressing Enter. **Delete** removes it.
- **Nothing to save.** Every change is written straight away and is there the next time you start the game.

Press **B** in play for the quick swap list: every loadout with a number beside it. On the range, press the number and you are carrying it, with full ammunition. In a match the choice is sent to the server and you carry it from the next round.

Loadouts are kept in a file called `loadouts.json` in the game's save folder; **Pulsebreak ▸ Open Saved Loadouts Folder** in the Editor takes you to it. Delete the file to start over. If it ever becomes unreadable, the game keeps it under another name and starts from the defaults.

Loadouts are saved locally, as asked. They do not follow you to another machine until accounts and the backend exist. In a match the server checks the loadout you bring against its own content and replaces anything you could not have.

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

Open **Window ▸ General ▸ Test Runner**, select **EditMode** and run all. The tests cover fire rate and timing, every fire mode, both reload styles, weapon switching, attachment maths, damage, the movement maths, loadout rules, the utility belt, creating, editing and saving loadouts, the real content assets, and the netcode's lower layers: packet reading and writing, the connection on a line that drops, reorders and repeats packets, every message, and the arena's layout.

The tests that play whole matches between a server and two clients need a stand-in for Unity's physics, so they live outside the project and were run before each commit. [ARCHITECTURE.md](docs/ARCHITECTURE.md#how-it-is-tested) says what they cover.

## Troubleshooting

**Everything is pink, or the scene looks flat and unlit.** The render pipeline did not get assigned. Run **Pulsebreak ▸ Run Project Doctor** and read the Console. If it reports that the URP asset could not be loaded, create one with **Assets ▸ Create ▸ Rendering ▸ URP Asset (with Universal Renderer)** and assign it under **Project Settings ▸ Graphics**. The game is built to run without it too, on the Built-in pipeline.

**Pressing Play shows an empty scene.** Use **Pulsebreak ▸ Rebuild Firing Range Scene**, or **Rebuild Duel Scene** if it was the duel scene.

**A build starts on the firing range, with no menu.** The duel scene is not first in the build. Run **Pulsebreak ▸ Run Project Doctor**, or drag **Duel** to the top under **File ▸ Build Profiles**.

**"Could not open port 7777" when hosting.** Another copy of the game on this computer is already hosting. Join that one at `127.0.0.1`, or close it.

**"No answer from the server" when joining.** Nothing is hosting at that address, or a firewall is in the way. Check the address against the one on the host's screen, and allow the game (or UDP port 7777) through the host's firewall.

**"This copy of the game is a different version from the server's."** The two copies were built from different code. Rebuild so both match; the Editor counts as a copy.

**The other player stutters, or you keep being pulled back.** Look at the readout at the top left. High ping or loss means the connection (or a simulated one left switched on). A corrections count that keeps climbing on a good connection is a bug I want to hear about.

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
        ├── Shared/    Gameplay rules: movement, weapons, projectiles, damage, loadouts, duel rules, and the
        │              network layer. Runs on client and server.
        ├── Server/    The authority for a match: runs every player's inputs, judges every shot, runs the duel.
        ├── Client/    Input, camera, weapon models, effects, audio, HUD, loadout screen and saves, the firing
        │              range, and the online duel: menu, prediction, the other player.
        ├── Editor/    Project Doctor.
        ├── Content/   Weapons, attachments, utility items, perk sets, default loadouts, movement.
        └── Tests/     Unit tests.
```
