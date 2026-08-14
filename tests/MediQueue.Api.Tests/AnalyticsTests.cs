using MediQueue.Domain.Entities;
using MediQueue.Domain.Enums;
using MediQueue.Infrastructure.Persistence;
using MediQueue.Shared.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace MediQueue.Api.Tests;

public class AnalyticsTests : IClassFixture<MediQueueApiFactory>
{
    private readonly MediQueueApiFactory _factory;

    public AnalyticsTests(MediQueueApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Only_administrators_can_read_reports()
    {
        var clinician = await _factory.CreateClientAsAsync(Roles.Clinician);

        var response = await clinician.GetAsync("/api/analytics/summary");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reports_are_not_public()
    {
        var response = await _factory.CreateClient().GetAsync("/api/analytics/summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_reversed_date_range_is_rejected()
    {
        var admin = await _factory.CreateClientAsAsync(Roles.Admin);

        var response = await admin.GetAsync("/api/analytics/summary?from=2026-08-14&to=2026-08-01");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Waits_and_consultations_are_averaged_over_completed_visits()
    {
        var department = await _factory.AddDepartmentAsync(name: "Analytics Ward", code: "AN1");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Two finished visits: waits of 10 and 30 minutes, consultations of 20
        // and 40. A third patient is still waiting and must not skew either.
        await SeedVisitAsync(department.Id, waitMinutes: 10, consultationMinutes: 20);
        await SeedVisitAsync(department.Id, waitMinutes: 30, consultationMinutes: 40);
        await SeedWaitingAsync(department.Id);

        var admin = await _factory.CreateClientAsAsync(Roles.Admin);
        var summary = await admin.GetJsonAsync<AnalyticsSummaryDto>(
            $"/api/analytics/summary?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");

        var ward = summary!.Departments.Single(d => d.DepartmentName == "Analytics Ward");

        Assert.Equal(3, ward.CheckIns);
        Assert.Equal(2, ward.Completed);
        Assert.Equal(20, ward.AverageWaitMinutes);
        Assert.Equal(30, ward.AverageConsultationMinutes);
    }

    [Fact]
    public async Task The_no_show_rate_counts_only_patients_who_were_called()
    {
        var department = await _factory.AddDepartmentAsync(name: "No Show Ward", code: "AN2");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await SeedVisitAsync(department.Id, waitMinutes: 5, consultationMinutes: 10);
        await SeedNoShowAsync(department.Id);

        // Never called, so this patient cannot have failed to appear.
        await SeedWaitingAsync(department.Id);

        var admin = await _factory.CreateClientAsAsync(Roles.Admin);
        var summary = await admin.GetJsonAsync<AnalyticsSummaryDto>(
            $"/api/analytics/summary?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");

        var ward = summary!.Departments.Single(d => d.DepartmentName == "No Show Ward");

        Assert.Equal(1, ward.NoShows);
        Assert.Equal(3, ward.CheckIns);
    }

    [Fact]
    public async Task Every_day_in_the_range_appears_including_closed_ones()
    {
        var admin = await _factory.CreateClientAsAsync(Roles.Admin);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddDays(-6);

        var summary = await admin.GetJsonAsync<AnalyticsSummaryDto>(
            $"/api/analytics/summary?from={from:yyyy-MM-dd}&to={today:yyyy-MM-dd}");

        Assert.Equal(7, summary!.DailyVolume.Count);
        Assert.Equal(from, summary.DailyVolume[0].Date);
        Assert.Equal(today, summary.DailyVolume[^1].Date);
    }

    [Fact]
    public async Task The_export_is_a_csv_download()
    {
        var admin = await _factory.CreateClientAsAsync(Roles.Admin);

        var response = await admin.GetAsync("/api/analytics/export");

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("Department,Check-ins,Completed", body);
    }

    private Task SeedVisitAsync(int departmentId, int waitMinutes, int consultationMinutes) =>
        SeedAsync(departmentId, ticket =>
        {
            var calledAt = ticket.CheckedInAt.AddMinutes(waitMinutes);

            ticket.Status = TicketStatus.Completed;
            ticket.CalledAt = calledAt;
            ticket.ConsultationStartedAt = calledAt;
            ticket.CompletedAt = calledAt.AddMinutes(consultationMinutes);
        });

    private Task SeedNoShowAsync(int departmentId) =>
        SeedAsync(departmentId, ticket =>
        {
            ticket.Status = TicketStatus.NoShow;
            ticket.CalledAt = ticket.CheckedInAt.AddMinutes(8);
        });

    private Task SeedWaitingAsync(int departmentId) =>
        SeedAsync(departmentId, _ => { });

    private async Task SeedAsync(int departmentId, Action<QueueTicket> arrange)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediQueueDbContext>();

        var patient = new Patient
        {
            MedicalRecordNumber = $"MRN-{Guid.NewGuid():N}"[..12],
            FullName = "Test Patient",
            PhoneNumber = "0244000000",
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Anchored a couple of hours back so the whole visit sits inside today.
        var ticket = new QueueTicket
        {
            TicketCode = $"AN-{Guid.NewGuid():N}"[..8],
            PatientId = patient.Id,
            DepartmentId = departmentId,
            Status = TicketStatus.Waiting,
            CheckedInAt = DateTimeOffset.UtcNow.AddHours(-3)
        };

        arrange(ticket);

        db.Patients.Add(patient);
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
    }
}
