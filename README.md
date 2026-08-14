# MediQueue

Digital outpatient registration and queue management for Ghanaian hospitals.

Patients check in from a kiosk or their own phone, watch their position advance in real time, and are called to a consultation room. Reception sees the whole floor at once. Clinicians work their department's list. Administrators get the numbers that show whether any of it is working.

## Stack

Blazor WebAssembly · ASP.NET Core Web API · SignalR · Entity Framework Core · SQL Server (SQLite in development) · .NET 10

## Running it

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/MediQueue.Api
```

The API serves the Blazor client, creates a SQLite database on first run, and seeds it with a fortnight of history plus a live queue, so every screen has something on it immediately.

To start over with fresh data, delete `src/MediQueue.Api/mediqueue.db` and run again.

### Signing in

Every seeded account uses the password `MediQueue#2026`. Development only — the seeder never runs outside Development unless `Seed:DemoData` is explicitly set.

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
| `MediQueue.Api.Tests` | Endpoints, authorization, the full consultation flow, analytics — against in-memory SQLite |
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
| `Database:Provider` | `Sqlite` | `SqlServer` for deployment |
| `ConnectionStrings:Default` | `Data Source=mediqueue.db` | |
| `Jwt:Key` | generated per run in Development | **Required** outside Development; startup fails without it |
| `Seed:DemoData` | `true` in Development only | Demo patients must never reach a real deployment |

## Deploying

```bash
docker compose up --build
```

Needs a `.env` file with `MSSQL_SA_PASSWORD` and `JWT_KEY`; Compose refuses to start without them. The API runs unprivileged and applies migrations on startup.

CI runs restore, build, and the full test suite on every push and pull request.

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
