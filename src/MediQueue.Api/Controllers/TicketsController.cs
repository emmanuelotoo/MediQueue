using System.Security.Claims;
using MediQueue.Api.Services;
using MediQueue.Shared.Authorization;
using MediQueue.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediQueue.Api.Controllers;

[ApiController]
[Route("api/tickets")]
public class TicketsController : ControllerBase
{
    private readonly IQueueService _queue;

    public TicketsController(IQueueService queue) => _queue = queue;

    private string ActorId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Authenticated request carries no user id.");

    /// <summary>
    /// A patient checking their own place in the queue. Anonymous, because they
    /// have no account; the ticket code is the credential.
    /// </summary>
    [HttpGet("{code}")]
    [AllowAnonymous]
    [ProducesResponseType<TicketStatusDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketStatusDto>> GetByCode(string code, CancellationToken ct)
    {
        var status = await _queue.GetTicketStatusAsync(code, ct);
        return status is null ? NotFound() : Ok(status);
    }

    /// <summary>Calls the patient to a consultation room.</summary>
    [HttpPost("{id:guid}/call")]
    [Authorize(Policy = Policies.TreatPatients)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Call(Guid id, CallPatientRequest request, CancellationToken ct)
    {
        await _queue.CallAsync(id, request.Room, ActorId, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/start")]
    [Authorize(Policy = Policies.TreatPatients)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Start(Guid id, CancellationToken ct)
    {
        await _queue.StartConsultationAsync(id, ActorId, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/complete")]
    [Authorize(Policy = Policies.TreatPatients)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Complete(Guid id, CancellationToken ct)
    {
        await _queue.CompleteAsync(id, ActorId, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/noshow")]
    [Authorize(Policy = Policies.TreatPatients)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> NoShow(Guid id, CancellationToken ct)
    {
        await _queue.MarkNoShowAsync(id, ActorId, ct);
        return NoContent();
    }

    /// <summary>Returns a patient who stepped out to their original place.</summary>
    [HttpPost("{id:guid}/requeue")]
    [Authorize(Policy = Policies.ManageQueue)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Requeue(Guid id, CancellationToken ct)
    {
        await _queue.RequeueAsync(id, ActorId, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Policies.ManageQueue)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await _queue.CancelAsync(id, ActorId, ct);
        return NoContent();
    }

    /// <summary>
    /// Raises or lowers priority. Open to clinicians as well as reception,
    /// because escalating to Emergency is a clinical judgement made at the point
    /// of care; doing so alerts the front desk immediately.
    /// </summary>
    [HttpPost("{id:guid}/priority")]
    [Authorize(Policy = Policies.TreatPatients)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangePriority(Guid id, ChangePriorityRequest request, CancellationToken ct)
    {
        await _queue.ChangePriorityAsync(id, request.Priority, ActorId, ct);
        return NoContent();
    }

    /// <summary>Moves a waiting patient to a different department.</summary>
    [HttpPost("{id:guid}/transfer")]
    [Authorize(Policy = Policies.ManageQueue)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Transfer(Guid id, TransferRequest request, CancellationToken ct)
    {
        await _queue.TransferAsync(id, request.ToDepartmentId, ActorId, ct);
        return NoContent();
    }
}
