using MediQueue.Domain.Enums;

namespace MediQueue.Domain.Queues;

/// <summary>Raised when a ticket is moved to a status it cannot legally reach.</summary>
public class InvalidTicketTransitionException : InvalidOperationException
{
    public InvalidTicketTransitionException(TicketStatus actual, params TicketStatus[] expected)
        : base($"Ticket is {actual}; this action requires it to be {string.Join(" or ", expected)}.")
    {
        Actual = actual;
        Expected = expected;
    }

    public TicketStatus Actual { get; }

    public IReadOnlyList<TicketStatus> Expected { get; }
}

/// <summary>Raised when a patient is called to a room that is already in use.</summary>
public class RoomOccupiedException : InvalidOperationException
{
    public RoomOccupiedException(string room, string occupyingTicketCode)
        : base($"Room {room} is already in use by ticket {occupyingTicketCode}.")
    {
        Room = room;
        OccupyingTicketCode = occupyingTicketCode;
    }

    public string Room { get; }

    public string OccupyingTicketCode { get; }
}
