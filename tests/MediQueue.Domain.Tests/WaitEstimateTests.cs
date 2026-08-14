using MediQueue.Domain.Entities;
using MediQueue.Domain.Enums;
using MediQueue.Domain.Queues;

namespace MediQueue.Domain.Tests;

public class WaitEstimateTests
{
    private static readonly DateTimeOffset Morning = new(2026, 8, 14, 8, 0, 0, TimeSpan.Zero);

    private readonly TestClock _clock = new(Morning.AddHours(3));
    private readonly QueueEngine _engine;

    public WaitEstimateTests() => _engine = new QueueEngine(_clock);

    private static QueueTicket Served(int startMinute, int minutesLong) =>
        TicketBuilder.A().Served(Morning.AddMinutes(startMinute), Morning.AddMinutes(startMinute + minutesLong));

    [Fact]
    public void Wait_is_the_departments_average_consultation_times_the_queue_position()
    {
        var waiting = TicketBuilder.A().CheckedInAtMinute(100).Build();
        QueueTicket[] history = [Served(0, 10), Served(20, 20), Served(50, 30), waiting];

        var estimate = _engine.EstimateWait(waiting, history, defaultServiceMinutes: 15);

        // Average of 10, 20, 30 is 20 minutes; the patient is first in line.
        Assert.Equal(TimeSpan.FromMinutes(20), estimate);
    }

    [Fact]
    public void Being_further_back_multiplies_the_wait()
    {
        var ahead = TicketBuilder.A().Code("A").CheckedInAtMinute(100).Build();
        var behind = TicketBuilder.A().Code("B").CheckedInAtMinute(110).Build();
        QueueTicket[] history = [Served(0, 10), Served(20, 20), Served(50, 30), ahead, behind];

        var estimate = _engine.EstimateWait(behind, history, defaultServiceMinutes: 15);

        Assert.Equal(TimeSpan.FromMinutes(40), estimate);
    }

    [Fact]
    public void Before_the_day_has_data_the_departments_default_is_used()
    {
        var waiting = TicketBuilder.A().CheckedInAtMinute(10).Build();
        QueueTicket[] history = [Served(0, 30), Served(0, 30), waiting];

        var estimate = _engine.EstimateWait(waiting, history, defaultServiceMinutes: 15);

        // Only two consultations have finished, which is too thin to average on.
        Assert.Equal(TimeSpan.FromMinutes(15), estimate);
    }

    [Fact]
    public void Yesterdays_consultations_do_not_count_towards_todays_average()
    {
        var waiting = TicketBuilder.A().CheckedInAtMinute(10).Build();
        QueueTicket[] history =
        [
            TicketBuilder.A().Served(Morning.AddDays(-1), Morning.AddDays(-1).AddHours(1)),
            TicketBuilder.A().Served(Morning.AddDays(-1), Morning.AddDays(-1).AddHours(1)),
            TicketBuilder.A().Served(Morning.AddDays(-1), Morning.AddDays(-1).AddHours(1)),
            waiting
        ];

        var estimate = _engine.EstimateWait(waiting, history, defaultServiceMinutes: 15);

        Assert.Equal(TimeSpan.FromMinutes(15), estimate);
    }

    [Fact]
    public void An_emergency_jumping_the_queue_shortens_no_one_elses_estimate()
    {
        var normal = TicketBuilder.A().Code("N").CheckedInAtMinute(100).Build();
        var emergency = TicketBuilder.A().Code("E")
            .WithPriority(TicketPriority.Emergency).CheckedInAtMinute(120).Build();
        QueueTicket[] history = [Served(0, 10), Served(20, 20), Served(50, 30), normal, emergency];

        var estimate = _engine.EstimateWait(normal, history, defaultServiceMinutes: 15);

        // The emergency now sits ahead, so the normal patient is second: 2 x 20.
        Assert.Equal(TimeSpan.FromMinutes(40), estimate);
    }

    [Fact]
    public void A_patient_no_longer_waiting_has_no_estimate()
    {
        var called = TicketBuilder.A().WithStatus(TicketStatus.Called).Build();

        Assert.Null(_engine.EstimateWait(called, [called], defaultServiceMinutes: 15));
    }
}
