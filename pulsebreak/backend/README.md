# Pulsebreak backend

The accounts and matchmaking API: sign-up and sign-in, email verification, password reset, the
player data that follows an account between computers (profile, loadouts, settings), and finding
matches: queues for each mode, the fleet of dedicated game servers, and the join tickets that tell
a game server who is connecting.

ASP.NET Core on .NET 10, PostgreSQL for everything that lasts, Redis for sessions, codes,
counters and matchmaking.

## Run it on your machine

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and either Docker or a local
PostgreSQL 16 and Redis 7.

```
cd pulsebreak/backend
docker compose up -d
dotnet run --project src/Pulsebreak.Api
```

The API listens on http://localhost:5080, which is where the game looks by default. In
development it creates the database tables itself on start, and instead of sending email it
**prints each email to the console**, so the verification and reset codes are right there.

Check it is up: http://localhost:5080/health/ready should say the database and Redis are both `true`.

## Tests

```
cd pulsebreak/backend
docker compose up -d
dotnet test
```

The tests start the whole API in memory against the real PostgreSQL and Redis, each test class
in a fresh database that is dropped afterwards. They expect PostgreSQL at 127.0.0.1:5432 with
the user `pulsebreak` / `pulsebreak_dev` (allowed to create databases) and Redis at
127.0.0.1:6379. Point them elsewhere with `PULSEBREAK_TEST_POSTGRES` (a connection string
without a database name) and `PULSEBREAK_TEST_REDIS`. They flush Redis databases 1 to 14, so
do not aim them at a Redis that holds anything you want.

## What it offers

All requests and replies are JSON. Errors always look like
`{"error": {"code": "...", "message": "...", "fields": [{"field": "...", "message": "..."}]}}`.

| Method and path | Signed in | What it does |
| --- | --- | --- |
| `POST /v1/auth/register` | | Create an account: `email`, `username`, `password`. Signs you in and emails a verification code |
| `POST /v1/auth/login` | | `login` (email or username) and `password` |
| `POST /v1/auth/refresh` | | Swap a refresh token for a new access token and a new refresh token |
| `POST /v1/auth/logout` | | End the session a refresh token belongs to |
| `POST /v1/auth/logout-all` | yes | End every session of the account, at once |
| `POST /v1/auth/verify-email` | yes | `code` from the email |
| `GET /v1/auth/verify-email?token=` | | The link in the email; answers with a page for a browser |
| `POST /v1/auth/resend-verification` | yes | A new code (at most one a minute) |
| `POST /v1/auth/forgot-password` | | Emails a reset code if the address has an account. Same answer either way |
| `POST /v1/auth/reset-password` | | `email`, `code`, `newPassword`. Signs every device out |
| `POST /v1/auth/change-password` | yes | `currentPassword`, `newPassword`. Signs other devices out |
| `GET /v1/me` | yes | The account and its profile (level, XP, currency, rating) |
| `GET /v1/loadouts` | yes | The account's loadouts |
| `POST /v1/loadouts/sync` | yes | Send every loadout you have; get back the merged set |
| `GET /v1/settings`, `PUT /v1/settings` | yes | The player's settings (sensitivity, field of view, damage numbers, toggle sprint, and the master, effects, music and interface volumes); the newest save wins. Fields added later have defaults, so older saves still read |
| `POST /v1/game/tickets` | yes | A join ticket for a game server. `mode` is `casual` or `ranked`; ranked needs a verified email |
| `POST /v1/matchmaking/guest` | | A guest pass for matchmaking without an account: `name` |
| `GET /v1/matchmaking/playlists` | | The modes on offer and how many are searching for each |
| `POST /v1/matchmaking/tickets` | yes, or guest | Join a queue: `playlist` (`duel`, `tdm`, `ffa`, `elimination`, `domination`, `hardpoint`, `gungame`, or ranked `duel_ranked`, `elimination_ranked`) and the game's network `version`. Ranked needs an account with a verified email |
| `GET /v1/matchmaking/tickets/{id}` | yes, or guest | How the search is going. Once `matched`: the server's `address` and a `joinTicket` for that match. Ask at least every 20 seconds or the place is given up |
| `DELETE /v1/matchmaking/tickets/{id}` | yes, or guest | Give up the place |
| `POST /v1/fleet/servers` | fleet key | A dedicated game server offers itself: `address`, `port`, `version` |
| `POST /v1/fleet/servers/{id}/heartbeat` | fleet key | Check in every few seconds: `state` (`idle` or `busy`), `matchId`, `players`. The reply is the match to start, if there is one: `matchId`, `playlist`, `ranked`, and the `players` with their side, level and weapon levels |
| `DELETE /v1/fleet/servers/{id}` | fleet key | Leave the fleet |
| `POST /v1/fleet/servers/{id}/results` | fleet key | A finished matchmade match: every player's stats and outcome. Counted once; XP, weapon XP, challenges, ratings and history follow |
| `GET /v1/progression` | yes | Level and XP, weapon levels, today's and this week's challenges, ranks, and what the last match earned |
| `GET /v1/matches` | yes | The account's recent matches (`limit`, default 10) |
| `GET /v1/ranked/{playlist}/leaderboard` | | The best players in `duel_ranked` or `elimination_ranked` who have finished placement (`limit`, default 50) |
| `GET /health`, `GET /health/ready` | | Alive; and able to reach the database and Redis |

