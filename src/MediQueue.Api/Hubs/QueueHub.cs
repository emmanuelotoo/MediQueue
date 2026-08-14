using MediQueue.Infrastructure.Persistence;
using MediQueue.Shared.Authorization;
using MediQueue.Shared.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace MediQueue.Api.Hubs;

/// <summary>
/// Read-only from the client's point of view: connections choose what to
/// receive, and never change anything. Every mutation goes through the REST
/// API, so authorization lives in exactly one place.
/// </summary>
public class QueueHub : Hub
{
    private readonly MediQueueDbContext _db;

    public QueueHub(MediQueueDbContext db) => _db = db;

    /// <summary>
    /// Staff consoles. Carries patient names, so it requires a signed-in user.
    /// </summary>
    [Authorize(Policy = Policies.TreatPatients)]
    public Task WatchDepartment(int departmentId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, QueueGroups.Department(departmentId));

    [Authorize(Policy = Policies.TreatPatients)]
    public Task StopWatchingDepartment(int departmentId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, QueueGroups.Department(departmentId));

    /// <summary>
    /// Waiting-room displays. Anonymous, because the board shows only ticket
    /// codes and room numbers.
    /// </summary>
    public Task WatchBoard(int departmentId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, QueueGroups.Board(departmentId));

    /// <summary>
    /// A patient's own device. Anonymous, but the caller must present a code
    /// that exists, so connections cannot enumerate other people's tickets.
    /// </summary>
    public async Task WatchTicket(string ticketCode)
    {
        var normalised = ticketCode.ToUpperInvariant();

        var exists = await _db.Tickets.AnyAsync(t => t.TicketCode == normalised);
        if (!exists)
        {
            throw new HubException("Unknown ticket code.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, QueueGroups.Ticket(normalised));
    }

    /// <summary>The front desk, which receives emergency escalations.</summary>
    [Authorize(Policy = Policies.ManageQueue)]
    public Task WatchReception() =>
        Groups.AddToGroupAsync(Context.ConnectionId, QueueGroups.Reception);
}
