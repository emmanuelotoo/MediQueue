using MediQueue.Api.Hubs;
using MediQueue.Domain.Abstractions;
using MediQueue.Domain.Entities;
using MediQueue.Domain.Enums;
using MediQueue.Domain.Queues;
using MediQueue.Infrastructure.Persistence;
using MediQueue.Shared.Contracts;
using MediQueue.Shared.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace MediQueue.Api.Services;

public class QueueService : IQueueService
{
    private readonly MediQueueDbContext _db;
    private readonly QueueEngine _engine;
    private readonly IClock _clock;
    private readonly IHubContext<QueueHub> _hub;
    private readonly ILogger<QueueService> _logger;

    private Dictionary<string, string>? _staffNames;

    public QueueService(
        MediQueueDbContext db,
        QueueEngine engine,
        IClock clock,
        IHubContext<QueueHub> hub,
        ILogger<QueueService> logger)
    {
        _db = db;
        _engine = engine;
        _clock = clock;
        _hub = hub;
        _logger = logger;
    }

    private DateTimeOffset TodayStart => new(_clock.Now.UtcDateTime.Date, TimeSpan.Zero);

    public async Task<IReadOnlyList<DepartmentDto>> GetDepartmentsAsync(CancellationToken ct = default)
    {
        var departments = await _db.Departments
            .AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .ToListAsync(ct);

        var todayStart = TodayStart;

        var todaysTickets = await _db.Tickets
            .AsNoTracking()
            .Where(t => t.CheckedInAt >= todayStart)
            .ToListAsync(ct);

        return departments.Select(d =>
        {
            var mine = todaysTickets.Where(t => t.DepartmentId == d.Id).ToList();
            var waiting = _engine.OrderWaiting(mine);

            // What the next person to arrive would be told.
            var wait = waiting.Count == 0
                ? TimeSpan.Zero
                : EstimateForPosition(mine, waiting.Count + 1, d.DefaultServiceMinutes);

            return new DepartmentDto
            {
                Id = d.Id,
                Name = d.Name,
                Code = d.Code,
                ConsultationRooms = d.ConsultationRooms,
                WaitingCount = waiting.Count,
                EstimatedWaitMinutes = (int)wait.TotalMinutes
            };
        }).ToList();
    }

    public async Task<CheckInResponse> CheckInAsync(
        CheckInRequest request,
        string? actorUserId,
        CancellationToken ct = default)
    {
        var department = await _db.Departments
            .FirstOrDefaultAsync(d => d.Id == request.DepartmentId && d.IsActive, ct)
            ?? throw new KeyNotFoundException($"Department {request.DepartmentId} was not found.");

        var patient = await FindOrCreatePatientAsync(request, ct);
        var dayTickets = await LoadDayAsync(department.Id, ct);

        var ticket = new QueueTicket
        {
            TicketCode = _engine.NextTicketCode(department.Code, dayTickets),
            PatientId = patient.Id,
            Patient = patient,
            DepartmentId = department.Id,
            Priority = request.Priority,
            Status = TicketStatus.Waiting,
            CheckedInAt = _clock.Now
        };

        _db.Tickets.Add(ticket);
        RecordEvent(ticket, VisitEventType.CheckedIn, actorUserId);
        await _db.SaveChangesAsync(ct);

        dayTickets.Add(ticket);

        var position = _engine.PositionOf(ticket, dayTickets) ?? 1;
        var estimate = _engine.EstimateWait(ticket, dayTickets, department.DefaultServiceMinutes);

        await BroadcastAsync(department.Id, ct);

        _logger.LogInformation(
            "{TicketCode} checked in to {Department} at position {Position}.",
            ticket.TicketCode,
            department.Name,
            position);

        return new CheckInResponse
        {
            TicketId = ticket.Id,
            TicketCode = ticket.TicketCode,
            PatientName = patient.FullName,
            MedicalRecordNumber = patient.MedicalRecordNumber,
            DepartmentId = department.Id,
            DepartmentName = department.Name,
            Position = position,
            EstimatedWaitMinutes = estimate is null ? null : (int)estimate.Value.TotalMinutes,
            CheckedInAt = ticket.CheckedInAt
        };
    }

    public async Task<TicketStatusDto?> GetTicketStatusAsync(string ticketCode, CancellationToken ct = default)
    {
        var normalised = ticketCode.ToUpperInvariant();

        var ticket = await _db.Tickets
            .AsNoTracking()
            .Include(t => t.Patient)
            .Include(t => t.Department)
            .Where(t => t.TicketCode == normalised)
            .OrderByDescending(t => t.CheckedInAt)
            .FirstOrDefaultAsync(ct);

        if (ticket?.Department is null)
        {
            return null;
        }

        var dayTickets = await LoadDayAsync(ticket.DepartmentId, ct);

        return QueueMapping.ToStatus(
            ticket,
            ticket.Department,
            _engine.PositionOf(ticket, dayTickets),
            _engine.EstimateWait(ticket, dayTickets, ticket.Department.DefaultServiceMinutes));
    }

