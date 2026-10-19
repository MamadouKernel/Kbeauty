using System.Collections.Concurrent;
using KekeBeauty.Infrastructure.Backups;
using KekeBeauty.Infrastructure.Billing;
using KekeBeauty.Modules.Identity.Application;
using KekeBeauty.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace KekeBeauty.IntegrationTests;

/// <summary>Boots the real API against a throw-away PostGIS container (migrations + seed applied at startup).</summary>
public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgis/postgis:16-3.4")
        .WithDatabase("kekebeauty")
        .WithUsername("keke")
        .WithPassword("keke_test")
        .Build();

    public string ConnectionString => _database.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _database.StartAsync();

        // WebApplicationFactory boots the host lazily; touch Services so migrations + seed have run
        // before any test (including ones that talk to PostgreSQL directly) starts.
        _ = Services;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }

    public const string AdminEmail = "admin@keke.test";
    public const string AdminPassword = "Zebra-Quartz-7391";

    /// <summary>Deterministic clock shared with the API (TOTP steps, lockouts, token lifetimes).</summary>
    public FakeTimeProvider Clock => Services.GetRequiredService<FakeTimeProvider>();

    public CapturingSmsSender Sms => Services.GetRequiredService<CapturingSmsSender>();

    public CapturingEmailSender Emails => Services.GetRequiredService<CapturingEmailSender>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Database:SeedOnStartup", "true");
        builder.UseSetting("Database:SeedDemoData", "true");
        builder.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-0123456789abcdef");
        builder.UseSetting("Auth:AuthPermitPerMinute", "100000");
        builder.UseSetting("Auth:Google:Enabled", "true");
        builder.UseSetting("Auth:Google:ClientId", "test-google-client");
        builder.UseSetting("Storage:Provider", "Memory");
        builder.UseSetting("Scheduling:MaintenanceEnabled", "false");
        builder.UseSetting("Billing:MaintenanceEnabled", "false");
        builder.UseSetting("Retention:MaintenanceEnabled", "false");
        builder.UseSetting("Reminders:MaintenanceEnabled", "false");
        builder.UseSetting("Reengagement:MaintenanceEnabled", "false");
        builder.UseSetting("RateLimits:GiftCardCodesPerMinute", "100000");
        builder.UseSetting("Settlements:MaintenanceEnabled", "false");
        builder.UseSetting("Backups:MaintenanceEnabled", "false");
        builder.UseSetting("LeTexto:WebhookSecret", "test-dlr-secret");
        builder.UseSetting("RateLimits:GlobalPerMinute", "1000000");
        builder.UseSetting("RateLimits:BookingPerMinute", "100000");
        builder.UseSetting("Admin:Email", AdminEmail);
        builder.UseSetting("Admin:Password", AdminPassword);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(new FakeTimeProvider(DateTimeOffset.UtcNow));
            services.AddSingleton<TimeProvider>(provider => provider.GetRequiredService<FakeTimeProvider>());
            services.AddSingleton<CapturingEmailSender>();
            services.AddSingleton<IEmailSender>(provider => provider.GetRequiredService<CapturingEmailSender>());
            // A CinetPay gateway talking to a stub, registered first so that the single-gateway lookup still resolves the Fake one.
            services.AddSingleton<CinetPayStub>();
            services.Insert(0, ServiceDescriptor.Singleton<IPaymentGateway>(provider => new CinetPayGateway(
                new HttpClient(provider.GetRequiredService<CinetPayStub>()),
                Options.Create(new PaymentsOptions
                {
                    CinetPay = new CinetPayOptions { ApiKey = "test-key", SiteId = "105000", SecretKey = CinetPayStub.Secret, BaseUrl = "http://cinetpay.test/v2" },
                }))));
            services.AddSingleton<WinipayStub>();
            services.Insert(0, ServiceDescriptor.Singleton<IPaymentGateway>(provider => new WinipayGateway(
                new HttpClient(provider.GetRequiredService<WinipayStub>()),
                Options.Create(new PaymentsOptions
                {
                    Winipay = new WinipayOptions { ApplyKey = "apply", Token = WinipayStub.Token, Env = "test", BaseUrl = "http://winipayer.test" },
                }))));
            services.AddSingleton<IGoogleIdTokenVerifier, FakeGoogleVerifier>();
            services.AddSingleton<CapturingSmsSender>();
            services.AddSingleton<ISmsSender>(provider => provider.GetRequiredService<CapturingSmsSender>());
            services.AddSingleton<CapturingWhatsAppSender>();
            services.AddSingleton<IWhatsAppSender>(provider => provider.GetRequiredService<CapturingWhatsAppSender>());
            services.AddSingleton<FakeDatabaseDumper>();
            services.AddSingleton<IDatabaseDumper>(provider => provider.GetRequiredService<FakeDatabaseDumper>());
            services.AddSingleton<CapturingPushSender>();
            services.AddSingleton<IPushSender>(provider => provider.GetRequiredService<CapturingPushSender>());
        });
    }
}

/// <summary>Records outgoing e-mails so tests can follow confirmation and reset links.</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _messages = new();

    public IReadOnlyCollection<EmailMessage> Messages => _messages.ToArray();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _messages.Enqueue(message);
        return Task.CompletedTask;
    }
}

/// <summary>Records outgoing SMS so tests can check who was told what.</summary>
public sealed class CapturingSmsSender : ISmsSender
{
    private readonly ConcurrentQueue<(string Phone, string Message)> _messages = new();