"Signed in" means an `Authorization: Bearer <access token>` header; "guest" means a guest pass
in the same header. "Fleet key" means an `X-Fleet-Key` header with the value of
`Matchmaking__FleetKey`.

## How it keeps accounts safe

- **Passwords** are stored only as Argon2id hashes (19 MiB, 2 passes by default, OWASP's
  recommended minimum), with a fresh salt each. Raising the cost later is safe: old hashes keep
  working and are upgraded the next time their owner signs in. Passwords need 10 characters; very
  common ones, and ones containing the username or email name, are refused.
- **Sessions.** An access token lasts 15 minutes. A refresh token lasts 30 days and works once:
  each use returns a new one. If a used one turns up again, someone has copied it, and the whole
  session is ended. Only hashes of refresh tokens are stored. A password reset, a password change
  or "sign out everywhere" also stops access tokens that have not expired yet.
- **Guessing.** Five wrong passwords lock that account's sign-in for a minute, doubling with each
  further failure up to 15 minutes. Names with no account are locked the same way, so a lock says
  nothing about whether an account exists. A wrong password and an unknown account get the same
  answer, in about the same time. Emailed codes stop working after five wrong guesses. Each client
  address gets 20 sign-in-type requests a minute and 300 requests of any kind.
- **Input.** Every field is checked on the server. Request bodies over 64 KB are refused unread.
  Loadouts are checked for shape and size here; whether they are legal to play with is checked
  by the game server against the game's own content.

## Configuration

Settings come from `appsettings.json`, then `appsettings.{Environment}.json`, then environment
variables (a setting `Auth:JwtSigningKey` is the variable `Auth__JwtSigningKey`). Outside
development, set at least these. The API refuses to start without the first.

