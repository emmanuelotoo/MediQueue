using System.Text.RegularExpressions;

namespace MediQueue.Api.Tests;

/// <summary>
/// The API hosts the Blazor client. These guard the one thing every screen
/// depends on: that the page's boot script is actually served. A published
/// build once shipped an index.html whose script tag still held an unreplaced
/// placeholder, which would have loaded no UI at all.
/// </summary>
public partial class ClientHostingTests : IClassFixture<MediQueueApiFactory>
{
    private readonly MediQueueApiFactory _factory;

    public ClientHostingTests(MediQueueApiFactory factory) => _factory = factory;

    [GeneratedRegex("""src="(?<src>_framework/blazor\.webassembly[^"]*\.js)""")]
    private static partial Regex BootScript();

    [Fact]
    public async Task The_boot_script_is_served_at_its_stable_address()
    {
        var response = await _factory.CreateClient().GetAsync("/_framework/blazor.webassembly.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.EndsWith("javascript", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_page_loads_a_boot_script_that_is_actually_served()
    {
        var client = _factory.CreateClient();
        var page = await client.GetStringAsync("/");

        var match = BootScript().Match(page);
        Assert.True(match.Success, "index.html has no boot script tag.");

        var script = match.Groups["src"].Value;
        Assert.DoesNotContain("#[", script);

        var response = await client.GetAsync("/" + script);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.EndsWith("javascript", response.Content.Headers.ContentType?.MediaType);
    }
}
