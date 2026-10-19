using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Growth;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

/// <summary>Lot 7, beyond V1: waiting list, loyalty, gift cards, group bookings, recommendations and re-engagement.</summary>
[Collection(ApiCollection.Name)]
public sealed class Lot7GrowthTests(ApiFixture api)
{
    private const string Phone = "07 00 00 12 34";

    private async Task WithDbAsync(Func<KekeBeautyDbContext, Task> action)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>());
    }

    private async Task<T> WithDbAsync<T>(Func<KekeBeautyDbContext, Task<T>> action)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>());
    }

    private async Task<AppointmentDto> BookAsync(UserSession client, Salon salon, DateTimeOffset startsAt)
    {
        var response = await client.Client.PostAsJsonAsync("/api/v1/appointments",
            new BookingRequest(salon.Id, [salon.ServiceHalfHour], null, startsAt, Phone, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AppointmentDto>())!;
    }

    /// <summary>The visit took place and ended <paramref name="endedAgo"/> ago.</summary>
    private Task CompleteAsync(Guid appointmentId, TimeSpan endedAgo)
    {
        var end = api.Clock.GetUtcNow() - endedAgo;
        return WithDbAsync(db => db.Appointments.Where(a => a.Id == appointmentId).ExecuteUpdateAsync(set => set
            .SetProperty(a => a.Status, AppointmentStatus.Completed)
            .SetProperty(a => a.StartsAt, end.AddMinutes(-30))
            .SetProperty(a => a.EndsAt, end)));
    }

    private Task<List<string>> NotificationTitlesAsync(Guid userId, string templateCode) =>
        WithDbAsync(db => db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId && n.TemplateCode == templateCode)
            .Select(n => n.Body).ToListAsync());

    [Fact]
    public async Task A_waiting_client_is_told_when_a_slot_of_her_day_frees_up_and_only_once()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, autoConfirm: true);
        using var booker = await TestAccounts.ClientAsync(api);
        using var waiter = await TestAccounts.ClientAsync(api);
        try
        {
            var start = TestSalons.NextOpenDay(api, 4);
            var day = DateOnly.FromDateTime(start.UtcDateTime);
            var booked = await BookAsync(booker, salon, start);

            var join = await waiter.Client.PostAsJsonAsync("/api/v1/waitlist", new JoinWaitlistInput(salon.Id, day, salon.ServiceHalfHour));
            join.StatusCode.Should().Be(HttpStatusCode.OK, await join.Content.ReadAsStringAsync());
            var entry = (await join.Content.ReadFromJsonAsync<WaitlistEntryDto>())!;
            entry.Status.Should().Be("Waiting");

            // Asking again for the same day keeps the single entry.
            var again = (await (await waiter.Client.PostAsJsonAsync("/api/v1/waitlist", new JoinWaitlistInput(salon.Id, day, null))).Content.ReadFromJsonAsync<WaitlistEntryDto>())!;
            again.Id.Should().Be(entry.Id);

            // A day in the past cannot be waited for.
            (await waiter.Client.PostAsJsonAsync("/api/v1/waitlist", new JoinWaitlistInput(salon.Id, day.AddDays(-30), null)))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // The establishment sees who waits.
            var seen = (await salon.Owner.Client.GetFromJsonAsync<List<WaitlistEntryDto>>($"/api/v1/pro/establishments/{salon.Id}/waitlist"))!;
            seen.Should().ContainSingle(w => w.Id == entry.Id).Which.ServiceName.Should().NotBeNull();

            (await booker.Client.PostAsJsonAsync($"/api/v1/appointments/{booked.Id}/cancel", new { })).EnsureSuccessStatusCode();

            (await NotificationTitlesAsync(waiter.UserId, "waitlist.slot_freed")).Should().ContainSingle().Which.Should().Contain("libérer");
            var mine = (await waiter.Client.GetFromJsonAsync<List<WaitlistEntryDto>>("/api/v1/me/waitlist"))!;
            mine.Single(w => w.Id == entry.Id).Status.Should().Be("Notified");

            // The canceller herself is never told, and a spent entry is not told again.
            (await NotificationTitlesAsync(booker.UserId, "waitlist.slot_freed")).Should().BeEmpty();
            var second = await BookAsync(booker, salon, start);
            (await booker.Client.PostAsJsonAsync($"/api/v1/appointments/{second.Id}/cancel", new { })).EnsureSuccessStatusCode();
            (await NotificationTitlesAsync(waiter.UserId, "waitlist.slot_freed")).Should().ContainSingle();

            // Joining again then leaving.
            var rejoin = (await (await waiter.Client.PostAsJsonAsync("/api/v1/waitlist", new JoinWaitlistInput(salon.Id, day, null))).Content.ReadFromJsonAsync<WaitlistEntryDto>())!;
            rejoin.Id.Should().NotBe(entry.Id);
            (await waiter.Client.DeleteAsync($"/api/v1/me/waitlist/{rejoin.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await waiter.Client.DeleteAsync($"/api/v1/me/waitlist/{rejoin.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task Finished_visits_earn_loyalty_points_once_and_the_owner_grants_the_reward()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, autoConfirm: true);
        using var client = await TestAccounts.ClientAsync(api);
        using var visitor = api.CreateClient();
        try
        {
            var url = $"/api/v1/pro/establishments/{salon.Id}/loyalty";
            (await salon.Owner.Client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await visitor.GetAsync($"/api/v1/establishments/{salon.Slug}/loyalty")).StatusCode.Should().Be(HttpStatusCode.NotFound);

            (await salon.Owner.Client.PutAsJsonAsync(url, new LoyaltyProgramInput(true, 0, 50, "Un soin offert"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // A visit booked before the card existed: it must not earn anything.
            var before = await BookAsync(client, salon, TestSalons.NextOpenDay(api, 3));
            await CompleteAsync(before.Id, TimeSpan.FromDays(3));

            var save = await salon.Owner.Client.PutAsJsonAsync(url, new LoyaltyProgramInput(true, 10, 10, "-2 000 F sur votre prochaine prestation"));
            save.StatusCode.Should().Be(HttpStatusCode.OK, await save.Content.ReadAsStringAsync());
            (await visitor.GetFromJsonAsync<PublicLoyaltyDto>($"/api/v1/establishments/{salon.Slug}/loyalty"))!.PointsPer1000.Should().Be(10);

            // A visit after: it earns floor(price / 1000) * 10 points, once however often the card is read.
            api.Clock.Advance(TimeSpan.FromMinutes(1));
            var visit = await BookAsync(client, salon, TestSalons.NextOpenDay(api, 3));
            await WithDbAsync(db => db.Appointments.Where(a => a.Id == visit.Id).ExecuteUpdateAsync(set => set.SetProperty(a => a.Status, AppointmentStatus.Completed)));
            var expected = LoyaltyService.PointsFor(visit.TotalPrice, 10);
            expected.Should().BeGreaterThanOrEqualTo(10);

            for (var read = 0; read < 2; read++)
            {
                var cards = (await client.Client.GetFromJsonAsync<List<LoyaltyCardDto>>("/api/v1/me/loyalty"))!;
                var card = cards.Single(c => c.EstablishmentId == salon.Id);
                card.Points.Should().Be(expected);
                card.RewardsAvailable.Should().Be(expected / 10);
                card.History.Should().ContainSingle(l => l.Kind == "Earn");
            }

            var members = (await salon.Owner.Client.GetFromJsonAsync<List<LoyaltyMemberDto>>($"{url}/members"))!;
            members.Should().ContainSingle(m => m.ClientUserId == client.UserId).Which.Points.Should().Be(expected);

            // Spend every reward, then one more is refused.
            for (var i = 0; i < expected / 10; i++)
            {
                (await salon.Owner.Client.PostAsJsonAsync($"{url}/redeem", new LoyaltyRedeemInput(client.UserId))).EnsureSuccessStatusCode();
            }

            var refused = await salon.Owner.Client.PostAsJsonAsync($"{url}/redeem", new LoyaltyRedeemInput(client.UserId));
            refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await refused.Content.ReadAsStringAsync()).Should().Contain("loyalty.not_enough_points");
            (await NotificationTitlesAsync(client.UserId, "loyalty.redeemed")).Should().NotBeEmpty();

            // Another owner cannot touch this card.
            using var stranger = await TestAccounts.PartnerAsync(api);
            (await stranger.Client.PostAsJsonAsync($"{url}/redeem", new LoyaltyRedeemInput(client.UserId))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task A_gift_card_is_issued_claimed_and_spent_without_ever_going_below_zero()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, subscribe: false);
        using var client = await TestAccounts.ClientAsync(api);
        using var other = await TestAccounts.ClientAsync(api);
        try
        {
            var baseUrl = $"/api/v1/pro/establishments/{salon.Id}/gift-cards";
            (await salon.Owner.Client.PostAsJsonAsync(baseUrl, new CreateGiftCardInput(500, "Aya", null, null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var created = await salon.Owner.Client.PostAsJsonAsync(baseUrl, new CreateGiftCardInput(20000, "Aya Traoré", "Fatou", "Joyeux anniversaire !"));
            created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
            var card = (await created.Content.ReadFromJsonAsync<GiftCardDto>())!;
            card.Code.Should().MatchRegex("^KB-[2-9A-Z]{4}-[2-9A-Z]{4}$");
            card.Balance.Should().Be(20000);
            card.Status.Should().Be("Active");

            // The client types it however she likes.
            var typed = card.Code.Replace("-", " ").ToLowerInvariant();
            var claim = await client.Client.PostAsJsonAsync("/api/v1/me/gift-cards", new ClaimGiftCardInput(typed));
            claim.StatusCode.Should().Be(HttpStatusCode.OK, await claim.Content.ReadAsStringAsync());
            (await other.Client.PostAsJsonAsync("/api/v1/me/gift-cards", new ClaimGiftCardInput(card.Code))).StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await other.Client.PostAsJsonAsync("/api/v1/me/gift-cards", new ClaimGiftCardInput("KB-2222-2222"))).StatusCode.Should().Be(HttpStatusCode.NotFound);

            var lookup = (await salon.Owner.Client.GetFromJsonAsync<GiftCardDto>($"{baseUrl}/lookup?code={Uri.EscapeDataString(typed)}"))!;
            lookup.Id.Should().Be(card.Id);
            lookup.HasHolder.Should().BeTrue();

            var spent = await salon.Owner.Client.PostAsJsonAsync($"/api/v1/pro/gift-cards/{card.Id}/redeem", new RedeemGiftCardInput(15000, null));
            spent.StatusCode.Should().Be(HttpStatusCode.OK, await spent.Content.ReadAsStringAsync());
            (await spent.Content.ReadFromJsonAsync<GiftCardDto>())!.Balance.Should().Be(5000);

            var over = await salon.Owner.Client.PostAsJsonAsync($"/api/v1/pro/gift-cards/{card.Id}/redeem", new RedeemGiftCardInput(6000, null));
            over.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await over.Content.ReadAsStringAsync()).Should().Contain("gift_card.insufficient_balance");

            // Several counters at once on the rest: exactly one debit of 5 000 goes through.
            var racing = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
                salon.Owner.Client.PostAsJsonAsync($"/api/v1/pro/gift-cards/{card.Id}/redeem", new RedeemGiftCardInput(5000, null))));
            racing.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);

            var mine = (await client.Client.GetFromJsonAsync<List<GiftCardDto>>("/api/v1/me/gift-cards"))!.Single(g => g.Id == card.Id);
            mine.Balance.Should().Be(0);
            mine.Status.Should().Be("Spent");
            mine.Redemptions.Should().HaveCount(2);
            (await NotificationTitlesAsync(client.UserId, "gift_card.redeemed")).Should().HaveCount(2);

            // A cancelled card can no longer be used; a stranger cannot cancel it.
            var second = (await (await salon.Owner.Client.PostAsJsonAsync(baseUrl, new CreateGiftCardInput(10000, "Mariam", null, null, 6))).Content.ReadFromJsonAsync<GiftCardDto>())!;
            using var stranger = await TestAccounts.PartnerAsync(api);
            (await stranger.Client.PostAsync($"/api/v1/pro/gift-cards/{second.Id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await salon.Owner.Client.PostAsync($"/api/v1/pro/gift-cards/{second.Id}/cancel", null)).EnsureSuccessStatusCode();
            (await salon.Owner.Client.PostAsJsonAsync($"/api/v1/pro/gift-cards/{second.Id}/redeem", new RedeemGiftCardInput(1000, null)))
                .StatusCode.Should().Be(HttpStatusCode.Conflict);

            var list = (await salon.Owner.Client.GetFromJsonAsync<List<GiftCardDto>>(baseUrl))!;
            list.Select(g => g.Status).Should().BeEquivalentTo(["Spent", "Cancelled"]);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task A_group_request_is_answered_with_a_quote_and_can_be_cancelled_by_the_client()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, subscribe: false);
        using var client = await TestAccounts.ClientAsync(api);
        try
        {
            var when = api.Clock.GetUtcNow().AddDays(30);
            (await client.Client.PostAsJsonAsync("/api/v1/group-bookings",
                new GroupBookingInput(salon.Id, "Wedding", when, 1, "Coiffure et maquillage", null, Phone))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await client.Client.PostAsJsonAsync("/api/v1/group-bookings",
                new GroupBookingInput(salon.Id, "Wedding", api.Clock.GetUtcNow().AddHours(2), 8, "Coiffure", null, Phone))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var created = await client.Client.PostAsJsonAsync("/api/v1/group-bookings",
                new GroupBookingInput(salon.Id, "Wedding", when, 8, "Coiffure et maquillage de la mariée et de 7 demoiselles", "Cérémonie à 15 h", Phone));
            created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
            var request = (await created.Content.ReadFromJsonAsync<GroupBookingDto>())!;
            request.Status.Should().Be("Pending");
            request.ClientPhone.Should().BeNull("the client's own view does not need to echo her number");
            (await NotificationTitlesAsync(salon.Owner.UserId, "group_booking.requested")).Should().ContainSingle();

            var seen = (await salon.Owner.Client.GetFromJsonAsync<List<GroupBookingDto>>($"/api/v1/pro/establishments/{salon.Id}/group-bookings"))!;
            seen.Single(r => r.Id == request.Id).ClientPhone.Should().NotBeNullOrEmpty();

            var answerUrl = $"/api/v1/pro/group-bookings/{request.Id}/answer";
            (await salon.Owner.Client.PostAsJsonAsync(answerUrl, new GroupBookingAnswerInput(true, null, "Avec plaisir"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var accepted = await salon.Owner.Client.PostAsJsonAsync(answerUrl, new GroupBookingAnswerInput(true, 240000, "Avec plaisir, nous venons à 10 h."));
            accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
            (await salon.Owner.Client.PostAsJsonAsync(answerUrl, new GroupBookingAnswerInput(false, null, "Finalement non"))).StatusCode.Should().Be(HttpStatusCode.Conflict);

            var mine = (await client.Client.GetFromJsonAsync<List<GroupBookingDto>>("/api/v1/me/group-bookings"))!.Single(r => r.Id == request.Id);
            mine.Status.Should().Be("Accepted");
            mine.QuotedPrice.Should().Be(240000);
            (await NotificationTitlesAsync(client.UserId, "group_booking.accepted")).Should().ContainSingle().Which.Should().Contain("240000");

            (await client.Client.PostAsync($"/api/v1/me/group-bookings/{request.Id}/cancel", null)).EnsureSuccessStatusCode();
            (await client.Client.PostAsync($"/api/v1/me/group-bookings/{request.Id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task Recommendations_suggest_nearby_places_with_a_reason_and_leave_out_places_already_visited()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, autoConfirm: true);
        using var client = await TestAccounts.ClientAsync(api);
        using var visitor = api.CreateClient();
        try
        {
            var around = (await visitor.GetFromJsonAsync<List<RecommendationDto>>("/api/v1/recommendations?lat=5.36&lng=-3.99&count=20"))!;
            around.Should().NotBeEmpty();
            around.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.Reason));
            around.Should().Contain(r => r.Establishment.Id == salon.Id);

            var visit = await BookAsync(client, salon, TestSalons.NextOpenDay(api, 3));
            await CompleteAsync(visit.Id, TimeSpan.FromDays(1));
            var forClient = (await client.Client.GetFromJsonAsync<List<RecommendationDto>>("/api/v1/recommendations?lat=5.36&lng=-3.99&count=20"))!;
            forClient.Should().NotContain(r => r.Establishment.Id == salon.Id);
            forClient.Should().Contain(r => r.Reason.StartsWith("Vous aimez", StringComparison.Ordinal), "the categories of the visited salon are preferred");

            // Without a position, the best rated places are suggested.
            (await visitor.GetFromJsonAsync<List<RecommendationDto>>("/api/v1/recommendations"))!.Should().NotBeEmpty();
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task A_client_is_invited_back_once_after_her_last_visit_unless_she_already_rebooked()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, autoConfirm: true);
        using var lapsed = await TestAccounts.ClientAsync(api);
        using var loyal = await TestAccounts.ClientAsync(api);
        var job = api.Services.GetRequiredService<ReengagementMaintenance>();
        try
        {
            var settings = (await admin.Client.GetFromJsonAsync<AppSettingsDto>("/api/v1/admin/settings"))!;
            settings.ReengagementDays.Should().Be(30);
            (await admin.Client.PutAsJsonAsync("/api/v1/admin/settings", settings with { ReengagementDays = 400 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var old = await BookAsync(lapsed, salon, TestSalons.NextOpenDay(api, 3));
            await CompleteAsync(old.Id, TimeSpan.FromDays(31));

            // The other client came at the same time but already booked again: nothing to send her.
            var hers = await BookAsync(loyal, salon, TestSalons.NextOpenDay(api, 3, hour: 11));
            await CompleteAsync(hers.Id, TimeSpan.FromDays(31));
            await BookAsync(loyal, salon, TestSalons.NextOpenDay(api, 5, hour: 14));

            await job.RunOnceAsync(CancellationToken.None);
            (await NotificationTitlesAsync(lapsed.UserId, "client.reengagement")).Should().ContainSingle();
            (await NotificationTitlesAsync(loyal.UserId, "client.reengagement")).Should().BeEmpty();

            await job.RunOnceAsync(CancellationToken.None);
            (await NotificationTitlesAsync(lapsed.UserId, "client.reengagement")).Should().ContainSingle("a visit is followed up once");

            // Switched off by the admins: nothing is sent at all.
            (await admin.Client.PutAsJsonAsync("/api/v1/admin/settings", settings with { ReengagementDays = 0 })).EnsureSuccessStatusCode();
            (await job.RunOnceAsync(CancellationToken.None)).Should().Be(0);
            (await admin.Client.PutAsJsonAsync("/api/v1/admin/settings", settings)).EnsureSuccessStatusCode();
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }
}
