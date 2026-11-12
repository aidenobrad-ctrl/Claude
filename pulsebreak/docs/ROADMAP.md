# Pulsebreak roadmap

The game is built in phases. Each one ends with something you can run and judge, and none starts until you approve it. Technology choices and structure are in [ARCHITECTURE.md](ARCHITECTURE.md).

| Phase | Theme | Status |
| --- | --- | --- |
| 1 | Movement, one weapon, firing range | Built, awaiting your playtest |
| 2 | Weapon system, 30 weapons, attachments | Built, awaiting your playtest |
| 3 | Loadouts, saved locally | Built, awaiting your playtest |
| 4 | Netcode and online 1v1 duels | Built, awaiting your playtest |
| 5 | Accounts, backend and synced loadouts | Built; switched off in the game for now |
| 6 | Every game mode, bots and matchmaking | Built, awaiting your playtest |
| 7 | Progression and unlocks | Planned |
| 8 | Abilities, streaks and more utility | Planned |
| 9 | More maps, lobby extras, parties | Planned |
| 10 | Ranked and social | Planned |
| 11 | Customization, store, battle pass | Planned |
| 12 | Settings, polish, anti-cheat hardening, launch readiness | Planned |

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

**Deferred from the brief:** the grapple gun (Phase 8, with other movement and ability items), weapon levels gating attachments (Phase 7; the unlock levels are already in the data), camos and charms (Phase 11).

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

- Killcam (Phase 8).
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

## Phase 7: Progression and unlocks

- Player level and XP; weapon levels.
- Unlock rules enforced: weapons, attachments, utility items and perk sets become available at the levels already set in their assets.
- Post-match XP breakdown.
- The loadout screen shows what is locked and why.

**You can test:** play matches, level up, and watch new options open up on the loadout screen.

## Phase 8: Abilities, streaks and more utility

- The grapple gun and any other movement tools.
- More utility items, including non-damaging ones (smoke, flash, scan).
- Kill streak and ability rewards: area scan, airstrike, personal shield.
- Killcam.

**You can test:** a match where streaks, utility and movement tools all come into play.

## Phase 9: More maps, lobby extras, parties

- At least three more maps, from duel arenas to medium 5v5 layouts, with clear lanes, cover and height.
- More private lobby settings: time limit, health, weapon restrictions.
- Parties: invite friends and queue together.
- Bots that use cover and call out to their team.

**You can test:** every mode on every map it supports, alone against bots or in a private lobby with friends.

## Phase 10: Ranked and social

- Skill rating, rank tiers and a ranked playlist, open only to verified accounts.
- Skill-based matchmaking for ranked, looser for casual.
- Friends list, party invites, block and report.
- Leaderboards.

**You can test:** play ranked matches and watch rating and tier move; add a friend and invite them.

## Phase 11: Customization, store, battle pass

- Operators, outfits, helmets, gloves, colours, emotes, victory poses, calling cards, name tags, with a rotating 3D preview.
- Weapon camos and charms.
- Battle-pass-style reward track, daily and weekly challenges.
- Store selling cosmetics only. Nothing that affects gameplay is ever sold.

**You can test:** earn and equip cosmetics and see them on your character in a match.

## Phase 12: Settings, polish, anti-cheat hardening, launch readiness

- Full settings: keybinds, graphics, audio, crosshair editor, colourblind modes.
- Proper audio and visual effects, real weapon models, performance work.
- Server-side anomaly detection on top of the built-in validation, and a decision on client-side anti-cheat for what a server cannot see (wallhacks, aim assistance).
- Build pipeline, server deployment, monitoring.

**You can test:** a complete session from sign-up to ranked match on a deployed environment.

## Decisions waiting on you

1. **The name.** "Pulsebreak" is a working title and I have not checked it against trademarks. It appears in namespaces, asset names and now the backend, so it is cheapest to change early.
2. **An email provider**, when you want real email sent: any provider with SMTP works. Nothing else needs it until then.

Decided so far: netcode is written for the game, over UDP (Phase 4); the backend is ASP.NET Core (Phase 5).
