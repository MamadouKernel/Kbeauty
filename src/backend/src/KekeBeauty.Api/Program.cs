using System.Text.Json.Serialization;
using KekeBeauty.Api.Endpoints;
using KekeBeauty.Api.Hubs;
using KekeBeauty.Api.Observability;
using KekeBeauty.Api.Http;
using KekeBeauty.Infrastructure;
using KekeBeauty.Modules.Identity;
using KekeBeauty.Modules.Identity.Application;
using KekeBeauty.Modules.Identity.Endpoints;
using KekeBeauty.SharedKernel;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

// The deployment's keystore: secrets mounted as files (Docker or Kubernetes secrets), one file per setting named like the setting with "__"
// for ":" (for example /run/secrets/Jwt__SigningKey). Optional, read last so that a mounted secret wins; the folder can be moved with
// SECRETS_DIR. No secret lives in the repository, which is public.
builder.Configuration.AddKeyPerFile(Environment.GetEnvironmentVariable("SECRETS_DIR") ?? "/run/secrets", optional: true);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    // A JSON request never needs more than a few kilobytes: refuse anything larger before reading it (an anonymous caller must not be able
    // to make the server parse megabytes). The four upload routes (photos, videos, identity documents, review photos) carry their own,
    // higher RequestSizeLimit.
    options.Limits.MaxRequestBodySize = 256 * 1024;
});

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
// The repository is public, so no signing key may be committed. A development instance given none signs with a random key that
// lives only as long as the process (set JWT_SIGNING_KEY in .env for a stable one); any other environment must provide its own.
if (builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(builder.Configuration["Jwt:SigningKey"]))
{
    builder.Configuration["Jwt:SigningKey"] = SigningKeyGuard.NewEphemeralKey();
}

builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddProxyAwareness(builder.Configuration);
builder.Services.AddGlobalRateLimit(builder.Configuration);
builder.Services.AddEstablishmentRateLimits();
builder.Services.AddGrowthRateLimits();
builder.Services.AddBillingRateLimits();
builder.Services.AddGeoRateLimits();
builder.Services.AddObservability(builder.Configuration);

// With several API servers behind a load balancer, a message sent from one server must reach the browsers connected to the others:
// a Redis backplane relays it. Without Redis:ConnectionString, SignalR stays in-process (single server).
var signalR = builder.Services.AddSignalR();
if (builder.Configuration["Redis:ConnectionString"] is { Length: > 0 } redisConnection)
{
    signalR.AddStackExchangeRedis(redisConnection, options => options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("keke"));

    // The same Redis also holds the rate-limit counters, so that a limit means the same thing on one instance or on ten. Short timeouts: a Redis
    // that does not answer fast must cost a request milliseconds, not seconds, before the limiter falls back to its own counter.
    var limitOptions = StackExchange.Redis.ConfigurationOptions.Parse(redisConnection);
    limitOptions.AbortOnConnectFail = false;
    limitOptions.ConnectTimeout = 1000;
    limitOptions.SyncTimeout = 250;
    limitOptions.AsyncTimeout = 250;
    builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ => StackExchange.Redis.ConnectionMultiplexer.Connect(limitOptions));
    builder.Services.AddSingleton<ISharedRateLimitStore, KekeBeauty.Api.Http.RedisRateLimitStore>();
}

