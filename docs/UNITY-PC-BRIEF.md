# Halcyon in Unity + Blender — brief for the session on the user's PC

You are the hands-on-the-PC half of a two-session team building **Halcyon**, an original-IP
open-world festival racer (in the spirit of Forza Horizon, but our own world, cars and brands).
You run on the user's Windows PC ("Hexro") through Remote Control. The other half is a cloud
Claude Code session that wrote the existing web version and is porting its data and physics.
The user's request: **"Make it in Unity and Blender by controlling my PC."** Your job is to do
exactly that, on this PC, for real. The user may follow along from the Claude app and may not
be sitting at the PC.

## The project

- Repo (public): https://github.com/aidenobrad-ctrl/Claude, branch `claude/make-this-gmpw4y`
  (PR #1). Commit and push only to that branch.
- It already holds a full web version (TypeScript + three.js): a 10.24 km square island
  (WORLD_HALF = 5120 m, sea level y = 0) with mountains, coast, rivers and lakes, a 63 km road
  network, villages, a city, the festival hub, and a 240 Hz car physics sim.
- Read first: `prompts/open-world-racer.md` (the original brief and priorities),
  `docs/DESIGN.md`, `docs/CARS.md` (the planned car roster), `docs/STATE.md`.
- Priorities, highest first: driving feel, AI, car visuals, map size, event variety, polish.
  On top of that, the user wants graphics that look insane.

## Split of work (so the two sessions never edit the same files)

**The cloud session owns** (don't edit; say in your transcript if something is wrong):

- `tools/export-unity.ts`
- `unity/Assets/Halcyon/Data/**`: the island exported from the web version (heightmap, splat
  weights, roads, guardrails, trees, buildings, festival layout, spawn), with a README
  describing every format and a C# loader.
- `unity/Assets/Halcyon/Core/**`: pure C#, covering the vehicle physics port, ground and collider
  queries, and routing.
- `unity/Assets/Halcyon/Scripts/Driving/**`: car controller, chase camera, input glue.

These land on the branch over time. Run `git pull --rebase` between tasks to pick them up. If
they break compilation in your Unity, make the smallest change that compiles and say exactly
what you changed.

**You own everything else**:

- The Unity project itself: `unity/ProjectSettings`, `unity/Packages`, scenes, render
  settings, shaders, materials, world builders, editor tools, UI.
- All Blender work: the `blender/` scripts, plus the exported models and textures under
  `unity/Assets/Halcyon/Art/**`.

## Step 0 — look before you touch

Report this first, briefly, without changing anything:

- Windows version.
- CPU, GPU and VRAM.
- Free disk space.
- git version, and whether push credentials exist (`git config --global credential.helper`,
  `gh auth status` if gh is installed).
- Blender installs and their versions.
- Unity Hub and the installed editors (`C:\Program Files\Unity\Hub\Editor\*`).
- Node (optional).

## Step 1 — tools and repo

- **Blender:** use the newest installed version. If there is none, get the latest stable
  release. A portable zip in `C:\Users\aiden\Tools\` needs no admin rights; winget is fine too.
- **Unity:** use the newest installed Unity 6 (6000.x) editor; otherwise the newest at or after
  2022.3 LTS.
  - If none is installed, install Unity Hub and the current Unity 6 LTS editor
    (`"Unity Hub.exe" -- --headless install --version <v> --changeset <c>`).
  - Then ask the user to sign in to Unity Hub once for a free Personal licence. Tell them
    exactly what to click, and keep going on Blender work while you wait.
  - Never ask for or handle their passwords.
- **Repo:** clone the branch to `C:\Users\aiden\Halcyon`. If you can't write outside your
  working folder, clone inside it instead.
- **Git identity:** use the existing one. If there is none, set a repo-local
  `user.name aidenobrad-ctrl` and `user.email aidenobrad@gmail.com`.
- **Scope:** stay inside the clone, the Tools folder and the app install folders. Don't modify
  anything else on the PC, don't change system settings, and don't close the user's other apps.

## Step 2 — the Unity project (`unity/` in the repo)

**Project setup**

- Create it from the URP template, e.g. `Unity.exe -batchmode -createProject <repo>\unity
  -cloneFromTemplate "<Editor>\Data\Resources\PackageManager\ProjectTemplates\com.unity.template.universal-*.tgz"`
  (or the URP blank template, or via the Hub).
- Use Linear colour space, Force Text serialization and visible meta files.
- Add a Unity `.gitignore` (Library, Temp, Logs, obj, UserSettings, Build*, *.csproj, *.sln).
- Add a `.gitattributes` marking fbx, blend, png, jpg, exr, tif, bytes and wav as binary.
- Packages: URP, Input System (both input handlers on is fine), TextMeshPro/uGUI. Cinemachine
  only if you want it.

**Graphics bar (PC/High tier)**

- HDR and Forward+.
- TAA (or MSAA 4x).
- SSAO.
- Soft shadows: 4 cascades over about 600 m, with a 4096 shadow map.
- A global Volume: bloom, ACES or neutral tonemapping, colour grading, a subtle vignette, light
  motion blur.
- Fog.
- A strong sun with an HDRI or procedural sky.
- A reflection probe that follows the car.

Medium and Low tiers (laptops, mobile) come later.

**Editor tooling to build early**

- A batchmode compile check: `Unity.exe -batchmode -quit -projectPath … -logFile …`, then grep
  the log for `error CS`.
- `Halcyon/Capture Screenshots`: an `-executeMethod` entry that opens a scene, renders named
  viewpoints to PNG at 1280×720, 390×844 and 844×390, and quits.
- LOOK at every PNG (Read tool), critique it honestly, fix, re-shoot. That is the playtest loop.
- Batchmode can't open a project that is open in the Editor GUI, so close the GUI before batch
  runs.

## Step 3 — Blender

**Pipeline**

- Everything is generated by Python scripts in `blender/`, run headless
  (`blender -b -P blender/x.py -- args`), so it is reproducible and diff-able.
- Export as FBX (or glTF) into `unity/Assets/Halcyon/Art/…`.
- Save preview renders (Cycles or EEVEE, 1280×720) to `blender/renders/`, and LOOK at them.

**The hero car comes first.** It is in every frame, so make it genuinely good.

- Shape and detail:
  - Correct proportions: wheelbase, track and overhangs per `docs/CARS.md`.
  - Smooth subdivided bodywork with proper wheel arches, and a real glasshouse.
  - Panel lines, emissive headlights and taillights, grille and intakes, mirrors, a diffuser.
  - Detailed wheels: multi-spoke rims, tyres with sidewall and tread, brake discs and callipers.
- Object setup: separate objects for body, glass, lights and each wheel, with each wheel's pivot
  at the hub.
- Materials: metallic paint with clear coat, glass, chrome/satin metal, rubber, plastic, lights.
- Fictional badges only, no real brands.
- Then turn it into a parametric generator and produce the full roster from `docs/CARS.md`: at
  least 12 cars with clearly different silhouettes (hatch, rally, muscle, supercar, SUV,
  pickup, classic, …).

**Then, in order:**

- Trees and plants: broadleaf, conifer, palm, birch-like, bush, grass clumps.
  - 2–3 LODs each, with a cross-billboard as the far LOD.
  - Vertex colours for wind.
- Rocks and cliffs, guardrail post and W-beam, lamp and power poles, road signs.
- Festival structures: main stage, arch/gantry, tents, flags, barriers, grandstand.
- Terrain and road textures for grass, meadow, dirt, rock, sand, snow, asphalt and concrete:
  - Either CC0 PBR sets (Poly Haven / ambientCG) or Blender-baked procedural ones.
  - Record every external source and its licence in `unity/Assets/Halcyon/Art/CREDITS.md`.

## Step 4 — the island in Unity (when the cloud data lands)

The exact data formats are in `unity/Assets/Halcyon/Data/README.md`.

- **Terrain** from the exported heightmap and splat weights.
- **Road meshes** from the road samples:
  - asphalt with markings
  - kerbs and sidewalks in towns
  - bridges
  - guardrails
- **Water:** a custom URP water shader with depth tint, scrolling normals, fresnel sky
  reflection and shore foam.
- **Vegetation:** trees and plants instanced from the data.
- **Buildings** and the festival hub.
- **The drivable car,** using the cloud session's controller.

First playable target: spawn at the festival and drive the island at 60 fps on this PC.

## How we work

**Commits**

- Commit small and often: `git pull --rebase`, then push to `claude/make-this-gmpw4y`.
- Don't commit Library/Temp, or .blend files over about 20 MB.
- Don't put AI model names in commits or code.
- If a push fails for credentials, keep committing locally and tell the user the one command
  that fixes it (e.g. `gh auth login`). Don't wait on it.

**Docs:** keep `docs/UNITY.md` current: how to open and run, what's done, decisions, what each
script does, known issues.

**Showing the user:** put the work on screen at good moments. Open the hero car in Blender's
GUI, and open the Unity project in the Editor on the scene. Close the Editor again before batch
runs.

**Reporting**

- Don't fake results. If a render or build fails, say so and fix it.
- Write a short progress line after each step. The cloud session reads this transcript and may
  send you messages (e.g. "data landed in <commit>").
- Work autonomously. Only stop to ask the user when something needs them (Unity sign-in, a UAC
  prompt).
