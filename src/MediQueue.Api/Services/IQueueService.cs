using MediQueue.Domain.Enums;
using MediQueue.Shared.Contracts;

namespace MediQueue.Api.Services;

/// <summary>
/// Every change to the queue passes through here. Each method applies the
/// domain rules, records what happened, saves, and then broadcasts — in that
/// order, so nothing is announced that did not commit.
/// </summary>
public interface IQueueService
{
    Task<IReadOnlyList<DepartmentDto>> GetDepartmentsAsync(CancellationToken ct = default);

    Task<CheckInResponse> CheckInAsync(CheckInRequest request, string? actorUserId, CancellationToken ct = default);

    Task<TicketStatusDto?> GetTicketStatusAsync(string ticketCode, CancellationToken ct = default);

    Task<DepartmentQueueDto> GetDepartmentQueueAsync(int departmentId, CancellationToken ct = default);

    Task<BoardDto> GetBoardAsync(int departmentId, CancellationToken ct = default);

    Task CallAsync(Guid ticketId, string room, string actorUserId, CancellationToken ct = default);

    Task StartConsultationAsync(Guid ticketId, string actorUserId, CancellationToken ct = default);

    Task CompleteAsync(Guid ticketId, string actorUserId, CancellationToken ct = default);

    Task MarkNoShowAsync(Guid ticketId, string actorUserId, CancellationToken ct = default);

    Task RequeueAsync(Guid ticketId, string actorUserId, CancellationToken ct = default);

    Task CancelAsync(Guid ticketId, string actorUserId, CancellationToken ct = default);

    Task ChangePriorityAsync(Guid ticketId, TicketPriority priority, string actorUserId, CancellationToken ct = default);

    Task TransferAsync(Guid ticketId, int toDepartmentId, string actorUserId, CancellationToken ct = default);
}
