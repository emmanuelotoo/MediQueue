using System.Text;

namespace MediQueue.Api;

/// <summary>
/// Configuration mistakes that would otherwise surface later, as a failure
/// with no hint of its cause, or not at all. Startup stops on them and says
/// what to change.
/// </summary>
public static class ConfigurationChecks
{
    /// <summary>HS256 will not sign with a key shorter than 256 bits.</summary>
    public const int MinimumJwtKeyBytes = 32;

    /// <summary>
    /// The nested settings the app reads. As environment variables, and so as
    /// Heroku config vars, each is written with a double underscore.
    /// </summary>
    private static readonly string[] Settings =
    [
        "Database:Provider",
        "ConnectionStrings:Default",
        "Jwt:Key",
        "Jwt:Issuer",
        "Jwt:Audience",
        "Jwt:LifetimeMinutes",
        "Seed:DemoData",
        "Seed:StaffPassword",
        "Hosting:TrustForwardedHeaders"
    ];

    /// <summary>
    /// .NET reads <c>Jwt__Key</c> as <c>Jwt:Key</c> but <c>Jwt_Key</c> as an
    /// unrelated setting, so one underscore silently leaves the real setting
    /// unset. Reports names only, never values: some of them are secrets.
    /// </summary>
    public static void RejectSingleUnderscoreNames(IConfiguration configuration)
    {
        var misnamed = Settings
            .Where(setting => configuration[setting] is null
                && configuration[setting.Replace(':', '_')] is not null)
            .Select(setting => $"{setting.Replace(':', '_')} should be {setting.Replace(":", "__")}")
            .ToList();

        if (misnamed.Count > 0)
        {
            throw new InvalidOperationException(
                "These settings are named with one underscore where .NET needs two, so the app cannot see them: "
                + string.Join("; ", misnamed) + ". Rename them.");
        }
    }

    /// <summary>
    /// A short key lets the app start and then fails every sign-in, because
    /// the key is first used when a token is signed.
    /// </summary>
    public static void RequireStrongJwtKey(string key)
    {
        var bytes = Encoding.UTF8.GetByteCount(key);

        if (bytes < MinimumJwtKeyBytes)
        {
            throw new InvalidOperationException(
                $"Jwt:Key is {bytes} bytes; signing tokens needs at least {MinimumJwtKeyBytes}. "
                + "Generate one as the README describes.");
        }
    }
}
