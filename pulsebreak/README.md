# Pulsebreak

A fast, competitive multiplayer FPS for PC, built in Unity 6. "Pulsebreak" is a working title.

Five phases are built so far: **movement and a firing range**, **the weapon system with 40 weapons and 21 attachments**, **loadouts**, **online 1v1 duels** on a server that owns the match, and **accounts** on a backend of their own. Accounts are switched off in the game for now, at your request; see [Accounts](#accounts). The plan for everything after is in [docs/ROADMAP.md](docs/ROADMAP.md), and how the code is organised is in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

> **Not yet run in Unity.** This was written without access to a Unity Editor. The C# was compiled against Unity's reference assemblies. The simulation, the weapon data and the netcode were tested outside the engine, including whole matches between a real server and two real clients on a simulated bad connection, and the game's account code was tested against the real backend. But nothing has been opened or played in Unity itself, so the menus, the HUD and everything else you look at are unverified. Expect to report a few first-run issues; [Troubleshooting](#troubleshooting) covers the likely ones.

## Open and play

1. Install **Unity 6** (6000.0 LTS or newer) through Unity Hub.
2. In Unity Hub choose **Add ▸ Add project from disk** and pick the `pulsebreak/game` folder. If Hub says the project's Editor version is not installed, pick the Unity 6 version you have.
3. Let the first import finish. The firing range scene opens by itself. If it does not, use **Pulsebreak ▸ Open Firing Range** in the menu bar.
4. Press **Play** and click inside the Game view to capture the mouse.

That is the offline range. The main menu and online play are in a second scene: **Pulsebreak ▸ Open Duel (Main Menu)**. See [Play online](#play-online).

On first open Unity creates several settings files and one asset (`Resources/UI/HudPanel.asset`). Commit them; they are part of the project.

## Accounts

**Switched off for now.** The menu has no sign-in, nothing in the game talks to the backend, and loadouts and settings are saved on your computer. Nobody needs an account to play, host or join.

Everything is still in the project, ready for when accounts become required:

- the backend (sign-up, email verification, sign-in, password reset, loadouts and settings saved to the account) is in `backend/`, with its own [README](backend/README.md) and tests;
- in the game, one line turns it all back on: `AccountsEnabled` in `Client/Online/Unity/GameServices.cs`.

If you signed in while accounts were on, the loadouts you made then are brought back to this computer the next time the game starts.

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
| Shift | Sprint (hold it, or set **Toggle sprint** in the Esc menu to press once) |
| Space | Jump |
| Ctrl or C | Crouch |
| Crouch while sprinting | Slide |
| Hold Space + W at a ledge | Mantle |
| Right mouse button | Aim down sights |
| Left mouse button | Fire (hold to charge on charge weapons) |
| R | Reload |
| 1 / 2 / 3 or mouse wheel | Switch to primary / secondary / melee |
| G | Throw your grenade (or other utility item) |
| B | Quick swap: pick another loadout by number |
| Esc | Pause: loadouts and settings; in a match, also where you leave it |
| F5 | Reset session stats (firing range) |
| F1 | Hide or show the controls panel (firing range) |
| F3 | Hide or show the connection readout (in a match) |

## Loadouts

A loadout is four things to carry and a perk set:

| Slot | What goes there |
| --- | --- |
| Primary | Rifles, SMGs, machine guns, shotguns, snipers, marksman rifles and the special weapons (Bow, Crossbow, Flamethrower...) |
| Secondary | Pistols, the Sawed-Off, launchers, Throwing Knives, Flare Gun, Slingshot |
| Melee | Knife, Katana, Fists, Scythe, Battle Axe |
| Utility | One grenade (or similar), thrown with **G** |
| Perk set | A passive bonus, such as faster reloads or a little more speed |

Press **Esc** in play to open the loadout screen.

- **Pick one.** The list on the left has eight default loadouts and, below them, your own. Each entry shows what it carries. Clicking one selects it.
- **Deploy** puts you back in the game carrying the selected loadout. On the range you have it at once, with full ammunition; in a match, from the next round.
- **Make your own.** Press **New** to copy the selected loadout, or just change something in a default: defaults cannot be altered, so the game makes your own copy and edits that. You can keep up to ten.
- **Edit it.** The tabs across the top are Primary, Secondary, Melee, Utility and Perks. Each lists what fits that slot, grouped by class. For a weapon you can cycle its attachments.
- **Compare.** The stat bars put each weapon against the others for the same slot: the weakest primary at something sits at the left of that bar, the strongest at the right. Green and red show what attachments and the perk set add or cost.
- **Rename it** by typing in the name box and pressing Enter. **Delete** removes it.
- **Nothing to save.** Every change is written straight away and is there the next time you start the game.

Press **B** in play for the quick swap list: every loadout, numbered, with its primary and secondary. Press the number and you carry it, straight away on the range and from the next round in a match.

Loadouts are kept in a file called `loadouts.json` in the game's save folder; **Pulsebreak ▸ Open Saved Loadouts Folder** in the Editor takes you to it. Delete the file to start over. If it ever becomes unreadable, the game keeps it under another name and starts from the defaults.

Loadouts are saved on your computer. In a match the server checks the loadout you bring against its own content and replaces anything you could not have.

## What to try

The range has three areas. You spawn at the firing line facing downrange.

- **Centre: shooting lanes.** Targets at 10, 20, 35, 50 and 75 m, two of them moving, and a tight group of three off to the right for explosives and piercing shots. Each target has head, body and limb hitboxes, 100 health, and stands back up after it drops. The panel at the top right tracks accuracy, headshot rate and the time your last kill took.
- **West: movement course.** Crates to mantle (0.9 m, 1.5 m, then a 2.6 m platform that needs a jump first), a ramp to slide down, a tunnel too low to walk through, and two jump gaps, the second of which needs a sprint.
- **East: the perch.** A raised platform reached by stairs, for long shots.

Utility and perks:

- **Frag Grenade** bounces and bursts on a fuse; **Impact Grenade** bursts on contact; **Sticky Grenade** stops dead where it lands, then bursts; **Shockwave Grenade** is a wide, weak blast; **Ricochet Disc** skips off surfaces and hits harder each bounce.
- On the range, used utility items come back after a few seconds.
- Perk sets are passive: try **Skirmisher** and watch the speed readout, or **Demolitionist** for one more grenade.

Things worth a look among the weapons:

- **Damage numbers** show what each hit did. Body hits are small and white, head hits big and gold, and the hit that finishes a target is red. A shotgun blast shows one total. Every number is a multiple of five.
- The **Pump Shotgun** loads shell by shell; fire mid-reload to cut it short.
- The **Bow** and the **Energy Rifle** charge while you hold the trigger; let go to fire. The Energy Rifle's beam goes through several targets in a line.
- The **Ricochet Blaster**'s shots and the **Slingshot**'s stones bounce; try them off the floor or the cover walls.
- The **Flamethrower** is deadly inside 12 metres and harmless past it.
- The **Grenade Launcher** bounces grenades that burst on a fuse; the **Rocket Launcher** flies straight and bursts on contact.
- The **Sniper Rifle** has a full-screen scope. Fit the 3x Optic on the loadout screen to swap it for an open sight.
- Fit a **Suppressor** and the shot goes quiet and loses its muzzle flash.

## Weapons

Damage is to the body up close, then to the head. Every target has 100 health.

| Class | Weapons |
| --- | --- |
| Assault rifles | Assault Rifle (25 / 35), Battle Rifle (35 / 50), Carbine (20 / 30), Burst Rifle (30 / 45, three-round bursts) |
| Submachine guns | Micro SMG (15 / 20), SMG (20 / 30), Silenced SMG (20 / 25), PDW (25 / 35) |
| Machine guns | Light Machine Gun (30 / 45), Minigun (15 / 20, very fast, very heavy) |
| Shotguns | Pump Shotgun (8 × 15), Auto Shotgun (6 × 10), Double Barrel (10 × 15), Sawed-Off (8 × 10, secondary) |
| Sniper rifles | Sniper Rifle (100 / 200), Semi-Auto Sniper (70 / 105) |
| Marksman rifles | Marksman Rifle (50 / 75), Burst DMR (40 / 60, two-round bursts) |
| Pistols | Handgun (25 / 40), Revolver (55 / 100), Machine Pistol (15 / 20), Burst Pistol (25 / 35), Laser Pistol (20 / 30, no drop-off) |
| Launchers | Rocket Launcher (140 blast), Grenade Launcher (110 blast) |
| Special | Bow (up to 100 / 150), Crossbow (45 / 90), Energy Rifle (up to 110 / 165), Plasma Rifle (25 / 35), Ricochet Blaster (35 / 50, more after each bounce), Paintball Gun (20 / 35), Flamethrower (10 a flame), Throwing Knives (60 / 120), Flare Gun (45 / 60), Slingshot (35 / 70) |
| Melee | Knife (50), Katana (65), Fists (35), Scythe (80, longest reach), Battle Axe (100, slowest) |

Damage drops off with range for most guns (an Assault Rifle hit is 20 beyond about 55 m). Explosions deal less the further from the centre. Whatever reduces it, what lands is always a multiple of five.

Attachments come in six slots: sights (Red Dot, Holo, 3x Optic, 6x Scope, 10x Scope), barrels (Suppressor, Long Barrel, Compensator, Muzzle Brake, Choke), magazines (Extended, Drum, Quick), grips (Vertical, Angled, Stubby), stocks (Light, Heavy, None) and lasers (Laser Sight, Tactical Laser). Each weapon accepts a subset.

Many of these are generic weapon types, the kinds most arena shooters share. None of their names or looks are taken from another game.

The grapple gun from the original brief is not here yet. It is a movement tool more than a weapon, so it is planned alongside abilities.

## Changing and adding content

Everything is data. Select an asset in the Project window and edit it in the Inspector. All of it lives under `Assets/_Project/Content/`.

| Folder | Assets | What they are |
| --- | --- | --- |
| `Movement/` | `DefaultMovement` | Speeds, jump, slide, mantle |
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

The backend has its own tests; see [backend/README.md](backend/README.md#tests).

## Troubleshooting

**Everything is pink, or the scene looks flat and unlit.** The render pipeline did not get assigned. Run **Pulsebreak ▸ Run Project Doctor** and read the Console. If it reports that the URP asset could not be loaded, create one with **Assets ▸ Create ▸ Rendering ▸ URP Asset (with Universal Renderer)** and assign it under **Project Settings ▸ Graphics**. The game is built to run without it too, on the Built-in pipeline.

**Pressing Play shows an empty scene.** Use **Pulsebreak ▸ Rebuild Firing Range Scene**, or **Rebuild Duel Scene** if it was the duel scene.

**A build starts on the firing range, with no menu.** The duel scene is not first in the build. Run **Pulsebreak ▸ Run Project Doctor**, or drag **Duel** to the top under **File ▸ Build Profiles**.

**"Could not open port 7777" when hosting.** Another copy of the game on this computer is already hosting. Join that one at `127.0.0.1`, or close it.

**"No answer from the server" when joining.** Nothing is hosting at that address, or a firewall is in the way. Check the address against the one on the host's screen, and allow the game (or UDP port 7777) through the host's firewall.

**"This copy of the game is a different version from the server's."** The two copies were built from different code. Rebuild so both match; the Editor counts as a copy.

**The other player stutters, or you keep being pulled back.** Look at the readout at the top left. High ping or loss means the connection (or a simulated one left switched on). A corrections count that keeps climbing on a good connection is a bug I want to hear about.

**My loadouts lost their second grenade.** Loadouts now carry one utility item. Saved loadouts keep working; pick the grenade you want on the Utility tab.

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
├── backend/     The accounts API (ASP.NET Core, PostgreSQL, Redis)
└── game/        The Unity project
    └── Assets/_Project/
        ├── Shared/    Gameplay rules: movement, weapons, projectiles, damage, loadouts, duel rules, and the
        │              network layer. Runs on client and server.
        ├── Server/    The authority for a match: runs every player's inputs, judges every shot, runs the duel.
        ├── Client/    Input, camera, weapon models, effects, audio, HUD, loadout screen and saves, the firing
        │              range, the online duel (menu, prediction, the other player), and the account client.
        ├── Editor/    Project Doctor.
        ├── Content/   Weapons, attachments, utility items, perk sets, default loadouts, movement.
        └── Tests/     Unit tests.
```
