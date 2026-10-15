using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Billing;
using KekeBeauty.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using KekeBeauty.Infrastructure.Scheduling;

namespace KekeBeauty.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PlanConditionsTests(ApiFixture api)
{
    /// <summary>Plans created by a test are removed so that the other tests still see exactly the seeded ones.</summary>
    private async Task DeletePlansAsync(params Guid[] ids)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>().Plans.Where(p => ids.Contains(p.Id)).ExecuteDeleteAsync();
    }

    private static PlanUpdate Essential(string name, int? appointments = null, int? resources = null, int videos = 0, int commitment = 3) =>
        new(name, 5000, 50000, new PlanFeaturesDto(true, videos > 0, 15, false, true, videos, resources, appointments), true, "Pour démarrer", "Populaire", commitment);

    [Fact]
    public async Task An_admin_creates_plans_with_their_own_conditions_and_visitors_see_the_active_ones()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var name = $"Essentiel {Guid.NewGuid():N}"[..20];

        var created = await admin.Client.PostAsJsonAsync("/api/v1/admin/plans", Essential(name, appointments: 50, resources: 2));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var plan = (await created.Content.ReadFromJsonAsync<PlanDto>())!;
        plan.Code.Should().NotBeNullOrEmpty();
        plan.Badge.Should().Be("Populaire");
        plan.CommitmentMonths.Should().Be(3);
        plan.Features.MaxAppointmentsPerMonth.Should().Be(50);

        using var visitor = api.CreateClient();
        (await visitor.GetFromJsonAsync<List<PlanDto>>("/api/v1/plans"))!.Should().Contain(p => p.Id == plan.Id);

        var hidden = await admin.Client.PutAsJsonAsync($"/api/v1/admin/plans/{plan.Id}", Essential(name) with { IsActive = false });
        hidden.StatusCode.Should().Be(HttpStatusCode.OK);
        (await visitor.GetFromJsonAsync<List<PlanDto>>("/api/v1/plans"))!.Should().NotContain(p => p.Id == plan.Id);

        // Refused: no name, a limit out of range, an engagement of 0 months, a price the gateway cannot take.
        (await admin.Client.PostAsJsonAsync("/api/v1/admin/plans", Essential(" "))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.Client.PostAsJsonAsync("/api/v1/admin/plans", Essential("Test", resources: 0))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.Client.PostAsJsonAsync("/api/v1/admin/plans", Essential("Test", commitment: 0))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.Client.PostAsJsonAsync("/api/v1/admin/plans", Essential("Test") with { MonthlyPrice = 1234 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var client = await TestAccounts.ClientAsync(api);
        (await client.Client.PostAsJsonAsync("/api/v1/admin/plans", Essential("Pirate"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await DeletePlansAsync(plan.Id);
    }

    [Fact]
    public async Task A_subscription_follows_the_commitment_and_the_limits_of_its_plan()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var plan = (await (await admin.Client.PostAsJsonAsync("/api/v1/admin/plans",
            Essential($"Mini {Guid.NewGuid():N}"[..14], appointments: 1, resources: 1, videos: 1, commitment: 3))).Content.ReadFromJsonAsync<PlanDto>())!;
        var salon = await TestSalons.PublishAsync(api, admin, subscribe: false, autoConfirm: true);
        using var client = await TestAccounts.ClientAsync(api);
        var baseUrl = $"/api/v1/pro/establishments/{salon.Id}";

        try
        {
            var checkout = (await (await salon.Owner.Client.PostAsJsonAsync($"{baseUrl}/subscription", new SubscribeInput(plan.Code, "Monthly")))
                .Content.ReadFromJsonAsync<CheckoutDto>())!;
            (await salon.Owner.Client.PostAsJsonAsync($"/api/v1/dev/payments/{checkout.TransactionId}/simulate", new { status = "accepted" })).EnsureSuccessStatusCode();
            (await salon.Owner.Client.PutAsJsonAsync($"{baseUrl}/booking-settings", new BookingSettingsDto(true, true, 0, 60, 120, 30))).EnsureSuccessStatusCode();

            var overview = (await salon.Owner.Client.GetFromJsonAsync<SubscriptionOverview>($"{baseUrl}/subscription"))!;
            overview.Subscription!.PlanCode.Should().Be(plan.Code);
            (overview.Subscription.CommitmentEndDate!.Value - overview.Subscription.StartDate!.Value).TotalDays.Should().BeInRange(88, 93);
            overview.Features.MaxAppointmentsPerMonth.Should().Be(1);

            // One appointment a month: the second one is refused, and the client is told why.
            var day = TestSalons.NextOpenDay(api, 3);
            var first = await client.Client.PostAsJsonAsync("/api/v1/appointments", new BookingRequest(salon.Id, [salon.ServiceHalfHour], null, day, "07 00 00 12 34", null));
            first.StatusCode.Should().Be(HttpStatusCode.Created);
            var second = await client.Client.PostAsJsonAsync("/api/v1/appointments", new BookingRequest(salon.Id, [salon.ServiceHalfHour], null, day.AddHours(2), "07 00 00 12 34", null));
            second.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await second.Content.ReadAsStringAsync()).Should().Contain("booking.limit_reached");

            // One resource included: the default one is there, a second is refused.
            var resources = await salon.Owner.Client.PostAsJsonAsync($"{baseUrl}/resources", new ResourceInput("Deuxième chaise", [], true));
            resources.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            await DeletePlansAsync(plan.Id);
        }
    }
}
