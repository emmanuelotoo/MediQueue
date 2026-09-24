using MediQueue.Infrastructure.Persistence;
using Npgsql;

namespace MediQueue.Api.Tests.Persistence;

public class PostgresConnectionStringTests
{
    private static NpgsqlConnectionStringBuilder Parse(string url) =>
        new(PostgresConnectionString.FromUrl(url));

    [Fact]
    public void Every_part_of_a_heroku_url_is_carried_across()
    {
        var result = Parse("postgres://u9x:s3cret@ec2-1-2-3-4.compute-1.amazonaws.com:5433/d7abc");

        Assert.Equal("ec2-1-2-3-4.compute-1.amazonaws.com", result.Host);
        Assert.Equal(5433, result.Port);
        Assert.Equal("d7abc", result.Database);
        Assert.Equal("u9x", result.Username);
        Assert.Equal("s3cret", result.Password);
    }

    [Fact]
    public void The_postgresql_scheme_is_accepted_too()
    {
        Assert.Equal("db", Parse("postgresql://u:p@host:5432/db").Database);
    }

    [Fact]
    public void A_missing_port_means_the_postgres_default()
    {
        Assert.Equal(5432, Parse("postgres://u:p@host/db").Port);
    }

    [Fact]
    public void Escaped_characters_in_credentials_are_decoded()
    {
        var result = Parse("postgres://user%40ops:p%3Ass%2Fword@host:5432/db");

        Assert.Equal("user@ops", result.Username);
        Assert.Equal("p:ss/word", result.Password);
    }

    [Fact]
    public void Connections_are_encrypted_because_heroku_refuses_plain_ones()
    {
        Assert.Equal(SslMode.Require, Parse("postgres://u:p@host:5432/db").SslMode);
    }

    [Fact]
    public void The_pool_stays_well_inside_the_plans_connection_limit()
    {
        Assert.Equal(10, Parse("postgres://u:p@host:5432/db").MaxPoolSize);
    }

    [Theory]
    [InlineData("mysql://u:p@host:3306/db")]
    [InlineData("not a url")]
    [InlineData("")]
    public void Anything_that_is_not_a_postgres_url_is_refused(string url)
    {
        Assert.Throws<ArgumentException>(() => PostgresConnectionString.FromUrl(url));
    }
}
