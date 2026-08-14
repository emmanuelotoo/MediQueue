using MediQueue.Api.Services;
using MediQueue.Infrastructure.Identity;
using MediQueue.Infrastructure.Persistence;
using MediQueue.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediQueue.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly ITokenService _tokens;
    private readonly MediQueueDbContext _db;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<ApplicationUser> users,
        ITokenService tokens,
        MediQueueDbContext db,
        ILogger<AuthController> logger)
    {
        _users = users;
        _tokens = tokens;
        _db = db;
        _logger = logger;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await _users.FindByEmailAsync(request.Email);

        // Same response whether the account is unknown or the password is wrong,
        // so the endpoint cannot be used to discover who works here.
        if (user is null || !user.IsActive)
        {
            return Unauthorized(Problem("Email or password is incorrect."));
        }

        if (await _users.IsLockedOutAsync(user))
        {
            _logger.LogWarning("Login attempt on locked account {Email}.", request.Email);
            return Unauthorized(Problem("This account is temporarily locked. Try again shortly."));
        }

        if (!await _users.CheckPasswordAsync(user, request.Password))
        {
            await _users.AccessFailedAsync(user);
            return Unauthorized(Problem("Email or password is incorrect."));
        }

        await _users.ResetAccessFailedCountAsync(user);

        var roles = await _users.GetRolesAsync(user);
        var (token, expiresAt) = _tokens.Issue(user, roles);

        var departmentName = user.DepartmentId is null
            ? null
            : await _db.Departments
                .Where(d => d.Id == user.DepartmentId)
                .Select(d => d.Name)
                .FirstOrDefaultAsync(ct);

        _logger.LogInformation("{Email} signed in.", user.Email);

        return Ok(new LoginResponse
        {
            AccessToken = token,
            ExpiresAt = expiresAt,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            Role = roles.FirstOrDefault() ?? string.Empty,
            DepartmentId = user.DepartmentId,
            DepartmentName = departmentName
        });
    }

    private static ProblemDetails Problem(string detail) => new()
    {
        Status = StatusCodes.Status401Unauthorized,
        Title = "Sign-in failed",
        Detail = detail
    };
}
