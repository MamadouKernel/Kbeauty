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
public sealed class TrialAndPlanChangeTests(ApiFixture api)
{
    private static string Sub(Salon salon) => $"/api/v1/pro/establishments/{salon.Id}/subscription";

    private static Task<SubscriptionOverview> OverviewAsync(Salon salon) => salon.Owner.Client.GetFromJsonAsync<SubscriptionOverview>(Sub(salon))!;

    private async Task<Salon> FreeSalonAsync()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        return await TestSalons.PublishAsync(api, admin, subscribe: false);
    }

    private async Task<PlanDto> CreatePlanAsync(string prefix, decimal monthly, int trialDays = 0, int commitment = 3)
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var body = new PlanUpdate(
            $"{prefix} {Guid.NewGuid():N}"[..16], monthly, monthly * 10, new PlanFeaturesDto(true, false, 15, false, true, 0), true, "Test", null,
            commitment, null, trialDays);
        var response = await admin.Client.PostAsJsonAsync("/api/v1/admin/plans", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PlanDto>())!;
    }

    private async Task WithDbAsync(Func<KekeBeautyDbContext, Task> action)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>());
    }

    private async Task CleanUpAsync(Salon salon, params PlanDto[] plans)
    {
        await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        var ids = plans.Select(p => p.Id).ToArray();
        await WithDbAsync(db => db.Plans.Where(p => ids.Contains(p.Id)).ExecuteDeleteAsync());
    }

    private async Task RunMaintenanceAsync() => await api.Services.GetRequiredService<BillingMaintenance>().RunOnceAsync(CancellationToken.None);

    private static async Task PayAsync(Salon salon, CheckoutDto checkout) =>
        (await salon.Owner.Client.PostAsJsonAsync($"/api/v1/dev/payments/{checkout.TransactionId}/simulate", new { status = "accepted" })).EnsureSuccessStatusCode();

    [Fact]
    public async Task A_trial_unlocks_the_plan_without_payment_and_paying_early_keeps_every_day_of_it()
    {
        var plan = await CreatePlanAsync("Essai", 5000, trialDays: 14);
        var salon = await FreeSalonAsync();
        try
        {
            (await OverviewAsync(salon)).TrialEligiblePlanCodes.Should().Contain(plan.Code);

            var started = await salon.Owner.Client.PostAsJsonAsync($"{Sub(salon)}/trial", new SubscribeInput(plan.Code, "Monthly"));
            started.StatusCode.Should().Be(HttpStatusCode.OK, await started.Content.ReadAsStringAsync());
            var trial = (await started.Content.ReadFromJsonAsync<SubscriptionDto>())!;
            trial.Status.Should().Be("Trialing");
            (trial.TrialEndsAt!.Value - api.Clock.GetUtcNow()).TotalDays.Should().BeInRange(13.9, 14.1);

            var overview = await OverviewAsync(salon);
            overview.EffectivePlanCode.Should().Be(plan.Code); // the features are available during the trial
            overview.TrialEligiblePlanCodes.Should().BeEmpty();
            (await salon.Owner.Client.PostAsJsonAsync($"{Sub(salon)}/trial", new SubscribeInput(plan.Code, "Monthly"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await salon.Owner.Client.PostAsJsonAsync(Sub(salon), new SubscribeInput(plan.Code, "Monthly"))).StatusCode.Should().Be(HttpStatusCode.Conflict);

            // Paying before the trial ends: the paid period starts when the trial ends.
            var paying = await salon.Owner.Client.PostAsync($"/api/v1/pro/subscriptions/{trial.Id}/trial/pay", null);
            paying.StatusCode.Should().Be(HttpStatusCode.OK, await paying.Content.ReadAsStringAsync());
            await PayAsync(salon, (await paying.Content.ReadFromJsonAsync<CheckoutDto>())!);

            var active = (await OverviewAsync(salon)).Subscription!;
            active.Status.Should().Be("Active");
            active.CurrentPeriodEnd!.Value.Should().BeCloseTo(trial.TrialEndsAt!.Value.AddMonths(1), TimeSpan.FromMinutes(1));
            active.CommitmentEndDate!.Value.Should().BeCloseTo(trial.TrialEndsAt!.Value.AddMonths(3), TimeSpan.FromMinutes(1));
        }
        finally
        {
            await CleanUpAsync(salon, plan);
        }
    }

    [Fact]
    public async Task A_trial_that_ends_unpaid_returns_to_the_free_plan_and_is_not_offered_again()
    {
        var plan = await CreatePlanAsync("Essai", 5000, trialDays: 7);
        var salon = await FreeSalonAsync();
        try
        {
            var trial = (await (await salon.Owner.Client.PostAsJsonAsync($"{Sub(salon)}/trial", new SubscribeInput(plan.Code, "Monthly")))
                .Content.ReadFromJsonAsync<SubscriptionDto>())!;

            var past = api.Clock.GetUtcNow().AddHours(-1);
            await WithDbAsync(db => db.Subscriptions.Where(s => s.Id == trial.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.TrialEndsAt, past).SetProperty(s => s.CurrentPeriodEnd, past)));
            await RunMaintenanceAsync();

            var overview = await OverviewAsync(salon);
            overview.Subscription.Should().BeNull();
            overview.EffectivePlanCode.Should().Be("free");
            overview.TrialEligiblePlanCodes.Should().NotContain(plan.Code);
            var again = await salon.Owner.Client.PostAsJsonAsync($"{Sub(salon)}/trial", new SubscribeInput(plan.Code, "Monthly"));
            again.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await again.Content.ReadAsStringAsync()).Should().Contain("subscription.trial_used");

            // Nothing stops a normal subscription afterwards.
            (await salon.Owner.Client.PostAsJsonAsync(Sub(salon), new SubscribeInput(plan.Code, "Monthly"))).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await CleanUpAsync(salon, plan);
        }
    }

    [Fact]
    public async Task Ending_a_trial_by_hand_is_final_and_a_plan_without_trial_refuses_one()
    {
        var withTrial = await CreatePlanAsync("Essai", 5000, trialDays: 7);
        var without = await CreatePlanAsync("Direct", 5000);
        var salon = await FreeSalonAsync();
        try
        {
            var refused = await salon.Owner.Client.PostAsJsonAsync($"{Sub(salon)}/trial", new SubscribeInput(without.Code, "Monthly"));
            refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await refused.Content.ReadAsStringAsync()).Should().Contain("plan.trial_not_offered");

            var trial = (await (await salon.Owner.Client.PostAsJsonAsync($"{Sub(salon)}/trial", new SubscribeInput(withTrial.Code, "Annual")))
                .Content.ReadFromJsonAsync<SubscriptionDto>())!;
            (await salon.Owner.Client.DeleteAsync($"/api/v1/pro/subscriptions/{trial.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

            var overview = await OverviewAsync(salon);
            overview.Subscription.Should().BeNull();
            overview.EffectivePlanCode.Should().Be("free");
            overview.TrialEligiblePlanCodes.Should().NotContain(withTrial.Code);
        }
        finally
        {
            await CleanUpAsync(salon, withTrial, without);
        }
    }

    [Fact]
    public async Task An_upgrade_is_quoted_with_the_unused_part_credited_and_replaces_the_subscription_once_paid()
    {
        var small = await CreatePlanAsync("Petit", 5000);
        var big = await CreatePlanAsync("Grand", 10000);
        var salon = await FreeSalonAsync();
        try
        {
            var first = (await (await salon.Owner.Client.PostAsJsonAsync(Sub(salon), new SubscribeInput(small.Code, "Monthly"))).Content.ReadFromJsonAsync<CheckoutDto>())!;
            await PayAsync(salon, first);
            var current = (await OverviewAsync(salon)).Subscription!;

            var quoted = await salon.Owner.Client.PostAsJsonAsync($"/api/v1/pro/subscriptions/{current.Id}/change-quote", new SubscribeInput(big.Code, "Monthly"));
            quoted.StatusCode.Should().Be(HttpStatusCode.OK, await quoted.Content.ReadAsStringAsync());
            var quote = (await quoted.Content.ReadFromJsonAsync<ChangeQuoteDto>())!;
            quote.Kind.Should().Be("Upgrade");
            quote.Credit.Should().BeInRange(4900, 5000);
            quote.NewPrice.Should().Be(10000);
            quote.AmountDue.Should().BeInRange(5000, 5100);

            // Nothing changes until the difference is paid.
            var changed = await salon.Owner.Client.PostAsJsonAsync($"/api/v1/pro/subscriptions/{current.Id}/change", new SubscribeInput(big.Code, "Monthly"));
            changed.StatusCode.Should().Be(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
            var result = (await changed.Content.ReadFromJsonAsync<ChangeResultDto>())!;
            result.Outcome.Should().Be("PaymentRequired");
            (await OverviewAsync(salon)).Subscription!.PlanCode.Should().Be(small.Code);

            await PayAsync(salon, result.Checkout!);

            var upgraded = (await OverviewAsync(salon)).Subscription!;
            upgraded.PlanCode.Should().Be(big.Code);
            upgraded.Status.Should().Be("Active");
            upgraded.UnitPrice.Should().Be(10000); // renewals are at the full price of the new plan
            upgraded.Id.Should().NotBe(current.Id);
            await WithDbAsync(async db =>
            {
                (await db.Subscriptions.AsNoTracking().SingleAsync(s => s.Id == current.Id)).Status.Should().Be(SubscriptionStatus.Cancelled);
                (await db.Subscriptions.CountAsync(s => s.EstablishmentId == salon.Id && s.Status == SubscriptionStatus.Active)).Should().Be(1);
                var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.SubscriptionId == upgraded.Id);
                invoice.Amount.Should().Be(quote.AmountDue);
            });
        }
        finally
        {
            await CleanUpAsync(salon, small, big);
        }
    }

    [Fact]
    public async Task A_downgrade_is_scheduled_for_the_end_of_the_commitment_can_be_cancelled_and_then_applies()
    {
        var small = await CreatePlanAsync("Petit", 5000);
        var big = await CreatePlanAsync("Grand", 10000);
        var salon = await FreeSalonAsync();
        try
        {
            await PayAsync(salon, (await (await salon.Owner.Client.PostAsJsonAsync(Sub(salon), new SubscribeInput(big.Code, "Monthly"))).Content.ReadFromJsonAsync<CheckoutDto>())!);
            var current = (await OverviewAsync(salon)).Subscription!;
            var url = $"/api/v1/pro/subscriptions/{current.Id}";

            var quote = (await (await salon.Owner.Client.PostAsJsonAsync($"{url}/change-quote", new SubscribeInput(small.Code, "Monthly"))).Content.ReadFromJsonAsync<ChangeQuoteDto>())!;
            quote.Kind.Should().Be("Downgrade");
            quote.AmountDue.Should().Be(0);
            quote.EffectiveAt.Should().BeCloseTo(current.CommitmentEndDate!.Value, TimeSpan.FromMinutes(1));

            var scheduled = (await (await salon.Owner.Client.PostAsJsonAsync($"{url}/change", new SubscribeInput(small.Code, "Monthly"))).Content.ReadFromJsonAsync<ChangeResultDto>())!;
            scheduled.Outcome.Should().Be("Scheduled");
            var pending = await OverviewAsync(salon);
            pending.EffectivePlanCode.Should().Be(big.Code); // the paid plan keeps its value until then
            pending.Subscription!.PendingPlanCode.Should().Be(small.Code);

            (await salon.Owner.Client.DeleteAsync($"{url}/change")).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await OverviewAsync(salon)).Subscription!.PendingPlanCode.Should().BeNull();
            (await salon.Owner.Client.DeleteAsync($"{url}/change")).StatusCode.Should().Be(HttpStatusCode.Conflict);

            // Scheduled again, and the day comes.
            await salon.Owner.Client.PostAsJsonAsync($"{url}/change", new SubscribeInput(small.Code, "Monthly"));
            var now = api.Clock.GetUtcNow();
            await WithDbAsync(db => db.Subscriptions.Where(s => s.Id == current.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.PendingChangeAt, now.AddMinutes(-5))));
            await RunMaintenanceAsync();

            var after = await OverviewAsync(salon);
            after.Subscription!.PlanCode.Should().Be(small.Code);
            after.Subscription.UnitPrice.Should().Be(5000);
            after.Subscription.PendingPlanCode.Should().BeNull();
            after.EffectivePlanCode.Should().Be(small.Code);
            after.Subscription.CommitmentEndDate!.Value.Should().BeCloseTo(now.AddMonths(3), TimeSpan.FromMinutes(10));
        }
        finally
        {
            await CleanUpAsync(salon, small, big);
        }
    }

    [Fact]
    public async Task A_switch_from_monthly_to_annual_bills_the_annual_price_for_a_full_year_at_the_end_of_the_commitment()
    {
        var plan = await CreatePlanAsync("Rythme", 5000);
        var salon = await FreeSalonAsync();
        try
        {
            await PayAsync(salon, (await (await salon.Owner.Client.PostAsJsonAsync(Sub(salon), new SubscribeInput(plan.Code, "Monthly"))).Content.ReadFromJsonAsync<CheckoutDto>())!);
            var current = (await OverviewAsync(salon)).Subscription!;
            var url = $"/api/v1/pro/subscriptions/{current.Id}";

            // Same plan, annual rhythm (10 months' price): cheaper per month, so it waits for the end of the commitment.
            var scheduled = (await (await salon.Owner.Client.PostAsJsonAsync($"{url}/change", new SubscribeInput(plan.Code, "Annual"))).Content.ReadFromJsonAsync<ChangeResultDto>())!;
            scheduled.Outcome.Should().Be("Scheduled");

            // The last monthly period is about to end on the boundary: the renewal invoice is the annual one.
            var boundary = api.Clock.GetUtcNow().AddDays(1);
            await WithDbAsync(db => db.Subscriptions.Where(s => s.Id == current.Id).ExecuteUpdateAsync(set => set
                .SetProperty(s => s.CurrentPeriodEnd, boundary).SetProperty(s => s.PendingChangeAt, boundary).SetProperty(s => s.CommitmentEndDate, boundary)));
            await RunMaintenanceAsync();

            Invoice renewal = null!;
            await WithDbAsync(async db => renewal = await db.Invoices.AsNoTracking().SingleAsync(i => i.SubscriptionId == current.Id && i.PeriodStart == boundary));
            renewal.Amount.Should().Be(50000); // not 5000: the price frozen when the change was chosen
            renewal.PeriodEnd.Should().BeCloseTo(BillingRules.PeriodEnd(boundary, BillingCycle.Annual), TimeSpan.FromSeconds(1));

            var paying = (await (await salon.Owner.Client.PostAsync($"/api/v1/pro/invoices/{renewal.Id}/pay", null)).Content.ReadFromJsonAsync<CheckoutDto>())!;
            await PayAsync(salon, paying);
            (await OverviewAsync(salon)).Subscription!.CurrentPeriodEnd!.Value.Should().BeCloseTo(renewal.PeriodEnd, TimeSpan.FromMinutes(1));

            // The day comes: the subscription itself becomes annual, and no further invoice is raised for this year.
            await WithDbAsync(db => db.Subscriptions.Where(s => s.Id == current.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.PendingChangeAt, api.Clock.GetUtcNow().AddMinutes(-5))));
            await RunMaintenanceAsync();

            var after = (await OverviewAsync(salon)).Subscription!;
            after.Cycle.Should().Be("Annual");
            after.UnitPrice.Should().Be(50000);
            after.PendingPlanCode.Should().BeNull();
            after.CurrentPeriodEnd!.Value.Should().BeCloseTo(renewal.PeriodEnd, TimeSpan.FromMinutes(1));
            await WithDbAsync(async db => (await db.Invoices.CountAsync(i => i.SubscriptionId == current.Id)).Should().Be(2));
        }
        finally
        {
            await CleanUpAsync(salon, plan);
        }
    }

    [Fact]
    public async Task A_change_is_refused_when_it_changes_nothing_when_the_subscription_is_not_active_or_belongs_to_someone_else()
    {
        var plan = await CreatePlanAsync("Petit", 5000);
        var salon = await FreeSalonAsync();
        using var stranger = await TestAccounts.PartnerAsync(api);
        try
        {
            await PayAsync(salon, (await (await salon.Owner.Client.PostAsJsonAsync(Sub(salon), new SubscribeInput(plan.Code, "Monthly"))).Content.ReadFromJsonAsync<CheckoutDto>())!);
            var current = (await OverviewAsync(salon)).Subscription!;
            var same = new SubscribeInput(plan.Code, "Monthly");

            var nothing = await salon.Owner.Client.PostAsJsonAsync($"/api/v1/pro/subscriptions/{current.Id}/change", same);
            nothing.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await nothing.Content.ReadAsStringAsync()).Should().Contain("subscription.no_change");

            (await stranger.Client.PostAsJsonAsync($"/api/v1/pro/subscriptions/{current.Id}/change-quote", new SubscribeInput(plan.Code, "Annual")))
                .StatusCode.Should().Be(HttpStatusCode.NotFound);

            await WithDbAsync(db => db.Subscriptions.Where(s => s.Id == current.Id).ExecuteUpdateAsync(set => set.SetProperty(s => s.Status, SubscriptionStatus.PastDue)));
            (await salon.Owner.Client.PostAsJsonAsync($"/api/v1/pro/subscriptions/{current.Id}/change", new SubscribeInput(plan.Code, "Annual")))
                .StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
        finally
        {
            await CleanUpAsync(salon, plan);
        }
    }
}
