using MediQueue.Domain.Abstractions;
using MediQueue.Domain.Entities;
using MediQueue.Domain.Enums;
using MediQueue.Infrastructure.Persistence;
using MediQueue.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace MediQueue.Api.Services;

public interface IAnalyticsService
{
    Task<AnalyticsSummaryDto> SummariseAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<string> ExportCsvAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}

/// <summary>
/// Reporting over the visit record. Aggregation runs in memory over a bounded
/// date window rather than in SQL, so the same numbers come out on SQLite and
/// Postgres; a fortnight of outpatient traffic is a few thousand rows.
/// </summary>
public class AnalyticsService : IAnalyticsService
{
    private readonly MediQueueDbContext _db;
    private readonly IClock _clock;

    public AnalyticsService(MediQueueDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<AnalyticsSummaryDto> SummariseAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        var tickets = await LoadAsync(from, to, ct);
        var departments = await _db.Departments.AsNoTracking().ToListAsync(ct);

        var completed = tickets.Where(t => t.Status == TicketStatus.Completed).ToList();
        var waits = completed.Select(t => t.WaitDuration).OfType<TimeSpan>().ToList();
        var services = completed.Select(t => t.ServiceDuration).OfType<TimeSpan>().ToList();

        var noShows = tickets.Count(t => t.Status == TicketStatus.NoShow);
        var everCalled = tickets.Count(t => t.CalledAt is not null);

        return new AnalyticsSummaryDto
        {
            From = from,
            To = to,
            TotalCheckIns = tickets.Count,
            Completed = completed.Count,
            NoShows = noShows,
            Cancelled = tickets.Count(t => t.Status == TicketStatus.Cancelled),
            StillWaiting = tickets.Count(t => t.Status == TicketStatus.Waiting),
            AverageWaitMinutes = AverageMinutes(waits),
            LongestWaitMinutes = waits.Count == 0 ? null : (int)waits.Max().TotalMinutes,
            AverageConsultationMinutes = AverageMinutes(services),

            // Measured against those actually called: a patient who never
            // reached the front of the queue cannot have failed to appear.
            NoShowRate = everCalled == 0 ? 0 : Math.Round(noShows * 100.0 / everCalled, 1),

            Departments = departments
                .Select(d => Performance(d, tickets.Where(t => t.DepartmentId == d.Id).ToList()))
                .Where(d => d.CheckIns > 0)
                .OrderByDescending(d => d.CheckIns)
                .ToList(),

            DailyVolume = DailyVolume(tickets, from, to),
            HourlyVolume = HourlyVolume(tickets)
        };
    }

    public async Task<string> ExportCsvAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var summary = await SummariseAsync(from, to, ct);

        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Department,Check-ins,Completed,No-shows,Average wait (min),Average consultation (min),Throughput per hour");

        foreach (var department in summary.Departments)
        {
            csv.AppendLine(string.Join(',',
                Escape(department.DepartmentName),
                department.CheckIns,
                department.Completed,
                department.NoShows,
                department.AverageWaitMinutes?.ToString() ?? string.Empty,
                department.AverageConsultationMinutes?.ToString() ?? string.Empty,
                department.ThroughputPerHour.ToString("0.0")));
        }

        return csv.ToString();
    }

    private async Task<List<QueueTicket>> LoadAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        return await _db.Tickets
            .AsNoTracking()
            .Where(t => t.CheckedInAt >= start && t.CheckedInAt < end)
            .ToListAsync(ct);
    }

    private DepartmentPerformanceDto Performance(Department department, List<QueueTicket> tickets)
    {
        var completed = tickets.Where(t => t.Status == TicketStatus.Completed).ToList();

        return new DepartmentPerformanceDto
        {
            DepartmentId = department.Id,
            DepartmentName = department.Name,
            DepartmentCode = department.Code,
            CheckIns = tickets.Count,
            Completed = completed.Count,
            NoShows = tickets.Count(t => t.Status == TicketStatus.NoShow),
            AverageWaitMinutes = AverageMinutes(completed.Select(t => t.WaitDuration).OfType<TimeSpan>().ToList()),
            AverageConsultationMinutes =
                AverageMinutes(completed.Select(t => t.ServiceDuration).OfType<TimeSpan>().ToList()),
            ThroughputPerHour = Throughput(completed)
        };
    }

    /// <summary>
    /// Patients completed per hour the department was actually open, taken from
    /// first to last consultation on each day it ran. Dividing by a nominal
    /// clinic day would flatter a department that only opened for a morning.
    /// </summary>
    private static double Throughput(List<QueueTicket> completed)
    {
        if (completed.Count == 0)
        {
            return 0;
        }

        var hours = completed
            .GroupBy(t => t.CheckedInAt.UtcDateTime.Date)
            .Sum(day =>
            {
                var opened = day.Min(t => t.CheckedInAt);
                var closed = day.Max(t => t.CompletedAt ?? t.CheckedInAt);

                // A single patient still represents time spent open.
                return Math.Max(0.5, (closed - opened).TotalHours);
            });

        return hours <= 0 ? 0 : Math.Round(completed.Count / hours, 1);
    }

    private static List<DailyVolumeDto> DailyVolume(List<QueueTicket> tickets, DateOnly from, DateOnly to)
    {
        var byDay = tickets
            .GroupBy(t => DateOnly.FromDateTime(t.CheckedInAt.UtcDateTime))
            .ToDictionary(g => g.Key, g => g.ToList());

        var days = new List<DailyVolumeDto>();

        // Every day in range appears, including the closed ones: a gap in the
        // chart is information, and omitting it would misdraw the trend line.
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            byDay.TryGetValue(day, out var onThatDay);
            var completed = onThatDay?.Where(t => t.Status == TicketStatus.Completed).ToList() ?? [];

            days.Add(new DailyVolumeDto
            {
                Date = day,
                CheckIns = onThatDay?.Count ?? 0,
                Completed = completed.Count,
                AverageWaitMinutes = AverageMinutes(
                    completed.Select(t => t.WaitDuration).OfType<TimeSpan>().ToList())
            });
        }

        return days;
    }

    private static List<HourlyVolumeDto> HourlyVolume(List<QueueTicket> tickets)
    {
        var byHour = tickets
            .GroupBy(t => t.CheckedInAt.UtcDateTime.Hour)
            .ToDictionary(g => g.Key, g => g.Count());

        // Clinic hours only; a flat overnight tail would squash the morning peak.
        return Enumerable.Range(6, 13)
            .Select(hour => new HourlyVolumeDto
            {
                Hour = hour,
                CheckIns = byHour.GetValueOrDefault(hour)
            })
            .ToList();
    }

    private static int? AverageMinutes(IReadOnlyCollection<TimeSpan> durations) =>
        durations.Count == 0 ? null : (int)durations.Average(d => d.TotalMinutes);

    private static string Escape(string value) =>
        value.Contains(',') ? $"\"{value}\"" : value;
}
