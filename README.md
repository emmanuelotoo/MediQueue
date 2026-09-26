# MediQueue

Digital outpatient registration and queue management for Ghanaian hospitals.

Patients check in from a kiosk or their own phone, watch their position advance in real time, and are called to a consultation room. Reception sees the whole floor at once. Clinicians work their department's list. Administrators get the numbers that show whether any of it is working.

## Stack

Blazor WebAssembly · ASP.NET Core Web API · SignalR · Entity Framework Core · PostgreSQL (SQLite in development) · .NET 10

## Running it

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/MediQueue.Api
```

The API serves the Blazor client, creates a SQLite database on first run, and seeds it with a fortnight of history plus a live queue, so every screen has something on it immediately.

To start over with fresh data, delete `src/MediQueue.Api/mediqueue.db` and run again.

### Signing in

Locally, every seeded account uses the password `MediQueue#2026`. It is published here, so a deployed site refuses it: deployments must set their own through `Seed:StaffPassword`.

| Role | Example account | Lands on |
| --- | --- | --- |
| Receptionist | `emmanuel.otoo@mediqueue.gh` | `/reception` |
| Clinician | `nii.anang@mediqueue.gh` | `/clinic` |
| Admin | `franklin.asare@mediqueue.gh` | `/analytics` |

Patients never sign in. Their ticket code is their credential.

## Demo script

Two browser windows side by side shows the whole system in about a minute.

1. **Window A** — open `/checkin`. Pick a department, enter a name and a phone number, join the queue. Note the ticket code.
2. **Window B** — sign in as a receptionist and open `/reception`. The new patient is already there; nothing was refreshed.
3. **Window A** — open `/ticket/{code}`. It shows the position and estimated wait.
4. **Window B** — call the patient to a room. Window A flips to *It is your turn* immediately.
5. Open `/board/1` on a third screen — the waiting-room display, showing codes and rooms and no names.
6. Sign in as a clinician, start and complete the consultation, then look at `/analytics`.

## Tests

```bash
dotnet test MediQueue.slnx
```

| Suite | Covers |
| --- | --- |
| `MediQueue.Domain.Tests` | Queue rules: ordering, ticket numbering, wait estimates, legal transitions, room exclusivity |
| `MediQueue.Api.Tests` | Endpoints, authorization, the full consultation flow, analytics, deployment settings. In-memory SQLite by default; set `MEDIQUEUE_TEST_POSTGRES` to a Postgres server connection string to run the same suite on Postgres, as CI does. |
| `MediQueue.Client.Tests` | Blazor components, via bUnit |

## Layout

| Project | Responsibility |
| --- | --- |
| `src/MediQueue.Domain` | Entities and the queue rules. No framework dependencies. |
| `src/MediQueue.Shared` | DTOs, role names, and real-time contract constants, shared by API and client. |
| `src/MediQueue.Infrastructure` | EF Core, Identity, migrations, demo data. |
| `src/MediQueue.Api` | REST endpoints, SignalR hub, authentication. |
| `src/MediQueue.Client` | Blazor WebAssembly UI. |

References run one way — `Domain ← Infrastructure ← Api → Shared ← Client` — so the queue rules stay testable without a database or a web host.

Every change goes through `QueueService`, which applies the rule, records a `VisitEvent`, commits, and only then broadcasts. The hub is read-only from the client's side, so authorization lives in exactly one place.

The design specification is in [`docs/superpowers/specs`](docs/superpowers/specs).

## Configuration

| Setting | Default | Notes |
| --- | --- | --- |
| `Database:Provider` | `Sqlite` | `Postgres` in deployment. Any other value stops startup. |
| `ConnectionStrings:Default` | `Data Source=mediqueue.db` | Used unless `DATABASE_URL` is set |
| `DATABASE_URL` | — | Set by Heroku Postgres; takes precedence when the provider is Postgres |
| `Jwt:Key` | generated per run in Development | **Required** outside Development; startup fails without it |
| `Seed:DemoData` | `true` in Development only | Seeds the demo hospital on an empty database |
| `Seed:StaffPassword` | README password in Development only | **Required** when seeding outside Development. Must meet the password policy and must not be the README password. |
| `Hosting:TrustForwardedHeaders` | `false` | `true` behind Heroku's router, so HTTPS is recognised |

