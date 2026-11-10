# Pulsebreak roadmap

The game is built in phases. Each one ends with something you can run and judge, and none starts until you approve it. Technology choices and structure are in [ARCHITECTURE.md](ARCHITECTURE.md).

| Phase | Theme | Status |
| --- | --- | --- |
| 1 | Movement, one weapon, firing range | Built, awaiting your playtest |
| 2 | Netcode foundation and 1v1 duels | Next, needs the netcode decision |
| 3 | Combat core and first arsenal | Planned |
| 4 | Accounts and backend | Planned |
| 5 | Matchmaking and team modes | Planned |
| 6 | Loadouts and progression | Planned |
| 7 | Full arsenal, attachments, streaks | Planned |
| 8 | Maps, objective modes, bots, custom lobbies | Planned |
| 9 | Ranked and social | Planned |
| 10 | Customization, store, battle pass | Planned |
| 11 | Settings, polish, anti-cheat hardening, launch readiness | Planned |

## Phase 1: Movement, one weapon, firing range

**Built.** Offline, single player.

- Fixed-tick character motor: run, sprint, crouch, slide, jump with coyote time and buffering, mantle, lean.
- One weapon, the Hadron AR, defined entirely by a ScriptableObject: fire rate, damage falloff, headshot and limb multipliers, magazine and reload, ADS, spread with bloom, a 20-shot recoil pattern.
- Hitscan against head, body and limb hitboxes; hit markers, damage numbers, dynamic crosshair, tracers, impacts, placeholder audio.
- Firing range: static and moving targets from 10 to 75 m, a movement course, a raised perch with stairs, session stats including time to kill.
- Pause menu with sensitivity, aim sensitivity, field of view, volume and a damage-number toggle.

**You can test:** how movement and the gun feel, and whether the numbers in the two tuning assets are right.

## Phase 2: Netcode foundation and 1v1 duels

The first online build: two players, one dedicated server, one small arena.

- Integrate the netcode library and a headless dedicated server build.
- Networked motor with client-side prediction and server reconciliation; remote players interpolated.
- Server-authoritative shooting with lag-compensated hitboxes. Server-side enforcement of fire rate, ammo and movement speed.
- Player health, death and respawn; a third-person placeholder body so players can see each other.
- Duel rules: best of five rounds, spawn points, round and match flow, a minimal scoreboard.
- One duel arena built from prototype geometry.

**You can test:** a full 1v1 match between two clients against a local server, including with simulated latency and packet loss.

**Needs from you:** the netcode choice (Fusion 2 recommended), and a Photon account if you take it.

## Phase 3: Combat core and first arsenal

- Weapon behaviours beyond hitscan rifles: projectiles with travel time and drop, shotgun pellets, scoped sniping, melee.
- Around ten weapons across the main classes, each its own definition asset.
- Weapon switching: primary, secondary, melee; two utility slots with the first grenades.
- Kill feed, damage direction indicator, death recap; killcam groundwork (recording the data it needs).

**You can test:** duels with a varied loadout chosen from a temporary in-game menu.

## Phase 4: Accounts and backend

- ASP.NET Core API with PostgreSQL and Redis.
- Sign-up, email verification, login with access and refresh tokens, password reset.
- Argon2id password hashing, rate limiting, login lockout, input validation.
- Profile storage; game client login and registration screens; dedicated servers authenticate to the API.

**You can test:** create an account, verify it by email, log in from the game, reset the password, and watch brute-force attempts get blocked.

## Phase 5: Matchmaking and team modes

- Redis-backed queues, a matchmaker service, and game server allocation.
- Casual playlist; parties queue together.
- 2v2 and 3v3 Elimination (no respawns in a round), Team Deathmatch.
- Results reported by the server; stats and match history saved; post-match scoreboard.

**You can test:** queue from the main menu and land in a match with other clients; see the result in your history.

## Phase 6: Loadouts and progression

- Default loadouts to pick instantly; custom loadouts (primary, secondary, melee, two utility, one perk set) created, renamed and saved to the backend.
- Quick-swap between rounds and on respawn.
- Player level and XP, weapon levels, unlock rules, post-match XP breakdown.
- Loadout editor and armory screens.

**You can test:** build a loadout on one machine, log in on another, and find it there.

## Phase 7: Full arsenal, attachments, streaks

- Thirty or more weapons, including the arcade set: grapple gun, burst crossbow, ricochet blaster, energy rifle, throwing knives, katana.
- Attachments (sights, barrels, magazines, grips, stocks, lasers) that modify weapon stats; unlocked by weapon level.
- Kill streak and ability rewards: area scan, airstrike, personal shield.
- Perk sets.

**You can test:** every weapon on the firing range and in matches; attachment effects visible in the stats and in the hands.

## Phase 8: Maps, objective modes, bots, custom lobbies

- At least five maps, from duel arenas to medium 5v5 layouts, with clear lanes, cover and height.
- Domination or Hardpoint, Free-for-All, Gun Game, 5v5.
- Bots for practice and for filling casual matches.
- Private lobbies with settings: map, mode, time, health, weapon restrictions.

**You can test:** every mode on every map it supports, alone against bots or in a private lobby with friends.

## Phase 9: Ranked and social

- Skill rating, rank tiers and a ranked playlist, open only to verified accounts.
- Skill-based matchmaking for ranked, looser for casual.
- Friends list, party invites, block and report.
- Leaderboards.

**You can test:** play ranked matches and watch rating and tier move; add a friend and invite them.

## Phase 10: Customization, store, battle pass

- Operators, outfits, helmets, gloves, colours, emotes, victory poses, calling cards, name tags, with a rotating 3D preview.
- Weapon camos and charms.
- Battle-pass-style reward track, daily and weekly challenges.
- Store selling cosmetics only. Nothing that affects gameplay is ever sold.

**You can test:** earn and equip cosmetics and see them on your character in a match.

## Phase 11: Settings, polish, anti-cheat hardening, launch readiness

- Full settings: keybinds, graphics, audio, crosshair editor, colourblind modes.
- Killcam, proper audio and visual effects, performance work.
- Server-side anomaly detection on top of the built-in validation.
- Build pipeline, server deployment, monitoring.

**You can test:** a complete session from sign-up to ranked match on a deployed environment.

## Decisions waiting on you

1. **Netcode library.** Fusion 2 (recommended) or Fish-Networking. Needed before Phase 2.
2. **Backend stack.** ASP.NET Core (recommended) or Node.js. Needed before Phase 4.
3. **The name.** "Pulsebreak" is a working title and I have not checked it against trademarks. It appears in namespaces and asset names, so it is cheapest to change early.
