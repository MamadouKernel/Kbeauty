using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using KekeBeauty.Infrastructure.Billing;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

/// <summary>The WiniPayer callback path through the real endpoint: signed callback URL, server-side read of the invoice, settlement, replays.</summary>
[Collection(ApiCollection.Name)]
public sealed class WinipayWebhookTests(ApiFixture api)
{
    private const string Url = "/api/v1/webhooks/payments/winipay";

    private async Task WithDbAsync(Func<KekeBeautyDbContext, Task> action)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>());
    }

    private static string Sign(string transaction) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(WinipayStub.Token), Encoding.UTF8.GetBytes(transaction)));

    /// <summary>A salon whose first invoice waits for a WiniPayer payment (the checkout itself used the development gateway).</summary>
    private async Task<(Salon Salon, string Transaction, string Uuid)> AwaitingPaymentAsync()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, subscribe: false);
        var checkout = await TestSalons.SubscribePremiumAsync(salon.Owner, salon.Id, pay: false);
        var transaction = "KBWP" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();
        var uuid = Guid.NewGuid().ToString();
        await WithDbAsync(async db =>
        {
            db.Payments.Add(new Payment
            {
                InvoiceId = checkout.InvoiceId,
                Provider = "winipay",
                ProviderTransactionId = transaction,
                ProviderReference = uuid,
                Amount = 15000,
                CurrencyCode = "XOF",
            });
            await db.SaveChangesAsync();
        });
        return (salon, transaction, uuid);
    }

    private async Task<HttpResponseMessage> CallbackAsync(string transaction, string? signature = null, HttpMethod? method = null, string? body = null)
    {
        using var client = api.CreateClient();
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, $"{Url}?tx={Uri.EscapeDataString(transaction)}&sig={signature ?? Sign(transaction)}")
        {
            // WiniPayer does not document the callback body: whatever it contains must not matter.
            Content = body is null ? null : new StringContent(body, Encoding.UTF8, "application/json"),
        };
        return await client.SendAsync(request);
    }

    private static async Task<string> StatusAsync(Salon salon) =>
        (await salon.Owner.Client.GetFromJsonAsync<SubscriptionOverview>($"/api/v1/pro/establishments/{salon.Id}/subscription"))!.Subscription!.Status;

    [Fact]
    public async Task A_signed_callback_for_a_successful_invoice_activates_the_subscription_once()
    {
        var (salon, transaction, uuid) = await AwaitingPaymentAsync();
        try
        {
            api.Services.GetRequiredService<WinipayStub>().Set(uuid, "success", "15000");

            (await CallbackAsync(transaction, body: """{"state":"success","amount":1}""")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await StatusAsync(salon)).Should().Be("Active");

            (await CallbackAsync(transaction, method: HttpMethod.Get)).StatusCode.Should().Be(HttpStatusCode.OK, "replays and GET callbacks are acknowledged");
            await WithDbAsync(async db =>
            {
                var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.ProviderTransactionId == transaction);
                payment.Status.Should().Be(PaymentStatus.Succeeded);
                payment.Method.Should().Be(PaymentMethod.Wave);
                (await db.PaymentWebhookEvents.CountAsync(e => e.Provider == "winipay" && e.EventId == transaction)).Should().Be(1);
            });
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task A_callback_without_the_right_signature_is_rejected()
    {
        var (salon, transaction, uuid) = await AwaitingPaymentAsync();
        try
        {
            api.Services.GetRequiredService<WinipayStub>().Set(uuid, "success", "15000");

            (await CallbackAsync(transaction, signature: "00ff")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await CallbackAsync(transaction, signature: Sign("KBSOMETHINGELSE"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            using var client = api.CreateClient();
            (await client.PostAsync($"{Url}?tx={transaction}", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await client.PostAsync(Url, null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            (await StatusAsync(salon)).Should().Be("PendingPayment");
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task The_invoice_at_the_provider_has_the_last_word()
    {
        var (salon, wrongAmount, wrongUuid) = await AwaitingPaymentAsync();
        var (other, waiting, waitingUuid) = await AwaitingPaymentAsync();
        try
        {
            var stub = api.Services.GetRequiredService<WinipayStub>();

            // Whatever the callback claims, the invoice says 100 was paid for a 15000 subscription.
            stub.Set(wrongUuid, "success", "100");
            (await CallbackAsync(wrongAmount)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await StatusAsync(salon)).Should().Be("PendingPayment");
            await WithDbAsync(async db => (await db.Payments.AsNoTracking().SingleAsync(p => p.ProviderTransactionId == wrongAmount)).Status.Should().Be(PaymentStatus.Failed));

            // Still pending at the provider: a retryable answer, and the later callback goes through.
            stub.Set(waitingUuid, "pending", "15000");
            (await CallbackAsync(waiting)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            stub.Set(waitingUuid, "success", "15000");
            (await CallbackAsync(waiting)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await StatusAsync(other)).Should().Be("Active");
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId, other.Owner.UserId);
        }
    }
}
