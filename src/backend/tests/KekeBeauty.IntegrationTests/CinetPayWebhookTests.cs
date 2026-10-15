using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Billing;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

/// <summary>The CinetPay notification path through the real endpoint: signature, server-side re-check, settlement, replays.</summary>
[Collection(ApiCollection.Name)]
public sealed class CinetPayWebhookTests(ApiFixture api)
{
    private const string Url = "/api/v1/webhooks/payments/cinetpay";

    private async Task WithDbAsync(Func<KekeBeautyDbContext, Task> action)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>());
    }

    /// <summary>A salon whose first invoice is waiting for a CinetPay payment (the checkout itself used the development gateway).</summary>
    private async Task<(Salon Salon, CheckoutDto Checkout, string Transaction)> AwaitingPaymentAsync()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, subscribe: false);
        var checkout = await TestSalons.SubscribePremiumAsync(salon.Owner, salon.Id, pay: false);
        var transaction = "KBCP" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();
        await WithDbAsync(async db =>
        {
            db.Payments.Add(new Payment
            {
                InvoiceId = checkout.InvoiceId,
                Provider = "cinetpay",
                ProviderTransactionId = transaction,
                Amount = 15000,
                CurrencyCode = "XOF",
            });
            await db.SaveChangesAsync();
        });
        return (salon, checkout, transaction);
    }

    private static Dictionary<string, string> Fields(string transaction, string amount = "15000") => new()
    {
        ["cpm_site_id"] = "105000",
        ["cpm_trans_id"] = transaction,
        ["cpm_trans_date"] = "2026-10-12 10:00:00",
        ["cpm_amount"] = amount,
        ["cpm_currency"] = "XOF",
        ["signature"] = "provider-signature",
        ["payment_method"] = "OM",
        ["cel_phone_num"] = "0700000000",
        ["cpm_phone_prefixe"] = "225",
        ["cpm_language"] = "fr",
        ["cpm_version"] = "V4",
        ["cpm_payment_config"] = "SINGLE",
        ["cpm_page_action"] = "PAYMENT",
        ["cpm_custom"] = string.Empty,
        ["cpm_designation"] = "Abonnement",
        ["cpm_error_message"] = "SUCCES",
    };

    private async Task<HttpResponseMessage> NotifyAsync(Dictionary<string, string> fields, string? token = null)
    {
        using var client = api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, Url) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Add("x-token", token ?? CinetPayGateway.ComputeToken(CinetPayStub.Secret, fields));
        return await client.SendAsync(request);
    }

    private static async Task<string> StatusAsync(Salon salon) =>
        (await salon.Owner.Client.GetFromJsonAsync<SubscriptionOverview>($"/api/v1/pro/establishments/{salon.Id}/subscription"))!.Subscription!.Status;

    [Fact]
    public async Task A_signed_notification_for_an_accepted_payment_activates_the_subscription_once()
    {
        var (salon, _, transaction) = await AwaitingPaymentAsync();
        try
        {
            api.Services.GetRequiredService<CinetPayStub>().Set(transaction, "ACCEPTED", "15000");

            (await NotifyAsync(Fields(transaction))).StatusCode.Should().Be(HttpStatusCode.OK);
            (await StatusAsync(salon)).Should().Be("Active");

            // CinetPay retries notifications: replays are acknowledged and change nothing.
            (await NotifyAsync(Fields(transaction))).StatusCode.Should().Be(HttpStatusCode.OK);
            await WithDbAsync(async db =>
            {
                var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.ProviderTransactionId == transaction);
                payment.Status.Should().Be(PaymentStatus.Succeeded);
                payment.Method.Should().Be(PaymentMethod.OrangeMoney);
                (await db.PaymentWebhookEvents.CountAsync(e => e.Provider == "cinetpay" && e.EventId == transaction)).Should().Be(1);
            });
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task A_wrong_signature_is_rejected_and_nothing_is_asked_of_the_provider()
    {
        var (salon, _, transaction) = await AwaitingPaymentAsync();
        try
        {
            api.Services.GetRequiredService<CinetPayStub>().Set(transaction, "ACCEPTED", "15000");

            (await NotifyAsync(Fields(transaction), token: "00ff")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            // Tampering with a field after signing breaks the signature too.
            var signed = Fields(transaction);
            var token = CinetPayGateway.ComputeToken(CinetPayStub.Secret, signed);
            signed["cpm_amount"] = "5";
            (await NotifyAsync(signed, token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            (await StatusAsync(salon)).Should().Be("PendingPayment");
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task The_provider_has_the_last_word_on_amount_and_status()
    {
        var (salon, _, wrongAmount) = await AwaitingPaymentAsync();
        var (other, _, stillWaiting) = await AwaitingPaymentAsync();
        try
        {
            var stub = api.Services.GetRequiredService<CinetPayStub>();

            // A genuine signature on a notification that claims 15000, while the provider says 100 was charged.
            stub.Set(wrongAmount, "ACCEPTED", "100");
            (await NotifyAsync(Fields(wrongAmount))).StatusCode.Should().Be(HttpStatusCode.OK);
            (await StatusAsync(salon)).Should().Be("PendingPayment");
            await WithDbAsync(async db => (await db.Payments.AsNoTracking().SingleAsync(p => p.ProviderTransactionId == wrongAmount)).Status.Should().Be(PaymentStatus.Failed));

            // Not settled yet at the provider: the notification is answered with a retryable error and can be replayed later.
            stub.Set(stillWaiting, "WAITING_FOR_CUSTOMER", "15000");
            (await NotifyAsync(Fields(stillWaiting))).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            stub.Set(stillWaiting, "ACCEPTED", "15000");
            (await NotifyAsync(Fields(stillWaiting))).StatusCode.Should().Be(HttpStatusCode.OK);
            (await StatusAsync(other)).Should().Be("Active");
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId, other.Owner.UserId);
        }
    }
}
