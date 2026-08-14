using System.Text;
using MediQueue.Api;
using MediQueue.Api.Hubs;
using MediQueue.Api.Services;
using MediQueue.Infrastructure;
using MediQueue.Infrastructure.Seed;
using MediQueue.Shared.Authorization;
using MediQueue.Shared.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

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

var app = builder.Build();

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

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<QueueHub>("/hubs/queue");

// Any route the API does not own belongs to the Blazor client's router.
app.MapFallbackToFile("index.html");

// Schema always; demo patients only where they belong. A real deployment gets
// the tables and nothing else, so no fictional people can reach a live report.
await DemoDataSeeder.MigrateAsync(app.Services);

if (app.Configuration.GetValue("Seed:DemoData", app.Environment.IsDevelopment()))
{
    await DemoDataSeeder.SeedAsync(app.Services);
}

app.Run();

/// <summary>Exposed so the integration tests can host the same pipeline.</summary>
public partial class Program;
