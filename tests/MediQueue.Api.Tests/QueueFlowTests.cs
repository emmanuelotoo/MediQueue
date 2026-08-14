using MediQueue.Domain.Enums;
using MediQueue.Shared.Authorization;

namespace MediQueue.Api.Tests;

public class QueueFlowTests : IClassFixture<MediQueueApiFactory>
{
    private readonly MediQueueApiFactory _factory;

    public QueueFlowTests(MediQueueApiFactory factory) => _factory = factory;

    private async Task<(HttpClient Staff, int DepartmentId)> ClinicAsync(string code)
    {
        var department = await _factory.AddDepartmentAsync(code: code);
        var staff = await _factory.CreateClientAsAsync(Roles.Receptionist);
        return (staff, department.Id);
    }

    private static async Task<CheckInResponse> CheckInAsync(HttpClient client, int departmentId, string name)
    {
        var response = await client.PostJsonAsync("/api/checkin", new CheckInRequest
        {
            FullName = name,
            PhoneNumber = $"02{Random.Shared.Next(10000000, 99999999)}",
            DepartmentId = departmentId
        });

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadJsonAsync<CheckInResponse>())!;
    }

    [Fact]
    public async Task A_patient_is_called_seen_and_sent_home()
    {
        var (staff, departmentId) = await ClinicAsync("FL1");
        var ticket = await CheckInAsync(staff, departmentId, "Esi Quartey");

        (await staff.PostJsonAsync($"/api/tickets/{ticket.TicketId}/call",
            new CallPatientRequest { Room = "2" })).EnsureSuccessStatusCode();

        var called = await staff.GetJsonAsync<TicketStatusDto>($"/api/tickets/{ticket.TicketCode}");
        Assert.Equal(TicketStatus.Called, called!.Status);
        Assert.Equal("2", called.RoomNumber);
        Assert.Null(called.Position);

        (await staff.PostAsync($"/api/tickets/{ticket.TicketId}/start", null)).EnsureSuccessStatusCode();
        (await staff.PostAsync($"/api/tickets/{ticket.TicketId}/complete", null)).EnsureSuccessStatusCode();

        var done = await staff.GetJsonAsync<TicketStatusDto>($"/api/tickets/{ticket.TicketCode}");
        Assert.Equal(TicketStatus.Completed, done!.Status);
    }

    [Fact]
    public async Task Calling_the_same_patient_twice_is_refused()
    {
        var (staff, departmentId) = await ClinicAsync("FL2");
        var ticket = await CheckInAsync(staff, departmentId, "Kwesi Ansah");

        await staff.PostJsonAsync($"/api/tickets/{ticket.TicketId}/call", new CallPatientRequest { Room = "1" });
        var again = await staff.PostJsonAsync($"/api/tickets/{ticket.TicketId}/call", new CallPatientRequest { Room = "1" });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Two_patients_cannot_be_sent_to_the_same_room()
    {
        var (staff, departmentId) = await ClinicAsync("FL3");
        var first = await CheckInAsync(staff, departmentId, "Adwoa Darko");
        var second = await CheckInAsync(staff, departmentId, "Nana Appiah");

        await staff.PostJsonAsync($"/api/tickets/{first.TicketId}/call", new CallPatientRequest { Room = "1" });
        var clash = await staff.PostJsonAsync($"/api/tickets/{second.TicketId}/call", new CallPatientRequest { Room = "1" });

        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);

        var problem = await clash.Content.ReadJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Contains(first.TicketCode, problem!.Detail);
    }

    [Fact]
    public async Task A_consultation_cannot_be_completed_before_it_starts()
    {
        var (staff, departmentId) = await ClinicAsync("FL4");
        var ticket = await CheckInAsync(staff, departmentId, "Fiifi Baidoo");

        await staff.PostJsonAsync($"/api/tickets/{ticket.TicketId}/call", new CallPatientRequest { Room = "1" });
        var response = await staff.PostAsync($"/api/tickets/{ticket.TicketId}/complete", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task An_escalated_patient_moves_to_the_front_of_the_queue()
    {
        var (staff, departmentId) = await ClinicAsync("FL5");
        await CheckInAsync(staff, departmentId, "Abena Ofori");
        await CheckInAsync(staff, departmentId, "Kojo Amoah");
        var last = await CheckInAsync(staff, departmentId, "Yaa Gyasi");

        Assert.Equal(3, last.Position);

        (await staff.PostJsonAsync(
            $"/api/tickets/{last.TicketId}/priority",
            new ChangePriorityRequest { Priority = TicketPriority.Emergency })).EnsureSuccessStatusCode();

        var status = await staff.GetJsonAsync<TicketStatusDto>($"/api/tickets/{last.TicketCode}");
        Assert.Equal(1, status!.Position);
        Assert.Equal(0, status.AheadOfYou);
    }

    [Fact]
    public async Task A_patient_who_steps_out_can_be_returned_to_their_place()
    {
        var (staff, departmentId) = await ClinicAsync("FL6");
        var first = await CheckInAsync(staff, departmentId, "Selorm Kudzo");
        await CheckInAsync(staff, departmentId, "Dela Anyidoho");

        await staff.PostJsonAsync($"/api/tickets/{first.TicketId}/call", new CallPatientRequest { Room = "1" });
        (await staff.PostAsync($"/api/tickets/{first.TicketId}/noshow", null)).EnsureSuccessStatusCode();

        var absent = await staff.GetJsonAsync<TicketStatusDto>($"/api/tickets/{first.TicketCode}");
        Assert.Equal(TicketStatus.NoShow, absent!.Status);

        (await staff.PostAsync($"/api/tickets/{first.TicketId}/requeue", null)).EnsureSuccessStatusCode();

        var back = await staff.GetJsonAsync<TicketStatusDto>($"/api/tickets/{first.TicketCode}");
        Assert.Equal(TicketStatus.Waiting, back!.Status);
        Assert.Equal(1, back.Position);
    }

    [Fact]
    public async Task A_transferred_patient_gets_the_new_departments_ticket_code()
    {
        var (staff, departmentId) = await ClinicAsync("FL7");
        var destination = await _factory.AddDepartmentAsync(name: "Ophthalmology", code: "FL8");
        var ticket = await CheckInAsync(staff, departmentId, "Ibrahim Musah");

        (await staff.PostJsonAsync(
            $"/api/tickets/{ticket.TicketId}/transfer",
            new TransferRequest { ToDepartmentId = destination.Id })).EnsureSuccessStatusCode();

        var moved = await staff.GetJsonAsync<TicketStatusDto>("/api/tickets/FL8-001");
        Assert.NotNull(moved);
        Assert.Equal(destination.Id, moved.DepartmentId);
        Assert.Equal(TicketStatus.Waiting, moved.Status);
    }

    [Fact]
    public async Task The_console_shows_who_is_waiting_and_who_is_in_a_room()
    {
        var (staff, departmentId) = await ClinicAsync("FL9");
        var inRoom = await CheckInAsync(staff, departmentId, "Grace Larbi");
        await CheckInAsync(staff, departmentId, "Isaac Nkrumah");

        await staff.PostJsonAsync($"/api/tickets/{inRoom.TicketId}/call", new CallPatientRequest { Room = "1" });

        var queue = await staff.GetJsonAsync<DepartmentQueueDto>($"/api/queue/{departmentId}");

        Assert.Single(queue!.Waiting);
        Assert.Single(queue.InProgress);
        Assert.Equal("Isaac Nkrumah", queue.Waiting[0].PatientName);
        Assert.Equal(1, queue.Waiting[0].Position);
        Assert.Equal("1", queue.InProgress[0].RoomNumber);
    }

    [Fact]
    public async Task The_waiting_room_board_lists_who_is_being_seen_and_who_is_next()
    {
        var (staff, departmentId) = await ClinicAsync("FLA");
        var inRoom = await CheckInAsync(staff, departmentId, "Comfort Adjei");
        var next = await CheckInAsync(staff, departmentId, "Prince Danso");

        await staff.PostJsonAsync($"/api/tickets/{inRoom.TicketId}/call", new CallPatientRequest { Room = "3" });

        var board = await _factory.CreateClient().GetJsonAsync<BoardDto>($"/api/board/{departmentId}");

        Assert.Equal(inRoom.TicketCode, board!.NowServing.Single().TicketCode);
        Assert.Equal("3", board.NowServing.Single().RoomNumber);
        Assert.Equal(next.TicketCode, board.UpNext.Single());
        Assert.Equal(1, board.WaitingCount);
    }
}
