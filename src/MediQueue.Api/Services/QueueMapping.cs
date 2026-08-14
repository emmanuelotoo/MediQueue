using MediQueue.Domain.Entities;
using MediQueue.Domain.Enums;
using MediQueue.Shared.Contracts;

namespace MediQueue.Api.Services;

/// <summary>Turns tickets into the shapes the three dashboards expect.</summary>
internal static class QueueMapping
{
    internal static QueueEntryDto ToEntry(
        QueueTicket ticket,
        DateTimeOffset now,
        int? position,
        IReadOnlyDictionary<string, string> staffNames)
    {
        // Once a patient has been called, "waited" freezes at what they actually
        // waited rather than continuing to climb while they are being seen.
        var until = ticket.CalledAt ?? now;

        return new QueueEntryDto
        {
            TicketId = ticket.Id,
            TicketCode = ticket.TicketCode,
            PatientName = ticket.Patient?.FullName ?? "Unknown",
            MedicalRecordNumber = ticket.Patient?.MedicalRecordNumber ?? string.Empty,
            PhoneNumber = ticket.Patient?.PhoneNumber,
            Priority = ticket.Priority,
            Status = ticket.Status,
            CheckedInAt = ticket.CheckedInAt,
            WaitedMinutes = (int)Math.Max(0, (until - ticket.CheckedInAt).TotalMinutes),
            Position = position,
            RoomNumber = ticket.RoomNumber,
            AssignedStaffName = ticket.AssignedStaffId is not null
                && staffNames.TryGetValue(ticket.AssignedStaffId, out var name)
                    ? name
                    : null
        };
    }

    internal static TicketStatusDto ToStatus(
        QueueTicket ticket,
        Department department,
        int? position,
        TimeSpan? estimatedWait) =>
        new()
        {
            TicketCode = ticket.TicketCode,
            PatientName = ticket.Patient?.FullName ?? "Unknown",
            DepartmentId = department.Id,
            DepartmentName = department.Name,
            Priority = ticket.Priority,
            Status = ticket.Status,
            Position = position,
            EstimatedWaitMinutes = estimatedWait is null ? null : (int)estimatedWait.Value.TotalMinutes,
            AheadOfYou = position is null ? null : position.Value - 1,
            RoomNumber = ticket.RoomNumber,
            CheckedInAt = ticket.CheckedInAt,
            CalledAt = ticket.CalledAt
        };

    /// <summary>
    /// Patients occupying a room, most recently called first, so the console
    /// reads top-down as "who just went in".
    /// </summary>
    internal static bool IsInProgress(QueueTicket ticket) =>
        ticket.Status is TicketStatus.Called or TicketStatus.InConsultation;
}