Environment variables use double underscores: `Seed__StaffPassword`.

## Deploying to Heroku

The app runs on one Basic dyno ($7/month) with Heroku Postgres Essential-0 ($5/month). The [GitHub Student Developer Pack](https://www.heroku.com/github-students/) gives $13/month of Heroku credit for 24 months, which covers both. Heroku requires a card on file even when credit covers the bill.

Everything below is done in the Heroku dashboard; no CLI is needed.

**1. Create the app.** [dashboard.heroku.com](https://dashboard.heroku.com) → **New** → **Create new app**. Choose a name and the **Europe** region, which is closer to Ghana than the United States.

**2. Add the database.** **Resources** → **Add-ons** → search *Heroku Postgres* → plan **Essential 0** → **Submit Order Form**. This sets `DATABASE_URL`.

**3. Set the config vars.** **Settings** → **Reveal Config Vars**, then add:

| Key | Value |
| --- | --- |
| `Database__Provider` | `Postgres` |
| `Jwt__Key` | a random value you generate (below) |
| `Seed__DemoData` | `true` |
| `Seed__StaffPassword` | a password you choose: 10+ characters, an uppercase letter, a digit and a symbol, and not the README password |
| `Hosting__TrustForwardedHeaders` | `true` |

Generate `Jwt__Key` on your own machine, so it never passes through chat or the repository:

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
```

```bash
openssl rand -base64 48
```

Share `Seed__StaffPassword` with the team privately. Every seeded staff account on the live site uses it.

**4. Connect GitHub.** **Deploy** → **Deployment method: GitHub** → connect → find `MediQueue` → **Connect**. Under **Automatic deploys**, choose `main`, tick **Wait for CI to pass before deploy**, then **Enable Automatic Deploys**.

**5. First deploy.** Under **Manual deploy**, choose `main` → **Deploy Branch**. The build log should show `Using configured solution file: MediQueue.Heroku.slnx`, the solution that contains only the API. The release log should end with `Seeded 6 departments, 11 staff, 180 patients.`

**6. Use a Basic dyno.** **Resources** → **Change Dyno Type** → **Basic**. Eco dynos sleep after 30 minutes idle, and the first visitor afterwards waits while it wakes. That's bad in a demo.

**7. Open the app.** Click **Open app**.

After that, every push to `main` deploys once CI passes. Migrations run in the release phase, before the new version takes traffic. A failed migration cancels the deploy and leaves the running version alone.

### When something goes wrong

**Activity** → the failed build or release → **View log**.

| Symptom | Cause |
| --- | --- |
| Release fails naming `Seed__StaffPassword` | The var is missing, too weak, or the README password |
| App crashes with `Jwt:Key is not configured` | `Jwt__Key` is missing |
| Startup fails with `Unknown Database:Provider` | `Database__Provider` is misspelt; it must be `Postgres` |
| Build fails with *Multiple .NET solution files* | `project.toml` was not picked up. Add config var `SOLUTION_FILE` = `MediQueue.Heroku.slnx` |

## Running with Docker

```bash
docker compose up --build
```

Needs a `.env` file with `POSTGRES_PASSWORD`, `JWT_KEY` and `SEED_STAFF_PASSWORD`; Compose refuses to start without them. The stack mirrors Heroku: Postgres and the seeded demo hospital, served on port 8080.

CI builds and tests on every push, once on SQLite and once against a real Postgres, and fails if a model change has no matching migration.

## Team

| Name | Role |
| --- | --- |
| Wiafe Franklin Asare | Project Manager |
| Abdul-Aziz Naeem | Backend Developer |
| Odoi Nii Anang | Frontend Developer |
| Derrick Debrah | Database Administrator |
| Moses Kwame Mensah | UI/UX Designer |
| Jeremiah Kwadwo Wiafe | Cloud & DevOps Engineer |
| Elsie Atsu | Security Analyst |
| Osman Umar Farouk | Backend Developer |
| Ivan Johnson | QA & Testing Engineer |
| Sangeerth Shyjith | Documentation & Research |
| Emmanuel Thisara Otoo | Frontend Developer |
