using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MediQueue.Api.Tests;

public class ForwardedHeadersTests : IClassFixture<MediQueueApiFactory>
{
    private readonly MediQueueApiFactory _factory;

    public ForwardedHeadersTests(MediQueueApiFactory factory) => _factory = factory;

    private HttpClient Client(bool trustForwardedHeaders)
    {
        WebApplicationFactory<Program> factory = trustForwardedHeaders
            ? _factory.WithWebHostBuilder(b => b.UseSetting("Hosting:TrustForwardedHeaders", "true"))
            : _factory;

        return factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    private static HttpRequestMessage ViaRouter(string forwardedProto)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/departments");
        request.Headers.Add("X-Forwarded-Proto", forwardedProto);
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");
        return request;
    }

    [Fact]
    public async Task Behind_the_router_plain_http_is_sent_to_https()
    {
        var response = await Client(trustForwardedHeaders: true).SendAsync(ViaRouter("http"));

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("https", response.Headers.Location!.Scheme);
    }

    [Fact]
    public async Task Behind_the_router_https_is_served()
    {
        var response = await Client(trustForwardedHeaders: true).SendAsync(ViaRouter("https"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_trusted_proxy_the_headers_are_ignored()
    {
        // A client talking to the app directly must not be able to steer it by
        // claiming to have come through a proxy.
        var response = await Client(trustForwardedHeaders: false).SendAsync(ViaRouter("http"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
