using MediQueue.Domain.Enums;

namespace MediQueue.Domain.Entities;

/// <summary>
/// One patient's place in one department's queue for one visit.
/// State transitions are performed by <see cref="Queues.QueueEngine"/>, never
/// by setting <see cref="Status"/> directly.
/// </summary>
public class QueueTicket
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Human-readable code shown to the patient, e.g. <c>CAR-014</c>.</summary>
    public string TicketCode { get; set; } = string.Empty;

    public Guid PatientId { get; set; }
    public Patient? Patient { get; set; }

    public int DepartmentId { get; set; }
    public Department? Department { get; set; }

    public TicketPriority Priority { get; set; } = TicketPriority.Normal;

    public TicketStatus Status { get; set; } = TicketStatus.Waiting;

    /// <summary>
    /// Ordering key within a priority band. Set once at check-in and preserved
    /// across a no-show requeue, so a returning patient keeps their place.
    /// </summary>
    public DateTimeOffset CheckedInAt { get; set; }

    public DateTimeOffset? CalledAt { get; set; }
    public DateTimeOffset? ConsultationStartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public string? RoomNumber { get; set; }

    public string? AssignedStaffId { get; set; }

    public string? Notes { get; set; }

    public ICollection<VisitEvent> Events { get; set; } = new List<VisitEvent>();

    /// <summary>Time the patient spent waiting before being called.</summary>
    public TimeSpan? WaitDuration => CalledAt is null ? null : CalledAt.Value - CheckedInAt;

    /// <summary>Time the consultation itself took.</summary>
    public TimeSpan? ServiceDuration =>
        ConsultationStartedAt is null || CompletedAt is null
            ? null
            : CompletedAt.Value - ConsultationStartedAt.Value;
}