builder.Services.AddSingleton<IRealtimeNotifier, SignalRRealtimeNotifier>();
// Strict numbers keep the OpenAPI contract precise (plain `number`, not "number or string") for generated clients.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<BadRequestExceptionHandler>();
builder.Services.AddExceptionHandler<InvalidInputExceptionHandler>();
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

    if (app.Environment.IsProduction() && KekeBeauty.Infrastructure.Security.KeyEncryption.Parse(app.Configuration["DataProtection:KeyEncryptionKey"]) is null)
    {
        throw new InvalidOperationException("DataProtection:KeyEncryptionKey is required in Production: it encrypts the Data Protection keys (which protect every TOTP secret) at rest. Generate one with `openssl rand -base64 32` and keep it in the secret manager.");
    }

    if (app.Environment.IsProduction() && SigningKeyGuard.RejectionReason(app.Configuration["Jwt:SigningKey"]) is { } keyProblem)
    {
        throw new InvalidOperationException($"Jwt:SigningKey is not acceptable in Production: {keyProblem}. Generate a random key (for example `openssl rand -base64 48`) and keep it in the secret manager.");
    }

    if (app.Environment.IsProduction() && string.Equals(payments.Provider, "Winipay", StringComparison.OrdinalIgnoreCase) && payments.Winipay.Env != "prod")
    {
        throw new InvalidOperationException("Payments:Winipay:Env must be 'prod' in Production.");
    }

    if (string.Equals(app.Configuration["WhatsApp:Provider"], "Zavu", StringComparison.OrdinalIgnoreCase))
    {
        var zavu = app.Configuration.GetSection(KekeBeauty.Infrastructure.Notifications.ZavuOptions.SectionName).Get<KekeBeauty.Infrastructure.Notifications.ZavuOptions>() ?? new();
        if (!zavu.HasApiKey)
        {
            throw new InvalidOperationException("WhatsApp:Provider=Zavu requires Zavu:ApiKey (ZAVU_API_KEY).");
        }

        if (app.Environment.IsProduction() && zavu.IsTestKey)
        {
            throw new InvalidOperationException("Zavu:ApiKey is a test key (zv_test_): in Production use the live key (zv_live_), otherwise messages only reach sandbox numbers.");
        }

        if (app.Environment.IsProduction() && zavu.WebhookSecret.Length == 0)
        {
            throw new InvalidOperationException("Zavu:WebhookSecret is required in Production: without it every Zavu webhook (delivery reports, STOP replies) is refused.");
        }
    }

    if (app.Environment.IsProduction() && string.IsNullOrWhiteSpace(app.Configuration["Email:Smtp:Host"]))
    {
        app.Logger.LogWarning("Email:Smtp:Host is not configured: no e-mail (account confirmation, password reset) will be delivered.");
    }
    else if (app.Environment.IsProduction() && (app.Configuration["Email:Smtp:Host"] is "mailpit" || app.Configuration["Email:Smtp:FromAddress"]?.EndsWith(".local", StringComparison.OrdinalIgnoreCase) == true))
    {
        app.Logger.LogWarning("Email:Smtp still points to the local test inbox (host or sender ends with .local): no real e-mail leaves the server. Set SMTP_HOST, SMTP_USER, SMTP_PASSWORD and SMTP_FROM.");
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
app.UseAuthentication();
// After authentication, so that the per-user limits (gift cards, payment refresh) see the signed-in user and not just the address.
app.UseRateLimiter();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    // The API contract and its interactive documentation are for development: they are not published in Production.
    app.MapOpenApi().AllowAnonymous();
    // Interactive API documentation (Scalar) at /scalar; the root URL lands on it.
    app.MapScalarApiReference(options => options.WithTitle("Keke Beauty API")).AllowAnonymous();
    app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription().AllowAnonymous();
}

// Health checks are public so the container orchestrator and probes can reach them without a token.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

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
app.MapTeamEndpoints();
app.MapDisputeEndpoints();
app.MapGrowthEndpoints();
app.MapNotificationEndpoints();
app.MapBillingEndpoints();
app.MapGeoEndpoints();
app.MapEngagementEndpoints();
app.MapAdminEndpoints();
app.MapPrivacyEndpoints();
// Availability is broadcast to the public booking page before sign-in; the Pro and Notifications hubs are [Authorize]d.
app.MapHub<AvailabilityHub>(HubRoutes.Availability).AllowAnonymous();
app.MapHub<ProHub>(HubRoutes.Pro);
app.MapHub<NotificationsHub>(HubRoutes.Notifications);
app.MapAuthEndpoints();
app.MapMeEndpoints();

app.Run();

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
