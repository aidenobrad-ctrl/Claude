# Pulsebreak roadmap

The game is built in phases. Each one ends with something you can run and judge, and none starts until you approve it. Technology choices and structure are in [ARCHITECTURE.md](ARCHITECTURE.md).

| Phase | Theme | Status |
| --- | --- | --- |
| 1 | Movement, one weapon, firing range | Built, awaiting your playtest |
| 2 | Weapon system, 30 weapons, attachments | Built, awaiting your playtest |
| 3 | Loadouts, saved locally | Built, awaiting your playtest |
| 4 | Netcode foundation and 1v1 duels | Needs the netcode decision |
| 5 | Accounts, backend and synced loadouts | Planned |
| 6 | Matchmaking and team modes | Planned |
| 7 | Progression and unlocks | Planned |
| 8 | Abilities, streaks and more utility | Planned |
| 9 | Maps, objective modes, bots, custom lobbies | Planned |
| 10 | Ranked and social | Planned |
| 11 | Customization, store, battle pass | Planned |
| 12 | Settings, polish, anti-cheat hardening, launch readiness | Planned |

## Phase 1: Movement, one weapon, firing range

**Built.** Offline, single player.

- Fixed-tick character motor: run, sprint, crouch, slide, jump with coyote time and buffering, mantle, lean.
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

- A loadout is a primary, a secondary, a melee weapon, two utility items and a perk set.
- Six default loadouts to pick from instantly.
- Custom loadouts: create, edit, rename, delete, up to ten, saved to a file as they change and still there next launch.
- Loadout rules that repair anything illegal or outdated when a loadout is loaded.
- Utility items that work: four throwables on G and F, with charges, cooldowns, and recharging on the range.
- Perk sets that work: five, changing weapon handling, movement speed or utility.
- A loadout screen in the pause menu (it absorbed the armory) and a quick swap list on B.

**You can test:** build a loadout, quit, start again and find it; swap between loadouts in play; throw every utility item; feel each perk set.

**Deliberately left for later:** syncing across devices (Phase 5; the save format is ready for it), unlock levels gating what can be equipped (Phase 7), choosing a loadout between rounds and on respawn (Phase 4, when there are rounds; quick swap stands in for it).

## Phase 4: Netcode foundation and 1v1 duels

The first online build: two players, one dedicated server, one small arena.

- Integrate the netcode library and a headless dedicated server build.
- Networked motor with client-side prediction and server reconciliation; remote players interpolated.
- Server-authoritative shooting with lag-compensated hitboxes; projectiles and utility simulated on the server. Server-side enforcement of fire rate, ammo and movement speed.
- Loadouts sent as ids and checked by the server with the same rules the client uses.
- Player health, death and respawn; a third-person placeholder body so players can see each other; kill feed.
- Duel rules: best of five rounds, spawn points, round and match flow, loadout choice between rounds, a minimal scoreboard.
- One duel arena built from prototype geometry.

**You can test:** a full 1v1 match between two clients against a local server, including with simulated latency and packet loss.

**Needs from you:** the netcode choice (Fusion 2 recommended), and a Photon account if you take it.

## Phase 5: Accounts, backend and synced loadouts

- ASP.NET Core API with PostgreSQL and Redis.
- Sign-up, email verification, login with access and refresh tokens, password reset.
- Argon2id password hashing, rate limiting, login lockout, input validation.
- Profile storage; game client login and registration screens; dedicated servers authenticate to the API.
- Loadouts stored on the account: a backend loadout store replaces the local file, with the newest change winning when two devices disagree.

**You can test:** create an account, verify it by email, log in from the game, reset the password, watch brute-force attempts get blocked, and find your loadouts on a second machine.

## Phase 6: Matchmaking and team modes

- Redis-backed queues, a matchmaker service, and game server allocation.
- Casual playlist; parties queue together.
- 2v2 and 3v3 Elimination (no respawns in a round), Team Deathmatch.
- Results reported by the server; stats and match history saved; post-match scoreboard.

**You can test:** queue from the main menu and land in a match with other clients; see the result in your history.

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

## Phase 9: Maps, objective modes, bots, custom lobbies

- At least five maps, from duel arenas to medium 5v5 layouts, with clear lanes, cover and height.
- Domination or Hardpoint, Free-for-All, Gun Game, 5v5.
- Bots for practice and for filling casual matches.
- Private lobbies with settings: map, mode, time, health, weapon restrictions.

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
- Server-side anomaly detection on top of the built-in validation.
- Build pipeline, server deployment, monitoring.

**You can test:** a complete session from sign-up to ranked match on a deployed environment.

## Decisions waiting on you

1. **Netcode library.** Fusion 2 (recommended) or Fish-Networking. Needed before Phase 4.
2. **Backend stack.** ASP.NET Core (recommended) or Node.js. Needed before Phase 5.
3. **The name.** "Pulsebreak" is a working title and I have not checked it against trademarks. It appears in namespaces and asset names, so it is cheapest to change early.
