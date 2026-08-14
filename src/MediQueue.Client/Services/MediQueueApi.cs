using System.Net;
using System.Net.Http.Json;
using MediQueue.Domain.Enums;
using MediQueue.Shared.Contracts;
using MediQueue.Shared.Serialization;

namespace MediQueue.Client.Services;

/// <summary>
/// Raised when the API refuses an action. Carries the server's explanation so
/// the UI can show why rather than a generic failure.
/// </summary>
public class ApiException : Exception
{
    public ApiException(HttpStatusCode status, string message) : base(message) => Status = status;

    public HttpStatusCode Status { get; }
}

/// <summary>
/// The only place the client talks HTTP. Components call named methods and
/// receive either a result or an <see cref="ApiException"/> they can display.
/// </summary>
public class MediQueueApi
{
    private readonly HttpClient _http;

    public MediQueueApi(HttpClient http) => _http = http;

    public Task<IReadOnlyList<DepartmentDto>> GetDepartmentsAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<DepartmentDto>>("api/departments", ct)!;

    public Task<TicketStatusDto?> GetTicketAsync(string code, CancellationToken ct = default) =>
        GetOrNullAsync<TicketStatusDto>($"api/tickets/{code}", ct);

    public Task<BoardDto?> GetBoardAsync(int departmentId, CancellationToken ct = default) =>
        GetOrNullAsync<BoardDto>($"api/board/{departmentId}", ct);

    public Task<DepartmentQueueDto?> GetQueueAsync(int departmentId, CancellationToken ct = default) =>
        GetOrNullAsync<DepartmentQueueDto>($"api/queue/{departmentId}", ct);

    public Task<AnalyticsSummaryDto?> GetAnalyticsAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
        GetOrNullAsync<AnalyticsSummaryDto>($"api/analytics/summary?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", ct);

    public Task<CheckInResponse> CheckInAsync(CheckInRequest request, CancellationToken ct = default) =>
        PostAsync<CheckInRequest, CheckInResponse>("api/checkin", request, ct);

    public Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default) =>
        PostAsync<LoginRequest, LoginResponse>("api/auth/login", request, ct);

    public Task CallAsync(Guid ticketId, string room, CancellationToken ct = default) =>
        CommandAsync($"api/tickets/{ticketId}/call", new CallPatientRequest { Room = room }, ct);

    public Task StartAsync(Guid ticketId, CancellationToken ct = default) =>
        CommandAsync($"api/tickets/{ticketId}/start", ct);

    public Task CompleteAsync(Guid ticketId, CancellationToken ct = default) =>
        CommandAsync($"api/tickets/{ticketId}/complete", ct);

    public Task NoShowAsync(Guid ticketId, CancellationToken ct = default) =>
        CommandAsync($"api/tickets/{ticketId}/noshow", ct);

    public Task RequeueAsync(Guid ticketId, CancellationToken ct = default) =>
        CommandAsync($"api/tickets/{ticketId}/requeue", ct);

    public Task CancelAsync(Guid ticketId, CancellationToken ct = default) =>
        CommandAsync($"api/tickets/{ticketId}/cancel", ct);

    public Task SetPriorityAsync(Guid ticketId, TicketPriority priority, CancellationToken ct = default) =>
        CommandAsync($"api/tickets/{ticketId}/priority", new ChangePriorityRequest { Priority = priority }, ct);

    public Task TransferAsync(Guid ticketId, int toDepartmentId, CancellationToken ct = default) =>
        CommandAsync($"api/tickets/{ticketId}/transfer", new TransferRequest { ToDepartmentId = toDepartmentId }, ct);

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
    {
        var response = await _http.GetAsync(url, ct);
        await ThrowIfRefusedAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<T>(MediQueueJson.Options, ct);
    }

    /// <summary>A missing resource is an answer, not a failure.</summary>
    private async Task<T?> GetOrNullAsync<T>(string url, CancellationToken ct)
    {
        var response = await _http.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        await ThrowIfRefusedAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<T>(MediQueueJson.Options, ct);
    }

    private async Task<TResult> PostAsync<TBody, TResult>(string url, TBody body, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync(url, body, MediQueueJson.Options, ct);
        await ThrowIfRefusedAsync(response, ct);

        return await response.Content.ReadFromJsonAsync<TResult>(MediQueueJson.Options, ct)
            ?? throw new ApiException(response.StatusCode, "The server returned an empty response.");
    }

    private async Task CommandAsync<TBody>(string url, TBody body, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync(url, body, MediQueueJson.Options, ct);
        await ThrowIfRefusedAsync(response, ct);
    }

    private async Task CommandAsync(string url, CancellationToken ct)
    {
        var response = await _http.PostAsync(url, null, ct);
        await ThrowIfRefusedAsync(response, ct);
    }

    private static async Task ThrowIfRefusedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await ReadProblemDetailAsync(response, ct);
        throw new ApiException(response.StatusCode, detail);
    }

    /// <summary>
    /// Prefers the server's own wording. A refusal like "Room 3 is already in
    /// use by ticket CAR-011" is far more use at a front desk than "409".
    /// </summary>
    private static async Task<string> ReadProblemDetailAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var problem = await response.Content
                .ReadFromJsonAsync<ProblemResponse>(MediQueueJson.Options, ct);

            if (!string.IsNullOrWhiteSpace(problem?.Detail))
            {
                return problem.Detail;
            }

            if (problem?.Errors is { Count: > 0 })
            {
                return string.Join(" ", problem.Errors.SelectMany(e => e.Value));
            }

            if (!string.IsNullOrWhiteSpace(problem?.Title))
            {
                return problem.Title;
            }
        }
        catch (Exception)
        {
            // Not a problem document; fall through to the generic wording.
        }

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Please sign in again.",
            HttpStatusCode.Forbidden => "Your role does not allow that.",
            HttpStatusCode.NotFound => "That could not be found.",
            _ => "Something went wrong. Please try again."
        };
    }

    private class ProblemResponse
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public Dictionary<string, string[]>? Errors { get; set; }
    }
}
