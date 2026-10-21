using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KekeBeauty.Modules.Identity.Application;
using KekeBeauty.SharedKernel;

namespace KekeBeauty.IntegrationTests;

/// <summary>The counters the alerts are built on must really move when the thing they watch happens.</summary>
[Collection(ApiCollection.Name)]
public sealed class TelemetryTests(ApiFixture api) : IDisposable
{
    private readonly ConcurrentBag<(string Name, long Value, Dictionary<string, string?> Tags)> _seen = [];
    private readonly MeterListener _listener = new();

    private void Listen()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == Telemetry.Name)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            _seen.Add((instrument.Name, value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value?.ToString()))));
        _listener.Start();
    }

    public void Dispose() => _listener.Dispose();

    private long Count(string name, params (string Key, string Value)[] tags) =>
        _seen.Where(m => m.Name == name && tags.All(t => m.Tags.GetValueOrDefault(t.Key) == t.Value)).Sum(m => m.Value);

    [Fact]
    public async Task Failed_sign_ins_wrong_second_factor_codes_and_lockouts_are_counted()
    {
        Listen();
        using var anon = api.CreateClient();
        using var partner = await TestAccounts.PartnerAsync(api);

        (await anon.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest($"nobody{Guid.NewGuid():N}@example.com", "Wrong-Password-1"))).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        Count("keke.auth.failed_logins", ("reason", "bad_credentials")).Should().BeGreaterThanOrEqualTo(1);

        var login = await anon.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(partner.Email, TestAccounts.Password));
        var challenge = (await login.Content.ReadFromJsonAsync<ChallengeResponse>())!;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await anon.PostAsJsonAsync("/api/v1/auth/2fa/verify", new ChallengeCodeRequest(challenge.ChallengeToken, "000000"));
        }

        Count("keke.auth.failed_second_factor").Should().BeGreaterThanOrEqualTo(5);
        Count("keke.auth.second_factor_lockouts").Should().Be(1, "the fifth wrong code locks the second factor once");
    }

    [Fact]
    public async Task A_forged_payment_notification_and_a_refused_payment_are_counted()
    {
        Listen();
        using var anonymous = api.CreateClient();
        (await anonymous.PostAsync("/api/v1/webhooks/payments/fake", new FormUrlEncodedContent(new Dictionary<string, string> { ["cpm_trans_id"] = "KBFORGED" })))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        Count("keke.payments.notifications", ("provider", "fake"), ("result", "rejected_signature")).Should().Be(1);

        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, subscribe: false);
        try
        {
            var checkout = await TestSalons.SubscribePremiumAsync(salon.Owner, salon.Id, pay: false);
            (await salon.Owner.Client.PostAsJsonAsync($"/api/v1/dev/payments/{checkout.TransactionId}/simulate", new { status = "refused" })).EnsureSuccessStatusCode();

            Count("keke.payments.settled", ("provider", "fake"), ("outcome", "failed")).Should().Be(1);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }
}
