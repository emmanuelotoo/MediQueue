# MediQueue — Design Specification

**Date:** 2026-08-14
**Status:** Approved
**Source:** `MediQueue_Project_Proposal_2.docx`

## Problem

Outpatient departments in Ghanaian hospitals suffer from disorganized queuing, long wait times, and poor communication between patients and clinical staff. Patients have no visibility into their wait; staff have no real-time view of patient load. MediQueue digitalizes outpatient registration and queue management to close that gap.

**Target users:** outpatients, hospital reception/administrative staff, and clinical staff (nurses, doctors) at public and private facilities.

## Scope

Four capabilities from the proposal, all in scope:

1. **Self-check-in and live queue tracking** — patients join the queue from a kiosk or their phone browser, receive a ticket code, and see their live position and estimated wait.
2. **Queue management console** — receptionists assign patients to departments, adjust priority, call patients to consultation rooms, and transfer between departments.
3. **Clinical staff workstation** — clinicians see their department's incoming list, start and complete consultations, and flag emergencies, which push instantly to reception.
4. **Analytics and reporting** — daily and weekly patient volume, average wait time, and department throughput.

**Out of scope:** SMS/Web Push notifications, appointment scheduling (this is walk-in outpatient), EMR/clinical notes, billing, multi-facility tenancy, Azure infrastructure-as-code.

## Technology

Per the proposal: Blazor WebAssembly (UI), ASP.NET Core Web API, SignalR, Entity Framework Core, SQL Server. Built on .NET 10 (LTS).

EF Core uses a provider switch: SQLite for local development so the project runs with zero setup, SQL Server for deployment. Authentication is ASP.NET Core Identity with JWT bearer tokens and role claims; patients are never required to hold an account.

## Architecture

```
src/
  MediQueue.Domain/          entities, enums, QueueEngine (pure logic), abstractions — zero framework deps
  MediQueue.Shared/          DTOs + SignalR contract constants — referenced by Api AND Client
  MediQueue.Infrastructure/  EF Core DbContext, Identity, repositories, migrations, seeding
  MediQueue.Api/             controllers, QueueHub, JWT auth, DI; hosts the WASM client
  MediQueue.Client/          Blazor WebAssembly UI
tests/
  MediQueue.Domain.Tests/    queue rules — the TDD core
  MediQueue.Api.Tests/       endpoint + authorization tests against SQLite in-memory
  MediQueue.Client.Tests/    bUnit component tests
```

References flow one way: `Domain ← Infrastructure ← Api → Shared ← Client`. The domain never references EF Core or ASP.NET, which keeps the queue rules testable in isolation and fast.

### Write path

All mutations go through REST endpoints. The SignalR hub is read-only from the client's perspective: the service layer writes, then broadcasts. Authorization therefore lives in exactly one place, and the hub cannot become a second, weaker way in.

## Domain model

| Entity | Purpose |
| --- | --- |
| `Patient` | Identity of the person queuing: medical record number, name, phone, date of birth, gender |
| `Department` | Outpatient department: name, short code, consultation room count, default service minutes |
| `QueueTicket` | One visit in the queue: code, patient, department, priority, status, and the timestamps for each transition |
| `ApplicationUser` | Identity user for staff, with full name, department, and role |
| `VisitEvent` | Append-only record of everything that happened to a ticket |

`Priority`: `Normal | Priority | Emergency`.
`TicketStatus`: `Waiting | Called | InConsultation | Completed | NoShow | Cancelled`.

`VisitEvent` serves as both audit trail and analytics source. Analytics reads an append-only log rather than reconstructing history from mutable ticket rows, and the security role gets a real audit artifact.

### Queue rules

Implemented in `QueueEngine`, pure and `IClock`-injected so time-dependent behaviour is deterministic under test.

- **Ticket codes** — `{DeptCode}-{seq:D3}`, sequence per department per day, resets at midnight.
- **Ordering** — Emergency band first, then Priority, then Normal; FIFO by check-in time within each band. Only `Waiting` tickets are in the ordered set.
- **Position** — 1-based index in that department's ordered waiting list.
- **Estimated wait** — position multiplied by the mean service duration for that department today, computed from completed tickets. Falls back to the department's configured default when fewer than three have completed.
- **Transitions** — `Waiting → Called → InConsultation → Completed`, plus `Called → NoShow`, `Waiting → Cancelled`, and `NoShow → Waiting`. A re-queued no-show re-enters at the head of its priority band, keeping its original check-in time. Any other transition throws `InvalidTicketTransitionException`.
- **Room exclusivity** — a consultation room cannot hold two tickets in `Called` or `InConsultation` at once.

## Real-time layer

`QueueHub` at `/hubs/queue`, with three group kinds:

- `dept-{departmentId}` — staff consoles and waiting-room boards
- `ticket-{ticketCode}` — one patient's own device
- `reception` — all receptionists, for emergency escalation

Server-to-client messages are named by constants in `MediQueue.Shared`, so the client and server cannot drift: `QueueUpdated`, `TicketCalled`, `PositionChanged`, `EmergencyFlagged`.

Patient devices connect anonymously and may only join the group for a ticket code they present; the hub verifies the code exists before adding the connection.

## API surface

| Method | Route | Access |
| --- | --- | --- |
| POST | `/api/checkin` | anonymous |
| GET | `/api/tickets/{code}` | anonymous |
| GET | `/api/board/{departmentId}` | anonymous |
| GET | `/api/departments` | anonymous |
| GET | `/api/queue/{departmentId}` | Receptionist, Clinician |
| POST | `/api/tickets/{id}/{action}` | role-gated per action |
| GET | `/api/analytics/*` | Admin |
| POST | `/api/auth/login`, `/api/auth/refresh` | anonymous |

Validation attributes live on the Shared DTOs so client and server enforce the same rules. Errors are returned as RFC 7807 `ProblemDetails`.

## Frontend

Routes: `/`, `/checkin`, `/ticket/{code}`, `/board/{dept}`, `/reception`, `/clinic`, `/analytics`, `/login`.

**Direction: clinical calm, kiosk-first.** Deep teal over a slate-neutral base, amber for Priority, restrained red for Emergency. Semantic design tokens are defined once and redefined for dark mode, because waiting-room displays are often dimmed.

Density scales by audience. Kiosk and patient views present one decision per screen with large touch targets and a ticket numeral sized to be read across a room. Staff consoles are dense, keyboard-first, with row actions always visible.

Queue figures use tabular numerals so positions and times do not jitter as they tick. State changes animate position rather than opacity, so a patient advancing in the queue visibly moves; this respects `prefers-reduced-motion`. Contrast meets WCAG AA, and the emergency state is never signalled by colour alone.

A single `QueueHubClient` service wraps the SignalR connection with automatic reconnect and exposes typed events, so no component touches `HubConnection` directly.

## Testing

Test-driven for the domain and API:

- **Domain tests** cover every queue rule listed above. No I/O.
- **API tests** use `WebApplicationFactory` against SQLite in-memory: check-in creates a ticket, role gates reject the wrong role, invalid transitions return 409, analytics aggregates correctly.
- **Client tests** use bUnit on the check-in wizard and the queue table.

## Deployment

Runs locally with `dotnet run` against a seeded SQLite database. A multi-stage Dockerfile and a Compose file with SQL Server are included, along with a GitHub Actions workflow that restores, builds, and tests.
