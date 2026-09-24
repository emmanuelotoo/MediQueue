using Npgsql;

namespace MediQueue.Infrastructure.Persistence;

/// <summary>
/// Heroku hands the app its database as a URL
/// (<c>postgres://user:password@host:port/database</c>); Npgsql wants
/// key-value pairs. This is the translation, plus the two settings Heroku
/// Postgres needs that the URL does not carry.
/// </summary>
public static class PostgresConnectionString
{
    /// <summary>
    /// Essential-0 allows 20 connections. Ten leaves room for the release
    /// phase and a <c>heroku pg:psql</c> session while the web dyno is busy.
    /// </summary>
    public const int MaxPoolSize = 10;

    public static string FromUrl(string databaseUrl)
    {
        if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
        {
            throw new ArgumentException(
                "DATABASE_URL must look like postgres://user:password@host:port/database.",
                nameof(databaseUrl));
        }

        var credentials = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : null,

            // Heroku Postgres refuses unencrypted connections. Its certificates
            // are not issued by a public CA, and Require encrypts without
            // validating the chain.
            SslMode = SslMode.Require,
            MaxPoolSize = MaxPoolSize
        };

        return builder.ConnectionString;
    }
}
