namespace MediQueue.Domain.Entities;

public class Department
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Short code used as the ticket prefix, e.g. <c>CAR</c>.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Number of consultation rooms, named "1".."N".</summary>
    public int ConsultationRooms { get; set; } = 1;

    /// <summary>
    /// Wait estimate fallback, used until enough consultations have completed
    /// today to compute a real average.
    /// </summary>
    public int DefaultServiceMinutes { get; set; } = 15;

    public bool IsActive { get; set; } = true;

    public ICollection<QueueTicket> Tickets { get; set; } = new List<QueueTicket>();
}
