namespace MediQueue.Shared.Realtime;

/// <summary>
/// Names of the messages the hub pushes to clients. Both sides reference these
/// constants, so a rename cannot silently break the wire contract.
/// </summary>
public static class QueueEvents
{
    /// <summary>A department's queue changed. Payload: <c>DepartmentQueueDto</c>.</summary>
    public const string QueueUpdated = nameof(QueueUpdated);

    /// <summary>A patient was called to a room. Payload: <c>TicketCalledDto</c>.</summary>
    public const string TicketCalled = nameof(TicketCalled);

    /// <summary>A tracked ticket moved. Payload: <c>TicketStatusDto</c>.</summary>
    public const string TicketUpdated = nameof(TicketUpdated);

    /// <summary>A clinician escalated a case. Payload: <c>EmergencyAlertDto</c>.</summary>
    public const string EmergencyFlagged = nameof(EmergencyFlagged);
}

/// <summary>Methods clients invoke on the hub to choose what they receive.</summary>
public static class QueueHubMethods
{
    public const string WatchDepartment = nameof(WatchDepartment);
    public const string StopWatchingDepartment = nameof(StopWatchingDepartment);
    public const string WatchTicket = nameof(WatchTicket);
    public const string WatchReception = nameof(WatchReception);
}

/// <summary>SignalR group names. Built in one place so both sides agree.</summary>
public static class QueueGroups
{
    public static string Department(int departmentId) => $"dept-{departmentId}";

    public static string Ticket(string ticketCode) => $"ticket-{ticketCode.ToUpperInvariant()}";

    public const string Reception = "reception";
}
