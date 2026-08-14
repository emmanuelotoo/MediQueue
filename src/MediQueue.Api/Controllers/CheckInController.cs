using System.Security.Claims;
using MediQueue.Api.Services;
using MediQueue.Domain.Enums;
using MediQueue.Shared.Authorization;
using MediQueue.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediQueue.Api.Controllers;

[ApiController]
[Route("api")]
public class CheckInController : ControllerBase
{
    private readonly IQueueService _queue;

    public CheckInController(IQueueService queue) => _queue = queue;

    /// <summary>
    /// Joins the queue. Open to anyone, because the whole point is that a
    /// patient can do this from the kiosk or their own phone without an account.
    /// </summary>
    [HttpPost("checkin")]
    [AllowAnonymous]
    [ProducesResponseType<CheckInResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CheckInResponse>> CheckIn(CheckInRequest request, CancellationToken ct)
    {
        var isStaff = User.Identity?.IsAuthenticated == true;

        // Only a member of staff decides that a case is urgent. A self-service
        // kiosk that let patients pick "Emergency" would be picked every time.
        if (!isStaff)
        {
            request.Priority = TicketPriority.Normal;
        }

        var actorUserId = isStaff ? User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
        var response = await _queue.CheckInAsync(request, actorUserId, ct);

        return CreatedAtAction(
            nameof(TicketsController.GetByCode),
            "Tickets",
            new { code = response.TicketCode },
            response);
    }

    /// <summary>Departments a patient can check in to.</summary>
    [HttpGet("departments")]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<DepartmentDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DepartmentDto>>> Departments(CancellationToken ct) =>
        Ok(await _queue.GetDepartmentsAsync(ct));

    /// <summary>
    /// The waiting-room display. Anonymous, and deliberately limited to ticket
    /// codes and room numbers.
    /// </summary>
    [HttpGet("board/{departmentId:int}")]
    [AllowAnonymous]
    [ProducesResponseType<BoardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BoardDto>> Board(int departmentId, CancellationToken ct) =>
        Ok(await _queue.GetBoardAsync(departmentId, ct));

    /// <summary>A department's full queue, with patient names. Staff only.</summary>
    [HttpGet("queue/{departmentId:int}")]
    [Authorize(Policy = Policies.TreatPatients)]
    [ProducesResponseType<DepartmentQueueDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DepartmentQueueDto>> Queue(int departmentId, CancellationToken ct) =>
        Ok(await _queue.GetDepartmentQueueAsync(departmentId, ct));
}
