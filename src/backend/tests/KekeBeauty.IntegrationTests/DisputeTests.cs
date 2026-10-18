using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Billing;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

/// <summary>Keke Protect: a client files a claim, the establishment answers, an administrator decides; the payout waits.</summary>
[Collection(ApiCollection.Name)]
public sealed class DisputeTests(ApiFixture api)
{
    private const string Phone = "07 00 00 12 34";
    private const string Text = "Le salon était fermé à mon arrivée.";

    private async Task ResetSettingsAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>().AppSettings.ExecuteDeleteAsync();
    }

    private static async Task<AppointmentDto> BookAsync(UserSession client, Salon salon, DateTimeOffset start)
    {
        var response = await client.Client.PostAsJsonAsync("/api/v1/appointments", new BookingRequest(salon.Id, [salon.ServiceHour], null, start, Phone, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AppointmentDto>())!;
    }

    /// <summary>Moves the appointment into the past without moving the clock (access tokens last only fifteen minutes).</summary>
    private async Task MakePastAsync(Guid appointmentId, TimeSpan endedAgo)
    {
        var end = api.Clock.GetUtcNow() - endedAgo;
        var start = end.AddHours(-1);
        var expired = api.Clock.GetUtcNow().AddMinutes(-1);
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE scheduling.appointments SET starts_at = {start}, ends_at = {end}, expires_at = CASE WHEN expires_at IS NULL THEN NULL ELSE {expired} END WHERE id = {appointmentId}");
    }

    private static Task<HttpResponseMessage> OpenAsync(UserSession client, Guid appointmentId, string reason = "SalonClosed", string description = Text) =>
        client.Client.PostAsJsonAsync("/api/v1/disputes", new OpenDisputeInput(appointmentId, reason, description));

    private async Task<(UserSession Admin, Salon Salon, UserSession Client)> ArrangeOnlinePaymentAsync()
    {
        await ResetSettingsAsync();
        var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, autoConfirm: true);
        var client = await TestAccounts.ClientAsync(api);
        var current = (await admin.Client.GetFromJsonAsync<AppSettingsDto>("/api/v1/admin/settings"))!;
        (await admin.Client.PutAsJsonAsync("/api/v1/admin/settings", current with { AppointmentPaymentsEnabled = true, PlatformFeePercent = 0 })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await salon.Owner.Client.PutAsJsonAsync($"/api/v1/pro/establishments/{salon.Id}/payment-settings",
            new PaymentSettingsDto("Full", 30, "wave-cote-divoire", "0700000001", "Awa Koné", true))).StatusCode.Should().Be(HttpStatusCode.OK);
        return (admin, salon, client);
    }

    private async Task<AppointmentDto> PaidAndOverAsync(UserSession client, Salon salon)
    {
        var booked = await BookAsync(client, salon, TestSalons.NextOpenDay(api));
        var transaction = System.Web.HttpUtility.ParseQueryString(new Uri(booked.Payment!.PaymentUrl!).Query)["tx"]!;
        using var anonymous = api.CreateClient();
        (await anonymous.PostAsJsonAsync($"/api/v1/dev/payments/{transaction}/simulate", new { status = "accepted" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await MakePastAsync(booked.Id, TimeSpan.FromHours(3));
        await api.Services.GetRequiredService<AppointmentMaintenance>().RunOnceAsync(CancellationToken.None);
        return booked;
    }

    private static async Task<BalanceDto> BalanceAsync(Salon salon) =>
        (await salon.Owner.Client.GetFromJsonAsync<PartnerPaymentsView>($"/api/v1/pro/establishments/{salon.Id}/payments"))!.Balance;

    [Fact]
    public async Task A_client_files_one_claim_on_a_past_appointment_and_the_establishment_answers_it()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, autoConfirm: true);
        using var client = await TestAccounts.ClientAsync(api);
        using var stranger = await TestAccounts.ClientAsync(api);
        using var otherOwner = await TestAccounts.PartnerAsync(api);
        try
        {
            var booked = await BookAsync(client, salon, TestSalons.NextOpenDay(api));

            // Not over yet: nothing to claim.
            (await OpenAsync(client, booked.Id)).StatusCode.Should().Be(HttpStatusCode.Conflict);

            await MakePastAsync(booked.Id, TimeSpan.FromHours(2));
            (await OpenAsync(stranger, booked.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await OpenAsync(client, booked.Id, "Nope")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await OpenAsync(client, booked.Id, description: "court")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var opened = await OpenAsync(client, booked.Id);
            opened.StatusCode.Should().Be(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync());
            var dispute = (await opened.Content.ReadFromJsonAsync<DisputeDto>())!;
            dispute.Status.Should().Be("Open");

            (await OpenAsync(client, booked.Id)).StatusCode.Should().Be(HttpStatusCode.Conflict, "one claim per appointment");
            (await client.Client.GetFromJsonAsync<List<DisputeDto>>("/api/v1/me/disputes"))!.Should().ContainSingle(d => d.Id == dispute.Id);
            (await stranger.Client.GetFromJsonAsync<List<DisputeDto>>("/api/v1/me/disputes"))!.Should().BeEmpty();

            // The owner is told, sees the claim, and only the owner may answer it.
            var listUrl = $"/api/v1/pro/establishments/{salon.Id}/disputes";
            (await salon.Owner.Client.GetFromJsonAsync<List<DisputeDto>>(listUrl))!.Should().ContainSingle(d => d.Id == dispute.Id);
            (await otherOwner.Client.GetAsync(listUrl)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.Client.PostAsJsonAsync($"/api/v1/pro/disputes/{dispute.Id}/respond", new DisputeResponseInput("Je réponds"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await otherOwner.Client.PostAsJsonAsync($"/api/v1/pro/disputes/{dispute.Id}/respond", new DisputeResponseInput("Je réponds"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await salon.Owner.Client.PostAsJsonAsync($"/api/v1/pro/disputes/{dispute.Id}/respond", new DisputeResponseInput(" "))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var answered = await salon.Owner.Client.PostAsJsonAsync($"/api/v1/pro/disputes/{dispute.Id}/respond", new DisputeResponseInput("Nous étions ouverts, la cliente est arrivée en retard."));
            answered.StatusCode.Should().Be(HttpStatusCode.OK, await answered.Content.ReadAsStringAsync());
            (await answered.Content.ReadFromJsonAsync<DisputeDto>())!.PartnerResponse.Should().StartWith("Nous étions ouverts");

            // The administrator decides; nobody else can.
            (await client.Client.GetAsync("/api/v1/admin/disputes")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await salon.Owner.Client.GetAsync("/api/v1/admin/disputes")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await admin.Client.GetFromJsonAsync<List<DisputeDto>>("/api/v1/admin/disputes?status=Open"))!.Should().Contain(d => d.Id == dispute.Id);
            (await admin.Client.GetAsync("/api/v1/admin/disputes?status=Nope")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var resolveUrl = $"/api/v1/admin/disputes/{dispute.Id}/resolve";
            (await admin.Client.PostAsJsonAsync(resolveUrl, new DisputeResolveInput("RejectedClaim", "x"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await admin.Client.PostAsJsonAsync(resolveUrl, new DisputeResolveInput("Maybe", "Motif suffisant"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var resolved = await admin.Client.PostAsJsonAsync(resolveUrl, new DisputeResolveInput("RejectedClaim", "Le salon prouve être resté ouvert."));
            resolved.StatusCode.Should().Be(HttpStatusCode.OK, await resolved.Content.ReadAsStringAsync());
            (await resolved.Content.ReadFromJsonAsync<DisputeDto>())!.Should().Match<DisputeDto>(d => d.Status == "Resolved" && d.Resolution == "RejectedClaim");
            (await admin.Client.PostAsJsonAsync(resolveUrl, new DisputeResolveInput("RejectedClaim", "Encore une fois"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await salon.Owner.Client.PostAsJsonAsync($"/api/v1/pro/disputes/{dispute.Id}/respond", new DisputeResponseInput("Trop tard"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task A_client_cannot_be_marked_absent_before_the_grace_period_is_over()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, autoConfirm: true);
        using var client = await TestAccounts.ClientAsync(api);
        try
        {
            var booked = await BookAsync(client, salon, TestSalons.NextOpenDay(api));
            var url = $"/api/v1/pro/appointments/{booked.Id}/no-show";

            await SetStartAsync(booked.Id, TimeSpan.FromMinutes(5));
            (await salon.Owner.Client.PostAsJsonAsync(url, new { })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "she may still arrive");

            await SetStartAsync(booked.Id, TimeSpan.FromMinutes(20));
            (await salon.Owner.Client.PostAsJsonAsync(url, new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    /// <summary>The appointment started <paramref name="startedAgo"/> ago and lasts an hour.</summary>
    private async Task SetStartAsync(Guid appointmentId, TimeSpan startedAgo)
    {
        var start = api.Clock.GetUtcNow() - startedAgo;
        var end = start.AddHours(1);
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE scheduling.appointments SET starts_at = {start}, ends_at = {end} WHERE id = {appointmentId}");
    }

    [Fact]
    public async Task A_claim_is_refused_long_after_the_appointment()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, autoConfirm: true);
        using var client = await TestAccounts.ClientAsync(api);
        try
        {
            var booked = await BookAsync(client, salon, TestSalons.NextOpenDay(api));
            await MakePastAsync(booked.Id, TimeSpan.FromDays(9));
            (await OpenAsync(client, booked.Id)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    [Fact]
    public async Task An_open_claim_holds_the_payout_and_a_rejected_one_releases_it()
    {
        var (admin, salon, client) = await ArrangeOnlinePaymentAsync();
        try
        {
            var booked = await PaidAndOverAsync(client, salon);
            (await BalanceAsync(salon)).Payable.Should().Be(10000, "the appointment is over and paid");

            var dispute = await (await OpenAsync(client, booked.Id, "ServiceNotAsDescribed", "La prestation ne correspondait pas du tout à la description.")).Content.ReadFromJsonAsync<DisputeDto>();
            (await BalanceAsync(salon)).Payable.Should().Be(0, "Keke Protect keeps the money while the claim is open");
            (await admin.Client.PostAsync($"/api/v1/admin/payouts/establishments/{salon.Id}", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);

            (await admin.Client.PostAsJsonAsync($"/api/v1/admin/disputes/{dispute!.Id}/resolve", new DisputeResolveInput("RejectedClaim", "La prestation était conforme."))).StatusCode.Should().Be(HttpStatusCode.OK);
            (await BalanceAsync(salon)).Payable.Should().Be(10000, "a rejected claim releases the money");
        }
        finally
        {
            admin.Dispose();
            client.Dispose();
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            await ResetSettingsAsync();
        }
    }

    [Fact]
    public async Task An_upheld_claim_refunds_the_client_and_nothing_is_left_to_pay_out()
    {
        var (admin, salon, client) = await ArrangeOnlinePaymentAsync();
        try
        {
            var booked = await PaidAndOverAsync(client, salon);
            var dispute = await (await OpenAsync(client, booked.Id)).Content.ReadFromJsonAsync<DisputeDto>();

            var resolved = await admin.Client.PostAsJsonAsync($"/api/v1/admin/disputes/{dispute!.Id}/resolve", new DisputeResolveInput("RefundedClient", "Salon fermé : remboursement intégral."));
            resolved.StatusCode.Should().Be(HttpStatusCode.OK, await resolved.Content.ReadAsStringAsync());

            var mine = (await client.Client.GetFromJsonAsync<List<AppointmentDto>>("/api/v1/me/appointments"))!.Single(a => a.Id == booked.Id);
            mine.Payment!.Status.Should().Be("Refunded");
            (await BalanceAsync(salon)).Payable.Should().Be(0, "the money went back to the client");
            (await client.Client.GetFromJsonAsync<List<DisputeDto>>("/api/v1/me/disputes"))!.Single().RefundStatus.Should().Be("Refunded");
        }
        finally
        {
            admin.Dispose();
            client.Dispose();
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            await ResetSettingsAsync();
        }
    }
}