    public async Task<DepartmentQueueDto> GetDepartmentQueueAsync(int departmentId, CancellationToken ct = default)
    {
        var department = await _db.Departments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == departmentId, ct)
            ?? throw new KeyNotFoundException($"Department {departmentId} was not found.");

        var dayTickets = await LoadDayAsync(departmentId, ct);
        var staffNames = await StaffNamesAsync(ct);
        var now = _clock.Now;

        var waiting = _engine.OrderWaiting(dayTickets)
            .Select((t, index) => QueueMapping.ToEntry(t, now, index + 1, staffNames))
            .ToList();

        var inProgress = dayTickets
            .Where(QueueMapping.IsInProgress)
            .OrderByDescending(t => t.CalledAt)
            .Select(t => QueueMapping.ToEntry(t, now, null, staffNames))
            .ToList();

        var completed = dayTickets.Where(t => t.Status == TicketStatus.Completed).ToList();

        var averageWait = completed
            .Select(t => t.WaitDuration)
            .OfType<TimeSpan>()
            .ToList();

        return new DepartmentQueueDto
        {
            DepartmentId = department.Id,
            DepartmentName = department.Name,
            DepartmentCode = department.Code,
            Waiting = waiting,
            InProgress = inProgress,
            CompletedToday = completed.Count,
            AverageWaitMinutes = averageWait.Count == 0
                ? null
                : (int)averageWait.Average(w => w.TotalMinutes),
            GeneratedAt = now
        };
    }

    public async Task<BoardDto> GetBoardAsync(int departmentId, CancellationToken ct = default)
    {
        var department = await _db.Departments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == departmentId, ct)
            ?? throw new KeyNotFoundException($"Department {departmentId} was not found.");

        var dayTickets = await LoadDayAsync(departmentId, ct);

        var nowServing = dayTickets
            .Where(QueueMapping.IsInProgress)
            .OrderBy(t => t.RoomNumber)
            .Select(t => new BoardEntryDto
            {
                TicketCode = t.TicketCode,
                RoomNumber = t.RoomNumber ?? "-"
            })
            .ToList();

        var waiting = _engine.OrderWaiting(dayTickets);

        return new BoardDto
        {
            DepartmentId = department.Id,
            DepartmentName = department.Name,
            NowServing = nowServing,
            // Codes only: the board hangs in a public waiting room.
            UpNext = waiting.Take(6).Select(t => t.TicketCode).ToList(),
            WaitingCount = waiting.Count,
            GeneratedAt = _clock.Now
        };
    }

    public Task CallAsync(Guid ticketId, string room, string actorUserId, CancellationToken ct = default) =>
        MutateAsync(ticketId, actorUserId, VisitEventType.Called, $"Room {room}", (ticket, dayTickets) =>
            _engine.Call(ticket, room, actorUserId, dayTickets), ct);

    public Task StartConsultationAsync(Guid ticketId, string actorUserId, CancellationToken ct = default) =>
        MutateAsync(ticketId, actorUserId, VisitEventType.ConsultationStarted, null,
            (ticket, _) => _engine.StartConsultation(ticket), ct);

    public Task CompleteAsync(Guid ticketId, string actorUserId, CancellationToken ct = default) =>
        MutateAsync(ticketId, actorUserId, VisitEventType.Completed, null,
            (ticket, _) => _engine.Complete(ticket), ct);

    public Task MarkNoShowAsync(Guid ticketId, string actorUserId, CancellationToken ct = default) =>
        MutateAsync(ticketId, actorUserId, VisitEventType.MarkedNoShow, null,
            (ticket, _) => _engine.MarkNoShow(ticket), ct);

    public Task RequeueAsync(Guid ticketId, string actorUserId, CancellationToken ct = default) =>
        MutateAsync(ticketId, actorUserId, VisitEventType.Requeued, null,
            (ticket, _) => _engine.Requeue(ticket), ct);

    public Task CancelAsync(Guid ticketId, string actorUserId, CancellationToken ct = default) =>
        MutateAsync(ticketId, actorUserId, VisitEventType.Cancelled, null,
            (ticket, _) => _engine.Cancel(ticket), ct);

    public Task ChangePriorityAsync(
        Guid ticketId,
        TicketPriority priority,
        string actorUserId,
        CancellationToken ct = default) =>
        MutateAsync(ticketId, actorUserId, VisitEventType.PriorityChanged, priority.ToString(),
            (ticket, _) => _engine.ChangePriority(ticket, priority), ct);

    public async Task TransferAsync(
        Guid ticketId,
        int toDepartmentId,
        string actorUserId,
        CancellationToken ct = default)
    {
        var ticket = await LoadTicketAsync(ticketId, ct);
        var from = ticket.DepartmentId;

        var target = await _db.Departments.FirstOrDefaultAsync(d => d.Id == toDepartmentId && d.IsActive, ct)
            ?? throw new KeyNotFoundException($"Department {toDepartmentId} was not found.");

        var targetDayTickets = await LoadDayAsync(toDepartmentId, ct);
        var newCode = _engine.NextTicketCode(target.Code, targetDayTickets);

        _engine.Transfer(ticket, toDepartmentId, newCode);
        RecordEvent(ticket, VisitEventType.Transferred, actorUserId, $"To {target.Name}");
        await _db.SaveChangesAsync(ct);

        // Both floors changed: one patient left, one arrived.
        await BroadcastAsync(from, ct);
        await BroadcastAsync(toDepartmentId, ct);
        await NotifyTicketAsync(ticket, ct);
    }

    /// <summary>
    /// The shared shape of every state change: load, apply the rule, record it,
    /// commit, then tell everyone watching.
    /// </summary>
    private async Task MutateAsync(
        Guid ticketId,
        string actorUserId,
        VisitEventType eventType,
        string? metadata,
        Action<QueueTicket, List<QueueTicket>> apply,
        CancellationToken ct)
    {
        var ticket = await LoadTicketAsync(ticketId, ct);
        var dayTickets = await LoadDayAsync(ticket.DepartmentId, ct);

        // The tracked instance must be the one the engine inspects, or a rule
        // like room exclusivity would compare a ticket against a stale copy.
        var index = dayTickets.FindIndex(t => t.Id == ticket.Id);
        if (index >= 0)
        {
            dayTickets[index] = ticket;
        }
        else
        {
            dayTickets.Add(ticket);
        }

        apply(ticket, dayTickets);
        RecordEvent(ticket, eventType, actorUserId, metadata);
        await _db.SaveChangesAsync(ct);

        await BroadcastAsync(ticket.DepartmentId, ct);
        await NotifyTicketAsync(ticket, ct);

        if (eventType == VisitEventType.Called)
        {
            await NotifyCalledAsync(ticket, ct);
        }

        if (eventType == VisitEventType.PriorityChanged && ticket.Priority == TicketPriority.Emergency)
        {
            await NotifyEmergencyAsync(ticket, actorUserId, ct);
        }
    }

    private async Task<QueueTicket> LoadTicketAsync(Guid ticketId, CancellationToken ct) =>
        await _db.Tickets
            .Include(t => t.Patient)
            .Include(t => t.Department)
            .FirstOrDefaultAsync(t => t.Id == ticketId, ct)
        ?? throw new KeyNotFoundException($"Ticket {ticketId} was not found.");

    /// <summary>
    /// Today's tickets for one department: everything the engine needs to order
    /// the queue, estimate waits from the day's own pace, and check rooms.
    /// </summary>
    private async Task<List<QueueTicket>> LoadDayAsync(int departmentId, CancellationToken ct)
    {
        var todayStart = TodayStart;

        return await _db.Tickets
            .Include(t => t.Patient)
            .Where(t => t.DepartmentId == departmentId && t.CheckedInAt >= todayStart)
            .ToListAsync(ct);
    }

    private async Task<Patient> FindOrCreatePatientAsync(CheckInRequest request, CancellationToken ct)
    {
        var name = request.FullName.Trim();
        var phone = request.PhoneNumber.Trim();

        if (!string.IsNullOrWhiteSpace(request.MedicalRecordNumber))
        {
            var mrn = request.MedicalRecordNumber.Trim();
            var byRecord = await _db.Patients.FirstOrDefaultAsync(p => p.MedicalRecordNumber == mrn, ct);
            if (byRecord is not null)
            {
                return byRecord;
            }
        }

        // A returning patient who has forgotten their folder number is matched on
        // phone plus name, which is specific enough without an ID document.
        var existing = await _db.Patients
            .FirstOrDefaultAsync(p => p.PhoneNumber == phone && p.FullName == name, ct);

        if (existing is not null)
        {
            return existing;
        }

        var patient = new Patient
        {
            MedicalRecordNumber = await NextMedicalRecordNumberAsync(ct),
            FullName = name,
            PhoneNumber = phone,
            DateOfBirth = request.DateOfBirth,
            Gender = request.Gender,
            CreatedAt = _clock.Now
        };

        _db.Patients.Add(patient);
        return patient;
    }

    private async Task<string> NextMedicalRecordNumberAsync(CancellationToken ct)
    {
        var count = await _db.Patients.CountAsync(ct);

        // Skip past any number already taken, which can happen after seeded data.
        for (var candidate = count + 1; ; candidate++)
        {
            var mrn = $"MRN-{candidate:D5}";
            if (!await _db.Patients.AnyAsync(p => p.MedicalRecordNumber == mrn, ct))
            {
                return mrn;
            }
        }
    }

    private TimeSpan EstimateForPosition(
        IReadOnlyCollection<QueueTicket> dayTickets,
        int position,
        int defaultServiceMinutes)
    {
        var probe = new QueueTicket
        {
            DepartmentId = dayTickets.FirstOrDefault()?.DepartmentId ?? 0,
            Status = TicketStatus.Waiting,
            Priority = TicketPriority.Normal,
            CheckedInAt = _clock.Now.AddYears(1)
        };

        var withProbe = dayTickets.Append(probe).ToList();
        var estimate = _engine.EstimateWait(probe, withProbe, defaultServiceMinutes);

        // The probe sits last, so its estimate is what an arriving patient sees.
        return estimate ?? TimeSpan.FromMinutes(defaultServiceMinutes * position);
    }

    private void RecordEvent(QueueTicket ticket, VisitEventType type, string? actorUserId, string? metadata = null) =>
        _db.VisitEvents.Add(new VisitEvent
        {
            TicketId = ticket.Id,
            EventType = type,
            OccurredAt = _clock.Now,
            ActorUserId = actorUserId,
            Metadata = metadata
        });

    private async Task<Dictionary<string, string>> StaffNamesAsync(CancellationToken ct) =>
        _staffNames ??= await _db.Users
            .AsNoTracking()
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

    /// <summary>
    /// Pushes the department's new state to the staff console and the waiting-room
    /// board, and refreshes the position shown on every waiting patient's phone.
    /// </summary>
    private async Task BroadcastAsync(int departmentId, CancellationToken ct)
    {
        var queue = await GetDepartmentQueueAsync(departmentId, ct);
        await _hub.Clients.Group(QueueGroups.Department(departmentId))
            .SendAsync(QueueEvents.QueueUpdated, queue, ct);

        var board = await GetBoardAsync(departmentId, ct);
        await _hub.Clients.Group(QueueGroups.Board(departmentId))
            .SendAsync(QueueEvents.BoardUpdated, board, ct);

        // Everyone behind the change moved up, so each of them needs their own
        // number refreshed. Sent per ticket rather than broadcasting the whole
        // queue, which would leak other patients' names to anonymous devices.
        var dayTickets = await LoadDayAsync(departmentId, ct);
        var department = await _db.Departments.AsNoTracking()
            .FirstAsync(d => d.Id == departmentId, ct);

        foreach (var waiting in _engine.OrderWaiting(dayTickets))
        {
            var status = QueueMapping.ToStatus(
                waiting,
                department,
                _engine.PositionOf(waiting, dayTickets),
                _engine.EstimateWait(waiting, dayTickets, department.DefaultServiceMinutes));

            await _hub.Clients.Group(QueueGroups.Ticket(waiting.TicketCode))
                .SendAsync(QueueEvents.TicketUpdated, status, ct);
        }
    }

    private async Task NotifyTicketAsync(QueueTicket ticket, CancellationToken ct)
    {
        var status = await GetTicketStatusAsync(ticket.TicketCode, ct);
        if (status is not null)
        {
            await _hub.Clients.Group(QueueGroups.Ticket(ticket.TicketCode))
                .SendAsync(QueueEvents.TicketUpdated, status, ct);
        }
    }

    private async Task NotifyCalledAsync(QueueTicket ticket, CancellationToken ct) =>
        await _hub.Clients.Group(QueueGroups.Ticket(ticket.TicketCode))
            .SendAsync(QueueEvents.TicketCalled, new TicketCalledDto
            {
                TicketCode = ticket.TicketCode,
                PatientName = ticket.Patient?.FullName ?? string.Empty,
                RoomNumber = ticket.RoomNumber ?? string.Empty,
                DepartmentName = ticket.Department?.Name ?? string.Empty,
                CalledAt = ticket.CalledAt ?? _clock.Now
            }, ct);

    private async Task NotifyEmergencyAsync(QueueTicket ticket, string actorUserId, CancellationToken ct)
    {
        var staffNames = await StaffNamesAsync(ct);

        await _hub.Clients.Group(QueueGroups.Reception)
            .SendAsync(QueueEvents.EmergencyFlagged, new EmergencyAlertDto
            {
                TicketId = ticket.Id,
                TicketCode = ticket.TicketCode,
                PatientName = ticket.Patient?.FullName ?? string.Empty,
                DepartmentId = ticket.DepartmentId,
                DepartmentName = ticket.Department?.Name ?? string.Empty,
                RaisedBy = staffNames.GetValueOrDefault(actorUserId),
                RaisedAt = _clock.Now
            }, ct);

        _logger.LogWarning("{TicketCode} escalated to emergency.", ticket.TicketCode);
    }
}
