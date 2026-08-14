using MediQueue.Domain.Entities;
using MediQueue.Domain.Queues;

namespace MediQueue.Domain.Tests;

public class TicketCodeTests
{
    private static readonly DateTimeOffset Morning = new(2026, 8, 14, 8, 0, 0, TimeSpan.Zero);

    private static QueueEngine EngineAt(DateTimeOffset now) => new(new TestClock(now));

    [Fact]
    public void First_ticket_of_the_day_is_numbered_001()
    {
        var engine = EngineAt(Morning);

        var code = engine.NextTicketCode("CAR", Array.Empty<QueueTicket>());

        Assert.Equal("CAR-001", code);
    }

    [Fact]
    public void Sequence_continues_from_the_highest_code_issued_today()
    {
        var engine = EngineAt(Morning);
        var issuedToday = new QueueTicket[]
        {
            TicketBuilder.A().Code("CAR-001").CheckedInAt(Morning),
            TicketBuilder.A().Code("CAR-002").CheckedInAt(Morning)
        };

        var code = engine.NextTicketCode("CAR", issuedToday);

        Assert.Equal("CAR-003", code);
    }

    [Fact]
    public void Sequence_resets_the_next_day()
    {
        var engine = EngineAt(Morning.AddDays(1));
        var issuedYesterday = new QueueTicket[]
        {
            TicketBuilder.A().Code("CAR-001").CheckedInAt(Morning),
            TicketBuilder.A().Code("CAR-002").CheckedInAt(Morning)
        };

        var code = engine.NextTicketCode("CAR", issuedYesterday);

        Assert.Equal("CAR-001", code);
    }

    [Fact]
    public void Sequence_is_independent_per_department()
    {
        var engine = EngineAt(Morning);
        var issuedToday = new QueueTicket[]
        {
            TicketBuilder.A().Code("CAR-001").CheckedInAt(Morning),
            TicketBuilder.A().Code("CAR-002").CheckedInAt(Morning)
        };

        var code = engine.NextTicketCode("PED", issuedToday);

        Assert.Equal("PED-001", code);
    }

    [Fact]
    public void Cancelled_tickets_still_consume_their_number()
    {
        // A patient who abandons the queue keeps their printed number; reusing it
        // would put two people in the waiting room holding the same ticket.
        var engine = EngineAt(Morning);
        var issuedToday = new QueueTicket[]
        {
            TicketBuilder.A().Code("CAR-001").CheckedInAt(Morning),
            TicketBuilder.A().Code("CAR-002").WithStatus(Domain.Enums.TicketStatus.Cancelled).CheckedInAt(Morning)
        };

        var code = engine.NextTicketCode("CAR", issuedToday);

        Assert.Equal("CAR-003", code);
    }

    [Fact]
    public void Department_code_is_normalised_to_upper_case()
    {
        var engine = EngineAt(Morning);

        var code = engine.NextTicketCode("car", Array.Empty<QueueTicket>());

        Assert.Equal("CAR-001", code);
    }

    [Fact]
    public void Sequence_beyond_999_keeps_counting_without_truncating()
    {
        var engine = EngineAt(Morning);
        var issuedToday = new[] { (QueueTicket)TicketBuilder.A().Code("CAR-999").CheckedInAt(Morning) };

        var code = engine.NextTicketCode("CAR", issuedToday);

        Assert.Equal("CAR-1000", code);
    }
}
