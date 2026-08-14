using System.Text;
using MediQueue.Api.Services;
using MediQueue.Domain.Abstractions;
using MediQueue.Shared.Authorization;
using MediQueue.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediQueue.Api.Controllers;

[ApiController]
[Route("api/analytics")]
[Authorize(Policy = Policies.ViewAnalytics)]
public class AnalyticsController : ControllerBase
{
    /// <summary>Guards against a request that would scan the whole history.</summary>
    private const int MaximumRangeDays = 400;

    private readonly IAnalyticsService _analytics;
    private readonly IClock _clock;

    public AnalyticsController(IAnalyticsService analytics, IClock clock)
    {
        _analytics = analytics;
        _clock = clock;
    }

    [HttpGet("summary")]
    [ProducesResponseType<AnalyticsSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AnalyticsSummaryDto>> Summary(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct)
    {
        var (start, end, error) = ResolveRange(from, to);
        if (error is not null)
        {
            return BadRequest(error);
        }

        return Ok(await _analytics.SummariseAsync(start, end, ct));
    }

    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Export(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct)
    {
        var (start, end, error) = ResolveRange(from, to);
        if (error is not null)
        {
            return BadRequest(error);
        }

        var csv = await _analytics.ExportCsvAsync(start, end, ct);

        return File(
            Encoding.UTF8.GetBytes(csv),
            "text/csv",
            $"mediqueue-{start:yyyy-MM-dd}-to-{end:yyyy-MM-dd}.csv");
    }

    /// <summary>Defaults to the last seven days, which is the report staff ask for.</summary>
    private (DateOnly From, DateOnly To, ProblemDetails? Error) ResolveRange(DateOnly? from, DateOnly? to)
    {
        var today = DateOnly.FromDateTime(_clock.Now.UtcDateTime);
        var end = to ?? today;
        var start = from ?? end.AddDays(-6);

        if (start > end)
        {
            return (start, end, new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid range",
                Detail = "The start date is after the end date."
            });
        }

        if (end.DayNumber - start.DayNumber > MaximumRangeDays)
        {
            return (start, end, new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Range too wide",
                Detail = $"Reports cover at most {MaximumRangeDays} days at a time."
            });
        }

        return (start, end, null);
    }
}
