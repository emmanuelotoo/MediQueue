namespace MediQueue.Client.Tests;

public class QueueTableTests : BunitContext
{
    private static QueueEntryDto Entry(
        string code = "CAR-001",
        string name = "Ama Boateng",
        TicketPriority priority = TicketPriority.Normal,
        int? position = 1) =>
        new()
        {
            TicketId = Guid.NewGuid(),
            TicketCode = code,
            PatientName = name,
            MedicalRecordNumber = "MRN-00001",
            Priority = priority,
            Status = TicketStatus.Waiting,
            CheckedInAt = DateTimeOffset.UtcNow.AddMinutes(-20),
            WaitedMinutes = 20,
            Position = position
        };

    [Fact]
    public void Each_waiting_patient_gets_a_row()
    {
        var component = Render<QueueTable>(parameters => parameters
            .Add(p => p.Entries, new List<QueueEntryDto>
            {
                Entry("CAR-001", "Ama Boateng"),
                Entry("CAR-002", "Kofi Mensah", position: 2)
            }));

        Assert.Equal(2, component.FindAll(".queue__row").Count);
        Assert.Contains("Ama Boateng", component.Markup);
        Assert.Contains("Kofi Mensah", component.Markup);
    }

    [Fact]
    public void An_empty_queue_says_so_rather_than_showing_nothing()
    {
        var component = Render<QueueTable>(parameters => parameters
            .Add(p => p.Entries, new List<QueueEntryDto>())
            .Add(p => p.EmptyTitle, "Nobody is waiting"));

        Assert.Contains("Nobody is waiting", component.Markup);
        Assert.Empty(component.FindAll(".queue__row"));
    }

    [Theory]
    [InlineData(TicketPriority.Emergency, "queue__row--emergency")]
    [InlineData(TicketPriority.Priority, "queue__row--priority")]
    public void Priority_marks_the_row_edge_as_well_as_showing_a_label(
        TicketPriority priority,
        string expectedClass)
    {
        var component = Render<QueueTable>(parameters => parameters
            .Add(p => p.Entries, new List<QueueEntryDto> { Entry(priority: priority) }));

        var row = component.Find(".queue__row");
        Assert.Contains(expectedClass, row.ClassName);

        // Never colour alone: the badge spells the priority out.
        Assert.Contains(priority.ToString(), component.Markup);
    }

    [Fact]
    public void A_normal_patient_carries_no_priority_badge()
    {
        var component = Render<QueueTable>(parameters => parameters
            .Add(p => p.Entries, new List<QueueEntryDto> { Entry() }));

        Assert.Empty(component.FindAll(".badge--emergency"));
        Assert.Empty(component.FindAll(".badge--priority"));
    }

    [Fact]
    public void A_patient_in_a_room_shows_the_room_number()
    {
        var entry = Entry(position: null);
        entry.Status = TicketStatus.InConsultation;
        entry.RoomNumber = "3";

        var component = Render<QueueTable>(parameters => parameters
            .Add(p => p.Entries, new List<QueueEntryDto> { entry })
            .Add(p => p.ShowStatus, true));

        Assert.Contains("Room", component.Find(".room-chip").TextContent);
        Assert.Contains("3", component.Find(".room-chip").TextContent);
    }

    [Fact]
    public void A_patient_who_just_moved_up_is_highlighted()
    {
        var entry = Entry();

        var component = Render<QueueTable>(parameters => parameters
            .Add(p => p.Entries, new List<QueueEntryDto> { entry })
            .Add(p => p.RecentlyChanged, new HashSet<Guid> { entry.TicketId }));

        Assert.Contains("queue__row--advanced", component.Find(".queue__row").ClassName);
    }

    [Fact]
    public void Row_actions_come_from_the_console_that_hosts_the_table()
    {
        var component = Render<QueueTable>(parameters => parameters
            .Add(p => p.Entries, new List<QueueEntryDto> { Entry() })
            .Add(p => p.Actions, entry =>
                builder =>
                {
                    builder.OpenElement(0, "button");
                    builder.AddContent(1, $"Call {entry.TicketCode}");
                    builder.CloseElement();
                }));

        Assert.Contains("Call CAR-001", component.Markup);
    }
}
