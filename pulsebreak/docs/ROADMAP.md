# Pulsebreak roadmap

The game is built in phases. Each one ends with something you can run and judge, and none starts until you approve it. Technology choices and structure are in [ARCHITECTURE.md](ARCHITECTURE.md).

| Phase | Theme | Status |
| --- | --- | --- |
| 1 | Movement, one weapon, firing range | Built, awaiting your playtest |
| 2 | Weapon system, 30 weapons, attachments | Built, awaiting your playtest |
| 3 | Loadouts, saved locally | Built, awaiting your playtest |
| 4 | Netcode and online 1v1 duels | Built, awaiting your playtest |
| 5 | Accounts, backend and synced loadouts | Built; switched back on in Phase 8 |
| 6 | Every game mode, bots and matchmaking | Built, awaiting your playtest |
| 7 | Character customization and cosmetics | Built, awaiting your playtest |
| 8 | Progression, challenges and ranked | Built, awaiting your playtest |
| 8b | Abilities, streaks and more utility | Planned |
| 9 | UI polish, audio, visual effects and optimization | Built, awaiting your playtest |
| 9b | More maps, lobby rules and smarter bots | Built, awaiting your playtest |
| 10 | Social: friends, parties, chat, reporting, ranked seasons | Built, awaiting your playtest |
| 11 | Store, battle pass, more cosmetics | Planned |
| 12 | Keybinds, accessibility, anti-cheat hardening, launch readiness | Planned |

## Phase 1: Movement, one weapon, firing range

**Built.** Offline, single player.

- Fixed-tick character motor: run, sprint, crouch, slide, jump with coyote time and buffering, mantle, lean. (Lean was removed later, at your request.)
- One weapon, the Hadron AR, defined entirely by a ScriptableObject.
- Hitscan against head, body and limb hitboxes; hit markers, damage numbers, dynamic crosshair, tracers, impacts, placeholder audio.
- Firing range: static and moving targets from 10 to 75 m, a movement course, a raised perch with stairs, session stats including time to kill.
- Pause menu with sensitivity, aim sensitivity, field of view, volume and a damage-number toggle.

**You can test:** how movement and the gun feel, and whether the numbers in the tuning assets are right.

## Phase 2: Weapon system, 30 weapons, attachments

**Built.** Offline, single player. You asked for this ahead of netcode, so the phases below moved down one.

- Weapons as data: a definition asset per weapon, an attachment asset per attachment, a catalog listing both, and a validator that checks them.
- Weapon behaviours: automatic, semi-automatic, burst and charge fire; bullets that can pierce; shotgun pellets; projectiles with gravity, bounces, fuses and explosions; melee. Magazine and shell-by-shell reloads.
- 30 weapons across assault rifles, SMGs, LMGs, shotguns, sniper and marksman rifles, pistols, launchers, special weapons and melee.
- 21 attachments in six slots (sights, barrels, magazines, grips, stocks, lasers) that change stats, with optic reticles and a full-screen scope.
- Three weapon slots with switching; an armory in the pause menu to pick weapons and attachments and compare stats.
- Placeholder first-person models built from each weapon's proportions and fitted attachments; a synthesised sound per weapon.

**You can test:** every weapon and attachment on the range, and whether each one feels distinct and fairly tuned.

**Deferred from the brief:** the grapple gun (Phase 8b, with other movement and ability items), weapon levels gating attachments (built in Phase 8), camos (built in Phase 7) and charms (Phase 11).

## Phase 3: Loadouts, saved locally

**Built.** Offline, single player. You asked for this next, local first.

- A loadout is a primary, a secondary, a melee weapon, two utility items and a perk set. (Since cut to one utility item: see below.)
- Six default loadouts to pick from instantly.
- Custom loadouts: create, edit, rename, delete, up to ten, saved to a file as they change and still there next launch.
- Loadout rules that repair anything illegal or outdated when a loadout is loaded.
- Utility items that work: four throwables on G and F, with charges, cooldowns, and recharging on the range.
- Perk sets that work: five, changing weapon handling, movement speed or utility.
- A loadout screen in the pause menu (it absorbed the armory) and a quick swap list on B.

