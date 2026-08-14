namespace MediQueue.Domain.Enums;

public enum VisitEventType
{
    CheckedIn = 0,
    Called = 1,
    ConsultationStarted = 2,
    Completed = 3,
    MarkedNoShow = 4,
    Requeued = 5,
    Cancelled = 6,
    PriorityChanged = 7,
    Transferred = 8
}
