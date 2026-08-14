using System.ComponentModel.DataAnnotations;
using MediQueue.Domain.Enums;

namespace MediQueue.Shared.Contracts;

/// <summary>
/// What a patient fills in at the kiosk. Validation attributes live here so the
/// Blazor form and the API enforce exactly the same rules.
/// </summary>
public class CheckInRequest
{
    [Required(ErrorMessage = "Please enter your full name.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "Please enter your full name.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter a phone number we can reach you on.")]
    [RegularExpression(
        @"^(\+233|0)\d{9}$",
        ErrorMessage = "Enter a Ghanaian number, like 0244123456 or +233244123456.")]
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Folder number for returning patients; blank for a first visit.</summary>
    [StringLength(32)]
    public string? MedicalRecordNumber { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    [StringLength(20)]
    public string? Gender { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Please choose a department.")]
    public int DepartmentId { get; set; }

    /// <summary>
    /// Set by reception when checking someone in at the desk. Kiosk check-ins
    /// are always Normal; a patient cannot promote themselves.
    /// </summary>
    public TicketPriority Priority { get; set; } = TicketPriority.Normal;
}

/// <summary>Issued ticket, shown to the patient immediately after check-in.</summary>
public class CheckInResponse
{
    public Guid TicketId { get; set; }
    public string TicketCode { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string MedicalRecordNumber { get; set; } = string.Empty;
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public int Position { get; set; }
    public int? EstimatedWaitMinutes { get; set; }
    public DateTimeOffset CheckedInAt { get; set; }
}
