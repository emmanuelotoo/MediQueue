using MediQueue.Domain.Enums;

namespace MediQueue.Domain.Entities;

/// <summary>
/// Append-only record of something that happened to a ticket. Serves as both
/// the audit trail and the source the analytics layer aggregates over, so
/// reporting never has to reconstruct history from mutable ticket rows.
/// </summary>
public class VisitEvent
{
    public long Id { get; set; }

    public Guid TicketId { get; set; }
    public QueueTicket? Ticket { get; set; }

    public VisitEventType EventType { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Identity user id of the staff member responsible, null for patient-initiated events.</summary>
    public string? ActorUserId { get; set; }

    /// <summary>Free-form detail, e.g. the room a patient was called to.</summary>
    public string? Metadata { get; set; }
}
