using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Billing;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Reviews;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

/// <summary>Late-cancellation fee, tips paid online, payouts to collaborators, frozen commissions and before/after review photos.</summary>
[Collection(ApiCollection.Name)]
public sealed class MoneyAndPhotosTests(ApiFixture api)
{
    private const string Phone = "07 00 00 12 34";

    private FakePayoutGateway Payouts => api.Services.GetRequiredService<FakePayoutGateway>();

    private sealed record Setup(UserSession Admin, Salon Salon, UserSession Client, UserSession? Staff, Guid ResourceId);

    private async Task ResetSettingsAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>().AppSettings.ExecuteDeleteAsync();
    }

    private async Task<T> WithDbAsync<T>(Func<KekeBeautyDbContext, Task<T>> read)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>());
    }

    /// <summary>A salon that takes full online payment, optionally with a linked collaborator at 30 % commission.</summary>
    private async Task<Setup> ArrangeAsync(bool withStaff, bool onlinePayments = true)
    {
        await ResetSettingsAsync();
        var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, autoConfirm: true);
        var client = await TestAccounts.ClientAsync(api);
        var settings = (await admin.Client.GetFromJsonAsync<AppSettingsDto>("/api/v1/admin/settings"))!;
        (await admin.Client.PutAsJsonAsync("/api/v1/admin/settings", settings with { AppointmentPaymentsEnabled = onlinePayments, PlatformFeePercent = 0 })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await salon.Owner.Client.PutAsJsonAsync($"/api/v1/pro/establishments/{salon.Id}/payment-settings",
            new PaymentSettingsDto("Full", 30, "wave-cote-divoire", "0700000001", "Awa Koné", true))).StatusCode.Should().Be(HttpStatusCode.OK);

        var resource = (await salon.Owner.Client.GetFromJsonAsync<List<TeamMemberDto>>($"/api/v1/pro/establishments/{salon.Id}/team"))!.Single();
        UserSession? staff = null;
        if (withStaff)
        {
            staff = await TestAccounts.StaffAsync(api);
            (await salon.Owner.Client.PutAsJsonAsync($"/api/v1/pro/establishments/{salon.Id}/team/{resource.ResourceId}", new TeamAssignmentInput(staff.Email, 30)))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        return new Setup(admin, salon, client, staff, resource.ResourceId);
    }

    private async Task CleanupAsync(Setup s)
    {
        Payouts.Refuse = false;
        Payouts.NextState = PayoutState.Succeeded;
        s.Admin.Dispose();
        s.Client.Dispose();
        s.Staff?.Dispose();
        await TestCleanup.DeleteEstablishmentsAsync(api, s.Salon.Owner.UserId);
        await ResetSettingsAsync();
    }

    private async Task<AppointmentDto> BookAndPayAsync(Setup s, int daysAhead = 2)
    {
        var response = await s.Client.Client.PostAsJsonAsync("/api/v1/appointments",
            new BookingRequest(s.Salon.Id, [s.Salon.ServiceHour], null, TestSalons.NextOpenDay(api, daysAhead), Phone, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var booked = (await response.Content.ReadFromJsonAsync<AppointmentDto>())!;
        await SimulateAsync(booked.Payment!.PaymentUrl!);
        return booked;
    }

    private async Task SimulateAsync(string paymentUrl, string status = "accepted")
    {
        var transaction = System.Web.HttpUtility.ParseQueryString(new Uri(paymentUrl).Query)["tx"]!;
        using var anonymous = api.CreateClient();
        (await anonymous.PostAsJsonAsync($"/api/v1/dev/payments/{transaction}/simulate", new { status })).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private async Task MakePastAsync(Guid appointmentId, TimeSpan endedAgo)
    {
        var end = api.Clock.GetUtcNow() - endedAgo;
        var start = end.AddHours(-1);
        var expired = api.Clock.GetUtcNow().AddMinutes(-1);
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE scheduling.appointments SET starts_at = {start}, ends_at = {end}, expires_at = CASE WHEN expires_at IS NULL THEN NULL ELSE {expired} END WHERE id = {appointmentId}");
    }

    private async Task<AppointmentDto> OverAsync(Setup s)
    {
        var booked = await BookAndPayAsync(s);
        await MakePastAsync(booked.Id, TimeSpan.FromHours(3));
        await api.Services.GetRequiredService<AppointmentMaintenance>().RunOnceAsync(CancellationToken.None);
        return booked;
    }

    private static async Task<AppointmentDto> MineAsync(UserSession client, Guid id) =>
        (await client.Client.GetFromJsonAsync<List<AppointmentDto>>("/api/v1/me/appointments"))!.Single(a => a.Id == id);

    private static async Task<BalanceDto> BalanceAsync(Salon salon) =>
        (await salon.Owner.Client.GetFromJsonAsync<PartnerPaymentsView>($"/api/v1/pro/establishments/{salon.Id}/payments"))!.Balance;

    // ───────── Late-cancellation fee ─────────

    [Fact]
    public async Task A_late_cancellation_keeps_the_fee_refunds_the_rest_and_pays_the_fee_to_the_salon()
    {
        var s = await ArrangeAsync(withStaff: false);
        try
        {
            var settingsUrl = $"/api/v1/pro/establishments/{s.Salon.Id}/booking-settings";
            var current = (await s.Salon.Owner.Client.GetFromJsonAsync<BookingSettingsDto>(settingsUrl))!;
            (await s.Salon.Owner.Client.PutAsJsonAsync(settingsUrl, current with { LateCancelFeePercent = 101 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await s.Salon.Owner.Client.PutAsJsonAsync(settingsUrl, current with { CancelLimitMinutes = 7 * 24 * 60, LateCancelFeePercent = 30 })).StatusCode.Should().Be(HttpStatusCode.OK);

            var booked = await BookAndPayAsync(s);
            var late = await MineAsync(s.Client, booked.Id);
            late.CanCancel.Should().BeTrue("a fee turns the refusal into a choice");
            late.LateCancelFee.Should().Be(3000, "30 % of 10 000");

            var url = $"/api/v1/appointments/{booked.Id}/cancel";
            var refused = await s.Client.Client.PostAsJsonAsync(url, new CancelByClientInput(false));
            refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await refused.Content.ReadAsStringAsync()).Should().Contain("appointment.fee_not_accepted");

            var cancelled = await s.Client.Client.PostAsJsonAsync(url, new CancelByClientInput(true));
            cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
            (await cancelled.Content.ReadFromJsonAsync<AppointmentDto>())!.Status.Should().Be("CancelledByClient");

            var payment = await WithDbAsync(db => db.AppointmentPayments.AsNoTracking().SingleAsync(p => p.AppointmentId == booked.Id));
            payment.RefundAmount.Should().Be(7000);
            payment.RetainedNet.Should().Be(3000);
            payment.Status.ToString().Should().Be("Refunded");
            Payouts.Sent.Should().Contain(p => p.Amount == 7000, "only the part above the fee goes back to the client");

            (await BalanceAsync(s.Salon)).Payable.Should().Be(3000, "the fee is the salon's");
            var paid = await s.Admin.Client.PostAsync($"/api/v1/admin/payouts/establishments/{s.Salon.Id}", null);
            paid.StatusCode.Should().Be(HttpStatusCode.OK, await paid.Content.ReadAsStringAsync());
            (await paid.Content.ReadFromJsonAsync<PayoutDto>())!.Amount.Should().Be(3000);
        }
        finally
        {
            await CleanupAsync(s);
        }
    }

    [Fact]
    public async Task Without_a_fee_or_an_online_payment_a_late_cancellation_stays_refused()
    {
        var s = await ArrangeAsync(withStaff: false);
        try
        {
            var settingsUrl = $"/api/v1/pro/establishments/{s.Salon.Id}/booking-settings";
            var current = (await s.Salon.Owner.Client.GetFromJsonAsync<BookingSettingsDto>(settingsUrl))!;

            // Fee set but the client never paid online: nothing to keep, so the usual refusal applies.
            await s.Salon.Owner.Client.PutAsJsonAsync($"/api/v1/pro/establishments/{s.Salon.Id}/payment-settings",
                new PaymentSettingsDto("None", 30, "wave-cote-divoire", "0700000001", "Awa Koné", true));
            (await s.Salon.Owner.Client.PutAsJsonAsync(settingsUrl, current with { CancelLimitMinutes = 7 * 24 * 60, LateCancelFeePercent = 30 })).StatusCode.Should().Be(HttpStatusCode.OK);
            var booking = await s.Client.Client.PostAsJsonAsync("/api/v1/appointments",
                new BookingRequest(s.Salon.Id, [s.Salon.ServiceHour], null, TestSalons.NextOpenDay(api), Phone, null));
            var booked = (await booking.Content.ReadFromJsonAsync<AppointmentDto>())!;
            booked.Payment.Should().BeNull();

            (await s.Client.Client.PostAsJsonAsync($"/api/v1/appointments/{booked.Id}/cancel", new CancelByClientInput(true))).StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await MineAsync(s.Client, booked.Id)).CanCancel.Should().BeFalse();
        }
        finally
        {
            await CleanupAsync(s);
        }
    }

    // ───────── Tips paid online, payouts to collaborators, frozen commissions ─────────

    [Fact]
    public async Task A_tip_is_paid_online_then_paid_out_to_the_collaborator_with_her_commission()
    {
        var s = await ArrangeAsync(withStaff: true);
        try
        {
            var booked = await OverAsync(s);
            var tipUrl = $"/api/v1/appointments/{booked.Id}/tip";
            (await MineAsync(s.Client, booked.Id)).CanTip.Should().BeTrue();

            // The client pays the tip on the payment page; only then does it count.
            var started = await s.Client.Client.PostAsJsonAsync(tipUrl, new TipInput(500));
            started.StatusCode.Should().Be(HttpStatusCode.OK, await started.Content.ReadAsStringAsync());
            var checkout = (await started.Content.ReadFromJsonAsync<TipResultDto>())!;
            checkout.Status.Should().Be("Awaiting");
            checkout.PaymentUrl.Should().NotBeNullOrEmpty();
            (await MineAsync(s.Client, booked.Id)).TipAmount.Should().Be(0);

            await SimulateAsync(checkout.PaymentUrl!);
            var after = await MineAsync(s.Client, booked.Id);
            after.TipAmount.Should().Be(500);
            after.CanTip.Should().BeFalse();
            (await s.Client.Client.PostAsJsonAsync(tipUrl, new TipInput(500))).StatusCode.Should().Be(HttpStatusCode.Conflict, "one tip per appointment");

            // The rate changes later: the commission earned on this service was frozen when it was done.
            var teamUrl = $"/api/v1/pro/establishments/{s.Salon.Id}/team/{s.ResourceId}";
            (await s.Salon.Owner.Client.PutAsJsonAsync(teamUrl, new TeamAssignmentInput(null, 10))).StatusCode.Should().Be(HttpStatusCode.OK);
            var commissions = (await s.Staff!.Client.GetFromJsonAsync<List<CommissionSummaryDto>>("/api/v1/team/commissions"))!.Single();
            commissions.Commission.Should().Be(3000, "frozen at 30 %, not recomputed at 10 %");
            commissions.Tips.Should().Be(500);

            // Without a payout account nothing can be sent.
            var payoutUrl = $"/api/v1/pro/establishments/{s.Salon.Id}/team-payouts";
            var view = (await s.Salon.Owner.Client.GetFromJsonAsync<CollaboratorPayoutsView>(payoutUrl))!;
            var owed = view.Owed.Should().ContainSingle().Subject;
            owed.Should().Match<CollaboratorOwedDto>(o => o.Commission == 3000 && o.PayableCommission == 3000 && o.Tips == 500 && o.Payable == 3500 && !o.HasAccount);
            (await s.Salon.Owner.Client.PostAsync($"{payoutUrl}/{owed.UserId}", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // The collaborator gives her account; strangers cannot pay her, and a refused transfer gives everything back.
            (await s.Staff.Client.PutAsJsonAsync("/api/v1/team/payout-account", new CollaboratorAccountInput("wave-cote-divoire", "07 00 00 99 88", "Awa Koné")))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            (await s.Staff.Client.PutAsJsonAsync("/api/v1/team/payout-account", new CollaboratorAccountInput("nope", "abc", "A"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            using var stranger = await TestAccounts.PartnerAsync(api);
            (await stranger.Client.PostAsync($"{payoutUrl}/{owed.UserId}", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await s.Client.Client.GetAsync("/api/v1/team/payouts")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            Payouts.Refuse = true;
            (await s.Salon.Owner.Client.PostAsync($"{payoutUrl}/{owed.UserId}", null)).StatusCode.Should().Be(HttpStatusCode.BadGateway);
            Payouts.Refuse = false;
            (await s.Salon.Owner.Client.GetFromJsonAsync<CollaboratorPayoutsView>(payoutUrl))!.Owed.Single().Payable.Should().Be(3500, "a refused transfer releases the services and tips");

            var sent = await s.Salon.Owner.Client.PostAsync($"{payoutUrl}/{owed.UserId}", null);
            sent.StatusCode.Should().Be(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync());
            var payout = (await sent.Content.ReadFromJsonAsync<CollaboratorPayoutDto>())!;
            payout.Should().Match<CollaboratorPayoutDto>(p => p.Amount == 3500 && p.CommissionAmount == 3000 && p.TipAmount == 500 && p.Status == "Succeeded");
            Payouts.Sent.Should().Contain(p => p.Amount == 3500 && p.Account == "2250700009988");

            // Nothing is owed any more, and the advance reduces what the salon itself receives.
            (await s.Salon.Owner.Client.PostAsync($"{payoutUrl}/{owed.UserId}", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
            var mine = (await s.Staff.Client.GetFromJsonAsync<MyPayoutsView>("/api/v1/team/payouts"))!;
            mine.History.Should().ContainSingle(p => p.Id == payout.Id);
            mine.Owed.Should().OnlyContain(o => o.Commission == 0 && o.Tips == 0);

            (await BalanceAsync(s.Salon)).Payable.Should().Be(7000, "10 000 minus the 3 000 advanced to the collaborator");
            var salonPayout = await s.Admin.Client.PostAsync($"/api/v1/admin/payouts/establishments/{s.Salon.Id}", null);
            salonPayout.StatusCode.Should().Be(HttpStatusCode.OK, await salonPayout.Content.ReadAsStringAsync());
            (await salonPayout.Content.ReadFromJsonAsync<PayoutDto>())!.Amount.Should().Be(7000);
            (await s.Admin.Client.GetFromJsonAsync<List<CollaboratorPayoutDto>>("/api/v1/admin/collaborator-payouts"))!.Should().Contain(p => p.Id == payout.Id);
        }
        finally
        {
            await CleanupAsync(s);
        }
    }

    [Fact]
    public async Task Without_online_payments_a_tip_is_only_recorded()
    {
        var s = await ArrangeAsync(withStaff: true, onlinePayments: false);
        try
        {
            var booking = await s.Client.Client.PostAsJsonAsync("/api/v1/appointments",
                new BookingRequest(s.Salon.Id, [s.Salon.ServiceHour], null, TestSalons.NextOpenDay(api), Phone, null));
            var booked = (await booking.Content.ReadFromJsonAsync<AppointmentDto>())!;
            await MakePastAsync(booked.Id, TimeSpan.FromHours(3));
            await api.Services.GetRequiredService<AppointmentMaintenance>().RunOnceAsync(CancellationToken.None);

            var result = await s.Client.Client.PostAsJsonAsync($"/api/v1/appointments/{booked.Id}/tip", new TipInput(1000));
            result.StatusCode.Should().Be(HttpStatusCode.OK, await result.Content.ReadAsStringAsync());
            (await result.Content.ReadFromJsonAsync<TipResultDto>())!.Should().Match<TipResultDto>(t => t.Status == "Recorded" && t.PaymentUrl == null);
            (await MineAsync(s.Client, booked.Id)).TipAmount.Should().Be(1000);
        }
        finally
        {
            await CleanupAsync(s);
        }
    }

    // ───────── Review photos ─────────

    [Fact]
    public async Task A_client_attaches_before_and_after_photos_to_her_review()
    {
        var s = await ArrangeAsync(withStaff: false);
        using var stranger = await TestAccounts.ClientAsync(api);
        using var visitor = api.CreateClient();
        try
        {
            var booked = await OverAsync(s);
            var review = await s.Client.Client.PostAsJsonAsync($"/api/v1/appointments/{booked.Id}/review", new ReviewInput(5, "Superbe résultat"));
            review.StatusCode.Should().Be(HttpStatusCode.Created, await review.Content.ReadAsStringAsync());
            var reviewId = (await review.Content.ReadFromJsonAsync<ReviewDto>())!.Id;
            var photos = $"/api/v1/reviews/{reviewId}/photos";

            static MultipartFormDataContent Form(string kind)
            {
                var form = TestAccounts.FileForm(TestAccounts.Png());
                form.Add(new StringContent(kind), "kind");
                return form;
            }

            (await stranger.Client.PostAsync(photos, Form("Before"))).StatusCode.Should().Be(HttpStatusCode.NotFound, "only the author");
            (await s.Client.Client.PostAsync(photos, Form("Nope"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await s.Client.Client.PostAsync(photos, TestAccounts.FileForm([1, 2, 3, 4]))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "not an image");

            var before = await s.Client.Client.PostAsync(photos, Form("Before"));
            before.StatusCode.Should().Be(HttpStatusCode.Created, await before.Content.ReadAsStringAsync());
            var beforePhoto = (await before.Content.ReadFromJsonAsync<ReviewPhotoDto>())!;
            (await s.Client.Client.PostAsync(photos, Form("After"))).StatusCode.Should().Be(HttpStatusCode.Created);

            // Everybody sees them on the public page, and the files are served.
            var page = (await visitor.GetFromJsonAsync<ReviewsPage>($"/api/v1/establishments/{s.Salon.Slug}/reviews"))!;
            var shown = page.Items.Single().Photos!;
            shown.Select(p => p.Kind).Should().Equal("Before", "After");
            (await visitor.GetAsync(beforePhoto.Url)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await visitor.GetAsync(beforePhoto.ThumbUrl!)).StatusCode.Should().Be(HttpStatusCode.OK);

            // At most four per review; the author can remove one; a hidden review shows no photo.
            (await s.Client.Client.PostAsync(photos, Form("Other"))).StatusCode.Should().Be(HttpStatusCode.Created);
            (await s.Client.Client.PostAsync(photos, Form("Other"))).StatusCode.Should().Be(HttpStatusCode.Created);
            (await s.Client.Client.PostAsync(photos, Form("Other"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await stranger.Client.DeleteAsync($"{photos}/{beforePhoto.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await s.Client.Client.DeleteAsync($"{photos}/{beforePhoto.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await visitor.GetFromJsonAsync<ReviewsPage>($"/api/v1/establishments/{s.Salon.Slug}/reviews"))!.Items.Single().Photos.Should().HaveCount(3);

            (await s.Admin.Client.PostAsJsonAsync($"/api/v1/admin/reviews/{reviewId}/hide", new HideReviewInput("Contenu inapproprié"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await visitor.GetFromJsonAsync<ReviewsPage>($"/api/v1/establishments/{s.Salon.Slug}/reviews"))!.Items.Should().BeEmpty("a hidden review leaves the public page");
            (await s.Salon.Owner.Client.GetFromJsonAsync<ReviewsPage>($"/api/v1/pro/establishments/{s.Salon.Id}/reviews"))!.Items.Single().Photos.Should().BeNull("and its photos are not served with it");
        }
        finally
        {
            await CleanupAsync(s);
        }
    }
}
