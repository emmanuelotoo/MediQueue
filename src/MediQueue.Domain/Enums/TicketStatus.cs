namespace MediQueue.Domain.Enums;

public enum TicketStatus
{
    Waiting = 0,
    Called = 1,
    InConsultation = 2,
    Completed = 3,
    NoShow = 4,
    Cancelled = 5
}
