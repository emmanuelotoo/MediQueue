using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MediQueue.Infrastructure.Identity;
using Microsoft.IdentityModel.Tokens;

namespace MediQueue.Api.Services;

public interface ITokenService
{
    (string Token, DateTimeOffset ExpiresAt) Issue(ApplicationUser user, IEnumerable<string> roles);
}

public class TokenService : ITokenService
{
    /// <summary>
    /// A shift is long, but a token left valid overnight on a shared terminal is
    /// a liability. Staff re-authenticate once per working day.
    /// </summary>
    private const int DefaultLifetimeMinutes = 600;

    private readonly JwtOptions _options;

    public TokenService(JwtOptions options) => _options = options;

    public (string Token, DateTimeOffset ExpiresAt) Issue(ApplicationUser user, IEnumerable<string> roles)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(
            _options.LifetimeMinutes > 0 ? _options.LifetimeMinutes : DefaultLifetimeMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.FullName)
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        if (user.DepartmentId is not null)
        {
            claims.Add(new Claim(MediQueueClaims.DepartmentId, user.DepartmentId.Value.ToString()));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}

public static class MediQueueClaims
{
    /// <summary>Home department, which scopes a clinician's workstation.</summary>
    public const string DepartmentId = "mediqueue:department";
}

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "MediQueue";
    public string Audience { get; set; } = "MediQueue";
    public int LifetimeMinutes { get; set; }
}
