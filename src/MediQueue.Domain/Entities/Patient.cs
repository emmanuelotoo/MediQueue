namespace MediQueue.Domain.Entities;

public class Patient
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Hospital-issued folder number, unique per facility.</summary>
    public string MedicalRecordNumber { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public DateOnly? DateOfBirth { get; set; }

    public string? Gender { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<QueueTicket> Tickets { get; set; } = new List<QueueTicket>();
}
