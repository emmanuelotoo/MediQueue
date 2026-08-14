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

The API serves the Blazor client, creates a SQLite database on first run, and seeds it with demo departments, staff, and a day's worth of traffic so the analytics dashboard has something to show.

## Tests

```bash
dotnet test MediQueue.slnx
```

## Layout

| Project | Responsibility |
| --- | --- |
| `src/MediQueue.Domain` | Entities and the queue rules. No framework dependencies. |
| `src/MediQueue.Shared` | DTOs and real-time contract constants, shared by API and client. |
| `src/MediQueue.Infrastructure` | EF Core, Identity, migrations, demo data. |
| `src/MediQueue.Api` | REST endpoints, SignalR hub, authentication. |
| `src/MediQueue.Client` | Blazor WebAssembly UI. |

The design specification is in [`docs/superpowers/specs`](docs/superpowers/specs).

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
