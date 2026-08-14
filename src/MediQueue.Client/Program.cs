using Blazored.LocalStorage;
using MediQueue.Client;
using MediQueue.Client.Services;
using MediQueue.Shared.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// One HttpClient, so the bearer token set at sign-in reaches every call, and
// one handler that turns a rejected token into a sign-in prompt.
builder.Services.AddScoped(sp =>
{
    var handler = new ExpiredSessionHandler(sp, sp.GetRequiredService<NavigationManager>())
    {
        InnerHandler = new HttpClientHandler()
    };

    return new HttpClient(handler)
    {
        BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
    };
});

builder.Services.AddBlazoredLocalStorage();

builder.Services.AddScoped<SessionStore>();
builder.Services.AddScoped<MediQueueApi>();
builder.Services.AddScoped<QueueHubClient>();

builder.Services.AddScoped<MediQueueAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<MediQueueAuthStateProvider>());
// The same policy definitions the API enforces: without them the router
// throws on an unknown policy name the moment a guarded page is reached.
builder.Services.AddAuthorizationCore(Policies.Configure);

await builder.Build().RunAsync();
