using MediQueue.Domain.Entities;
using MediQueue.Domain.Enums;
using MediQueue.Domain.Queues;

namespace MediQueue.Domain.Tests;

public class QueueOrderingTests
{
    private static readonly DateTimeOffset Morning = new(2026, 8, 14, 8, 0, 0, TimeSpan.Zero);

    private static QueueEngine Engine() => new(new TestClock(Morning));

    [Fact]
    public void Waiting_patients_are_served_in_the_order_they_arrived()
    {
        var second = TicketBuilder.A().Code("CAR-002").CheckedInAtMinute(10).Build();
        var first = TicketBuilder.A().Code("CAR-001").CheckedInAtMinute(0).Build();
        var third = TicketBuilder.A().Code("CAR-003").CheckedInAtMinute(20).Build();

        var ordered = Engine().OrderWaiting([second, first, third]);

        Assert.Equal(["CAR-001", "CAR-002", "CAR-003"], ordered.Select(t => t.TicketCode));
    }

    [Fact]
    public void Emergencies_are_served_before_everyone_waiting()
    {
        var normal = TicketBuilder.A().Code("CAR-001").CheckedInAtMinute(0).Build();
        var emergency = TicketBuilder.A().Code("CAR-009")
            .WithPriority(TicketPriority.Emergency).CheckedInAtMinute(90).Build();

        var ordered = Engine().OrderWaiting([normal, emergency]);

        Assert.Equal(["CAR-009", "CAR-001"], ordered.Select(t => t.TicketCode));
    }

    [Fact]
    public void Priority_patients_come_after_emergencies_but_before_normal()
    {
        var normal = TicketBuilder.A().Code("N").CheckedInAtMinute(0).Build();
        var priority = TicketBuilder.A().Code("P")
            .WithPriority(TicketPriority.Priority).CheckedInAtMinute(30).Build();
        var emergency = TicketBuilder.A().Code("E")
            .WithPriority(TicketPriority.Emergency).CheckedInAtMinute(60).Build();

        var ordered = Engine().OrderWaiting([normal, priority, emergency]);

        Assert.Equal(["E", "P", "N"], ordered.Select(t => t.TicketCode));
    }

    [Fact]
    public void Arrival_order_is_preserved_within_a_priority_band()
    {
        var later = TicketBuilder.A().Code("E-late")
            .WithPriority(TicketPriority.Emergency).CheckedInAtMinute(30).Build();
        var earlier = TicketBuilder.A().Code("E-early")
            .WithPriority(TicketPriority.Emergency).CheckedInAtMinute(10).Build();

        var ordered = Engine().OrderWaiting([later, earlier]);

        Assert.Equal(["E-early", "E-late"], ordered.Select(t => t.TicketCode));
    }

    [Theory]
    [InlineData(TicketStatus.Called)]
    [InlineData(TicketStatus.InConsultation)]
    [InlineData(TicketStatus.Completed)]
    [InlineData(TicketStatus.NoShow)]
    [InlineData(TicketStatus.Cancelled)]
    public void Only_waiting_patients_are_in_the_queue(TicketStatus status)
    {
        var waiting = TicketBuilder.A().Code("CAR-002").CheckedInAtMinute(10).Build();
        var notWaiting = TicketBuilder.A().Code("CAR-001").CheckedInAtMinute(0).WithStatus(status).Build();

        var ordered = Engine().OrderWaiting([notWaiting, waiting]);

        Assert.Equal(["CAR-002"], ordered.Select(t => t.TicketCode));
    }

    [Fact]
    public void Position_is_one_based()
    {
        var first = TicketBuilder.A().Code("CAR-001").CheckedInAtMinute(0).Build();
        var second = TicketBuilder.A().Code("CAR-002").CheckedInAtMinute(10).Build();

        var engine = Engine();

        Assert.Equal(1, engine.PositionOf(first, [first, second]));
        Assert.Equal(2, engine.PositionOf(second, [first, second]));
    }

    [Fact]
    public void A_patient_already_called_has_no_position()
    {
        var called = TicketBuilder.A().Code("CAR-001").WithStatus(TicketStatus.Called).Build();

        Assert.Null(Engine().PositionOf(called, [called]));
    }

    [Fact]
    public void Ordering_only_considers_the_patients_it_was_given()
    {
        // Callers scope by department; the engine does not filter for them, so a
        // mixed list must be pre-filtered. This documents that contract.
        var cardiology = TicketBuilder.A().Code("CAR-001").InDepartment(1).CheckedInAtMinute(0).Build();

        var ordered = Engine().OrderWaiting([cardiology]);

        Assert.Single(ordered);
    }
}
