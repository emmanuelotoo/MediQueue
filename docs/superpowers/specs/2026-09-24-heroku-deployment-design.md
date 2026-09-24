# MediQueue — Heroku Deployment Design

**Date:** 2026-09-24
**Status:** Approved
**Builds on:** [2026-08-14-mediqueue-design.md](2026-08-14-mediqueue-design.md)

## Why

MediQueue runs locally and passes CI, but has never been deployed, and its production database path is broken: the EF Core migrations were generated against SQLite and contain SQLite-only column types (`TEXT` as key columns) and annotations (`Sqlite:Autoincrement`). Applying them to any other provider fails. The `docker-compose.yml` and README describe that path as working; it was never run.

The goal is a public, always-on deployment the team can use for demos and assessment, with the production database path verified before it reaches the host.

## Decisions

| Question | Decision |
| --- | --- |
| Host | Heroku (native .NET support, GA June 2026) |
| Dyno | One Basic dyno ($7/mo). Eco sleeps after 30 minutes idle, which would stall a live demo. |
| Database | Heroku Postgres Essential-0 ($5/mo): 20 connections, 1 GB, SSL required |
| Providers | SQLite in development, Postgres in production. SQL Server is removed. |
| Live data | Seeded demo hospital, with the staff password taken from a config var rather than the public README |
| Deploys | Heroku GitHub integration: automatic on push to `main`, gated on CI passing |
| Cost | $12/mo, covered by the GitHub Student Developer Pack's $13/mo Heroku credit |

Dropping SQL Server diverges from the proposal's technology table. It was accepted because Heroku offers no SQL Server, the existing SQL Server path did not work, and keeping it would mean a third migration set that nothing deploys.

## Architecture on Heroku

```
Heroku app
├─ release phase   migrate schema → seed demo data (first deploy only) → exit
├─ web dyno        ASP.NET Core API + Blazor WASM static files + SignalR hub
└─ Postgres        Essential-0, reached via DATABASE_URL over SSL
```

**Release phase.** Migrations and seeding run in Heroku's release phase, before new dynos start. A failed migration aborts the release instead of leaving the web dyno in a crash loop, and first-boot seeding stays clear of Heroku's 60-second limit for binding the port. The web process still calls the same idempotent migrate-and-seed path at startup, which is a no-op once the release phase has run, so `dotnet run` locally behaves exactly as today.

**Single dyno.** SignalR needs no backplane and no session affinity. Heroku's router closes idle connections after 55 seconds; SignalR's default 15-second keep-alive stays inside that.

## Database providers and migrations

The provider is selected by `Database:Provider`, which accepts `Sqlite` (the default) or `Postgres`.

Each provider gets a `DbContext` subclass whose only job is to own a migration set:

```
Persistence/
  MediQueueDbContext.cs            model, configurations — everything injects this
  SqliteMediQueueDbContext.cs      owns Migrations/Sqlite
  PostgresMediQueueDbContext.cs    owns Migrations/Postgres
  Migrations/Sqlite/               existing InitialSchema, same migration ID
  Migrations/Postgres/             new InitialSchema generated against Npgsql
```

DI registers the provider's subclass as the `MediQueueDbContext` service, so nothing outside `Infrastructure` changes.

The existing SQLite migration moves folder but **keeps its migration ID**, so local databases already created by `dotnet run` continue to work without being reset.

The SQLite timestamp converter introduced earlier stays SQLite-only. Postgres stores `DateTimeOffset` natively as `timestamp with time zone`; Npgsql requires those values to be UTC, which they already are, because every timestamp comes from the UTC clock.

**Connection string.** Heroku supplies `DATABASE_URL` in URI form (`postgres://user:pass@host:port/db`). When the provider is Postgres and `DATABASE_URL` is present, it is translated into an Npgsql connection string with `SSL Mode=Require` and `Maximum Pool Size=10`, which leaves half of Essential-0's 20 connections free for `heroku pg:psql` and the release phase. `ConnectionStrings:Default` is used otherwise, so docker-compose and local Postgres keep working.

## Running behind Heroku's router

Heroku terminates TLS and forwards plain HTTP with `X-Forwarded-Proto` and `X-Forwarded-For`. Without honouring those headers the app believes every request is HTTP, so HSTS is never sent and `http://` requests are never redirected.

- Forwarded headers are honoured when `Hosting:TrustForwardedHeaders` is true, set on Heroku only. The router's addresses are not fixed, so known proxies are cleared; the default forward limit of one takes the address Heroku's router appended, not one a client supplied.
- The HTTPS redirect targets port 443 when behind the proxy.

**Procfile.** An explicit Procfile replaces Heroku's auto-detected process types, which it only generates when no Procfile exists. Both processes `cd` into the publish directory first, because ASP.NET Core resolves `appsettings.json` and the Blazor `wwwroot` relative to the working directory.

```
release: cd src/MediQueue.Api/bin/publish && ./MediQueue.Api --migrate-only
web:     cd src/MediQueue.Api/bin/publish && ./MediQueue.Api --urls http://*:$PORT
```

`--migrate-only` runs migration and seeding, then exits without starting the server.

## Secrets and demo data

| Config var | Purpose |
| --- | --- |
| `Database__Provider` | `Postgres` |
| `DATABASE_URL` | Set automatically by the Postgres add-on |
| `Jwt__Key` | Token signing key. Generated by the user locally; never passes through a chat or the repo. |
| `Seed__DemoData` | `true` |
| `Seed__StaffPassword` | Password for every seeded staff account on the live site |
| `Hosting__TrustForwardedHeaders` | `true` |

**Fail fast on the seed password.** When demo seeding is on outside Development, a missing `Seed:StaffPassword` stops the release phase with a clear message, and so does a password that fails the Identity password policy. Today a rejected password is logged and skipped, which would produce a live site nobody can sign in to. In Development, the README password remains the fallback.

The README's password is public because the repository is public, and after this change it does not work on the live site.

## Verification

Postgres is exercised before it reaches Heroku, so the provider mistake cannot recur.

1. **Local publish.** `dotnet publish -c Release` has never run. Heroku runs it, and warnings-as-errors combined with Blazor trimming may fail it. It is run and fixed locally first.
2. **CI against real Postgres.** The workflow gains a job that runs the API integration suite against a Postgres service container. The test factory selects its provider from an environment variable, and each test class gets its own throwaway database. The existing SQLite run is unchanged.
3. **Migration drift.** CI fails if either provider's model has changes not captured in a migration.
4. **Post-deploy smoke test.** Once live: check in on the public URL, see the patient arrive on the reception console without a refresh, call them and watch the ticket screen change, check the board, open reports, and confirm the README password is refused.

Local verification of Postgres itself is not possible on the development machine (no Docker, no Postgres install); CI is the gate.

## Division of work

**Implementation** covers all code, both migration sets, the Procfile, the CI job, the compose change, and README instructions precise enough to follow click by click.

**The user** does what requires their identity or credentials:

1. Create a Heroku account and claim the GitHub Student offer (a card is required on file).
2. Create the app and attach Heroku Postgres Essential-0.
3. Set the config vars, generating `Jwt__Key` locally.
4. Connect the GitHub repository, enable automatic deploys from `main`, and tick "wait for CI to pass".

## Out of scope

Custom domain, multiple dynos, a SignalR backplane, staff account management, database backups beyond Heroku's defaults, and log drains. The Dockerfile and compose file are updated for Postgres but remain untested, since Docker is not available on the development machine.
