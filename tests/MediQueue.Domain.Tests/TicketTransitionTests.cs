using MediQueue.Domain.Entities;
using MediQueue.Domain.Enums;
using MediQueue.Domain.Queues;

namespace MediQueue.Domain.Tests;

public class TicketTransitionTests
{
    private static readonly DateTimeOffset Morning = new(2026, 8, 14, 8, 0, 0, TimeSpan.Zero);

    private readonly TestClock _clock = new(Morning);
    private readonly QueueEngine _engine;

    public TicketTransitionTests() => _engine = new QueueEngine(_clock);

    [Fact]
    public void Calling_a_patient_records_the_room_and_the_time()
    {
        var ticket = TicketBuilder.A().Build();
        _clock.Set(Morning.AddMinutes(25));

        _engine.Call(ticket, room: "3", staffUserId: "nurse-1", departmentTickets: [ticket]);

        Assert.Equal(TicketStatus.Called, ticket.Status);
        Assert.Equal("3", ticket.RoomNumber);
        Assert.Equal("nurse-1", ticket.AssignedStaffId);
        Assert.Equal(Morning.AddMinutes(25), ticket.CalledAt);
    }

    [Fact]
    public void A_room_can_only_hold_one_patient_at_a_time()
    {
        var occupying = TicketBuilder.A().Code("CAR-001")
            .WithStatus(TicketStatus.InConsultation).InRoom("3").Build();
        var next = TicketBuilder.A().Code("CAR-002").Build();

        var error = Assert.Throws<RoomOccupiedException>(
            () => _engine.Call(next, room: "3", staffUserId: "nurse-1", departmentTickets: [occupying, next]));

        Assert.Contains("3", error.Message);
        Assert.Equal(TicketStatus.Waiting, next.Status);
    }

    [Fact]
    public void A_room_frees_up_once_its_patient_is_done()
    {
        var finished = TicketBuilder.A().Code("CAR-001")
            .WithStatus(TicketStatus.Completed).InRoom("3").Build();
        var next = TicketBuilder.A().Code("CAR-002").Build();

        _engine.Call(next, room: "3", staffUserId: "nurse-1", departmentTickets: [finished, next]);

        Assert.Equal(TicketStatus.Called, next.Status);
    }

    [Fact]
    public void A_consultation_runs_from_start_to_completion()
    {
        var ticket = TicketBuilder.A().WithStatus(TicketStatus.Called).InRoom("3").Build();

        _clock.Set(Morning.AddMinutes(30));
        _engine.StartConsultation(ticket);
        Assert.Equal(TicketStatus.InConsultation, ticket.Status);
        Assert.Equal(Morning.AddMinutes(30), ticket.ConsultationStartedAt);

        _clock.Set(Morning.AddMinutes(48));
        _engine.Complete(ticket);
        Assert.Equal(TicketStatus.Completed, ticket.Status);
        Assert.Equal(Morning.AddMinutes(48), ticket.CompletedAt);
        Assert.Equal(TimeSpan.FromMinutes(18), ticket.ServiceDuration);
    }

    [Fact]
    public void A_patient_who_does_not_answer_is_marked_no_show()
    {
        var ticket = TicketBuilder.A().WithStatus(TicketStatus.Called).InRoom("3").Build();

        _engine.MarkNoShow(ticket);

        Assert.Equal(TicketStatus.NoShow, ticket.Status);
        Assert.Null(ticket.RoomNumber);
    }

    [Fact]
    public void A_no_show_who_returns_keeps_their_original_place()
    {
        var returning = TicketBuilder.A().Code("CAR-001")
            .CheckedInAtMinute(0).WithStatus(TicketStatus.NoShow).InRoom("3").Build();
        var arrivedLater = TicketBuilder.A().Code("CAR-002").CheckedInAtMinute(20).Build();

        _engine.Requeue(returning);

        Assert.Equal(TicketStatus.Waiting, returning.Status);
        Assert.Null(returning.RoomNumber);
        Assert.Null(returning.CalledAt);

        var ordered = _engine.OrderWaiting([arrivedLater, returning]);
        Assert.Equal(["CAR-001", "CAR-002"], ordered.Select(t => t.TicketCode));
    }

    [Fact]
    public void A_waiting_patient_can_leave()
    {
        var ticket = TicketBuilder.A().Build();

        _engine.Cancel(ticket);

        Assert.Equal(TicketStatus.Cancelled, ticket.Status);
    }

