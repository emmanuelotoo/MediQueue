using Blazored.LocalStorage;
using MediQueue.Client;
using MediQueue.Client.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// One HttpClient, so the bearer token set at sign-in reaches every call.
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});

builder.Services.AddBlazoredLocalStorage();

builder.Services.AddScoped<SessionStore>();
builder.Services.AddScoped<MediQueueApi>();
builder.Services.AddScoped<QueueHubClient>();

builder.Services.AddScoped<MediQueueAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<MediQueueAuthStateProvider>());
builder.Services.AddAuthorizationCore();

await builder.Build().RunAsync();