**You can test:** build a loadout, quit, start again and find it; swap between loadouts in play; throw every utility item; feel each perk set.

**Deliberately left for later:** syncing across devices (Phase 5; the save format is ready for it), unlock levels gating what can be equipped (Phase 7), choosing a loadout between rounds and on respawn (Phase 4, when there are rounds; quick swap stands in for it).

## Phase 4: Netcode and online 1v1 duels

**Built.** The first online build: two players, one server, one small arena. You chose to have the netcode written for the game rather than bought, so there is no library and no third-party account.

- A server that owns the match. It can run on its own (a dedicated server) or inside the game of a player who hosts.
- Client-side prediction: your own movement and shooting happen at once, and are checked against the server and corrected if they differ.
- Lag compensation: instant shots are judged against where the target was on the shooter's screen.
- The other player drawn smoothly from snapshots, as a placeholder body holding a block for a weapon, with tracers and shot sounds.
- Projectiles and utility items simulated on the server and drawn on both clients.
- The server runs movement, fire rate and ammunition itself, limits how fast a client can send commands, and checks every loadout with the same rules the client uses.
- Health, elimination and a kill feed.
- Duel rules: best of five, a countdown, a 90 second round clock, swapping ends each round, loadout changes applied at the next round, forfeit on leaving, a fresh match after the result.
- One arena, Relay Yard, built from prototype geometry.
- A main menu: host a match, join one by address, or go to the firing range.
- A way to make your own connection worse on purpose, to feel the game at a given ping.

**You can test:** a full 1v1 between two copies of the game, on one computer or two on the same network, with and without simulated lag. The README has the steps.

**Checked before you see it:** a real server and two real clients play whole matches inside the test suite, on connections as bad as a 325 ms round trip with a quarter of packets lost. What that cannot check is anything that only exists in Unity: the menu, the HUD, how the other player looks, and whether Unity's physics gives the client and the server the same answer as often as the test world does.

**Deliberately left for later:**

- Killcam (Phase 8b).
- Predicting your own projectiles. They appear when the server launches them, one round trip after the trigger.
- Encryption, and tying a connection to a logged-in account (Phase 5, with accounts).
- Hosting across the internet without port forwarding. Dedicated servers on public addresses arrive with matchmaking (Phase 6).
- Sending less data per player, which team modes will need (Phase 6).
- Players colliding with each other, and sound that has a position.

## Phase 5: Accounts, backend and synced loadouts

**Built.** You said "Continue", which I took as approval for this phase on the recommended stack (ASP.NET Core).

- An ASP.NET Core API (on .NET 10) with PostgreSQL and Redis, in `backend/`, with a Docker Compose file to run both locally.
- Sign up with email, username and password; sign in with either; verify the email by code or link; reset a forgotten password by emailed code; change a password.
- Passwords stored only as Argon2id hashes. Short-lived access tokens and single-use refresh tokens; a copied refresh token ends the session; a password reset signs every device out at once.
- Brute-force protection: per-account lockout that doubles, codes that die after five wrong guesses, per-address request limits, no hint whether an account exists.
- A profile per account (level, XP, currency, rating), at starting values until progression and ranked use them.
- Loadouts and settings on the account: change them on one computer and they are on the next, deletions included. Without an account the game works as before; loadouts made that way move onto your account when you first sign in.
- Sign-in, create-account, verify and reset screens in the main menu. The game stays signed in between launches.
- Join tickets: a game server given the backend's key admits only signed-in players, under their account names; started with `-ranked`, only those with a verified email. Hosting from the menu stays open to anyone.

