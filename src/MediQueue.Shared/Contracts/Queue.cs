using System.ComponentModel.DataAnnotations;
using MediQueue.Domain.Enums;

namespace MediQueue.Shared.Contracts;

/// <summary>A department, as offered on the check-in screen.</summary>
public class DepartmentDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int ConsultationRooms { get; set; }
    public int WaitingCount { get; set; }
    public int? EstimatedWaitMinutes { get; set; }
}

/// <summary>One patient as seen on a staff console.</summary>
public class QueueEntryDto
{
    public Guid TicketId { get; set; }
    public string TicketCode { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string MedicalRecordNumber { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public TicketPriority Priority { get; set; }
    public TicketStatus Status { get; set; }
    public DateTimeOffset CheckedInAt { get; set; }

    /// <summary>Minutes elapsed since check-in, for the "waiting 42m" column.</summary>
    public int WaitedMinutes { get; set; }

    /// <summary>Place in the queue, or null once the patient has been called.</summary>
    public int? Position { get; set; }

    public string? RoomNumber { get; set; }
    public string? AssignedStaffName { get; set; }
}

/// <summary>
/// A department's whole queue: everyone still waiting, plus everyone currently
/// in a room. Pushed over SignalR whenever anything changes.
/// </summary>
public class DepartmentQueueDto
{
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public string DepartmentCode { get; set; } = string.Empty;

    public List<QueueEntryDto> Waiting { get; set; } = [];

    /// <summary>Patients called or in consultation, i.e. occupying a room.</summary>
    public List<QueueEntryDto> InProgress { get; set; } = [];

    public int CompletedToday { get; set; }
    public int? AverageWaitMinutes { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
}

/// <summary>What the patient's own screen shows.</summary>
public class TicketStatusDto
{
    public string TicketCode { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public TicketPriority Priority { get; set; }
    public TicketStatus Status { get; set; }
    public int? Position { get; set; }
    public int? EstimatedWaitMinutes { get; set; }
    public int? AheadOfYou { get; set; }
    public string? RoomNumber { get; set; }
    public DateTimeOffset CheckedInAt { get; set; }
    public DateTimeOffset? CalledAt { get; set; }
}

/// <summary>Pushed to a patient's device the moment they are called.</summary>
public class TicketCalledDto
{
    public string TicketCode { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public DateTimeOffset CalledAt { get; set; }
}

/// <summary>Pushed to reception when a clinician escalates a case.</summary>
public class EmergencyAlertDto
{
    public Guid TicketId { get; set; }
    public string TicketCode { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public string? RaisedBy { get; set; }
    public DateTimeOffset RaisedAt { get; set; }
}

/// <summary>The waiting-room display: who is being seen, and who is next.</summary>
public class BoardDto
{
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public List<BoardEntryDto> NowServing { get; set; } = [];
    public List<string> UpNext { get; set; } = [];
    public int WaitingCount { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
}

public class BoardEntryDto
{
    public string TicketCode { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
}

public class CallPatientRequest
{
    [Required(ErrorMessage = "Choose a room.")]
    [StringLength(10)]
    public string Room { get; set; } = string.Empty;
}

public class ChangePriorityRequest
{
    public TicketPriority Priority { get; set; }
}

public class TransferRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose a department.")]
    public int ToDepartmentId { get; set; }
}
