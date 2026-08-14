using MediQueue.Domain.Abstractions;
using MediQueue.Domain.Entities;
using MediQueue.Domain.Enums;

namespace MediQueue.Domain.Queues;

/// <summary>
/// Every rule about who is served next, how long they will wait, and which
/// state changes are legal. Deliberately free of persistence and framework
/// concerns: callers hand in the tickets to consider, already scoped to a
/// department, and the engine reasons over them in memory.
/// </summary>
public sealed class QueueEngine
{
    /// <summary>
    /// Consultations that must have finished today before the measured average
    /// is trusted over the department's configured default.
    /// </summary>
    private const int MinimumSamplesForAverage = 3;

    private readonly IClock _clock;

    public QueueEngine(IClock clock) => _clock = clock;

    /// <summary>
    /// Next code for a department, e.g. <c>CAR-014</c>. Numbering runs per
    /// department per day and restarts each morning. Codes are never reused,
    /// even by patients who cancelled, so no two people in the waiting room
    /// ever hold the same number.
    /// </summary>
    public string NextTicketCode(string departmentCode, IEnumerable<QueueTicket> ticketsIssued)
    {
        var prefix = departmentCode.ToUpperInvariant();
        var today = _clock.Now.Date;

        var highestToday = ticketsIssued
            .Where(t => t.CheckedInAt.Date == today)
            .Select(t => SequenceIn(t.TicketCode, prefix))
            .DefaultIfEmpty(0)
            .Max();

        return $"{prefix}-{highestToday + 1:D3}";
    }

    /// <summary>
    /// The waiting patients in the order they will be seen: emergencies first,
    /// then priority cases, then everyone else, each band in arrival order.
    /// </summary>
    public IReadOnlyList<QueueTicket> OrderWaiting(IEnumerable<QueueTicket> tickets) =>
        tickets
            .Where(t => t.Status == TicketStatus.Waiting)
            .OrderBy(t => t.Priority)
            .ThenBy(t => t.CheckedInAt)
            .ToList();

    /// <summary>
    /// The patient's place in the queue, counting from 1, or null once they are
    /// no longer waiting.
    /// </summary>
    public int? PositionOf(QueueTicket ticket, IEnumerable<QueueTicket> departmentTickets)
    {
        var ordered = OrderWaiting(departmentTickets);
        var index = ordered.ToList().FindIndex(t => t.Id == ticket.Id);
        return index < 0 ? null : index + 1;
    }

    /// <summary>
    /// How long the patient can expect to wait: their position multiplied by how
    /// long consultations have actually been taking in that department today.
    /// Falls back to the department's configured default until enough have
    /// finished for the average to mean anything.
    /// </summary>
    public TimeSpan? EstimateWait(
        QueueTicket ticket,
        IEnumerable<QueueTicket> departmentTickets,
        int defaultServiceMinutes)
    {
        var tickets = departmentTickets as IReadOnlyCollection<QueueTicket> ?? departmentTickets.ToList();

        var position = PositionOf(ticket, tickets);
        if (position is null)
        {
            return null;
        }

        return position.Value * AverageServiceTimeToday(tickets, defaultServiceMinutes);
    }

    /// <summary>
    /// Calls a waiting patient to a consultation room.
    /// </summary>
    /// <exception cref="RoomOccupiedException">The room already holds a patient.</exception>
    public void Call(
        QueueTicket ticket,
        string room,
        string staffUserId,
        IEnumerable<QueueTicket> departmentTickets)
    {
        Require(ticket, TicketStatus.Waiting);

        var occupant = departmentTickets.FirstOrDefault(t =>
            t.Id != ticket.Id
            && t.RoomNumber == room
            && t.Status is TicketStatus.Called or TicketStatus.InConsultation);

        if (occupant is not null)
        {
            throw new RoomOccupiedException(room, occupant.TicketCode);
        }

        ticket.Status = TicketStatus.Called;
        ticket.CalledAt = _clock.Now;
        ticket.RoomNumber = room;
        ticket.AssignedStaffId = staffUserId;
    }

    public void StartConsultation(QueueTicket ticket)
    {
        Require(ticket, TicketStatus.Called);

        ticket.Status = TicketStatus.InConsultation;
        ticket.ConsultationStartedAt = _clock.Now;
    }

    public void Complete(QueueTicket ticket)
    {
        Require(ticket, TicketStatus.InConsultation);

        ticket.Status = TicketStatus.Completed;
        ticket.CompletedAt = _clock.Now;
    }

    /// <summary>Marks a called patient as absent and releases their room.</summary>
    public void MarkNoShow(QueueTicket ticket)
    {
        Require(ticket, TicketStatus.Called);

        ticket.Status = TicketStatus.NoShow;
        ticket.RoomNumber = null;
    }

    /// <summary>
    /// Returns an absent patient to the queue. Their original check-in time is
    /// kept, so they rejoin ahead of everyone who arrived after them rather than
    /// being sent to the back for stepping out.
    /// </summary>
    public void Requeue(QueueTicket ticket)
    {
        Require(ticket, TicketStatus.NoShow);

        ticket.Status = TicketStatus.Waiting;
        ticket.CalledAt = null;
        ticket.RoomNumber = null;
        ticket.AssignedStaffId = null;
    }

    public void Cancel(QueueTicket ticket)
    {
        Require(ticket, TicketStatus.Waiting);

        ticket.Status = TicketStatus.Cancelled;
    }

    public void ChangePriority(QueueTicket ticket, TicketPriority priority)
    {
        Require(ticket, TicketStatus.Waiting);

        ticket.Priority = priority;
    }

    /// <summary>
    /// Moves a waiting patient to another department, where they are new to that
    /// department's clinicians and so join its queue from the moment of transfer.
    /// </summary>
    public void Transfer(QueueTicket ticket, int toDepartmentId, string newTicketCode)
    {
        Require(ticket, TicketStatus.Waiting);

        ticket.DepartmentId = toDepartmentId;
        ticket.TicketCode = newTicketCode;
        ticket.CheckedInAt = _clock.Now;
    }

    private TimeSpan AverageServiceTimeToday(
        IEnumerable<QueueTicket> departmentTickets,
        int defaultServiceMinutes)
    {
        var today = _clock.Now.Date;

        var durations = departmentTickets
            .Where(t => t.Status == TicketStatus.Completed && t.CompletedAt?.Date == today)
            .Select(t => t.ServiceDuration)
            .OfType<TimeSpan>()
            .ToList();

        return durations.Count < MinimumSamplesForAverage
            ? TimeSpan.FromMinutes(defaultServiceMinutes)
            : TimeSpan.FromTicks((long)durations.Average(d => d.Ticks));
    }

    /// <summary>
    /// The numeric part of a code belonging to <paramref name="prefix"/>, or 0
    /// for codes issued by another department.
    /// </summary>
    private static int SequenceIn(string ticketCode, string prefix)
    {
        if (!ticketCode.StartsWith($"{prefix}-", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var suffix = ticketCode[(prefix.Length + 1)..];
        return int.TryParse(suffix, out var sequence) ? sequence : 0;
    }

    private static void Require(QueueTicket ticket, params TicketStatus[] allowed)
    {
        if (!allowed.Contains(ticket.Status))
        {
            throw new InvalidTicketTransitionException(ticket.Status, allowed);
        }
    }
}
