using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

/// <summary>V1 parity: the client moves an appointment herself and tells the salon she is running late.</summary>
[Collection(ApiCollection.Name)]
public sealed class ClientMoveAndLateTests(ApiFixture api)
{
    private const string Phone = "07 00 00 12 34";

    private static async Task<string> CodeOfAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("code").GetString()!;
    }

    private static async Task<AppointmentDto> BookedAsync(UserSession client, Salon salon, DateTimeOffset start)
    {
        var response = await client.Client.PostAsJsonAsync("/api/v1/appointments", new BookingRequest(salon.Id, [salon.ServiceHour], null, start, Phone, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<AppointmentDto>())!;
    }

    private static Task<HttpResponseMessage> MoveAsync(UserSession client, Guid id, DateTimeOffset to) =>
        client.Client.PostAsJsonAsync($"/api/v1/appointments/{id}/reschedule", new RescheduleByClientInput(to));

    private static Task<HttpResponseMessage> LateAsync(UserSession client, Guid id, int minutes) =>
        client.Client.PostAsJsonAsync($"/api/v1/appointments/{id}/late", new LateInput(minutes));

    /// <summary>Access tokens last 15 minutes: after the test clock jumps ahead, a plain client signs in again.</summary>
    private async Task<UserSession> SignInAgainAsync(UserSession session)
    {
        var client = api.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new KekeBeauty.Modules.Identity.Application.LoginRequest(session.Email, TestAccounts.Password));
        login.EnsureSuccessStatusCode();
        var challenge = (await login.Content.ReadFromJsonAsync<KekeBeauty.Modules.Identity.Application.ChallengeResponse>())!;
        var complete = await client.PostAsJsonAsync("/api/v1/auth/complete-login", new KekeBeauty.Modules.Identity.Application.ChallengeRequest(challenge.ChallengeToken));
        complete.EnsureSuccessStatusCode();
        var auth = (await complete.Content.ReadFromJsonAsync<KekeBeauty.Modules.Identity.Application.AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return session with { Client = client };
    }

    private static async Task<AppointmentDto> MineAsync(UserSession client, Guid id) =>
        (await client.Client.GetFromJsonAsync<List<AppointmentDto>>("/api/v1/me/appointments"))!.Single(a => a.Id == id);

    private async Task<bool> HasNotificationAsync(Guid userId, string template)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
        return await db.Notifications.AnyAsync(n => n.UserId == userId && n.TemplateCode == template && n.Channel == NotificationChannel.InApp);
    }

    [Fact]
    public async Task A_client_can_move_her_own_appointment_to_a_free_slot_a_limited_number_of_times()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, autoConfirm: true);
        using var client = await TestAccounts.ClientAsync(api);
        using var rival = await TestAccounts.ClientAsync(api);
        using var stranger = await TestAccounts.ClientAsync(api);

        try
        {
            var start = TestSalons.NextOpenDay(api, daysAhead: 3);
            var booked = await BookedAsync(client, salon, start);
            booked.CanReschedule.Should().BeTrue();
            booked.CanSignalLate.Should().BeFalse();
            booked.Lines.Should().OnlyContain(l => l.ServiceId == salon.ServiceHour);
            await BookedAsync(rival, salon, start.AddHours(4));

            // Somebody else's appointment is never revealed.
            (await MoveAsync(stranger, booked.Id, start.AddHours(2))).StatusCode.Should().Be(HttpStatusCode.NotFound);

            // Occupied slot, past slot, same slot: refused, the appointment does not move.
            var taken = await MoveAsync(client, booked.Id, start.AddHours(4));
            taken.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await CodeOfAsync(taken)).Should().Be("slot.unavailable");
            (await MoveAsync(client, booked.Id, api.Clock.GetUtcNow().AddHours(-1))).StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await MoveAsync(client, booked.Id, start)).StatusCode.Should().Be(HttpStatusCode.Conflict);

            var first = await MoveAsync(client, booked.Id, start.AddHours(2));
            first.StatusCode.Should().Be(HttpStatusCode.OK);
            var moved = (await first.Content.ReadFromJsonAsync<AppointmentDto>())!;
            moved.Status.Should().Be("Confirmed");
            moved.StartsAt.Should().Be(start.AddHours(2));
            moved.EndsAt.Should().Be(start.AddHours(3));
            (await HasNotificationAsync(salon.Owner.UserId, "appointment.rescheduled_by_client.partner")).Should().BeTrue();
            (await HasNotificationAsync(client.UserId, "appointment.rescheduled_by_client")).Should().BeTrue();

            // The old slot is free again for someone else.
            (await MoveAsync(stranger, (await BookedAsync(stranger, salon, start.AddHours(6))).Id, start)).StatusCode.Should().Be(HttpStatusCode.OK);

            (await MoveAsync(client, booked.Id, start.AddHours(3))).StatusCode.Should().Be(HttpStatusCode.OK);
            (await MineAsync(client, booked.Id)).CanReschedule.Should().BeFalse();
            var third = await MoveAsync(client, booked.Id, start.AddHours(1));
            third.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await CodeOfAsync(third)).Should().Be("appointment.reschedule_limit");
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }

    [Fact]
    public async Task Moving_follows_the_cancellation_cut_off_and_never_applies_to_finished_appointments()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, autoConfirm: true);
        using var client = await TestAccounts.ClientAsync(api);

        try
        {
            var start = TestSalons.NextOpenDay(api, daysAhead: 3);
            var booked = await BookedAsync(client, salon, start);

            var settings = (await salon.Owner.Client.GetFromJsonAsync<BookingSettingsDto>($"/api/v1/pro/establishments/{salon.Id}/booking-settings"))!;
            (await salon.Owner.Client.PutAsJsonAsync($"/api/v1/pro/establishments/{salon.Id}/booking-settings", settings with { CancelLimitMinutes = 7 * 24 * 60 }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            var refused = await MoveAsync(client, booked.Id, start.AddHours(2));
            refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await CodeOfAsync(refused)).Should().Be("appointment.too_late");

            (await salon.Owner.Client.PutAsJsonAsync($"/api/v1/pro/establishments/{salon.Id}/booking-settings", settings))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.Client.PostAsync($"/api/v1/appointments/{booked.Id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.OK);
            var cancelled = await MoveAsync(client, booked.Id, start.AddHours(2));
            cancelled.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await CodeOfAsync(cancelled)).Should().Be("appointment.invalid_transition");
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }

    [Fact]
    public async Task A_client_can_signal_one_delay_around_her_visit_and_the_salon_is_told()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, autoConfirm: true);
        using var client = await TestAccounts.ClientAsync(api);
        using var stranger = await TestAccounts.ClientAsync(api);

        try
        {
            var start = TestSalons.NextOpenDay(api, daysAhead: 2);
            var booked = await BookedAsync(client, salon, start);

            // Only 10, 15 or 30 minutes; too early to be late; other people's appointments stay invisible.
            (await LateAsync(client, booked.Id, 20)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await LateAsync(client, booked.Id, 15)).StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await LateAsync(stranger, booked.Id, 15)).StatusCode.Should().Be(HttpStatusCode.NotFound);

            api.Clock.Advance(start - api.Clock.GetUtcNow() - TimeSpan.FromHours(1));
            using var later = await SignInAgainAsync(client);
            (await HasNotificationAsync(salon.Owner.UserId, "appointment.client_late.partner")).Should().BeFalse();
            (await MineAsync(later, booked.Id)).CanSignalLate.Should().BeTrue();
            var signalled = await LateAsync(later, booked.Id, 15);
            signalled.StatusCode.Should().Be(HttpStatusCode.OK);
            (await signalled.Content.ReadFromJsonAsync<AppointmentDto>())!.CanSignalLate.Should().BeFalse();
            (await HasNotificationAsync(salon.Owner.UserId, "appointment.client_late.partner")).Should().BeTrue();

            // Once only.
            (await LateAsync(later, booked.Id, 30)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // A cancelled appointment cannot be late.
            var other = await BookedAsync(later, salon, start.AddHours(3));
            (await later.Client.PostAsync($"/api/v1/appointments/{other.Id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await LateAsync(later, other.Id, 10)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }

    [Fact]
    public async Task Address_search_needs_a_signed_in_user_and_refuses_bad_input_without_calling_the_provider()
    {
        using var anonymous = api.CreateClient();
        (await anonymous.GetAsync("/api/v1/geo/search?q=Cocody")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/v1/geo/reverse?lat=5.3&lon=-4.0")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var client = await TestAccounts.ClientAsync(api);
        (await client.Client.GetAsync("/api/v1/geo/search")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.Client.GetAsync("/api/v1/geo/search?q=ab")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.Client.GetAsync($"/api/v1/geo/search?q={new string('a', 121)}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.Client.GetAsync("/api/v1/geo/reverse?lat=91&lon=0")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.Client.GetAsync("/api/v1/geo/reverse?lat=abc&lon=0")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
