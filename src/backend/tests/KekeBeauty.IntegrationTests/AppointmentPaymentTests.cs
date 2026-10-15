using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Billing;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Billing.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

/// <summary>Online payment of appointments: deposit or full price, hold, refunds, platform fee and payouts to the establishment.</summary>
[Collection(ApiCollection.Name)]
public sealed class AppointmentPaymentTests(ApiFixture api)
{
    private const string ClientPhone = "07 00 00 12 34";

    private FakePayoutGateway Payouts => api.Services.GetRequiredService<FakePayoutGateway>();

    private async Task ResetSettingsAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>().AppSettings.ExecuteDeleteAsync();
    }

    private static async Task<PaymentSettingsDto> ConfigureAsync(Salon salon, string mode = "Deposit", int percent = 30)
    {
        var response = await salon.Owner.Client.PutAsJsonAsync(
            $"/api/v1/pro/establishments/{salon.Id}/payment-settings",
            new PaymentSettingsDto(mode, percent, "wave-cote-divoire", "0700000001", "Awa Koné", true));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PaymentSettingsDto>())!;
    }

    private static async Task<AppointmentDto> BookAsync(UserSession client, Salon salon, DateTimeOffset start)
    {
        var response = await client.Client.PostAsJsonAsync("/api/v1/appointments", new BookingRequest(salon.Id, [salon.ServiceHour], null, start, ClientPhone, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AppointmentDto>())!;
    }

    private static string TransactionOf(AppointmentDto appointment)
    {
        var url = new Uri(appointment.Payment!.PaymentUrl!);
        return System.Web.HttpUtility.ParseQueryString(url.Query)["tx"]!;
    }

    private async Task PayAsync(string transaction, string status = "accepted")
    {
        using var anonymous = api.CreateClient();
        (await anonymous.PostAsJsonAsync($"/api/v1/dev/payments/{transaction}/simulate", new { status })).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private async Task<T> WithDbAsync<T>(Func<KekeBeautyDbContext, Task<T>> read)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>());
    }

    private static async Task<List<PartnerAppointmentDto>> PendingOfAsync(Salon salon) =>
        (await salon.Owner.Client.GetFromJsonAsync<List<PartnerAppointmentDto>>($"/api/v1/pro/establishments/{salon.Id}/appointments?status=Pending"))!;

    private static async Task<AppointmentDto> MineAsync(UserSession client, Guid id) =>
        (await client.Client.GetFromJsonAsync<List<AppointmentDto>>("/api/v1/me/appointments"))!.Single(a => a.Id == id);

    private async Task<(UserSession Admin, Salon Salon, UserSession Client)> ArrangeAsync(bool autoConfirm = false, string mode = "Deposit", int percent = 30, int feePercent = 0)
    {
        await ResetSettingsAsync();
        var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, autoConfirm);
        var client = await TestAccounts.ClientAsync(api);
        var current = (await admin.Client.GetFromJsonAsync<AppSettingsDto>("/api/v1/admin/settings"))!;
        (await admin.Client.PutAsJsonAsync("/api/v1/admin/settings", current with { AppointmentPaymentsEnabled = true, PlatformFeePercent = feePercent })).StatusCode.Should().Be(HttpStatusCode.OK);
        await ConfigureAsync(salon, mode, percent);
        return (admin, salon, client);
    }

    /// <summary>Moves the appointment into the past (and past its hold) without moving the clock: access tokens last only fifteen minutes.</summary>
    private async Task MakePastAsync(Guid appointmentId, TimeSpan endedAgo)
    {
        var end = api.Clock.GetUtcNow() - endedAgo;
        var start = end.AddHours(-1);
        var expired = api.Clock.GetUtcNow().AddMinutes(-1);
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE scheduling.appointments SET starts_at = {start}, ends_at = {end}, expires_at = CASE WHEN expires_at IS NULL THEN NULL ELSE {expired} END WHERE id = {appointmentId}");
    }

    private async Task CleanupAsync(UserSession admin, UserSession client, Salon salon)
    {
        Payouts.Refuse = false;
        Payouts.NextState = PayoutState.Succeeded;
        admin.Dispose();
        client.Dispose();
        await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        await ResetSettingsAsync();
    }

    // ───────── Settings and quote ─────────

    [Fact]
    public async Task Online_payment_is_off_until_the_admins_and_the_establishment_both_turn_it_on()
    {
        await ResetSettingsAsync();
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin);
        using var client = await TestAccounts.ClientAsync(api);
        try
        {
            // Platform switch off: the partner may configure, but bookings stay free of payment.
            await ConfigureAsync(salon);
            var booked = await BookAsync(client, salon, TestSalons.NextOpenDay(api));
            booked.Payment.Should().BeNull();
            booked.Status.Should().Be("Pending");
            (await PendingOfAsync(salon)).Should().ContainSingle("a normal request reaches the partner at once");
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task Payment_settings_are_validated_and_need_the_booking_plan()
    {
        await ResetSettingsAsync();
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin);
        var free = await TestSalons.PublishAsync(api, admin, subscribe: false);
        using var stranger = await TestAccounts.PartnerAsync(api);
        try
        {
            var url = $"/api/v1/pro/establishments/{salon.Id}/payment-settings";
            (await salon.Owner.Client.GetFromJsonAsync<PaymentSettingsDto>(url))!.Mode.Should().Be("None");

            foreach (var bad in new[]
            {
                new PaymentSettingsDto("Quoi", 30, "wave-cote-divoire", "0700000001", "Awa", true),
                new PaymentSettingsDto("Deposit", 5, "wave-cote-divoire", "0700000001", "Awa", true),
                new PaymentSettingsDto("Deposit", 30, "inconnu", "0700000001", "Awa", true),
                new PaymentSettingsDto("Deposit", 30, "wave-cote-divoire", "abc", "Awa", true),
                new PaymentSettingsDto("Full", 30, "wave-cote-divoire", "0700000001", "", true),
            })
            {
                (await salon.Owner.Client.PutAsJsonAsync(url, bad)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            }

            (await stranger.Client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await free.Owner.Client.PutAsJsonAsync($"/api/v1/pro/establishments/{free.Id}/payment-settings",
                new PaymentSettingsDto("Deposit", 30, "wave-cote-divoire", "0700000001", "Awa Koné", true))).StatusCode.Should().Be(HttpStatusCode.PaymentRequired);

            (await ConfigureAsync(salon, "Full")).Mode.Should().Be("Full");
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId, free.Owner.UserId);
        }
    }

    [Fact]
    public async Task The_calendar_quotes_the_deposit_and_the_rest_to_pay_on_the_spot()
    {
        var (admin, salon, client) = await ArrangeAsync(percent: 30);
        try
        {
            var day = TestSalons.NextOpenDay(api);
            var availability = await client.Client.GetFromJsonAsync<AvailabilityResponse>(
                $"/api/v1/establishments/{salon.Id}/availability?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}&serviceIds={salon.ServiceHour}");
            availability!.TotalPrice.Should().Be(10000);
            availability.Payment.Should().NotBeNull();
            availability.Payment!.Should().Match<PaymentQuote>(q => q.Kind == "Deposit" && q.Amount == 3000 && q.OnTheSpot == 7000 && q.DepositPercent == 30);
        }
        finally
        {
            await CleanupAsync(admin, client, salon);
        }
    }

    // ───────── Booking, hold, payment ─────────

    [Fact]
    public async Task A_paid_deposit_releases_the_request_to_the_partner_who_never_saw_it_before()
    {
        var (admin, salon, client) = await ArrangeAsync(percent: 30, feePercent: 10);
        try
        {
            var start = TestSalons.NextOpenDay(api);
            var booked = await BookAsync(client, salon, start);

            booked.Status.Should().Be("Pending");
            booked.Payment.Should().NotBeNull();
            booked.Payment!.Should().Match<AppointmentPaymentDto>(p => p.Status == "Awaiting" && p.Amount == 3000 && p.Kind == "Deposit" && p.PaymentUrl != null);
            booked.ExpiresAt.Should().Be(api.Clock.GetUtcNow().AddMinutes(AppointmentPaymentService.HoldMinutes), "the slot is only held for a short while");

            // Held: invisible to the partner, silent, but the slot is no longer available to others.
            (await PendingOfAsync(salon)).Should().BeEmpty();
            (await WithDbAsync(db => db.Notifications.AnyAsync(n => n.UserId == salon.Owner.UserId && n.TemplateCode.StartsWith("appointment")))).Should().BeFalse();

            using var other = await TestAccounts.ClientAsync(api);
            (await other.Client.PostAsJsonAsync("/api/v1/appointments", new BookingRequest(salon.Id, [salon.ServiceHour], null, start, ClientPhone, null)))
                .StatusCode.Should().Be(HttpStatusCode.Conflict);

            await PayAsync(TransactionOf(booked));

            var mine = await MineAsync(client, booked.Id);
            mine.Payment!.Status.Should().Be("Paid");
            mine.Payment.PaidAt.Should().NotBeNull();
            mine.ExpiresAt.Should().BeCloseTo(api.Clock.GetUtcNow().AddHours(AppointmentWorkflow.RequestValidityHours), TimeSpan.FromSeconds(1), "the usual 24 h for the partner to answer start now");

            (await PendingOfAsync(salon)).Should().ContainSingle().Which.Id.Should().Be(booked.Id);

            var stored = await WithDbAsync(db => db.AppointmentPayments.AsNoTracking().SingleAsync(p => p.AppointmentId == booked.Id));
            stored.Should().Match<AppointmentPayment>(p => p.Status == AppointmentPaymentStatus.Paid && p.PlatformFee == 300 && p.NetAmount == 2700);
            (await WithDbAsync(db => db.Notifications.AnyAsync(n => n.UserId == salon.Owner.UserId && n.TemplateCode == "appointment.requested.partner"))).Should().BeTrue();
            (await WithDbAsync(db => db.Notifications.AnyAsync(n => n.UserId == client.UserId && n.TemplateCode == "appointment.payment_received"))).Should().BeTrue();
        }
        finally
        {
            await CleanupAsync(admin, client, salon);
        }
    }

    [Fact]
    public async Task With_automatic_confirmation_the_payment_confirms_the_appointment()
    {
        var (admin, salon, client) = await ArrangeAsync(autoConfirm: true, mode: "Full");
        try
        {
            var booked = await BookAsync(client, salon, TestSalons.NextOpenDay(api));
            booked.Status.Should().Be("Pending", "it is only held until the money is in");
            booked.Payment!.Amount.Should().Be(10000);
            booked.Payment.Kind.Should().Be("Full");

            await PayAsync(TransactionOf(booked));

            var mine = await MineAsync(client, booked.Id);
            mine.Status.Should().Be("Confirmed");
            mine.Payment!.Status.Should().Be("Paid");
            (await WithDbAsync(db => db.Notifications.AnyAsync(n => n.UserId == salon.Owner.UserId && n.TemplateCode == "appointment.confirmed.partner"))).Should().BeTrue();
        }
        finally
        {
            await CleanupAsync(admin, client, salon);
        }
    }

    [Fact]
    public async Task A_refused_payment_can_be_retried_while_the_slot_is_held()
    {
        var (admin, salon, client) = await ArrangeAsync();
        try
        {
            var booked = await BookAsync(client, salon, TestSalons.NextOpenDay(api));

            await PayAsync(TransactionOf(booked), "refused");
            (await MineAsync(client, booked.Id)).Payment!.Status.Should().Be("Failed");
            (await WithDbAsync(db => db.Notifications.AnyAsync(n => n.UserId == client.UserId && n.TemplateCode == "appointment.payment_failed"))).Should().BeTrue();

            // Back to the same appointment: a new payment page, and this time it works.
            var again = await client.Client.PostAsync($"/api/v1/appointments/{booked.Id}/pay", null);
            again.StatusCode.Should().Be(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());
            var retried = (await again.Content.ReadFromJsonAsync<AppointmentDto>())!;
            retried.Payment!.Status.Should().Be("Awaiting");
            TransactionOf(retried).Should().NotBe(TransactionOf(booked));

            // Asking again while the page is open gives the same page.
            var same = (await (await client.Client.PostAsync($"/api/v1/appointments/{booked.Id}/pay", null)).Content.ReadFromJsonAsync<AppointmentDto>())!;
            TransactionOf(same).Should().Be(TransactionOf(retried));

            await PayAsync(TransactionOf(retried));
            (await MineAsync(client, booked.Id)).Payment!.Status.Should().Be("Paid");
            (await client.Client.PostAsync($"/api/v1/appointments/{booked.Id}/pay", null)).StatusCode.Should().Be(HttpStatusCode.Conflict, "nothing left to pay");
        }
        finally
        {
            await CleanupAsync(admin, client, salon);
        }
    }

    [Fact]
    public async Task An_unpaid_hold_expires_silently_frees_the_slot_and_a_late_payment_is_refunded()
    {
        var (admin, salon, client) = await ArrangeAsync();
        try
        {
            var start = TestSalons.NextOpenDay(api);
            var booked = await BookAsync(client, salon, start);
            var transaction = TransactionOf(booked);

            await MakePastAsync(booked.Id, TimeSpan.FromHours(-2));
            await api.Services.GetRequiredService<AppointmentMaintenance>().RunOnceAsync(CancellationToken.None);

            var expired = await MineAsync(client, booked.Id);
            expired.Status.Should().Be("Expired");
            expired.Payment!.Status.Should().Be("Expired");
            (await WithDbAsync(db => db.Notifications.AnyAsync(n => n.UserId == salon.Owner.UserId && n.TemplateCode.StartsWith("appointment")))).Should().BeFalse("the partner never knew about it");
            (await client.Client.PostAsync($"/api/v1/appointments/{booked.Id}/pay", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);

            // The slot is free again for somebody else.
            using var other = await TestAccounts.ClientAsync(api);
            (await other.Client.PostAsJsonAsync("/api/v1/appointments", new BookingRequest(salon.Id, [salon.ServiceHour], null, start, ClientPhone, null))).StatusCode.Should().Be(HttpStatusCode.Created);

            // The client pays on the old page after all: the money goes back instead of buying nothing.
            var sentBefore = Payouts.Sent.Count;
            await PayAsync(transaction);
            var record = await WithDbAsync(db => db.AppointmentPayments.AsNoTracking().SingleAsync(p => p.AppointmentId == booked.Id));
            record.Status.Should().Be(AppointmentPaymentStatus.Refunded);
            Payouts.Sent.Count.Should().Be(sentBefore + 1);
        }
        finally
        {
            await CleanupAsync(admin, client, salon);
        }
    }

    // ───────── Refunds ─────────

    [Fact]
    public async Task Cancelling_a_paid_appointment_refunds_the_client_through_the_same_operator()
    {
        var (admin, salon, client) = await ArrangeAsync();
        try
        {
            var booked = await BookAsync(client, salon, TestSalons.NextOpenDay(api));
            await PayAsync(TransactionOf(booked));
            var before = Payouts.Sent.Count;

            (await client.Client.PostAsync($"/api/v1/appointments/{booked.Id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.OK);

            var sent = Payouts.Sent.Skip(before).Should().ContainSingle().Subject;
            sent.Should().Match<PayoutRequest>(r => r.Amount == 3000 && r.Operator == "wave-cote-divoire" && r.Account == "2250700001234");
            (await MineAsync(client, booked.Id)).Payment!.Status.Should().Be("Refunded");
            (await WithDbAsync(db => db.Notifications.AnyAsync(n => n.UserId == client.UserId && n.TemplateCode == "appointment.refunded"))).Should().BeTrue();
        }
        finally
        {
            await CleanupAsync(admin, client, salon);
        }
    }

    [Fact]
    public async Task A_partner_refusal_refunds_too_and_a_failed_refund_can_be_retried_or_settled_by_hand()
    {
        var (admin, salon, client) = await ArrangeAsync();
        try
        {
            var booked = await BookAsync(client, salon, TestSalons.NextOpenDay(api));
            await PayAsync(TransactionOf(booked));

            Payouts.Refuse = true;
            (await salon.Owner.Client.PostAsJsonAsync($"/api/v1/pro/appointments/{booked.Id}/reject", new RejectInput("Complet"))).StatusCode.Should().Be(HttpStatusCode.OK);

            var failed = (await admin.Client.GetFromJsonAsync<List<RefundDto>>("/api/v1/admin/refunds"))!.Single(r => r.AppointmentId == booked.Id);
            failed.Status.Should().Be("RefundFailed");
            failed.Error.Should().NotBeNullOrEmpty();

            // Still refused: nothing changes. Then the operator answers and the retry goes through.
            (await admin.Client.PostAsync($"/api/v1/admin/refunds/{failed.Id}/retry", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await admin.Client.GetFromJsonAsync<List<RefundDto>>("/api/v1/admin/refunds"))!.Single(r => r.Id == failed.Id).Status.Should().Be("RefundFailed");
            Payouts.Refuse = false;
            (await admin.Client.PostAsync($"/api/v1/admin/refunds/{failed.Id}/retry", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await admin.Client.GetFromJsonAsync<List<RefundDto>>("/api/v1/admin/refunds"))!.Single(r => r.Id == failed.Id).Status.Should().Be("Refunded");
            (await admin.Client.PostAsync($"/api/v1/admin/refunds/{failed.Id}/retry", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "already refunded");

            // A refund the operator keeps refusing is settled by hand, with a reference.
            Payouts.Refuse = true;
            var second = await BookAsync(client, salon, TestSalons.NextOpenDay(api, daysAhead: 4));
            await PayAsync(TransactionOf(second));
            (await client.Client.PostAsync($"/api/v1/appointments/{second.Id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.OK);
            Payouts.Refuse = false;
            var stuck = (await admin.Client.GetFromJsonAsync<List<RefundDto>>("/api/v1/admin/refunds"))!.Single(r => r.AppointmentId == second.Id);
            (await admin.Client.PostAsJsonAsync($"/api/v1/admin/refunds/{stuck.Id}/manual", new RefundReferenceInput("x"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await admin.Client.PostAsJsonAsync($"/api/v1/admin/refunds/{stuck.Id}/manual", new RefundReferenceInput("Wave-REF-001"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await admin.Client.GetFromJsonAsync<List<RefundDto>>("/api/v1/admin/refunds"))!.Single(r => r.Id == stuck.Id).Status.Should().Be("Refunded");
        }
        finally
        {
            await CleanupAsync(admin, client, salon);
        }
    }

    // ───────── Payouts ─────────

    [Fact]
    public async Task The_partner_is_paid_out_once_after_the_appointment_took_place_and_sees_where_the_money_is()
    {
        var (admin, salon, client) = await ArrangeAsync(autoConfirm: true, mode: "Full", feePercent: 10);
        try
        {
            var start = TestSalons.NextOpenDay(api);
            var booked = await BookAsync(client, salon, start);
            await PayAsync(TransactionOf(booked));

            var view = (await salon.Owner.Client.GetFromJsonAsync<PartnerPaymentsView>($"/api/v1/pro/establishments/{salon.Id}/payments"))!;
            view.Balance.Should().Match<BalanceDto>(b => b.Upcoming == 9000 && b.Payable == 0 && b.PaidOut == 0 && b.PayoutConfigured);
            view.Payments.Should().ContainSingle().Which.Should().Match<PartnerPaymentDto>(p => p.Amount == 10000 && p.PlatformFee == 1000 && p.NetAmount == 9000);

            // Nothing to pay before the appointment has taken place.
            (await admin.Client.PostAsync($"/api/v1/admin/payouts/establishments/{salon.Id}", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);

            await MakePastAsync(booked.Id, TimeSpan.FromHours(3));
            await api.Services.GetRequiredService<AppointmentMaintenance>().RunOnceAsync(CancellationToken.None);
            (await salon.Owner.Client.GetFromJsonAsync<PartnerPaymentsView>($"/api/v1/pro/establishments/{salon.Id}/payments"))!.Balance.Payable.Should().Be(9000);

            var paid = await admin.Client.PostAsync($"/api/v1/admin/payouts/establishments/{salon.Id}", null);
            paid.StatusCode.Should().Be(HttpStatusCode.OK, await paid.Content.ReadAsStringAsync());
            var payout = (await paid.Content.ReadFromJsonAsync<PayoutDto>())!;
            payout.Should().Match<PayoutDto>(p => p.Amount == 9000 && p.Status == "Succeeded" && p.Operator == "wave-cote-divoire");
            Payouts.Sent.Last().Should().Match<PayoutRequest>(r => r.Amount == 9000 && r.Account == "2250700000001" && r.AccountName == "Awa Koné");

            var after = (await salon.Owner.Client.GetFromJsonAsync<PartnerPaymentsView>($"/api/v1/pro/establishments/{salon.Id}/payments"))!;
            after.Balance.Should().Match<BalanceDto>(b => b.Payable == 0 && b.PaidOut == 9000);
            after.Payments.Single().PaidOut.Should().BeTrue();
            after.Payouts.Should().ContainSingle();
            (await admin.Client.PostAsync($"/api/v1/admin/payouts/establishments/{salon.Id}", null)).StatusCode.Should().Be(HttpStatusCode.Conflict, "the same appointment is never paid twice");
            (await WithDbAsync(db => db.Notifications.AnyAsync(n => n.UserId == salon.Owner.UserId && n.TemplateCode == "billing.payout_sent"))).Should().BeTrue();
            (await admin.Client.GetFromJsonAsync<List<PayoutDto>>("/api/v1/admin/payouts"))!.Should().Contain(p => p.Id == payout.Id);
        }
        finally
        {
            await CleanupAsync(admin, client, salon);
        }
    }

    [Fact]
    public async Task A_refused_payout_gives_the_appointments_back_and_a_slow_operator_is_followed_up_later()
    {
        var (admin, salon, client) = await ArrangeAsync(autoConfirm: true, mode: "Full");
        try
        {
            var start = TestSalons.NextOpenDay(api);
            var booked = await BookAsync(client, salon, start);
            await PayAsync(TransactionOf(booked));
            await MakePastAsync(booked.Id, TimeSpan.FromHours(3));
            await api.Services.GetRequiredService<AppointmentMaintenance>().RunOnceAsync(CancellationToken.None);

            Payouts.Refuse = true;
            (await admin.Client.PostAsync($"/api/v1/admin/payouts/establishments/{salon.Id}", null)).StatusCode.Should().Be(HttpStatusCode.BadGateway);
            (await salon.Owner.Client.GetFromJsonAsync<PartnerPaymentsView>($"/api/v1/pro/establishments/{salon.Id}/payments"))!.Balance.Payable.Should().Be(10000, "the appointments were given back");
            Payouts.Refuse = false;

            // The operator takes its time: the payout stays pending until the maintenance job sees it succeed.
            Payouts.NextState = PayoutState.Pending;
            var slow = await (await admin.Client.PostAsync($"/api/v1/admin/payouts/establishments/{salon.Id}", null)).Content.ReadFromJsonAsync<PayoutDto>();
            slow!.Status.Should().Be("Pending");
            (await salon.Owner.Client.GetFromJsonAsync<PartnerPaymentsView>($"/api/v1/pro/establishments/{salon.Id}/payments"))!.Balance.Should().Match<BalanceDto>(b => b.PaidOut == 0 && b.Payable == 0);

            var transaction = await WithDbAsync(db => db.Payouts.AsNoTracking().Where(p => p.Id == slow.Id).Select(p => p.TransactionId).SingleAsync());
            Payouts.Settle(transaction, PayoutState.Succeeded);
            await api.Services.GetRequiredService<PaymentSettlementMaintenance>().RunOnceAsync(CancellationToken.None);
            (await salon.Owner.Client.GetFromJsonAsync<PartnerPaymentsView>($"/api/v1/pro/establishments/{salon.Id}/payments"))!.Balance.PaidOut.Should().Be(10000);
        }
        finally
        {
            await CleanupAsync(admin, client, salon);
        }
    }

    [Fact]
    public async Task Only_admins_manage_payouts_and_refunds()
    {
        using var client = await TestAccounts.ClientAsync(api);
        using var partner = await TestAccounts.PartnerAsync(api);

        foreach (var url in new[] { "/api/v1/admin/payouts", "/api/v1/admin/refunds" })
        {
            (await client.Client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await partner.Client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        (await partner.Client.PostAsync("/api/v1/admin/payouts/run", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.Client.PostAsync($"/api/v1/appointments/{Guid.NewGuid()}/pay", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
