using MediQueue.Domain.Queues;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MediQueue.Api;

/// <summary>
/// Turns domain refusals into HTTP the client can act on. A rejected transition
/// is a conflict, not a server fault: two receptionists racing for the same
/// patient is expected, and the loser should be told why.
/// </summary>
public class ProblemDetailsExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ProblemDetailsExceptionHandler> _logger;

    public ProblemDetailsExceptionHandler(ILogger<ProblemDetailsExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            RoomOccupiedException e => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Room already in use",
                Detail = e.Message
            },
            InvalidTicketTransitionException e => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "That is no longer possible",
                Detail = e.Message
            },
            KeyNotFoundException e => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not found",
                Detail = e.Message
            },
            _ => null
        };

        if (problem is null)
        {
            return false;
        }

        _logger.LogInformation(
            "{Path} refused: {Detail}",
            context.Request.Path,
            problem.Detail);

        problem.Instance = context.Request.Path;
        context.Response.StatusCode = problem.Status!.Value;
        await context.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }
}
