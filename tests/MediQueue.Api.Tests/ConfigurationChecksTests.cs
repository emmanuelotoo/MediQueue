using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace MediQueue.Api.Tests;

/// <summary>
/// Each of these mistakes reached the first Heroku deploy, and each surfaced as
/// a crash elsewhere or not at all. Startup now names them.
/// </summary>
public class ConfigurationChecksTests : IClassFixture<MediQueueApiFactory>
{
    private readonly MediQueueApiFactory _factory;

    public ConfigurationChecksTests(MediQueueApiFactory factory) => _factory = factory;

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void A_setting_named_with_one_underscore_stops_startup_and_names_the_fix()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ConfigurationChecks.RejectSingleUnderscoreNames(Config(new() { ["Jwt_Key"] = "value" })));

        Assert.Contains("Jwt__Key", error.Message);
    }

    [Fact]
    public void Every_misnamed_setting_is_reported_at_once()
    {
        // One name per failed deploy would take five deploys to get through.
        var error = Assert.Throws<InvalidOperationException>(
            () => ConfigurationChecks.RejectSingleUnderscoreNames(Config(new()
            {
                ["Database_Provider"] = "Postgres",
                ["Seed_StaffPassword"] = "value",
                ["Hosting_TrustForwardedHeaders"] = "true"
            })));

        Assert.Contains("Database__Provider", error.Message);
        Assert.Contains("Seed__StaffPassword", error.Message);
        Assert.Contains("Hosting__TrustForwardedHeaders", error.Message);
    }

    [Fact]
    public void Misnamed_settings_are_found_whatever_their_case()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ConfigurationChecks.RejectSingleUnderscoreNames(Config(new() { ["SEED_DEMODATA"] = "true" })));

        Assert.Contains("Seed__DemoData", error.Message);
    }

    [Fact]
    public void The_error_names_settings_but_never_shows_their_values()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ConfigurationChecks.RejectSingleUnderscoreNames(Config(new() { ["Jwt_Key"] = "a-secret-value" })));

        Assert.DoesNotContain("a-secret-value", error.Message);
    }

    [Fact]
    public void A_leftover_old_name_is_ignored_once_the_real_setting_exists()
    {
        ConfigurationChecks.RejectSingleUnderscoreNames(Config(new()
        {
            ["Jwt_Key"] = "old",
            ["Jwt:Key"] = "new"
        }));
    }

    [Fact]
    public void Herokus_DATABASE_URL_is_not_mistaken_for_a_misnamed_setting()
    {
        ConfigurationChecks.RejectSingleUnderscoreNames(Config(new() { ["DATABASE_URL"] = "postgres://u:p@host:5432/db" }));
    }

    [Fact]
    public void A_jwt_key_shorter_than_256_bits_stops_startup()
    {
        // HS256 refuses to sign with it, so every sign-in would fail with a 500.
        var error = Assert.Throws<InvalidOperationException>(
            () => ConfigurationChecks.RequireStrongJwtKey(new string('k', 31)));

        Assert.Contains("32", error.Message);
    }

    [Fact]
    public void The_short_key_error_never_shows_the_key()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ConfigurationChecks.RequireStrongJwtKey("short-secret"));

        Assert.DoesNotContain("short-secret", error.Message);
    }

    [Fact]
    public void A_256_bit_jwt_key_is_accepted()
    {
        ConfigurationChecks.RequireStrongJwtKey(new string('k', 32));
    }

    [Fact]
    public void The_app_runs_the_name_check_before_it_starts()
    {
        var factory = _factory.WithWebHostBuilder(b => b.UseSetting("Seed_StaffPassword", "Str0ng#Password!"));

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Seed__StaffPassword", error.Message);
    }

    [Fact]
    public void The_app_runs_the_key_check_before_anyone_can_sign_in()
    {
        var factory = _factory.WithWebHostBuilder(b => b.UseSetting("Jwt:Key", "too-short"));

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Jwt:Key", error.Message);
    }
}
