using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Billing;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PromotionTests(ApiFixture api)
{
    private static string Sub(Salon salon) => $"/api/v1/pro/establishments/{salon.Id}/subscription";

    private async Task<Salon> FreeSalonAsync()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        return await TestSalons.PublishAsync(api, admin, subscribe: false);
    }

    private async Task<PlanDto> CreatePlanAsync(decimal monthly, int promoPercent = 0, int promoPeriods = 1, DateTimeOffset? promoEnds = null)
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var body = new PlanUpdate(
            $"Promo {Guid.NewGuid():N}"[..16], monthly, monthly * 10, new PlanFeaturesDto(true, false, 15, false, true, 0), true, "Test", null,
            3, null, 0, promoPercent, promoEnds, promoPeriods);
        var response = await admin.Client.PostAsJsonAsync("/api/v1/admin/plans", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PlanDto>())!;
    }

    private async Task<PromoCodeDto> CreateCodeAsync(int percent, int periods = 1, int? maxUses = null, DateTimeOffset? until = null, IReadOnlyList<string>? plans = null)
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var code = $"T{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var response = await admin.Client.PostAsJsonAsync("/api/v1/admin/promo-codes",
            new PromoCodeInput(code, percent, periods, null, until, maxUses, plans));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PromoCodeDto>())!;
    }

    private async Task WithDbAsync(Func<KekeBeautyDbContext, Task> action)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>());
    }

    private async Task CleanUpAsync(Salon[] salons, PlanDto[] plans, PromoCodeDto[] codes)
    {
        await TestCleanup.DeleteEstablishmentsAsync(api, salons.Select(s => s.Owner.UserId).ToArray());
        var planIds = plans.Select(p => p.Id).ToArray();
        var codeIds = codes.Select(c => c.Id).ToArray();
        await WithDbAsync(async db =>
        {
            await db.Plans.Where(p => planIds.Contains(p.Id)).ExecuteDeleteAsync();
            await db.PromoCodes.Where(c => codeIds.Contains(c.Id)).ExecuteDeleteAsync();
        });
    }

    private static Task<HttpResponseMessage> SubscribeAsync(Salon salon, PlanDto plan, string? code) =>
        salon.Owner.Client.PostAsJsonAsync(Sub(salon), new SubscribeInput(plan.Code, "Monthly", code));

    private async Task<List<decimal>> InvoiceAmountsAsync(Guid subscriptionId)
    {
        var amounts = new List<decimal>();
        await WithDbAsync(async db => amounts = await db.Invoices.AsNoTracking().Where(i => i.SubscriptionId == subscriptionId)
            .OrderBy(i => i.PeriodStart).Select(i => i.Amount).ToListAsync());
        return amounts;
    }

    private async Task<Guid> InvoiceByStartAsync(Guid subscriptionId, DateTimeOffset start)
    {
        Guid id = default;
        await WithDbAsync(async db => id = (await db.Invoices.AsNoTracking().SingleAsync(i => i.SubscriptionId == subscriptionId && i.PeriodStart == start)).Id);
        return id;
    }

    [Fact]
    public async Task A_code_discounts_the_first_invoices_only_then_the_plan_price_returns()
    {
        var plan = await CreatePlanAsync(10000);
        var code = await CreateCodeAsync(20, periods: 2);
        var salon = await FreeSalonAsync();
        try
        {
            var subscribed = await SubscribeAsync(salon, plan, code.Code.ToLowerInvariant()); // case does not matter
            subscribed.StatusCode.Should().Be(HttpStatusCode.OK, await subscribed.Content.ReadAsStringAsync());
            var checkout = (await subscribed.Content.ReadFromJsonAsync<CheckoutDto>())!;
            (await salon.Owner.Client.PostAsJsonAsync($"/api/v1/dev/payments/{checkout.TransactionId}/simulate", new { status = "accepted" })).EnsureSuccessStatusCode();
            var subscription = (await salon.Owner.Client.GetFromJsonAsync<SubscriptionOverview>(Sub(salon)))!.Subscription!;
            subscription.UnitPrice.Should().Be(10000); // the frozen price is the plan's: only the invoices are discounted
            subscription.DiscountPercent.Should().Be(20);

            // Two renewals: the second discounted invoice, then the full price.
            for (var i = 0; i < 2; i++)
            {
                var end = api.Clock.GetUtcNow().AddDays(i + 1); // the fake clock does not move: each renewal gets its own start
                await WithDbAsync(db => db.Subscriptions.Where(s => s.Id == subscription.Id).ExecuteUpdateAsync(set => set.SetProperty(s => s.CurrentPeriodEnd, end)));
                await api.Services.GetRequiredService<BillingMaintenance>().RunOnceAsync(CancellationToken.None);
                var invoice = await InvoiceByStartAsync(subscription.Id, end);
                var payResponse = await salon.Owner.Client.PostAsync($"/api/v1/pro/invoices/{invoice}/pay", null);
                payResponse.StatusCode.Should().Be(HttpStatusCode.OK, await payResponse.Content.ReadAsStringAsync());
                var paying = (await payResponse.Content.ReadFromJsonAsync<CheckoutDto>())!;
                (await salon.Owner.Client.PostAsJsonAsync($"/api/v1/dev/payments/{paying.TransactionId}/simulate", new { status = "accepted" })).EnsureSuccessStatusCode();
            }

            (await InvoiceAmountsAsync(subscription.Id)).Should().Equal(8000m, 8000m, 10000m);
            await WithDbAsync(async db => (await db.PromoCodes.AsNoTracking().SingleAsync(c => c.Id == code.Id)).UsedCount.Should().Be(1));
        }
        finally
        {
            await CleanUpAsync([salon], [plan], [code]);
        }
    }

    [Fact]
    public async Task The_better_of_the_plan_promotion_and_the_code_applies_never_both()
    {
        var plan = await CreatePlanAsync(10000, promoPercent: 30);
        var weaker = await CreateCodeAsync(20);
        var stronger = await CreateCodeAsync(50);
        var salon = await FreeSalonAsync();
        var other = await FreeSalonAsync();
        try
        {
            // The plan's own 30 % beats a 20 % code, and the code is not used up.
            var check = (await (await salon.Owner.Client.PostAsJsonAsync("/api/v1/pro/promo-codes/check", new SubscribeInput(plan.Code, "Monthly", weaker.Code)))
                .Content.ReadFromJsonAsync<PromoCheckDto>())!;
            check.Should().BeEquivalentTo(new PromoCheckDto(true, 30, 1, "Plan", 10000, 7000, null));
            (await SubscribeAsync(salon, plan, weaker.Code)).StatusCode.Should().Be(HttpStatusCode.OK);
            await WithDbAsync(async db => (await db.PromoCodes.AsNoTracking().SingleAsync(c => c.Id == weaker.Id)).UsedCount.Should().Be(0));

            // A 50 % code beats the plan's 30 %.
            var better = (await (await other.Owner.Client.PostAsJsonAsync("/api/v1/pro/promo-codes/check", new SubscribeInput(plan.Code, "Monthly", stronger.Code)))
                .Content.ReadFromJsonAsync<PromoCheckDto>())!;
            better.Percent.Should().Be(50);
            better.Source.Should().Be("Code");
            better.DiscountedPrice.Should().Be(5000);
        }
        finally
        {
            await CleanUpAsync([salon, other], [plan], [weaker, stronger]);
        }
    }

    [Fact]
    public async Task A_code_is_refused_when_unknown_expired_used_up_or_for_another_plan()
    {
        var plan = await CreatePlanAsync(10000);
        var elsewhere = await CreatePlanAsync(10000);
        var oneUse = await CreateCodeAsync(10, maxUses: 1);
        var expired = await CreateCodeAsync(10, until: api.Clock.GetUtcNow().AddMinutes(1));
        var restricted = await CreateCodeAsync(10, plans: [elsewhere.Code]);
        var first = await FreeSalonAsync();
        var second = await FreeSalonAsync();
        try
        {
            (await SubscribeAsync(first, plan, "N-EXISTE-PAS")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await SubscribeAsync(first, plan, restricted.Code)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            await WithDbAsync(db => db.PromoCodes.Where(c => c.Id == expired.Id).ExecuteUpdateAsync(set => set.SetProperty(c => c.ValidUntil, api.Clock.GetUtcNow().AddMinutes(-1))));
            var late = await SubscribeAsync(first, plan, expired.Code);
            late.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await late.Content.ReadAsStringAsync()).Should().Contain("promo.invalid");

            (await SubscribeAsync(first, plan, oneUse.Code)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await SubscribeAsync(second, plan, oneUse.Code)).StatusCode.Should().Be(HttpStatusCode.BadRequest); // the only allowed use is gone

            // The check says why instead of failing.
            var check = (await (await second.Owner.Client.PostAsJsonAsync("/api/v1/pro/promo-codes/check", new SubscribeInput(plan.Code, "Monthly", oneUse.Code)))
                .Content.ReadFromJsonAsync<PromoCheckDto>())!;
            check.Valid.Should().BeFalse();
            check.DiscountedPrice.Should().Be(10000);
            check.Message.Should().NotBeNullOrEmpty();
        }
        finally
        {
            await CleanUpAsync([first, second], [plan, elsewhere], [oneUse, expired, restricted]);
        }
    }

    [Fact]
    public async Task Only_an_administrator_manages_codes_and_their_rules_are_enforced()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        using var client = await TestAccounts.ClientAsync(api);
        var created = await CreateCodeAsync(15);
        try
        {
            (await client.Client.GetAsync("/api/v1/admin/promo-codes")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await client.Client.PostAsJsonAsync("/api/v1/admin/promo-codes", new PromoCodeInput("PIRATE", 99))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            (await admin.Client.PostAsJsonAsync("/api/v1/admin/promo-codes", new PromoCodeInput(created.Code, 10))).StatusCode.Should().Be(HttpStatusCode.Conflict); // taken
            (await admin.Client.PostAsJsonAsync("/api/v1/admin/promo-codes", new PromoCodeInput("OK-CODE", 95))).StatusCode.Should().Be(HttpStatusCode.BadRequest); // above 90 %
            (await admin.Client.PostAsJsonAsync("/api/v1/admin/promo-codes", new PromoCodeInput("a b", 10))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await admin.Client.PostAsJsonAsync("/api/v1/admin/promo-codes", new PromoCodeInput("PLANX", 10, PlanCodes: ["inconnue"]))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var updated = await admin.Client.PutAsJsonAsync($"/api/v1/admin/promo-codes/{created.Id}", new PromoCodeInput("IGNORED", 25, IsActive: false));
            updated.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = (await updated.Content.ReadFromJsonAsync<PromoCodeDto>())!;
            dto.Code.Should().Be(created.Code); // a code never changes
            dto.Percent.Should().Be(25);
            dto.IsActive.Should().BeFalse();
        }
        finally
        {
            await CleanUpAsync([], [], [created]);
        }
    }
}
