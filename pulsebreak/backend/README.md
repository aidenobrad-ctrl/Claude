# Pulsebreak backend

The accounts API: sign-up and sign-in, email verification, password reset, and the player data
that follows an account between computers (profile, loadouts, settings). It also hands out the
join tickets that tell a game server who is connecting.

ASP.NET Core on .NET 10, PostgreSQL for everything that lasts, Redis for sessions, codes and
counters.

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
| `GET /v1/settings`, `PUT /v1/settings` | yes | The player's settings; the newest save wins |
| `POST /v1/game/tickets` | yes | A join ticket for a game server. `mode` is `casual` or `ranked`; ranked needs a verified email |
| `GET /health`, `GET /health/ready` | | Alive; and able to reach the database and Redis |

"Signed in" means an `Authorization: Bearer <access token>` header.

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

## Changing the database

The schema is in `src/Pulsebreak.Api/Data/`. After changing it, add a migration:

```
dotnet tool restore
dotnet ef migrations add <Name> --project src/Pulsebreak.Api --output-dir Data/Migrations
```

## Shared with the game

Three files are the game's own source, compiled into the API as well, so the two cannot drift
apart: the stored loadout format (`LoadoutData.cs`), the rule for merging two copies of a
player's loadouts (`LoadoutSync.cs`), and the join ticket (`GameTicket.cs`). They live under
`game/Assets/_Project/Shared/`.