    public IReadOnlyCollection<(string Phone, string Message)> Messages => _messages.ToArray();

    public Task<SmsResult> SendAsync(string phone, string message, CancellationToken cancellationToken = default)
    {
        _messages.Enqueue((phone, message));
        return Task.FromResult(new SmsResult(true, "test-sms"));
    }
}

/// <summary>Records WhatsApp template messages; <see cref="Succeed"/> turns delivery failures on and off.</summary>
public sealed class CapturingWhatsAppSender : IWhatsAppSender
{
    private readonly ConcurrentQueue<(string Phone, string Template, string[] Parameters)> _messages = new();

    public bool Succeed { get; set; } = true;

    public IReadOnlyCollection<(string Phone, string Template, string[] Parameters)> Messages => _messages.ToArray();

    public Task<WhatsAppResult> SendTemplateAsync(string phone, string template, string language, IReadOnlyList<string> parameters, bool withCodeButton, CancellationToken cancellationToken = default)
    {
        _messages.Enqueue((phone, template, parameters.ToArray()));
        return Task.FromResult(Succeed ? new WhatsAppResult(true, "test-wa") : new WhatsAppResult(false, Error: "indisponible"));
    }
}

/// <summary>Stands in for pg_dump: writes a small recognisable file, or fails when <see cref="Succeed"/> is off.</summary>
public sealed class FakeDatabaseDumper : IDatabaseDumper
{
    public bool Succeed { get; set; } = true;

    public async Task<Result> DumpAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        if (!Succeed)
        {
            return Result.Failure(new Error("backup.dump_failed", "pg_dump a échoué ; consultez les journaux du serveur."));
        }

        await File.WriteAllBytesAsync(destinationPath, "PGDMP-fake-backup"u8.ToArray(), cancellationToken);
        return Result.Success();
    }
}

/// <summary>Records Web Push messages; <see cref="Outcome"/> chooses what the push service answers.</summary>
public sealed class CapturingPushSender : IPushSender
{
    private readonly ConcurrentQueue<(string Endpoint, PushMessage Message)> _messages = new();

    public PushOutcome Outcome { get; set; } = PushOutcome.Sent;

    public IReadOnlyCollection<(string Endpoint, PushMessage Message)> Messages => _messages.ToArray();

    public Task<PushOutcome> SendAsync(PushSubscriptionInfo subscription, PushMessage message, CancellationToken cancellationToken = default)
    {
        _messages.Enqueue((subscription.Endpoint, message));
        return Task.FromResult(Outcome);
    }
}

/// <summary>Accepts tokens shaped like <c>email|subject|first|last|verified</c> instead of calling Google.</summary>
public sealed class FakeGoogleVerifier : IGoogleIdTokenVerifier
{
    public static string TokenFor(string email, string first, string last, bool verified = true) =>
        $"{email}|sub-{email}|{first}|{last}|{verified}";

    public Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken cancellationToken = default)
    {
        var parts = idToken.Split('|');
        GoogleIdentity? identity = parts.Length == 5
            ? new GoogleIdentity(parts[1], parts[0], bool.Parse(parts[4]), parts[2], parts[3])
            : null;
        return Task.FromResult(identity);
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}

/// <summary>Plays CinetPay's <c>payment/check</c> endpoint: tests decide what the provider "says" about each transaction.</summary>
public sealed class CinetPayStub : HttpMessageHandler
{
    public const string Secret = "sandbox-test-secret";

    private readonly ConcurrentDictionary<string, (string Status, string Amount)> _transactions = new();

    public void Set(string transactionId, string status, string amount) => _transactions[transactionId] = (status, amount);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
        using var json = System.Text.Json.JsonDocument.Parse(body);
        var id = json.RootElement.TryGetProperty("transaction_id", out var value) ? value.GetString() ?? string.Empty : string.Empty;
        var answer = _transactions.TryGetValue(id, out var known)
            ? $$$"""{"code":"00","message":"SUCCES","data":{"amount":"{{{known.Amount}}}","currency":"XOF","status":"{{{known.Status}}}","payment_method":"OM"}}"""
            : """{"code":"625","message":"TRANSACTION_NOT_FOUND"}""";
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(answer, System.Text.Encoding.UTF8, "application/json") };
    }
}

/// <summary>Plays WiniPayer's <c>checkout/standard/detail/{uuid}</c>: tests decide what the provider "says" about each invoice.</summary>
public sealed class WinipayStub : HttpMessageHandler
{
    public const string Token = "winipay-test-token";

    private readonly ConcurrentDictionary<string, (string State, string Amount)> _invoices = new();

    public void Set(string uuid, string state, string amount) => _invoices[uuid] = (state, amount);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uuid = request.RequestUri!.AbsolutePath.Split('/').Last();
        var answer = _invoices.TryGetValue(uuid, out var known)
            ? $$$"""{"success":true,"results":{"invoice":{"uuid":"{{{uuid}}}","state":"{{{known.State}}}","amount_init":{{{known.Amount}}},"amount":{{{known.Amount}}},"currency":"xof","operator":"wave-cote-divoire"}},"errors":[],"messages":[]}"""
            : """{"success":false,"results":[],"errors":{"code":3000,"key":"uuid","msg":"Invalide or missing invoice"},"messages":[]}""";
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(answer, System.Text.Encoding.UTF8, "application/json") });
    }
}