| Variable | What it is |
| --- | --- |
| `Auth__JwtSigningKey` | Secret that signs access tokens. 32 characters or more |
| `Game__TicketSigningKey` | Secret that signs join tickets. Give dedicated game servers the same value. Different from the one above |
| `ConnectionStrings__Postgres` | For example `Host=db;Database=pulsebreak;Username=...;Password=...` |
| `ConnectionStrings__Redis` | For example `redis:6379` |
| `Email__Mode` | `Smtp` to send real email (`Log` prints it, `File` writes it to `Email__FileDirectory`) |
| `Email__SmtpHost`, `Email__SmtpPort`, `Email__SmtpUser`, `Email__SmtpPassword`, `Email__From` | Your mail provider's SMTP details |
| `Email__PublicBaseUrl` | Where players' browsers reach this API, for the link in the verification email |
| `RateLimits__TrustForwardedHeaders` | `true` only behind a reverse proxy that sets `X-Forwarded-For`, and only if the API cannot be reached any other way |
| `Database__MigrateOnStartup` | `true` to bring the schema up to date on start. Otherwise run the migrations yourself |
| `Matchmaking__FleetKey` | Secret dedicated game servers show to join the fleet. 32 characters or more. Empty means no servers, so nobody gets matched |
| `Matchmaking__FillAfterSeconds` | How long a queue waits to fill before starting with whoever is there, bots taking the other places. Default 20 |

Make a secret with `openssl rand -base64 48`.

Run it behind HTTPS in production. The game talks to it over plain HTTP only for local development.

## Game servers that check tickets

A dedicated game server given the ticket key admits only players who show a valid ticket, and
uses the account's username rather than whatever the client calls itself:

```
PULSEBREAK_TICKET_KEY=<same as Game__TicketSigningKey> Pulsebreak.exe -batchmode -nographics -server
```

Add `-ranked` and it admits only tickets good for ranked play, which the API gives only to
accounts with a verified email. The game server checks a ticket by itself, with no call to this
API, so the API being slow or down does not affect matches already being joined.

## Matchmaking

Each mode has a queue. Players join one with their account, or a guest pass if they are not
signed in (casual playlists only), and ask about it every second. Dedicated game servers join the fleet with the
fleet key and check in every three seconds. Once a second the matchmaker takes the oldest
players in each queue and, when the queue is full or the oldest has waited
`Matchmaking__FillAfterSeconds`, sends them to a free server built from the same game version.
The server hears about the match on its next check-in and switches to its mode and map; each
player gets the server's address and a join ticket good for that match only. A Duel always waits
for two people; the other modes start with one, and bots take the empty places.

Queues, tickets and the fleet live in Redis, and the matchmaker runs under a lock, so several API
processes can serve the same players. A dedicated server joins the fleet like this:

```
PULSEBREAK_FLEET_KEY=<Matchmaking__FleetKey> PULSEBREAK_TICKET_KEY=<Game__TicketSigningKey> \
  Pulsebreak.exe -batchmode -nographics -server -fleet https://api.example.com -public-address 203.0.113.5 -port 7777
```

`-public-address` is where players reach it. Run one per port to host several matches on one
machine. The development keys are in `appsettings.Development.json`.

## Progression and ranked

Only matches the matchmaker set up count. When one is over, its game server posts a report; the
backend checks the match went to that server and has not been counted, then in one transaction
adds match XP and weapon XP, moves the player's three daily and three weekly challenges along
(paying out the finished ones), changes ranked ratings (Elo against the other side's average, five
placement matches), and records the match. Players the matchmaker sent to a ranked match who are
missing from the report lose. The tables are `weapon_progress`, `ratings`, `challenges`,
`matches` and `match_players`, added by the `Progression` migration.

The XP, level and rank formulas are the game's own `Shared/Progression/Progression.cs`, compiled
in here, so the game always shows what the backend awards.

## Changing the database

The schema is in `src/Pulsebreak.Api/Data/`. After changing it, add a migration:

```
dotnet tool restore
dotnet ef migrations add <Name> --project src/Pulsebreak.Api --output-dir Data/Migrations
```

## Shared with the game

Five files are the game's own source, compiled into the API as well, so the two cannot drift
apart: the stored loadout format (`LoadoutData.cs`), the rule for merging two copies of a
player's loadouts (`LoadoutSync.cs`), the join ticket (`GameTicket.cs`), the playlists
matchmaking fills (`Playlists.cs`), and the progression rules and report formats
(`Progression.cs`). They live under
`game/Assets/_Project/Shared/`.
