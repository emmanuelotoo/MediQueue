using MediQueue.Shared.Authorization;

namespace MediQueue.Api.Tests;

public class AuthorizationTests : IClassFixture<MediQueueApiFactory>
{
    private readonly MediQueueApiFactory _factory;

    public AuthorizationTests(MediQueueApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Patient_names_are_not_served_to_anonymous_callers()
    {
        var department = await _factory.AddDepartmentAsync(code: "AZ1");
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/queue/{department.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_waiting_room_board_is_open_to_everyone()
    {
        var department = await _factory.AddDepartmentAsync(code: "AZ2");
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/board/{department.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_board_reveals_ticket_codes_but_never_names()
    {
        var department = await _factory.AddDepartmentAsync(code: "AZ3");
        var client = _factory.CreateClient();

        await client.PostJsonAsync("/api/checkin", new CheckInRequest
        {
            FullName = "Akosua Frimpong",
            PhoneNumber = "0244000111",
            DepartmentId = department.Id
        });

        var body = await client.GetStringAsync($"/api/board/{department.Id}");

        Assert.Contains("AZ3-001", body);
        Assert.DoesNotContain("Akosua", body);
    }

    [Theory]
    [InlineData(Roles.Receptionist)]
    [InlineData(Roles.Clinician)]
    [InlineData(Roles.Admin)]
    public async Task Every_staff_role_can_read_a_department_queue(string role)
    {
        var department = await _factory.AddDepartmentAsync(code: MediQueueApiFactory.UniqueDepartmentCode());
        var client = await _factory.CreateClientAsAsync(role);

        var response = await client.GetAsync($"/api/queue/{department.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_clinician_cannot_transfer_patients_between_departments()
    {
        // Moving someone to another department is a front-desk decision.
        var department = await _factory.AddDepartmentAsync(code: "AZ4");
        var other = await _factory.AddDepartmentAsync(name: "Dental", code: "AZ5");
        var client = await _factory.CreateClientAsAsync(Roles.Clinician, department.Id);

        var checkIn = await client.PostJsonAsync("/api/checkin", new CheckInRequest
        {
            FullName = "Yaw Osei",
            PhoneNumber = "0209998877",
            DepartmentId = department.Id
        });
        var ticket = await checkIn.Content.ReadJsonAsync<CheckInResponse>();

        var response = await client.PostJsonAsync(
            $"/api/tickets/{ticket!.TicketId}/transfer",
            new TransferRequest { ToDepartmentId = other.Id });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_correct_password_returns_a_token_naming_the_role()
    {
        var user = await _factory.AddStaffAsync(Roles.Admin, "login-ok@mediqueue.test");
        var client = _factory.CreateClient();

        var response = await client.PostJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = user.Email!,
            Password = MediQueueApiFactory.Password
        });

        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadJsonAsync<LoginResponse>();

        Assert.Equal(Roles.Admin, login!.Role);
        Assert.NotEmpty(login.AccessToken);
        Assert.True(login.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task A_wrong_password_is_rejected()
    {
        var user = await _factory.AddStaffAsync(Roles.Clinician, "login-bad@mediqueue.test");
        var client = _factory.CreateClient();

        var response = await client.PostJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = user.Email!,
            Password = "NotThePassword#1"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_email_fails_exactly_like_a_wrong_password()
    {
        // Identical responses, so the endpoint cannot be used to learn who works here.
        var user = await _factory.AddStaffAsync(Roles.Clinician, "login-enum@mediqueue.test");
        var client = _factory.CreateClient();

        var wrongPassword = await client.PostJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = user.Email!,
            Password = "NotThePassword#1"
        });

        var unknownAccount = await client.PostJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "nobody@mediqueue.test",
            Password = "NotThePassword#1"
        });

        Assert.Equal(wrongPassword.StatusCode, unknownAccount.StatusCode);
        Assert.Equal(
            await wrongPassword.Content.ReadAsStringAsync(),
            await unknownAccount.Content.ReadAsStringAsync());
    }
}
