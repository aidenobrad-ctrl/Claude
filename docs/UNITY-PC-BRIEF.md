# Halcyon in Unity + Blender — brief for the session on the user's PC

You are the hands-on-the-PC half of a two-session team building **Halcyon**, an original-IP
open-world festival racer (in the spirit of Forza Horizon, but our own world, cars and
brands). You run on the user's Windows PC ("Hexro") through Remote Control. The other half
is a cloud Claude Code session that wrote the web version and the Unity version so far.

The user asked: **"Make it in Unity and Blender by controlling my PC."** Your job is to
run, verify and polish it in the real Unity Editor and Blender on this PC, visibly. The
user may follow along from the Claude app rather than sitting at the PC.

## What already exists

Repo (public): https://github.com/aidenobrad-ctrl/Claude, branch `claude/make-this-gmpw4y`
(PR #1). Commit and push only to that branch. Read `docs/UNITY.md` first; it has the full
layout and how everything was verified.

- `unity/`: a Unity 6 project.
  - It has a package manifest (URP, Input System) and an editor script that sets up URP,
    linear colour and the game scene on first open.
  - Pressing Play in any scene builds the island: terrain, roads, water, 185k trees,
    buildings and the festival.
  - It spawns the Halden Aster GT on the festival road, driven by a C# port of the web
    version's 240 Hz physics. That port is bit-exact with the TypeScript.
  - **G** opens the garage on a turntable: your cars, the dealer, upgrades, tuning (graphs,
    auto-tune, share codes), the test track and career data. **T** opens festival events
    with class limits: the Proving Ground GP, island sprints, the Highcrest Hill Climb, a
    night race and a storm, against AI rivals matched to your car's PI. Results pay credits
    and XP into a career saved as JSON. The AI passes a 300-race head-to-head tournament
    headless (`dotnet/Halcyon.AI.Tests`), and the career layer has its own suite
    (`dotnet/Halcyon.Career.Tests`).
  - The island lives (M6): a 24-minute day with night lighting, weather that wets the
    roads, traffic, landmarks, discoveries, a map (**M**) with fog of war, fast travel and
    a GPS (`dotnet/Halcyon.Sim.Tests`).
  - M7: stunts by the roads (speed traps, speed zones, drift zones, danger signs on
    jumps), a drag race, a rally, a head-to-head and a ghost time trial, three
    championships, festival levels and accolades; **Esc** opens the menu (settings and
    accessibility, accolades, controls), **P** photo mode, and races show a racing line.
  - Every script compiles against Unity reference assemblies, but **it has never run in a
    real Unity Editor**. That is your first job.
- `blender/build_car.py`: generates the cars in Blender 5.1 (`--car all`). It has been
  tested with the bpy 5.1.1 module.
  - All 12 FBX files and style files are in `unity/Assets/Halcyon/Art/Resources/Cars/`, and
    review sheets are in `blender/renders/`.
  - Blender 5.1.1 is installed here at
    `C:\Program Files\Blender Foundation\Blender 5.1\blender.exe`.

## Step 0 — look before you touch

Report briefly, changing nothing:

- Windows version.
- CPU, GPU and VRAM.
- Free disk space.
- git, and whether push credentials exist.
- Blender version.
- Unity Hub and installed editors (`C:\Program Files\Unity\Hub\Editor\*`).

## Step 1 — tools and repo

**Unity.** Use the newest installed Unity 6 (6000.x).

- If none is installed, install Unity Hub and the current Unity 6 LTS editor with Windows
  Build Support.
- Then ask the user to sign in to the Hub once for a free Personal licence. Tell them
  exactly what to click.
- Never handle their passwords.

**Repo.** Clone the branch to `C:\Users\aiden\Halcyon`, or inside your working folder if
you can't write outside it.

- Use the existing git identity. If there is none, set a repo-local `user.name
  aidenobrad-ctrl` and `user.email aidenobrad@gmail.com`.

**Scope.** Stay inside the clone, a Tools folder and the app install folders. Don't change
system settings or close the user's apps.

## Step 2 — first real run

1. Open `unity/` in the Editor and let it import.
   - Watch the Console. `Halcyon: created and assigned Assets/Settings/Halcyon_URP.asset`
     means setup worked.
   - Fix any compile error that only real Unity reports with the smallest change, and
     note it.
2. Build and capture screenshots, then look at every PNG (Read tool) and judge it
   honestly:

   ```
   Unity.exe -batchmode -quit -projectPath unity -executeMethod Halcyon.EditorTools.HalcyonBuild.BuildWindows -logFile build.log
   unity\Builds\Windows\Halcyon.exe -screen-width 1280 -screen-height 720 -halcyon-capture shots
   ```

   Batchmode can't open a project that is open in the Editor GUI, so close the GUI first.
3. Open the project in the Editor GUI, open `Assets/Scenes/Halcyon.unity`, and press Play
   so the user sees the island and the car on their screen.
4. Press **G** and page through all 12 cars. Check materials (paint, glass, lights), LOD
   switching and the wheel positions. Then press **T** and race the Proving Ground GP and
   one sprint. Watch the grid, the countdown, the AI's lines and passes, and the HUD and
   results. Note anything that differs from `docs/PLAYTEST.md` (M3, M4).
5. The M5 career, all compile-checked only so far. In the garage:
   - buy a cheap D car at the dealer, fit a few upgrades, and watch the PI change;
   - open Tuning, move sliders (locked ones should say which part they need), check that
     the power and gear graphs redraw, press 2 to auto-tune for a circuit, then Enter for
     the test track;
   - copy a tune code (C), paste it back (V), and copy the career to the clipboard.
   Then race an event your car is too fast for (it should refuse with the reason), one it
   may enter, and check the payout on the results screen. Quit and restart: the career
   should come back as you left it (`%USERPROFILE%\AppData\LocalLow\<company>\<product>\halcyon-career.json`).
6. The M6 living island, all compile-checked only so far (results headless are in
   `docs/PLAYTEST.md`, M6):
   - Press `]` through a whole day. Check the sunrise and sunset colours, the moon (the
     sky's disc at night), lit windows and lamps at night, and that the headlights come on at dusk.
   - Press `\` through the skies: clouds, rain (streaks, splashes, the sound), the wet
     road's sheen and the spray from the wheels, a storm's lightning and thunder, and fog.
     Drive in the rain and feel the grip drop.
   - Drive among the traffic: cars should keep their lanes, stop behind you, go round you
     when you stop, and pass each other on the dual carriageways.
   - Press **M**: the fog of war, the roads you have driven (blue), places and landmarks.
     Pick a destination and press Enter: a violet route on the map, the minimap and the
     road, and the next turn at the top of the screen. Click the map for a route to there.
     Fast travel (F twice) to a place you have found.
   - Visit a landmark (the lighthouse beam turns, the turbines spin, the mast blinks).
   - Race the Highcrest Hill Climb, the Proving Ground by Night and the Storm Run.
7. M7, all compile-checked only so far (results headless are in `docs/PLAYTEST.md`, M7):
   - Drive through a speed trap, a speed zone, a drift zone and a danger sign (the boards
     stand by the road, and the map shows them). Check the live panel, the stars and the
     rewards, and that a jump at full speed lands about where its third star says.
   - Press **T**: the event kinds and levels (a new career is level 1, so most events are
     locked), then race the Highway Quarter Mile (trap speed), the Proving Ground Time
     Attack (a see-through ghost car and the gap to it), and the first round of the
     Grassroots Trophy in a class D car (standings after the race).
   - Press **Esc**: change the settings one by one and watch them apply: units, HUD size,
     the colour-blind palettes (the gaps and the racing line), camera shake, field of view,
     motion blur, fewer flashes in a storm, the minimap, key hints, volume, and the
     graphics quality. Restart: they should come back.
   - Press **P** in a race and on the road: orbit, zoom, depth of field on the car, the
     filters and the hour, and save a photo (it lands in `...\LocalLow\<company>\<product>\Photos`).

## Step 3 — make it look insane

Work through these, highest value first. Shoot screenshots and look at them after each.

- **Terrain:** swap the procedural terrain layers for CC0 PBR sets from Poly Haven or
  ambientCG. Record sources and licences in `unity/Assets/Halcyon/Art/CREDITS.md`.
- **Water:** a real URP water shader (depth tint, normals, fresnel, shore foam) in place
  of the transparent Lit.
- **Trees:** Blender-made trees in place of `TreeMeshes`.
- **Lighting and post:** sky, fog and post-processing tuned from screenshots.
- **The car:** check it in the Editor. Paint should use Complex Lit clear coat, and the
  wheels should sit right and spin the right way.
- **Blender on screen:** run `blender/build_car.py` here, then open the result in
  Blender's GUI for the user.

## How we work

- **Commits:**
  - Commit small and often: `git pull --rebase`, then push to `claude/make-this-gmpw4y`.
  - Don't commit `Library/`, `Temp/`, `Builds/` or big `.blend` files.
  - No AI model names in commits or code.
  - If a push fails for credentials, keep committing locally and tell the user the one
    command that fixes it (e.g. `gh auth login`).
- **Scripts:**
  - `Core/`, `Data/`, `Scripts/` and `blender/` were written by the cloud session. Fix
    what Unity reports, and improve freely.
  - Say what you changed and why in your progress notes and in `docs/UNITY.md`, so the
    cloud session can keep the TypeScript references and tests in step.
- **Honesty:** don't fake results. If something fails, say so and fix it.
- **Progress:** write a short progress line after each step.
- **Autonomy:** work on your own, and stop only for things that need the user (Unity
  sign-in, a UAC prompt).
