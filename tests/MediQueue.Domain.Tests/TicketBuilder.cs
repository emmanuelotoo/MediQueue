using MediQueue.Domain.Entities;
using MediQueue.Domain.Enums;

namespace MediQueue.Domain.Tests;

/// <summary>
/// Builds tickets for tests. Keeps the arrange step of each test to the one or
/// two fields that test actually cares about.
/// </summary>
public sealed class TicketBuilder
{
    private readonly QueueTicket _ticket = new()
    {
        TicketCode = "GEN-001",
        DepartmentId = 1,
        PatientId = Guid.NewGuid(),
        Priority = TicketPriority.Normal,
        Status = TicketStatus.Waiting,
        CheckedInAt = new DateTimeOffset(2026, 8, 14, 8, 0, 0, TimeSpan.Zero)
    };

    public static TicketBuilder A() => new();

    public TicketBuilder Code(string code)
    {
        _ticket.TicketCode = code;
        return this;
    }

    public TicketBuilder InDepartment(int departmentId)
    {
        _ticket.DepartmentId = departmentId;
        return this;
    }

    public TicketBuilder WithPriority(TicketPriority priority)
    {
        _ticket.Priority = priority;
        return this;
    }

    public TicketBuilder WithStatus(TicketStatus status)
    {
        _ticket.Status = status;
        return this;
    }

    public TicketBuilder CheckedInAt(DateTimeOffset at)
    {
        _ticket.CheckedInAt = at;
        return this;
    }

    /// <summary>Checked in this many minutes past 08:00 on the test day.</summary>
    public TicketBuilder CheckedInAtMinute(int minute)
    {
        _ticket.CheckedInAt = new DateTimeOffset(2026, 8, 14, 8, 0, 0, TimeSpan.Zero).AddMinutes(minute);
        return this;
    }

    public TicketBuilder InRoom(string room)
    {
        _ticket.RoomNumber = room;
        return this;
    }

    public TicketBuilder Served(DateTimeOffset started, DateTimeOffset completed)
    {
        _ticket.Status = TicketStatus.Completed;
        _ticket.CalledAt = started;
        _ticket.ConsultationStartedAt = started;
        _ticket.CompletedAt = completed;
        return this;
    }

    public QueueTicket Build() => _ticket;

    public static implicit operator QueueTicket(TicketBuilder builder) => builder.Build();
}
