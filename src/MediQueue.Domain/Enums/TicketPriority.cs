namespace MediQueue.Domain.Enums;

/// <summary>
/// Priority bands, ordered so that a lower value is served first.
/// </summary>
public enum TicketPriority
{
    Emergency = 0,
    Priority = 1,
    Normal = 2
}
