using System.Net;
using Microsoft.AspNetCore.Components;

namespace MediQueue.Client.Services;

/// <summary>
/// Turns a rejected token into a sign-in prompt instead of a broken screen.
/// A staff token outlives a shift but not forever, and in development the
/// signing key is regenerated on every restart, so this path is ordinary
/// rather than exceptional.
/// </summary>
public class ExpiredSessionHandler : DelegatingHandler
{
    private readonly IServiceProvider _services;
    private readonly NavigationManager _navigation;

    public ExpiredSessionHandler(IServiceProvider services, NavigationManager navigation)
    {
        _services = services;
        _navigation = navigation;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        // Only a request that presented credentials tells us anything about the
        // session. Anonymous calls get 401s for their own reasons.
        var presentedCredentials = request.Headers.Authorization is not null;

        if (response.StatusCode == HttpStatusCode.Unauthorized && presentedCredentials)
        {
            // Resolved here rather than injected: the auth provider itself
            // depends on the HttpClient this handler is attached to.
            var auth = (MediQueueAuthStateProvider)_services.GetService(typeof(MediQueueAuthStateProvider))!;
            await auth.SignOutAsync();

            _navigation.NavigateTo("/login", forceLoad: false);
        }

        return response;
    }
}
