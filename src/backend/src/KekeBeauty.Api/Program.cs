using System.Text.Json.Serialization;
using KekeBeauty.Api.Endpoints;
using KekeBeauty.Api.Hubs;
using KekeBeauty.Api.Observability;
using KekeBeauty.Api.Http;
using KekeBeauty.Infrastructure;
using KekeBeauty.Modules.Identity;
using KekeBeauty.Modules.Identity.Endpoints;
using KekeBeauty.SharedKernel;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Host.UseSerilog((context, _, logger) =>
{
    logger.ReadFrom.Configuration(context.Configuration).Enrich.FromLogContext();

    if (context.HostingEnvironment.IsDevelopment())
    {
        logger.WriteTo.Console();
    }
    else
    {
        logger.WriteTo.Console(new CompactJsonFormatter());
    }
});

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddProxyAwareness(builder.Configuration);
builder.Services.AddGlobalRateLimit(builder.Configuration);
builder.Services.AddEstablishmentRateLimits();
builder.Services.AddObservability(builder.Configuration);

// With several API servers behind a load balancer, a message sent from one server must reach the browsers connected to the others:
// a Redis backplane relays it. Without Redis:ConnectionString, SignalR stays in-process (single server).
var signalR = builder.Services.AddSignalR();
if (builder.Configuration["Redis:ConnectionString"] is { Length: > 0 } redisConnection)
{
    signalR.AddStackExchangeRedis(redisConnection, options => options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("keke"));
}

builder.Services.AddSingleton<IRealtimeNotifier, SignalRRealtimeNotifier>();
// Strict numbers keep the OpenAPI contract precise (plain `number`, not "number or string") for generated clients.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<BadRequestExceptionHandler>();
builder.Services.AddOpenApi();

var allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"];
builder.Services.AddCors(options => options.AddPolicy("web", policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString(DependencyInjection.ConnectionStringName)!,
        name: "postgres",
        tags: ["ready"]);

var app = builder.Build();

{
    var payments = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<KekeBeauty.Infrastructure.Billing.PaymentsOptions>>().Value;
    if (string.Equals(payments.Provider, "CinetPay", StringComparison.OrdinalIgnoreCase)
        && (string.IsNullOrWhiteSpace(payments.CinetPay.ApiKey) || string.IsNullOrWhiteSpace(payments.CinetPay.SiteId) || string.IsNullOrWhiteSpace(payments.CinetPay.SecretKey)))
    {
        throw new InvalidOperationException("Payments:Provider=CinetPay requires Payments:CinetPay:ApiKey, SiteId and SecretKey.");
    }

    if (string.Equals(payments.Provider, "Winipay", StringComparison.OrdinalIgnoreCase)
        && (string.IsNullOrWhiteSpace(payments.Winipay.ApplyKey) || string.IsNullOrWhiteSpace(payments.Winipay.Token)
            || !(payments.Winipay.Env is "test" or "prod")))
    {
        throw new InvalidOperationException("Payments:Provider=Winipay requires Payments:Winipay:ApplyKey, Token and Env (test or prod).");
    }

    if (app.Environment.IsProduction() && string.Equals(payments.Provider, "Winipay", StringComparison.OrdinalIgnoreCase) && payments.Winipay.Env != "prod")
    {
        throw new InvalidOperationException("Payments:Winipay:Env must be 'prod' in Production.");
    }

    if (app.Environment.IsProduction() && string.IsNullOrWhiteSpace(app.Configuration["Email:Smtp:Host"]))
    {
        app.Logger.LogWarning("Email:Smtp:Host is not configured: no e-mail (account confirmation, password reset) will be delivered.");
    }
}

if (app.Environment.IsProduction() && app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<KekeBeauty.Infrastructure.Billing.PaymentsOptions>>().Value.IsFake)
{
    throw new InvalidOperationException("Payments:Provider=Fake is for development and tests only; configure CinetPay.");
}

app.UseProxyAwareness();
app.UseSecurityHeaders();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors("web");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    // The API contract and its interactive documentation are for development: they are not published in Production.
    app.MapOpenApi();
    // Interactive API documentation (Scalar) at /scalar; the root URL lands on it.
    app.MapScalarApiReference(options => options.WithTitle("Keke Beauty API"));
    app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapCatalogEndpoints();
app.MapLegalEndpoints();
app.MapGeographyEndpoints();
app.MapEstablishmentEndpoints();
app.MapFileEndpoints();
app.MapPartnerEndpoints();
app.MapPartnerKycEndpoints();
app.MapAdminReviewEndpoints();
app.MapAppointmentEndpoints();
app.MapAgendaEndpoints();
app.MapNotificationEndpoints();
app.MapBillingEndpoints();
app.MapEngagementEndpoints();
app.MapAdminEndpoints();
app.MapPrivacyEndpoints();
app.MapHub<AvailabilityHub>(HubRoutes.Availability);
app.MapHub<ProHub>(HubRoutes.Pro);
app.MapHub<NotificationsHub>(HubRoutes.Notifications);
app.MapAuthEndpoints();
app.MapMeEndpoints();

app.Run();

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