**You can test:** start the backend, create an account from the game, verify it with the code (printed in the backend's console during development), sign in on a second copy of the game and find your loadouts there. The README has the steps.

**Checked before you see it:** 61 backend tests against a real PostgreSQL and Redis, and end-to-end tests that run the game's own account code against the real API and take its ticket into a real game server. What could not be checked here: the menu screens in Unity, and the Docker Compose file (no Docker daemon was available; PostgreSQL and Redis were run directly instead).

**Left for later:**

- Real email delivery needs an SMTP account from you; until then emails are printed or written to files.
- HTTPS and a hosted deployment (Phase 12). The game talks to a local backend over plain HTTP, which is allowed in the project settings for now and must be switched off before release.
- Encrypting game traffic, with a key handed out alongside the ticket (before public play).
- A breached-password check, an account deletion and data export flow, and a moderation tool to disable accounts (the flag exists).

## After your first playtest

You opened the project, liked it, and asked for a round of changes before the next phase. All of them are in:

- **Damage numbers** that tell body and head apart at a glance: small and white for the body, big, gold and popping for the head, red for the hit that finishes. A shotgun blast shows one total.
- **Standard damage numbers.** Every hit lands as a multiple of five, and the base numbers are clean: an Assault Rifle is 25 to the body and 35 to the head.
- **Loadouts like the arena shooters'**: primary, secondary, melee and one utility item (a grenade or similar), plus the perk set from the original brief. A Deploy button and loadout contents in the list make picking one quicker.
- **More weapons in that style**, with standard names: Bow, Flamethrower, Paintball Gun, Sawed-Off, Flare Gun, Slingshot, Laser Pistol, Scythe, Battle Axe and Fists, for 40 in all, and every existing weapon renamed to what it is (Assault Rifle, SMG, Pump Shotgun, Sniper Rifle...). Two new default loadouts use the new ones.
- **Stat bars that show the differences**, by comparing each weapon with the others for the same slot.
- **Sprint** 10% faster (8.6 m/s), and a **toggle sprint** setting.
- **Lean removed.** Q and E no longer tilt; the movement code, its settings and the camera tilt are gone, and servers ignore the old lean input.
- **Accounts switched off** until they are made required. The backend stays in the repository, tested.

## Phase 6: Every game mode, bots and matchmaking

You asked for all the game modes and matchmaking together, so this phase also took in the modes, bots and private lobbies that were planned for Phase 9.

- **Matches of up to twelve players**, in teams or everyone for themselves. Friendly fire is off. Players come back away from enemies, never on top of anyone.
- **Every mode in the brief:** Duel (best of five), Team Deathmatch, Free-for-All, Elimination (no respawns in a round), Domination, Hardpoint and Gun Game. Each is a small class on the server; the rest of the server does not know which is running.
- **Bots** that take empty places and step aside for people: they find paths over the map (including steps and ledges), react, aim with skill-based error, control recoil, strafe, reload, switch weapons, throw grenades and play the objective. Three skill levels.
- **Crossroads**, a 64 × 48 m map for teams of up to five, with three Domination zones and three hardpoints.
- **Private matches** from the menu: mode, map, team size, win limit, bots on or off and their skill.
- **Matchmaking:** a queue per mode on the backend (Redis), guest passes while accounts are off, dedicated servers that register and check in, and a matchmaker that hands a free server each match with join tickets good for that match only. A queue that does not fill starts after 20 seconds with bots in the empty places.
- **In a match:** team scores, zones (on screen and in the world), respawn timers, Gun Game progress, a Tab scoreboard with kills, deaths and score, and teammates drawn blue.

Since then, from your notes: the weapons sit along the bottom centre of the screen as three cards (key, slot, weapon), and the loadout screen lists only your own loadouts (the presets are starting points, out of sight).

Still to come from the original Phase 6 plan: parties queuing together, results and match history saved to the account (with accounts on), and containerised servers started on demand rather than a fixed set.

**You can test:** every mode against bots from **Host a private match**; two copies of the game in one match; and with the backend and a dedicated server running, **Find match**.

## Phase 7: Character customization and cosmetics

You asked for this next, ahead of progression, with everything free to pick for now.

- **A look of your own:** suit and trim colours, a pattern printed on the suit, headgear, a visor glow and a back item, all original designs built from simple shapes and patterns drawn in code.
- **Weapon skins:** twelve skins plus the factory finish, one for every weapon or one per weapon. They show on your first-person weapon and in your hands to others.
- **The Customize screen** from the main menu and the Esc menu, with a live preview that turns, and Randomize and Reset.
- **Seen online:** your look travels with you into a match and changes there straight away; the server passes on at most two changes a second per player. Bots wear random looks.
- **Fair by construction:** cosmetics have no colliders and never touch the hitboxes, and team-coloured shoulder pads sit on every outfit.
- Saved on your computer (`cosmetics.json`). With accounts on, it would follow the account like loadouts do; that wiring is for when accounts are switched on.

**You can test:** open Customize, dress up, check your weapon skin in first person on the range, then join a match (two copies, or bots) and see each other's looks.

## Phase 8: Progression, challenges and ranked

You chose: accounts back on, unlocks enforced except in private matches and on the range, and ranked for Duel and Elimination 3v3.

- **Accounts on again.** Sign-in is back in the menu; guests can still play casual and everything offline.
- **Trusted results.** A matchmade server plays one match, reports it to the backend (kills, deaths, headshots, objective points, rounds won, kills per weapon, who won, who left), and sends everyone back to the menu. Each match counts once, only from the server it was given to, only for the players it was sent.
- **XP and 50 levels**, weapon XP and 20 weapon levels, with the formulas in one file shared by the game and the backend.
- **Unlocks** from the levels already in the content: a starter set for new players and guests in matchmaking, everything on the range and in private matches. The loadout screen marks what is locked.
- **Challenges:** three daily and three weekly per player, paying XP when done.
- **Ranked Duel and Ranked Elimination (3v3):** verified accounts only, full matches of people, matched by rating with a widening window, teams balanced by rating, five placement matches, seven tiers with divisions, leaderboards. Leaving or not showing up is a loss.
- **Progress screen and post-match summary** in the game; match history on the account.

**You can test:** with the backend and a dedicated server running, sign in, find a few matches, and watch XP, levels, challenges, weapon levels and unlocks move; verify your email and play ranked with a second account.

## Phase 8b: Abilities, streaks and more utility

- The grapple gun and any other movement tools.
- More utility items, including non-damaging ones (smoke, flash, scan).
- Kill streak and ability rewards: area scan, airstrike, personal shield.
- Killcam.

**You can test:** a match where streaks, utility and movement tools all come into play.

## Phase 9: UI polish, audio, visual effects and optimization

You chose: hits on players are bursts of energy in their colour (no blood), and music in the menus only, plus short stingers for the big moments.

- **Sound you can place.** Other players' shots, footsteps, jumps, landings, slides, throws and explosions play from where they happened: quieter and duller with distance, muffled behind walls. Footsteps tell you who is near: sprinting is loud, running is clear, crouch-walking is nearly silent. Bullets that pass close crack past your head.
- **More sounds**, all generated in code: footsteps, being hit (a heavier one to the head), going down, a heartbeat on low health, arriving, a headshot kill, countdown beeps, zone captured and lost, the hardpoint moving, Gun Game promotions, match found, a finished challenge, and clicks for every menu.
- **Music:** a synth piece in the menus, made as it plays, that fades out when a match starts; stingers for the match start, a round won or lost, victory, defeat and a level up.
- **Four volume sliders:** master, effects, music, interface. They follow your account.
- **Effects:** muzzle flashes (now for other players too), glowing tracers, bullet holes with sparks and dust, energy bursts where shots land on players, a body coming apart into light when a player goes down and a shimmer when one arrives, rockets trailing smoke and energy shots trailing light, and explosions with fire, smoke, sparks, a shock ring, a scorch mark and a flash.
- **The picture:** bloom, a neutral tone curve, a little contrast and a vignette. When you are hit the screen edges flash red and a red arc points to where it came from; on low health the edges stay red and the colour drains; when you are out it goes grey. Explosions shake the view and hits make it flinch (both can be turned off).
- **Interface:** a Settings screen (gameplay, video, audio) from the menu and the pause menu, which is now a short column of buttons; buttons, tabs and screens ease in and out; announcements punch in; medals for headshots, multi-kills and streaks; an "eliminated by" line saying who got you, with what, and how much health they had left; the camera drifts slowly over the arena behind the main menu.
- **Video settings:** quality preset (Low, Medium, High, Ultra), display mode, frame rate limit, VSync, render scale, and a frame rate readout.
- **Optimization, measured:** in a twelve-player match each player now downloads about 300 kbit/s instead of 510 (own state packed without losing anything, other players' angles and speeds sent to a hundredth), the server allocates no garbage of its own walking the player list, and each arena is drawn in a handful of batches instead of one draw call per block. A test holds these numbers.
- **A hurt message.** The server tells a player how much damage they took and where it came from, which the arcs and the flinch use.

**You can test:** play a match against bots with headphones and find them by their footsteps; take fire from behind and follow the arc; throw a grenade near a wall; turn every slider in Settings; try Low and Ultra and watch the frame rate readout.

## Phase 9b: More maps, lobby rules and smarter bots

Built together with Phase 10, at your request.

- **Three new maps:** Kiln (a small arena for duels and 2v2), Saltworks (four a side: roofed sheds, a square of pans, raised gantries) and Lattice (five a side: a grid of blocks, hollow blocks the centre lane runs through, a raised plaza). Each has spawns for both sides and for Free-for-All; Saltworks and Lattice have Domination zones and hardpoints.
- **Map rotation:** a matchmade server plays a different map each match, from those that suit the playlist.
- **Private match rules:** time limit (or round time), health (50 to 200), respawn delay, and weapons: any, sidearms only, snipers only, shotguns only, no explosives, melee only. Whatever a loadout has that the rule does not allow is swapped at spawn.
- **Bots** fall back to cover when badly hurt or reloading, then peek back out; they share where they saw enemies, so teammates converge; and they call it out to the people on their team in team chat.

**You can test:** every mode on every map it supports, against bots; a private match on snipers only or melee only; a team match with bots on your side, to see their callouts.

## Phase 10: Social

You chose: ranked stays solo; party and match chat with a word filter; ranked seasons with a soft reset.

- **Friends:** add by username, accept or decline, remove; see who is online and where (menu, match, range).
- **Parties** of up to five: invite online friends; the leader searches for everyone, the others follow automatically; the whole party is placed on one side. Leaving, kicking or joining ends a running search. Members whose game stops checking in drop out.
- **Chat:** party chat in the menu; match chat to everyone (Enter or T) or your team (Y), rate-limited on the server; a filter for strong language (on by default), mute per player, and blocked players hidden.
- **Block and report:** from the social panel or the Players list in a match. Reports carry a reason, a few words and the player's recent chat, and go to a list for moderators (behind an admin key).
- **Ranked seasons:** eight weeks each; ratings move halfway back to 1000 at the turn, placements are replayed, each season's finish is kept, and leaderboards show the current season.

**You can test:** two accounts (two copies of the game with different `-profile`s): add each other, form a party, chat, find a casual match together and check you are on the same side; report and block; mute someone in a match.

## Phase 11: Store, battle pass, more cosmetics

- Cosmetics earned or bought rather than all free: a store selling cosmetics only (nothing that affects gameplay is ever sold), a battle-pass-style reward track, daily and weekly challenges.
- More to wear: gloves, emotes, victory poses, calling cards, name tags, weapon charms, and real character and weapon models in place of the shapes.
- Season rewards for ranked: something to show for where you finished.

**You can test:** earn and buy cosmetics, and equip them on the Customize screen.

## Phase 12: Keybinds, accessibility, anti-cheat hardening, launch readiness

- The rest of the settings: keybinds, a crosshair editor, colourblind modes, subtitles for sound cues. (Graphics and audio settings were built in Phase 9.)
- Server-side anomaly detection on top of the built-in validation, and a decision on client-side anti-cheat for what a server cannot see (wallhacks, aim assistance).
- Build pipeline, server deployment, monitoring.

**You can test:** a complete session from sign-up to ranked match on a deployed environment.

## Decisions waiting on you

1. **The name.** "Pulsebreak" is a working title and I have not checked it against trademarks. It appears in namespaces, asset names and now the backend, so it is cheapest to change early.
2. **An email provider**, when you want real email sent: any provider with SMTP works. Nothing else needs it until then.

Decided so far: netcode is written for the game, over UDP (Phase 4); the backend is ASP.NET Core (Phase 5).
