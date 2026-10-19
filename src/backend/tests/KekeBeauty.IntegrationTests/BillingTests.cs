using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Billing;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class BillingTests(ApiFixture api)
{
    private static string Sub(Salon salon) => $"/api/v1/pro/establishments/{salon.Id}/subscription";

    private static Task<SubscriptionOverview> OverviewAsync(Salon salon) =>
        salon.Owner.Client.GetFromJsonAsync<SubscriptionOverview>(Sub(salon))!;

    private static Task<HttpResponseMessage> EnableBookingAsync(Salon salon) =>
        salon.Owner.Client.PutAsJsonAsync($"/api/v1/pro/establishments/{salon.Id}/booking-settings", new BookingSettingsDto(true, false, 0, 60, 120, 30));

    private async Task<Salon> FreeSalonAsync()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        return await TestSalons.PublishAsync(api, admin, subscribe: false);
    }

    private async Task WithDbAsync(Func<KekeBeautyDbContext, Task> action)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>());
    }

    private async Task RunMaintenanceAsync() => await api.Services.GetRequiredService<BillingMaintenance>().RunOnceAsync(CancellationToken.None);

    [Fact]
    public async Task Plans_are_public_and_the_free_plan_locks_premium_features()
    {
        using var anonymous = api.CreateClient();
        var plans = (await anonymous.GetFromJsonAsync<List<PlanDto>>("/api/v1/plans"))!;
        plans.Select(p => p.Code).Should().Equal("free", "premium");
        var premium = plans.Single(p => p.Code == "premium").Features;
        premium.Should().BeEquivalentTo(new PlanFeaturesDto(true, true, 30, true, true), options => options.Excluding(f => f.Perks));
        premium.Perks.Should().BeNullOrEmpty();

        var salon = await FreeSalonAsync();
        try
        {
            var overview = await OverviewAsync(salon);
            overview.EffectivePlanCode.Should().Be("free");
            overview.Subscription.Should().BeNull();

            var enable = await EnableBookingAsync(salon);
            enable.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
            (await enable.Content.ReadAsStringAsync()).Should().Contain("plan.feature_required");
            (await salon.Owner.Client.GetAsync($"/api/v1/pro/establishments/{salon.Id}/stats")).StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task Paying_activates_the_plan_unlocks_booking_and_issues_a_numbered_invoice()
    {
        var salon = await FreeSalonAsync();
        try
        {
            var checkout = await TestSalons.SubscribePremiumAsync(salon.Owner, salon.Id, pay: false);
            checkout.PaymentUrl.Should().Contain("/paiement/simulation?tx=");

            // Not paid yet: nothing is unlocked.
            (await OverviewAsync(salon)).Subscription!.Status.Should().Be("PendingPayment");
            (await EnableBookingAsync(salon)).StatusCode.Should().Be(HttpStatusCode.PaymentRequired);

            (await salon.Owner.Client.PostAsJsonAsync($"/api/v1/dev/payments/{checkout.TransactionId}/simulate", new { status = "accepted" }))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);

            var overview = await OverviewAsync(salon);
            overview.EffectivePlanCode.Should().Be("premium");
            overview.Subscription!.Status.Should().Be("Active");
            overview.Subscription.CommitmentEndDate.Should().BeAfter(overview.Subscription.StartDate!.Value.AddMonths(11));
            (await EnableBookingAsync(salon)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await salon.Owner.Client.GetAsync($"/api/v1/pro/establishments/{salon.Id}/stats")).StatusCode.Should().Be(HttpStatusCode.OK);

            var invoices = (await salon.Owner.Client.GetFromJsonAsync<List<InvoiceDto>>("/api/v1/pro/invoices"))!;
            var invoice = invoices.Should().ContainSingle().Subject;
            invoice.Status.Should().Be("Paid");
            invoice.Number.Should().MatchRegex(@"^KB-\d{4}-\d{6}$");
            invoice.Amount.Should().Be(15000);

            var pdf = await salon.Owner.Client.GetAsync($"/api/v1/pro/invoices/{invoice.Id}/pdf");
            pdf.StatusCode.Should().Be(HttpStatusCode.OK);
            pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
            (await pdf.Content.ReadAsByteArrayAsync()).Take(5).Should().Equal("%PDF-"u8.ToArray());

            // A second subscription while one is running is refused.
            var again = await salon.Owner.Client.PostAsJsonAsync(Sub(salon), new SubscribeInput("premium", "Annual"));
            again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task Notifications_are_idempotent_and_refused_or_mismatched_payments_do_not_activate()
    {
        var salon = await FreeSalonAsync();
        try
        {
            var refused = await TestSalons.SubscribePremiumAsync(salon.Owner, salon.Id, pay: false);
            (await salon.Owner.Client.PostAsJsonAsync($"/api/v1/dev/payments/{refused.TransactionId}/simulate", new { status = "refused" })).EnsureSuccessStatusCode();
            (await OverviewAsync(salon)).Subscription!.Status.Should().Be("PendingPayment");

            // Retrying from the invoice starts a fresh transaction; a different amount reported by the provider is refused.
            var retry = (await (await salon.Owner.Client.PostAsync($"/api/v1/pro/invoices/{refused.InvoiceId}/pay", null)).Content.ReadFromJsonAsync<CheckoutDto>())!;
            retry.TransactionId.Should().NotBe(refused.TransactionId);
            api.Services.GetRequiredService<FakePaymentGateway>().OverrideAmount(retry.TransactionId, 1);
            (await salon.Owner.Client.PostAsJsonAsync($"/api/v1/dev/payments/{retry.TransactionId}/simulate", new { status = "accepted" })).EnsureSuccessStatusCode();
            (await OverviewAsync(salon)).Subscription!.Status.Should().Be("PendingPayment");

            var good = (await (await salon.Owner.Client.PostAsync($"/api/v1/pro/invoices/{refused.InvoiceId}/pay", null)).Content.ReadFromJsonAsync<CheckoutDto>())!;
            for (var i = 0; i < 3; i++)
            {
                (await salon.Owner.Client.PostAsJsonAsync($"/api/v1/dev/payments/{good.TransactionId}/simulate", new { status = "accepted" })).EnsureSuccessStatusCode();
            }

            (await OverviewAsync(salon)).Subscription!.Status.Should().Be("Active");
            await WithDbAsync(async db =>
            {
                var payments = await db.Payments.Where(p => p.InvoiceId == refused.InvoiceId).ToListAsync();
                payments.Count(p => p.Status == PaymentStatus.Succeeded).Should().Be(1);
                payments.Count(p => p.Status == PaymentStatus.Failed).Should().Be(2);
                (await db.PaymentWebhookEvents.CountAsync(e => e.EventId == good.TransactionId)).Should().Be(1);
            });
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task Webhook_rejects_unsigned_calls_and_unknown_providers()
    {
        using var anonymous = api.CreateClient();
        var body = new FormUrlEncodedContent(new Dictionary<string, string> { ["cpm_trans_id"] = "KBFAKE" });

        (await anonymous.PostAsync("/api/v1/webhooks/payments/fake", body)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsync("/api/v1/webhooks/payments/nobody", body)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var signed = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/payments/fake") { Content = body };
        signed.Headers.Add("x-token", "fake-secret");
        (await anonymous.SendAsync(signed)).StatusCode.Should().Be(HttpStatusCode.OK, "an unknown transaction is acknowledged and ignored");
    }

    [Fact]
    public async Task An_unpaid_invoice_closes_booking_after_the_grace_period_and_paying_reopens_it()
    {
        var salon = await FreeSalonAsync();
        try
        {
            await TestSalons.SubscribePremiumAsync(salon.Owner, salon.Id);
            (await EnableBookingAsync(salon)).EnsureSuccessStatusCode();
            using var client = api.CreateClient();
            (await client.GetAsync($"/api/v1/establishments/{salon.Slug}/booking")).StatusCode.Should().Be(HttpStatusCode.OK);

            // The paid period ended ten days ago and the next invoice was never settled.
            await WithDbAsync(db => db.Subscriptions.Where(s => s.EstablishmentId == salon.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.CurrentPeriodEnd, api.Clock.GetUtcNow().AddDays(-10))));
            await RunMaintenanceAsync();
            await RunMaintenanceAsync();

            var overview = await OverviewAsync(salon);
            overview.Subscription!.Status.Should().Be("PastDue");
            overview.EffectivePlanCode.Should().Be("free");
            (await client.GetAsync($"/api/v1/establishments/{salon.Slug}/booking")).StatusCode.Should().Be(HttpStatusCode.Conflict, "booking is closed while unpaid");

            var invoices = (await salon.Owner.Client.GetFromJsonAsync<List<InvoiceDto>>("/api/v1/pro/invoices"))!;
            invoices.Should().HaveCount(2, "the maintenance is idempotent");
            var overdue = invoices.Single(i => i.Status == "Overdue");
            (await salon.Owner.Client.GetFromJsonAsync<NotificationList>("/api/v1/me/notifications"))!
                .Items.Should().Contain(n => n.TemplateCode == "billing.past_due");

            var checkout = (await (await salon.Owner.Client.PostAsync($"/api/v1/pro/invoices/{overdue.Id}/pay", null)).Content.ReadFromJsonAsync<CheckoutDto>())!;
            (await salon.Owner.Client.PostAsJsonAsync($"/api/v1/dev/payments/{checkout.TransactionId}/simulate", new { status = "accepted" })).EnsureSuccessStatusCode();

            (await OverviewAsync(salon)).Subscription!.Status.Should().Be("Active");
            (await client.GetAsync($"/api/v1/establishments/{salon.Slug}/booking")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task Cancelling_ends_the_plan_at_the_end_of_the_commitment()
    {
        var salon = await FreeSalonAsync();
        try
        {
            await TestSalons.SubscribePremiumAsync(salon.Owner, salon.Id);
            var subscription = (await OverviewAsync(salon)).Subscription!;
            (await salon.Owner.Client.DeleteAsync($"/api/v1/pro/subscriptions/{subscription.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

            // Still premium until the commitment is over.
            await RunMaintenanceAsync();
            var running = await OverviewAsync(salon);
            running.Subscription!.CancelAtCommitmentEnd.Should().BeTrue();
            running.EffectivePlanCode.Should().Be("premium");

            await WithDbAsync(db => db.Subscriptions.Where(s => s.Id == subscription.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.CommitmentEndDate, api.Clock.GetUtcNow().AddDays(-1))));
            await RunMaintenanceAsync();

            var ended = await OverviewAsync(salon);
            ended.Subscription.Should().BeNull();
            ended.EffectivePlanCode.Should().Be("free");
            await WithDbAsync(async db => (await db.Establishments.AsNoTracking().SingleAsync(e => e.Id == salon.Id)).BookingAllowed.Should().BeFalse());
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task A_price_change_leaves_existing_subscriptions_untouched()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await FreeSalonAsync();
        var original = (await admin.Client.GetFromJsonAsync<List<PlanDto>>("/api/v1/admin/plans"))!.Single(p => p.Code == "premium");
        try
        {
            await TestSalons.SubscribePremiumAsync(salon.Owner, salon.Id);

            var changed = await admin.Client.PutAsJsonAsync($"/api/v1/admin/plans/{original.Id}",
                new PlanUpdate(original.Name, 20000, 200000, original.Features, true));
            changed.EnsureSuccessStatusCode();

            (await OverviewAsync(salon)).Subscription!.UnitPrice.Should().Be(15000);
            using var anonymous = api.CreateClient();
            (await anonymous.GetFromJsonAsync<List<PlanDto>>("/api/v1/plans"))!.Single(p => p.Code == "premium").MonthlyPrice.Should().Be(20000);
            await WithDbAsync(async db => (await db.PlanPriceHistory.CountAsync(h => h.PlanId == original.Id)).Should().BeGreaterThan(0));

            // Not an admin: no access.
            (await salon.Owner.Client.GetAsync("/api/v1/admin/plans")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await admin.Client.PutAsJsonAsync($"/api/v1/admin/plans/{original.Id}", new PlanUpdate(original.Name, original.MonthlyPrice, original.AnnualPrice, original.Features, original.IsActive));
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task Subscriptions_and_invoices_belong_to_their_owner()
    {
        var salon = await FreeSalonAsync();
        using var intruder = await TestAccounts.PartnerAsync(api);
        try
        {
            var checkout = await TestSalons.SubscribePremiumAsync(salon.Owner, salon.Id);

            (await intruder.Client.GetAsync(Sub(salon))).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await intruder.Client.PostAsJsonAsync(Sub(salon), new SubscribeInput("premium", "Monthly"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await intruder.Client.GetAsync($"/api/v1/pro/invoices/{checkout.InvoiceId}/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await intruder.Client.PostAsync($"/api/v1/pro/invoices/{checkout.InvoiceId}/pay", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await intruder.Client.DeleteAsync($"/api/v1/pro/subscriptions/{checkout.SubscriptionId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await intruder.Client.GetFromJsonAsync<List<InvoiceDto>>("/api/v1/pro/invoices"))!.Should().BeEmpty();
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task The_photo_limit_follows_the_plan()
    {
        var salon = await FreeSalonAsync();
        try
        {
            var detailUrl = $"/api/v1/pro/establishments/{salon.Id}";
            (await salon.Owner.Client.GetFromJsonAsync<KekeBeauty.Infrastructure.Partner.PartnerEstablishmentDetail>(detailUrl))!.MaxPhotos.Should().Be(5);

            await TestSalons.SubscribePremiumAsync(salon.Owner, salon.Id);
            (await salon.Owner.Client.GetFromJsonAsync<KekeBeauty.Infrastructure.Partner.PartnerEstablishmentDetail>(detailUrl))!.MaxPhotos.Should().Be(30);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task Demo_establishments_left_without_a_subscription_get_their_premium_plan_back()
    {
        await WithDbAsync(async db =>
        {
            var demoIds = await db.Establishments.Where(e => e.OwnerUserId == null && e.BookingEnabled).Select(e => e.Id).ToListAsync();
            demoIds.Should().NotBeEmpty();
            await db.Subscriptions.Where(s => demoIds.Contains(s.EstablishmentId)).ExecuteDeleteAsync();
            await db.Establishments.Where(e => demoIds.Contains(e.Id)).ExecuteUpdateAsync(set => set.SetProperty(e => e.BookingAllowed, false));

            await DemoDataSeeder.SeedAsync(db);
            await DemoDataSeeder.SeedAsync(db); // idempotent

            (await db.Subscriptions.CountAsync(s => demoIds.Contains(s.EstablishmentId) && s.Status == SubscriptionStatus.Active)).Should().Be(demoIds.Count);
            (await db.Establishments.CountAsync(e => demoIds.Contains(e.Id) && e.BookingAllowed)).Should().Be(demoIds.Count);
        });
    }
}
