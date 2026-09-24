using MediQueue.Domain.Enums;

namespace MediQueue.Api.Tests;

public class CheckInEndpointTests : IClassFixture<MediQueueApiFactory>
{
    private readonly MediQueueApiFactory _factory;

    public CheckInEndpointTests(MediQueueApiFactory factory) => _factory = factory;

    private static CheckInRequest ValidRequest(int departmentId) => new()
    {
        FullName = "Ama Boateng",
        PhoneNumber = "0244123456",
        DepartmentId = departmentId
    };

    [Fact]
    public async Task A_patient_can_join_the_queue_without_an_account()
    {
        var department = await _factory.AddDepartmentAsync(code: "CH1");
        var client = _factory.CreateClient();

        var response = await client.PostJsonAsync("/api/checkin", ValidRequest(department.Id));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var ticket = await response.Content.ReadJsonAsync<CheckInResponse>();
        Assert.NotNull(ticket);
        Assert.Equal("CH1-001", ticket.TicketCode);
        Assert.Equal(1, ticket.Position);
        Assert.Equal("Ama Boateng", ticket.PatientName);
        Assert.StartsWith("MRN-", ticket.MedicalRecordNumber);
    }

    [Fact]
    public async Task The_second_patient_is_told_they_are_second()
    {
        var department = await _factory.AddDepartmentAsync(code: "CH2");
        var client = _factory.CreateClient();

        await client.PostJsonAsync("/api/checkin", ValidRequest(department.Id));

        var second = ValidRequest(department.Id);
        second.FullName = "Kofi Mensah";
        second.PhoneNumber = "0201112222";
        var response = await client.PostJsonAsync("/api/checkin", second);

        var ticket = await response.Content.ReadJsonAsync<CheckInResponse>();
        Assert.Equal(2, ticket!.Position);
        Assert.Equal("CH2-002", ticket.TicketCode);
    }

    [Fact]
    public async Task A_returning_patient_keeps_the_same_folder_number()
    {
        var department = await _factory.AddDepartmentAsync(code: "CH3");
        var client = _factory.CreateClient();

        var first = await client.PostJsonAsync("/api/checkin", ValidRequest(department.Id));
        var firstTicket = await first.Content.ReadJsonAsync<CheckInResponse>();

        var second = await client.PostJsonAsync("/api/checkin", ValidRequest(department.Id));
        var secondTicket = await second.Content.ReadJsonAsync<CheckInResponse>();

        Assert.Equal(firstTicket!.MedicalRecordNumber, secondTicket!.MedicalRecordNumber);
    }

    [Fact]
    public async Task A_patient_cannot_declare_their_own_case_an_emergency()
    {
        var department = await _factory.AddDepartmentAsync(code: "CH4");
        var client = _factory.CreateClient();

        var request = ValidRequest(department.Id);
        request.Priority = TicketPriority.Emergency;

        var response = await client.PostJsonAsync("/api/checkin", request);
        var ticket = await response.Content.ReadJsonAsync<CheckInResponse>();

        var status = await client.GetJsonAsync<TicketStatusDto>($"/api/tickets/{ticket!.TicketCode}");
        Assert.Equal(TicketPriority.Normal, status!.Priority);
    }

    [Fact]
    public async Task Reception_can_mark_a_case_urgent_when_checking_someone_in()
    {
        var department = await _factory.AddDepartmentAsync(code: "CH5");
        var client = await _factory.CreateClientAsAsync(Shared.Authorization.Roles.Receptionist);

        var request = ValidRequest(department.Id);
        request.Priority = TicketPriority.Emergency;

        var response = await client.PostJsonAsync("/api/checkin", request);
        var ticket = await response.Content.ReadJsonAsync<CheckInResponse>();

        var status = await client.GetJsonAsync<TicketStatusDto>($"/api/tickets/{ticket!.TicketCode}");
        Assert.Equal(TicketPriority.Emergency, status!.Priority);
    }

    [Theory]
    [InlineData("", "0244123456")]
    [InlineData("Ama Boateng", "")]
    [InlineData("Ama Boateng", "12345")]
    [InlineData("Ama Boateng", "+1 555 0100")]
    public async Task Incomplete_details_are_rejected(string name, string phone)
    {
        var department = await _factory.AddDepartmentAsync(code: MediQueueApiFactory.UniqueDepartmentCode());
        var client = _factory.CreateClient();

        var response = await client.PostJsonAsync("/api/checkin", new CheckInRequest
        {
            FullName = name,
            PhoneNumber = phone,
            DepartmentId = department.Id
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Checking_in_to_a_department_that_does_not_exist_is_a_404()
    {
        var client = _factory.CreateClient();

        var response = await client.PostJsonAsync("/api/checkin", ValidRequest(9_999));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_ticket_code_is_a_404()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/tickets/ZZZ-999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