    [Theory]
    [InlineData(TicketStatus.Completed)]
    [InlineData(TicketStatus.Cancelled)]
    [InlineData(TicketStatus.NoShow)]
    [InlineData(TicketStatus.InConsultation)]
    public void A_patient_cannot_be_called_unless_they_are_waiting(TicketStatus status)
    {
        var ticket = TicketBuilder.A().WithStatus(status).Build();

        Assert.Throws<InvalidTicketTransitionException>(
            () => _engine.Call(ticket, "3", "nurse-1", [ticket]));
    }

    [Theory]
    [InlineData(TicketStatus.Waiting)]
    [InlineData(TicketStatus.InConsultation)]
    [InlineData(TicketStatus.Completed)]
    public void A_consultation_cannot_start_unless_the_patient_was_called(TicketStatus status)
    {
        var ticket = TicketBuilder.A().WithStatus(status).Build();

        Assert.Throws<InvalidTicketTransitionException>(() => _engine.StartConsultation(ticket));
    }

    [Theory]
    [InlineData(TicketStatus.Waiting)]
    [InlineData(TicketStatus.Called)]
    [InlineData(TicketStatus.Completed)]
    public void A_consultation_cannot_complete_unless_it_started(TicketStatus status)
    {
        var ticket = TicketBuilder.A().WithStatus(status).Build();

        Assert.Throws<InvalidTicketTransitionException>(() => _engine.Complete(ticket));
    }

    [Theory]
    [InlineData(TicketStatus.Waiting)]
    [InlineData(TicketStatus.Completed)]
    public void Only_a_called_patient_can_be_marked_no_show(TicketStatus status)
    {
        var ticket = TicketBuilder.A().WithStatus(status).Build();

        Assert.Throws<InvalidTicketTransitionException>(() => _engine.MarkNoShow(ticket));
    }

    [Theory]
    [InlineData(TicketStatus.Waiting)]
    [InlineData(TicketStatus.Completed)]
    public void Only_a_no_show_can_be_requeued(TicketStatus status)
    {
        var ticket = TicketBuilder.A().WithStatus(status).Build();

        Assert.Throws<InvalidTicketTransitionException>(() => _engine.Requeue(ticket));
    }

    [Theory]
    [InlineData(TicketStatus.Completed)]
    [InlineData(TicketStatus.InConsultation)]
    public void A_patient_already_seen_cannot_leave_the_queue(TicketStatus status)
    {
        var ticket = TicketBuilder.A().WithStatus(status).Build();

        Assert.Throws<InvalidTicketTransitionException>(() => _engine.Cancel(ticket));
    }

    [Fact]
    public void The_error_names_both_the_status_it_found_and_the_one_it_needed()
    {
        var ticket = TicketBuilder.A().WithStatus(TicketStatus.Completed).Build();

        var error = Assert.Throws<InvalidTicketTransitionException>(() => _engine.StartConsultation(ticket));

        Assert.Contains("Completed", error.Message);
        Assert.Contains("Called", error.Message);
    }

    [Fact]
    public void Priority_can_be_raised_while_a_patient_waits()
    {
        var ticket = TicketBuilder.A().Build();

        _engine.ChangePriority(ticket, TicketPriority.Emergency);

        Assert.Equal(TicketPriority.Emergency, ticket.Priority);
    }

    [Fact]
    public void Priority_cannot_be_changed_once_the_patient_has_been_seen()
    {
        var ticket = TicketBuilder.A().WithStatus(TicketStatus.Completed).Build();

        Assert.Throws<InvalidTicketTransitionException>(
            () => _engine.ChangePriority(ticket, TicketPriority.Emergency));
    }

    [Fact]
    public void A_waiting_patient_can_be_moved_to_another_department()
    {
        var ticket = TicketBuilder.A().InDepartment(1).Build();

        _engine.Transfer(ticket, toDepartmentId: 4, newTicketCode: "PED-007");

        Assert.Equal(4, ticket.DepartmentId);
        Assert.Equal("PED-007", ticket.TicketCode);
        Assert.Equal(TicketStatus.Waiting, ticket.Status);
    }

    [Fact]
    public void Transferring_resets_the_patient_to_the_back_of_the_new_queue()
    {
        // They are new to that department's clinicians, so they join its queue
        // at the moment of transfer rather than keeping their original arrival.
        var ticket = TicketBuilder.A().InDepartment(1).CheckedInAtMinute(0).Build();
        _clock.Set(Morning.AddMinutes(40));

        _engine.Transfer(ticket, toDepartmentId: 4, newTicketCode: "PED-007");

        Assert.Equal(Morning.AddMinutes(40), ticket.CheckedInAt);
    }
}
