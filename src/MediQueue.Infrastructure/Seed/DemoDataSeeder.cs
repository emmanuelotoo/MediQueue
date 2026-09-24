using MediQueue.Domain.Abstractions;
using MediQueue.Domain.Entities;
using MediQueue.Domain.Enums;
using MediQueue.Infrastructure.Identity;
using MediQueue.Infrastructure.Persistence;
using MediQueue.Shared.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediQueue.Infrastructure.Seed;

/// <summary>
/// Fills an empty database with a hospital that looks like it has been running
/// for a fortnight: past visits for the reports to aggregate, and a live queue
/// so every screen has something on it the moment the app starts.
/// </summary>
public static class DemoDataSeeder
{
    /// <summary>Fixed so every teammate's demo database is identical.</summary>
    private const int RandomSeed = 20260814;

    private const int HistoryDays = 14;

    /// <summary>Brings the schema up to date without adding any data.</summary>
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediQueueDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }

    /// <param name="staffPassword">
    /// Already validated by <see cref="DemoSeedPassword"/>; every seeded staff account gets it.
    /// </param>
    public static async Task SeedAsync(
        IServiceProvider services,
        string staffPassword,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<MediQueueDbContext>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DemoDataSeeder));

        if (await db.Departments.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Database already seeded; leaving it alone.");
            return;
        }

        logger.LogInformation("Seeding demo data.");

        var clock = sp.GetRequiredService<IClock>();
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();

        await SeedRolesAsync(roles);
        var departments = await SeedDepartmentsAsync(db, cancellationToken);
        var staff = await SeedStaffAsync(users, departments, staffPassword);
        var patients = await SeedPatientsAsync(db, clock, cancellationToken);

        await SeedVisitsAsync(db, clock, departments, staff, patients, cancellationToken);

        logger.LogInformation(
            "Seeded {Departments} departments, {Staff} staff, {Patients} patients.",
            departments.Count,
            staff.Count,
            patients.Count);
    }

    private static async Task SeedRolesAsync(RoleManager<IdentityRole> roles)
    {
        foreach (var role in Roles.All)
        {
            if (!await roles.RoleExistsAsync(role))
            {
                await roles.CreateAsync(new IdentityRole(role));
            }
        }
    }

    private static async Task<List<Department>> SeedDepartmentsAsync(
        MediQueueDbContext db,
        CancellationToken cancellationToken)
    {
        var departments = DemoData.Departments.ToList();
        db.Departments.AddRange(departments);
        await db.SaveChangesAsync(cancellationToken);
        return departments;
    }

    private static async Task<List<ApplicationUser>> SeedStaffAsync(
        UserManager<ApplicationUser> users,
        List<Department> departments,
        string staffPassword)
    {
        var created = new List<ApplicationUser>();

        foreach (var seed in DemoData.Staff)
        {
            var user = new ApplicationUser
            {
                UserName = seed.Email,
                Email = seed.Email,
                EmailConfirmed = true,
                FullName = seed.FullName,
                DepartmentId = seed.DepartmentCode is null
                    ? null
                    : departments.First(d => d.Code == seed.DepartmentCode).Id
            };

            var result = await users.CreateAsync(user, staffPassword);

            // A demo hospital missing some of its staff is broken, not partial:
            // stop rather than log and carry on.
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not create {seed.Email}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
            }

            await users.AddToRoleAsync(user, seed.Role);
            created.Add(user);
        }

        return created;
    }

    private static async Task<List<Patient>> SeedPatientsAsync(
        MediQueueDbContext db,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var random = new Random(RandomSeed);
        var patients = new List<Patient>();
        var seen = new HashSet<string>();

        for (var i = 1; i <= 180; i++)
        {
            string name;
            do
            {
                var first = DemoData.FirstNames[random.Next(DemoData.FirstNames.Length)];
                var last = DemoData.LastNames[random.Next(DemoData.LastNames.Length)];
                name = $"{first} {last}";
            }
            while (!seen.Add(name));

            patients.Add(new Patient
            {
                MedicalRecordNumber = $"MRN-{i:D5}",
                FullName = name,
                PhoneNumber = $"0{random.Next(24, 28)}{random.Next(1000000, 9999999)}",
                DateOfBirth = DateOnly.FromDateTime(
                    DateTime.UtcNow.AddYears(-random.Next(1, 82)).AddDays(-random.Next(0, 365))),
                Gender = DemoData.Genders[random.Next(DemoData.Genders.Length)],
                CreatedAt = clock.Now.AddDays(-random.Next(1, 900))
            });
        }

        db.Patients.AddRange(patients);
        await db.SaveChangesAsync(cancellationToken);
        return patients;
    }

    /// <summary>
    /// Builds a fortnight of resolved visits, then today's live queue.
    /// </summary>
    private static async Task SeedVisitsAsync(
        MediQueueDbContext db,
        IClock clock,
        List<Department> departments,
        List<ApplicationUser> staff,
        List<Patient> patients,
        CancellationToken cancellationToken)
    {
        var random = new Random(RandomSeed);
        var now = clock.Now;
        var tickets = new List<QueueTicket>();
        var events = new List<VisitEvent>();

        for (var daysAgo = HistoryDays; daysAgo >= 1; daysAgo--)
        {
            var day = now.AddDays(-daysAgo);

            // The outpatient department does not run on Sundays.
            if (day.DayOfWeek == DayOfWeek.Sunday)
            {
                continue;
            }

            foreach (var department in departments)
            {
                var arrivals = random.Next(7, 16);
                var sequence = 0;

                foreach (var arrival in ArrivalTimes(day, arrivals, random))
                {
                    sequence++;
                    var ticket = NewTicket(department, patients, random, arrival, sequence);
                    ResolvePastVisit(ticket, department, staff, random, events);
                    tickets.Add(ticket);
                }
            }
        }

        foreach (var department in departments)
        {
            tickets.AddRange(BuildTodaysQueue(department, patients, staff, random, now, events));
        }

        db.Tickets.AddRange(tickets);
        await db.SaveChangesAsync(cancellationToken);

        // Events reference tickets by id, which the tickets already carry.
        db.VisitEvents.AddRange(events);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Check-in times across a clinic day, weighted towards the morning because
    /// that is when Ghanaian outpatient departments actually fill up.
    /// </summary>
    private static IEnumerable<DateTimeOffset> ArrivalTimes(DateTimeOffset day, int count, Random random)
    {
        var opening = new DateTimeOffset(day.Year, day.Month, day.Day, 7, 0, 0, TimeSpan.Zero);

        return Enumerable.Range(0, count)
            .Select(_ =>
            {
                // Two draws, keeping the lower: a cheap skew towards opening time.
                var a = random.Next(0, 390);
                var b = random.Next(0, 390);
                return opening.AddMinutes(Math.Min(a, b));
            })
            .OrderBy(t => t)
            .ToList();
    }

    private static QueueTicket NewTicket(
        Department department,
        List<Patient> patients,
        Random random,
        DateTimeOffset arrival,
        int sequence)
    {
        var roll = random.Next(100);
        var priority = roll switch
        {
            < 4 => TicketPriority.Emergency,
            < 18 => TicketPriority.Priority,
            _ => TicketPriority.Normal
        };

        return new QueueTicket
        {
            TicketCode = $"{department.Code}-{sequence:D3}",
            PatientId = patients[random.Next(patients.Count)].Id,
            DepartmentId = department.Id,
            Priority = priority,
            Status = TicketStatus.Waiting,
            CheckedInAt = arrival
        };
    }

    /// <summary>Plays a past visit through to one of its terminal states.</summary>
    private static void ResolvePastVisit(
        QueueTicket ticket,
        Department department,
        List<ApplicationUser> staff,
        Random random,
        List<VisitEvent> events)
    {
        Record(events, ticket, VisitEventType.CheckedIn, ticket.CheckedInAt, null);

        var outcome = random.Next(100);

        if (outcome < 4)
        {
            ticket.Status = TicketStatus.Cancelled;
            Record(events, ticket, VisitEventType.Cancelled, ticket.CheckedInAt.AddMinutes(random.Next(15, 70)), null);
            return;
        }

        var clinician = ClinicianFor(department, staff, random);
        var waited = random.Next(8, department.DefaultServiceMinutes * 5);
        var calledAt = ticket.CheckedInAt.AddMinutes(waited);

        ticket.CalledAt = calledAt;
        ticket.RoomNumber = random.Next(1, department.ConsultationRooms + 1).ToString();
        ticket.AssignedStaffId = clinician?.Id;
        Record(events, ticket, VisitEventType.Called, calledAt, clinician?.Id, $"Room {ticket.RoomNumber}");

        if (outcome < 11)
        {
            ticket.Status = TicketStatus.NoShow;
            ticket.RoomNumber = null;
            Record(events, ticket, VisitEventType.MarkedNoShow, calledAt.AddMinutes(3), clinician?.Id);
            return;
        }

        var startedAt = calledAt.AddMinutes(random.Next(1, 4));
        var spread = Math.Max(4, department.DefaultServiceMinutes / 2);
        var duration = department.DefaultServiceMinutes + random.Next(-spread, spread + 1);
        var completedAt = startedAt.AddMinutes(Math.Max(3, duration));

        ticket.Status = TicketStatus.Completed;
        ticket.ConsultationStartedAt = startedAt;
        ticket.CompletedAt = completedAt;

        Record(events, ticket, VisitEventType.ConsultationStarted, startedAt, clinician?.Id);
        Record(events, ticket, VisitEventType.Completed, completedAt, clinician?.Id);
    }

    /// <summary>
    /// Today's queue: a few already seen, a couple in rooms now, the rest still
    /// waiting, all within the last three hours so the app looks busy whenever
    /// it is started.
    /// </summary>
    private static List<QueueTicket> BuildTodaysQueue(
        Department department,
        List<Patient> patients,
        List<ApplicationUser> staff,
        Random random,
        DateTimeOffset now,
        List<VisitEvent> events)
    {
        var tickets = new List<QueueTicket>();
        var arrivals = random.Next(9, 15);
        var windowStart = now.AddHours(-3);

        var times = Enumerable.Range(0, arrivals)
            .Select(i => windowStart.AddMinutes(i * (180.0 / arrivals) + random.Next(0, 6)))
            .OrderBy(t => t)
            .ToList();

        var clinician = ClinicianFor(department, staff, random);
        var roomsInUse = 0;

        for (var i = 0; i < times.Count; i++)
        {
            var ticket = NewTicket(department, patients, random, times[i], i + 1);
            Record(events, ticket, VisitEventType.CheckedIn, ticket.CheckedInAt, null);

            var share = (double)i / times.Count;

            if (share < 0.45)
            {
                // Long enough ago to have been seen and sent home.
                var calledAt = ticket.CheckedInAt.AddMinutes(random.Next(5, 30));
                var startedAt = calledAt.AddMinutes(random.Next(1, 4));
                var completedAt = startedAt.AddMinutes(
                    Math.Max(4, department.DefaultServiceMinutes + random.Next(-5, 6)));

                if (completedAt < now)
                {
                    ticket.Status = TicketStatus.Completed;
                    ticket.CalledAt = calledAt;
                    ticket.ConsultationStartedAt = startedAt;
                    ticket.CompletedAt = completedAt;
                    ticket.RoomNumber = null;
                    ticket.AssignedStaffId = clinician?.Id;

                    Record(events, ticket, VisitEventType.Called, calledAt, clinician?.Id);
                    Record(events, ticket, VisitEventType.ConsultationStarted, startedAt, clinician?.Id);
                    Record(events, ticket, VisitEventType.Completed, completedAt, clinician?.Id);
                    tickets.Add(ticket);
                    continue;
                }
            }

            if (share < 0.65 && roomsInUse < department.ConsultationRooms)
            {
                roomsInUse++;
                var calledAt = now.AddMinutes(-random.Next(2, 12));

                ticket.Status = roomsInUse % 2 == 0 ? TicketStatus.Called : TicketStatus.InConsultation;
                ticket.CalledAt = calledAt;
                ticket.RoomNumber = roomsInUse.ToString();
                ticket.AssignedStaffId = clinician?.Id;
                Record(events, ticket, VisitEventType.Called, calledAt, clinician?.Id, $"Room {roomsInUse}");

                if (ticket.Status == TicketStatus.InConsultation)
                {
                    ticket.ConsultationStartedAt = calledAt.AddMinutes(2);
                    Record(events, ticket, VisitEventType.ConsultationStarted, ticket.ConsultationStartedAt.Value, clinician?.Id);
                }

                tickets.Add(ticket);
                continue;
            }

            tickets.Add(ticket);
        }

        return tickets;
    }

    private static ApplicationUser? ClinicianFor(Department department, List<ApplicationUser> staff, Random random)
    {
        var candidates = staff.Where(s => s.DepartmentId == department.Id).ToList();
        return candidates.Count == 0 ? null : candidates[random.Next(candidates.Count)];
    }

    private static void Record(
        List<VisitEvent> events,
        QueueTicket ticket,
        VisitEventType type,
        DateTimeOffset at,
        string? actorUserId,
        string? metadata = null) =>
        events.Add(new VisitEvent
        {
            TicketId = ticket.Id,
            EventType = type,
            OccurredAt = at,
            ActorUserId = actorUserId,
            Metadata = metadata
        });
}
