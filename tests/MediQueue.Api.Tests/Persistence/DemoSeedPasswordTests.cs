using MediQueue.Infrastructure.Identity;
using MediQueue.Infrastructure.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediQueue.Api.Tests.Persistence;

public class DemoSeedPasswordTests : IClassFixture<MediQueueApiFactory>
{
    /// <summary>The development password, published in the README.</summary>
    private const string PublicPassword = "MediQueue#2026";

    private readonly MediQueueApiFactory _factory;

    public DemoSeedPasswordTests(MediQueueApiFactory factory) => _factory = factory;

    private async Task<string> ResolveAsync(string? configured, bool isDevelopment)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DemoSeedPassword.ConfigKey] = configured })
            .Build();

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        return await DemoSeedPassword.ResolveAsync(users, configuration, isDevelopment);
    }

    [Fact]
    public async Task Development_falls_back_to_the_documented_password()
    {
        Assert.Equal(PublicPassword, await ResolveAsync(null, isDevelopment: true));
    }

    [Fact]
    public async Task A_deployment_without_a_password_refuses_to_seed()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ResolveAsync(null, isDevelopment: false));

        Assert.Contains("Seed__StaffPassword", error.Message);
    }

    [Fact]
    public async Task The_public_readme_password_is_refused_on_a_deployment()
    {
        // The repository is public; this password must never open a live site.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ResolveAsync(PublicPassword, isDevelopment: false));

        Assert.Contains("public", error.Message);
    }

    [Fact]
    public async Task A_password_the_policy_would_reject_fails_before_anything_is_created()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ResolveAsync("short", isDevelopment: false));

        Assert.Contains("Seed__StaffPassword", error.Message);
        Assert.Contains("at least 10", error.Message);
    }

    [Fact]
    public async Task A_strong_configured_password_is_used_in_any_environment()
    {
        Assert.Equal("Str0ng#Password!", await ResolveAsync("Str0ng#Password!", isDevelopment: false));
        Assert.Equal("Str0ng#Password!", await ResolveAsync("Str0ng#Password!", isDevelopment: true));
    }
}
