# Pulsebreak

A fast, competitive multiplayer FPS for PC, built in Unity 6. "Pulsebreak" is a working title.

Eight phases are built so far: **movement and a firing range**, **the weapon system with 40 weapons and 21 attachments**, **loadouts**, **online play** on a server that owns the match, **accounts** on a backend of their own, **every game mode, bots and matchmaking**, **character customization with weapon skins**, and **progression, challenges and ranked**. Accounts are switched back on; see [Accounts](#accounts). The plan for everything after is in [docs/ROADMAP.md](docs/ROADMAP.md), and how the code is organised is in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

> **Not yet run in Unity.** This was written without access to a Unity Editor. The C# was compiled against Unity's reference assemblies. The simulation, the weapon data and the netcode were tested outside the engine, including whole matches between a real server and real clients on a simulated bad connection, every game mode, bots playing full matches, and the game's account and matchmaking code against the real backend. But nothing has been opened or played in Unity itself, so the menus, the HUD and everything else you look at are unverified. Expect to report a few first-run issues; [Troubleshooting](#troubleshooting) covers the likely ones.

## Open and play

1. Install **Unity 6** (6000.0 LTS or newer) through Unity Hub.
2. In Unity Hub choose **Add ▸ Add project from disk** and pick the `pulsebreak/game` folder. If Hub says the project's Editor version is not installed, pick the Unity 6 version you have.
3. Let the first import finish. The firing range scene opens by itself. If it does not, use **Pulsebreak ▸ Open Firing Range** in the menu bar.
4. Press **Play** and click inside the Game view to capture the mouse.

That is the offline range. The main menu and online play are in a second scene: **Pulsebreak ▸ Open Duel (Main Menu)**. See [Play online](#play-online).

On first open Unity creates several settings files and one asset (`Resources/UI/HudPanel.asset`). Commit them; they are part of the project.

## Accounts

**On again.** Sign in (or create an account) from the top of the main menu. An account keeps your loadouts, settings, level, weapon levels, challenges and ranks, and they follow you to any computer. Ranked needs a verified email; the menu tells you how to verify.

Without an account you can still do everything offline, host and join private matches, and play casual matchmaking as a guest, but nothing you play is saved to your progress and you carry only the starter weapons in matchmaking.

The backend has to be running for any of this: see [backend/README.md](backend/README.md). Turning accounts off again is one line: `AccountsEnabled` in `Client/Online/Unity/GameServices.cs`.

## Play online

Online play is in the duel scene (**Pulsebreak ▸ Open Duel (Main Menu)**), which is also the main menu. Pick a **mode** at the top of the menu, then:

- **Find match** puts you in that mode's queue on the backend, and drops you into a match on a dedicated server when one is found. Tick **Ranked** (Duel and Elimination) to play for rating. See [Matchmaking](#matchmaking) and [Progress and ranked](#progress-and-ranked).
- **Host a private match** runs the match on your own computer with the rules you pick: map, team size, what wins it, and whether bots take the empty places (and how good they are). With bots on, you can play any mode on your own straight away.
- **Join by address** connects to someone else's private match or to a dedicated server.

### The modes

| Mode | Teams | Respawns | Wins |
| --- | --- | --- | --- |
| Duel | 1 v 1 | No, rounds | First to 3 rounds (best of five) |
| Team Deathmatch | up to 5 v 5 | Yes | First team to the kill limit |
| Free-for-All | Everyone for themselves, up to 12 | Yes | First to the kill limit |
| Elimination | up to 5 v 5 | No, rounds | Wipe the other team out; first to 4 rounds |
| Domination | up to 5 v 5 | Yes | Hold zones A, B and C; each zone held scores a point a second |
| Hardpoint | up to 5 v 5 | Yes | One zone at a time, moving every minute; hold it alone to score |
| Gun Game | Everyone for themselves | Yes | Every kill hands you the next weapon; a kill with the last one (the knife) wins. Being knifed sets you back one |

Two maps: **Relay Yard**, the small duel arena, and **Crossroads**, 64 × 48 m with three lanes, a central square, raised decks at each end, three Domination zones and three hardpoints. The menu offers only the maps that suit the chosen mode.

Teammates are drawn blue with a marker over their heads; everyone else is red. Teammates cannot hurt each other.

### Bots

Bots fill any place nobody has taken, and step aside the moment a person joins. They find their way round the map (including up steps and onto ledges), react after a short delay, aim with a little error that settles while they track you, control recoil, strafe at their weapon's best range, reload and switch weapons, throw grenades, and go for the zones in Domination and Hardpoint. Easy, Normal and Hard differ in reaction time, aim and how fast they turn.

### The quickest test: on your own

1. Open the duel scene and press **Play**.
2. Pick a mode, open **Host a private match**, leave **Empty places** on **Normal bots**, and press **Start private match**.

You are in a match against bots a few seconds later.

### Two people

You need two copies of the game running. The Editor is one; a build is the other.

1. Make a build: **File ▸ Build Profiles** (Build Settings in older versions), check that **Duel** is the first scene in the list and **FiringRange** the second, and press **Build**. If the list is wrong, run **Pulsebreak ▸ Run Project Doctor** first.
2. One copy hosts a private match. The banner shows the address others should use, something like `192.168.1.20` (on one computer, `127.0.0.1`).
3. The other types that address and chooses **Join by address**.

Both copies keep running when their window is not in front, so you can click between them. If the second computer gets "No answer from the server", the host's firewall is the usual cause: allow the game through it, or allow UDP port 7777. Over the internet the host also has to forward UDP port 7777 on their router; matchmaking does away with that.

### A dedicated server

Start a build with no window and no player:

```
Pulsebreak.exe -batchmode -nographics -server -mode tdm -map crossroads -bots
```

`-mode` is one of `duel`, `tdm`, `ffa`, `elimination`, `domination`, `hardpoint`, `gungame`; `-bots` fills empty places. Players **Join by address**. Add `-logFile server.log` to choose where the log goes.

### Matchmaking

**Find match** needs the backend running (see [backend/README.md](backend/README.md)) and at least one dedicated server offered to it:

```
set PULSEBREAK_FLEET_KEY=development-only-fleet-key-do-not-use-anywhere-real
set PULSEBREAK_TICKET_KEY=development-only-ticket-key-do-not-use-anywhere-real
Pulsebreak.exe -batchmode -nographics -server -fleet http://localhost:5080 -public-address 127.0.0.1
```

(Those are the development keys from the backend's settings; use your own anywhere real.) The server registers and checks in every few seconds. Signed-in players queue as themselves, everyone else as a guest. A full queue starts a match at once; otherwise, after 20 seconds, a casual match starts with whoever is waiting and bots take the other places (a Duel always waits for two people; ranked always waits for a full match). The server switches to the match's mode and map, lets in only the players matchmaking sent it, plays **one** match, reports it to the backend and sends everyone back to the menu, where a line says what the match earned. Run several servers on different `-port`s to host several matches.

### In a match

- **Warm-up** until there is someone on each side. Ammunition and utility never run out. Once there are enough players, a short "Starting in" wait lets anyone on their way arrive.
- **Countdown.** You can look around and swap weapons; movement and fire unlock when it ends.
- **Play.** Round modes end a round when one side is wiped out (or on time: more players left, then more health). Respawning modes bring you back a few seconds after you go down, away from enemies, and end at the score limit or when time runs out.
- **Changing loadout:** **B** or **Esc**. It is used from your next spawn (next round in round modes). Gun Game hands out its own weapons.
- A new match starts by itself a few seconds after the result. If everyone on one side leaves, the other side wins.

On screen: scores, clock and zones at the top; your **weapons along the bottom centre** (the one in hand lit up), with the loadout's name above them; health at the bottom left; ammunition and your grenade at the bottom right; eliminations at the top right; and at the top left your ping, packet loss and a count of corrections (**F3** hides it). Hold **Tab** for the scoreboard. **Esc ▸ Leave match** returns to the menu.

### Trying it with a bad connection

To feel the game at a given ping, make your own connection worse on purpose. Each value applies in both directions, so 60 ms of delay adds about 120 ms to your ping.

- In the Editor: select the **Duel** object in the duel scene and set **Testing ▸ Simulated Connection** before pressing Play.
- In a build: start it with, for example, `-lag 60 -jitter 10 -loss 2`.

### Command line

| Option | Effect |
| --- | --- |
| `-server` | Run only the server. `-batchmode` implies it |
| `-port N` | Port to host on. Default 7777 |
| `-mode ID`, `-map ID`, `-bots` | The rules a server started from the command line plays by |
| `-fleet URL`, `-public-address HOST` | Offer this dedicated server to matchmaking at that backend, telling players to connect to that address |
| `-host` | Go straight to hosting a match (with `-mode`, `-map`, `-bots` if given) |
| `-connect ADDRESS` | Go straight to joining, for example `-connect 192.168.1.20` |
| `-name NAME` | Player name, instead of the one in the menu |
| `-lag MS`, `-jitter MS`, `-loss PERCENT` | Make this copy's connection worse on purpose |

### What I would most like to hear about

- **The bots.** Too easy, too hard, stuck anywhere, doing anything silly?
- **The corrections counter.** It should jump a little when a round starts and then sit still while you run around. If it climbs steadily, Unity's physics is giving the client and the server different answers, and I need to know how fast it climbs and whether you can feel it.
- **Hits.** Whether shots that looked on target count, especially on a moving opponent with simulated lag on.
- **The other player.** Smooth, or jittery? Do their shots come from their weapon?
- **Anything on screen** that overlaps, is missing or reads wrong.

## Progress and ranked

Only matches found through **Find match** count, because only a matchmade server's report is trusted. Private matches, the range and LAN games are for practice and fun.

- **XP and levels.** A match pays for finishing it (100), winning (150, or 75 for a draw), each kill (25) and headshot kill (10), each round won (30) and objective points (a quarter of them). Leaving early forfeits the finishing and winning parts. Ranked pays a quarter more. Levels go to 50; everything in the game is unlocked by level 30, about 50 good matches.
- **Unlocks.** New players carry a starter set in matchmaking: the Assault Rifle, SMG, Pistol, Combat Knife and Fists, plus the first utility item and perk set. Other weapons, utility items and perk sets open with your level; attachments with the level of the weapon they fit (kills with a weapon level it up). The loadout screen marks locked items with the level they open at. You can still pick them: **on the range and in private matches everything is unlocked**, and in matchmaking a locked item is swapped for a starter.
- **Challenges.** Three a day and three a week, different for each player: kills with a kind of weapon, headshots, wins, objective points, rounds and so on. Finishing one pays extra XP straight away. Days reset at midnight UTC, weeks on Monday.
- **Ranked.** **Ranked Duel** and **Ranked Elimination (3v3)**, for accounts with a verified email. Matches are full, people only, with players near your rating (the range widens the longer you wait) and teams balanced by rating. Your rank shows after five placement matches: **Spark, Pulse, Surge, Arc, Flux, Nova, Zenith**, each but the top in three divisions. Leaving a ranked match, or not turning up, is a loss.
- **The Progress screen** (from the menu, signed in) shows your level and XP, today's and this week's challenges, what unlocks next, every weapon's level, your ranks with each ranked playlist's leaderboard, and your last ten matches.

## Customize

**Customize** is on the main menu and in the Esc menu (on the range and in a match). Everything is free to pick for now.

- **Suit** and **Trim**: fifteen colours each. The suit covers your body and legs; the trim your arms, headgear and back item.
- **Pattern** printed on the suit: Racing Stripes, Chevrons, Honeycomb, Splinter, Pixel Camo, Dotwork, Weave, Tiger or Circuit.
- **Headgear**: Field Cap, Crest Helm, Halo, Antenna Rig, Horned Helm, Bucket Helm or Wraparound visor.
- **Visor**: the glow across your face, in seven colours.
- **Back**: Field Pack, Twin Tanks, Dorsal Fin, Signal Mast, Jet Pack or Long Scarf.
- **Weapon skins**: twelve (Carbon, Arctic, Woodland, Sandstorm, Tiger, Gilded, Mirror, Neon Grid, Magma, Nebula, Bubblegum, Redline) plus the factory finish. Pick **Every weapon** to skin them all, or a single weapon to give it a skin of its own. You see your skins on your own weapon in first person; others see them in your hands.

The preview in the middle shows the result and turns by itself; drag it to turn it yourself. **Randomize** rolls a new outfit (keeping your weapon skins), and **Reset to default** starts over. Choices are saved as you make them, in `cosmetics.json` beside your loadouts.

In a match everyone sees your look, and a change shows up for them straight away. Bots wear random looks. **Looks never change how anyone plays:** helmets and back items are not part of the body that bullets hit, so everyone is exactly as easy to hit whatever they wear. Coloured shoulder pads on every player (blue for your team, red for the other) keep sides readable whatever the outfit.

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
| Tab (hold) | Scoreboard (in a match) |
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

Press **Esc** in play to open the loadout screen. Items locked for matchmaking show the level they open at; see [Progress and ranked](#progress-and-ranked).

- **Pick one.** The list on the left is your loadouts. Each entry shows what it carries. Clicking one selects it. The first time you play there is one, ready to change.
- **Deploy** puts you back in the game carrying the selected loadout. On the range you have it at once, with full ammunition; in a match, from your next spawn.
- **Make another.** Press **New** to copy the selected loadout, then change the copy. You can keep up to ten.
- **Edit it.** The tabs across the top are Primary, Secondary, Melee, Utility and Perks. Each lists what fits that slot, grouped by class. For a weapon you can cycle its attachments.
- **Compare.** The stat bars put each weapon against the others for the same slot: the weakest primary at something sits at the left of that bar, the strongest at the right. Green and red show what attachments and the perk set add or cost.
- **Rename it** by typing in the name box and pressing Enter. **Delete** removes it (you always keep at least one).
- **Nothing to save.** Every change is written straight away and is there the next time you start the game.

Press **B** in play for the quick swap list: your loadouts, numbered, with each one's primary and secondary. Press the number and you carry it, straight away on the range and from your next spawn in a match.

Loadouts are kept in a file called `loadouts.json` in the game's save folder (your look is in `cosmetics.json` beside it); **Pulsebreak ▸ Open Saved Loadouts Folder** in the Editor takes you to it. Delete the file to start over. If it ever becomes unreadable, the game keeps it under another name and starts over with one loadout.

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

Open **Window ▸ General ▸ Test Runner**, select **EditMode** and run all. The tests cover fire rate and timing, every fire mode, both reload styles, weapon switching, attachment maths, damage, the movement maths, loadout rules, the utility belt, creating, editing and saving loadouts, the real content assets (including every Gun Game weapon), the netcode's lower layers (packet reading and writing, the connection on a line that drops, reorders and repeats packets, every message), the maps and the modes' shared rules, and cosmetics (the catalog, the rules for a look, how it is sent and saved).

The tests that play whole matches (every mode, bots, matchmaking end to end) need a stand-in for Unity's physics, so they live outside the project and were run before each commit. [ARCHITECTURE.md](docs/ARCHITECTURE.md#how-it-is-tested) says what they cover.

The backend has its own tests; see [backend/README.md](backend/README.md#tests).

## Troubleshooting

**Everything is pink, or the scene looks flat and unlit.** The render pipeline did not get assigned. Run **Pulsebreak ▸ Run Project Doctor** and read the Console. If it reports that the URP asset could not be loaded, create one with **Assets ▸ Create ▸ Rendering ▸ URP Asset (with Universal Renderer)** and assign it under **Project Settings ▸ Graphics**. The game is built to run without it too, on the Built-in pipeline.

**Pressing Play shows an empty scene.** Use **Pulsebreak ▸ Rebuild Firing Range Scene**, or **Rebuild Duel Scene** if it was the duel scene.

**A build starts on the firing range, with no menu.** The duel scene is not first in the build. Run **Pulsebreak ▸ Run Project Doctor**, or drag **Duel** to the top under **File ▸ Build Profiles**.

**"Could not open port 7777" when hosting.** Another copy of the game on this computer is already hosting. Join that one at `127.0.0.1`, or close it.

**No sign-in on the menu, or "Could not reach the account server".** The backend is not running, or the game is looking for it somewhere else. Start it (see [backend/README.md](backend/README.md)), or start the game with `-backend URL`. Everything offline works without it.

**The Ranked box is greyed out.** Ranked is for Duel and Elimination, signed in, with a verified email; the line beside the box says which is missing.

**A match did not count.** Only matchmade matches count, and only once the server has reported them; the menu says "still being counted" if that takes a moment. Private matches never count.

**"Could not reach matchmaking."** The backend is not running, or the game is looking for it somewhere else. Start it (see [backend/README.md](backend/README.md)), or start the game with `-backend URL`. Hosting and joining by address work without it.

**Find match searches for ever.** No dedicated server is offered to matchmaking, or they are all busy, or they were built from different code than your copy. Start one as in [Matchmaking](#matchmaking) and check its log for "Registered".

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
├── backend/     Accounts and matchmaking (ASP.NET Core, PostgreSQL, Redis)
└── game/        The Unity project
    └── Assets/_Project/
        ├── Shared/    Gameplay rules: movement, weapons, projectiles, damage, loadouts, maps, match rules,
        │              playlists, the cosmetic catalog, and the network layer. Runs on client and server.
        ├── Server/    The authority for a match: runs every player's inputs, judges every shot, runs the
        │              mode (Modes/) and the bots (Bots/), and offers itself to matchmaking.
        ├── Client/    Input, camera, weapon models, effects, audio, HUD, loadout screen and saves, the firing
        │              range, online play (menu, prediction, other players, zones, scoreboard), the
        │              Customize screen and how looks are drawn, and the account and matchmaking clients.
        ├── Editor/    Project Doctor.
        ├── Content/   Weapons, attachments, utility items, perk sets, default loadouts, movement.
        └── Tests/     Unit tests.
```
