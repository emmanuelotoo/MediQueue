using System.Text;
using MediQueue.Api;
using MediQueue.Api.Hubs;
using MediQueue.Api.Services;
using MediQueue.Infrastructure;
using MediQueue.Shared.Authorization;
using MediQueue.Shared.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;

// Heroku's release phase runs the app with this switch: migrate, seed, exit.
// Removed before configuration parses the arguments, so it is never read as a setting.
const string MigrateOnlySwitch = "--migrate-only";
var migrateOnly = args.Contains(MigrateOnlySwitch);

var builder = WebApplication.CreateBuilder(args.Where(arg => arg != MigrateOnlySwitch).ToArray());

// First, so a misnamed config var is reported as itself rather than as
// whichever missing setting would have failed next.
ConfigurationChecks.RejectSingleUnderscoreNames(builder.Configuration);

builder.Services.AddMediQueueInfrastructure(builder.Configuration);

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

if (string.IsNullOrWhiteSpace(jwt.Key))
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Jwt:Key is not configured. Set it via environment variable or user secrets before deploying.");
    }

    // Development only: a per-run key, so no signing secret is ever committed.
    // Restarting the app signs everyone out, which is correct for a dev default.
    jwt.Key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
}

ConfigurationChecks.RequireStrongJwtKey(jwt.Key);

builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddScoped<IQueueService, QueueService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        // WebSockets cannot carry an Authorization header, so SignalR passes the
        // token as a query parameter. Accepted only for the hub path.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token)
                    && context.HttpContext.Request.Path.StartsWithSegments("/hubs/queue"))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(Policies.Configure);

builder.Services
    .AddControllers()
    .AddJsonOptions(options => MediQueueJson.Configure(options.JsonSerializerOptions));

builder.Services.AddSignalR()
    .AddJsonProtocol(options => MediQueueJson.Configure(options.PayloadSerializerOptions));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
builder.Services.AddOpenApi();

// Heroku terminates TLS at its router and forwards plain HTTP. Trusting its
// X-Forwarded-* headers is how the app learns a request arrived over HTTPS, so
// HSTS is sent and http:// is redirected. Off unless configured, because a
// client talking to the app directly could otherwise claim anything.
var behindProxy = builder.Configuration.GetValue<bool>("Hosting:TrustForwardedHeaders");

if (behindProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        // The router has no fixed address to allow-list. The default forward
        // limit of one still takes only the entry the router appended.
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });

    builder.Services.AddHttpsRedirection(options => options.HttpsPort = 443);
}

var app = builder.Build();

// First, so everything after it sees the scheme the browser actually used.
if (behindProxy)
{
    app.UseForwardedHeaders();
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// The Blazor client's files, served from the manifest the build produces:
// stable paths such as _framework/blazor.webassembly.js resolve to their
// fingerprinted files, revalidated on each load, while fingerprinted paths are
// cached for good. A deploy therefore reaches browsers straight away. Plain
// static files could not serve the stable paths, because only fingerprinted
// names exist on disk.
app.MapStaticAssets();

app.MapControllers();
app.MapHub<QueueHub>("/hubs/queue");

// Any route the API does not own belongs to the Blazor client's router.
app.MapFallbackToFile("index.html");

await DatabaseStartup.RunAsync(app);

if (migrateOnly)
{
    return;
}

app.Run();

/// <summary>Exposed so the integration tests can host the same pipeline.</summary>
public partial class Program;
