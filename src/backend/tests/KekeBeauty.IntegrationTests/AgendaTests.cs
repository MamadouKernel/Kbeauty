using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KekeBeauty.Api.Endpoints;
using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Scheduling.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AgendaTests(ApiFixture api)
{
    private const string Phone = "07 00 00 12 34";

    private static string Url(Salon salon, string path) => $"/api/v1/pro/establishments/{salon.Id}/{path}";

    private static Task<HttpResponseMessage> BookAsync(UserSession client, Salon salon, DateTimeOffset start) =>
        client.Client.PostAsJsonAsync("/api/v1/appointments", new BookingRequest(salon.Id, [salon.ServiceHour], null, start, Phone, null));

    private HubConnection Connect(string hub, string? token = null)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(api.Server.BaseAddress, hub), options =>
            {
                options.HttpMessageHandlerFactory = _ => api.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                if (token is not null)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                }
            })
            .Build();
        return connection;
    }

    private static string TokenOf(UserSession session) => session.Client.DefaultRequestHeaders.Authorization!.Parameter!;

    // ───────── Booking settings ─────────

    [Fact]
    public async Task Booking_cannot_be_enabled_before_the_establishment_is_ready()
    {
        using var partner = await TestAccounts.PartnerAsync(api);
        using var anonymous = api.CreateClient();

        try
        {
            // A fresh draft: not published, no hours, no services.
            var geo = await anonymous.GetFromJsonAsync<List<System.Text.Json.JsonElement>>("/api/v1/geo/countries");
            geo.Should().NotBeNull();
            var draft = await new PartnerOnboardingHelper(api).CreateDraftAsync(partner);

            var settings = new BookingSettingsDto(true, false, 120, 60, 120, 15);
            var refused = await partner.Client.PutAsJsonAsync($"/api/v1/pro/establishments/{draft}/booking-settings", settings);

            refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await refused.Content.ReadAsStringAsync()).Should().Contain("publié");
            var current = await partner.Client.GetFromJsonAsync<BookingSettingsDto>($"/api/v1/pro/establishments/{draft}/booking-settings");
            current!.BookingEnabled.Should().BeFalse();
            current.MinLeadMinutes.Should().Be(120);
            current.MaxHorizonDays.Should().Be(60);
            current.SlotStepMinutes.Should().Be(15);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, partner.UserId);
        }
    }

    [Fact]
    public async Task Booking_settings_are_validated_and_saved()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin);

        try
        {
            var current = (await salon.Owner.Client.GetFromJsonAsync<BookingSettingsDto>(Url(salon, "booking-settings")))!;
            current.BookingEnabled.Should().BeTrue();

            foreach (var invalid in new[]
            {
                current with { SlotStepMinutes = 7 },
                current with { MaxHorizonDays = 0 },
                current with { MaxHorizonDays = 400 },
                current with { MinLeadMinutes = -1 },
                current with { CancelLimitMinutes = 99_999 },
            })
            {
                (await salon.Owner.Client.PutAsJsonAsync(Url(salon, "booking-settings"), invalid)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            }

            var updated = current with { AutoConfirm = true, MinLeadMinutes = 60, MaxHorizonDays = 30, CancelLimitMinutes = 240, SlotStepMinutes = 15 };
            (await salon.Owner.Client.PutAsJsonAsync(Url(salon, "booking-settings"), updated)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await salon.Owner.Client.GetFromJsonAsync<BookingSettingsDto>(Url(salon, "booking-settings"))).Should().Be(updated);

            // The 15-minute grid now applies to the public calendar.
            using var anonymous = api.CreateClient();
            var day = TestSalons.NextOpenDay(api);
            var availability = await anonymous.GetFromJsonAsync<AvailabilityResponse>(
                $"/api/v1/establishments/{salon.Id}/availability?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}&serviceIds={salon.ServiceHalfHour}");
            availability!.Days.Single().Slots.Take(3).Select(s => s.Time).Should().Equal("09:00", "09:15", "09:30");
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }

    // ───────── Resources and closures ─────────

    [Fact]
    public async Task Resources_can_be_managed_but_the_last_active_one_is_protected()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin);
        using var client = await TestAccounts.ClientAsync(api);

        try
        {
            var resources = (await salon.Owner.Client.GetFromJsonAsync<List<ResourceDto>>(Url(salon, "resources")))!;
            var main = resources.Should().ContainSingle().Subject;
            main.Name.Should().Be("Principal");

            // The only active resource cannot be switched off or removed while booking is enabled.
            (await salon.Owner.Client.PutAsJsonAsync(Url(salon, $"resources/{main.Id}"), new ResourceInput("Principal", [], false))).StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await salon.Owner.Client.DeleteAsync(Url(salon, $"resources/{main.Id}"))).StatusCode.Should().Be(HttpStatusCode.Conflict);

            (await salon.Owner.Client.PostAsJsonAsync(Url(salon, "resources"), new ResourceInput("X", [], true))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await salon.Owner.Client.PostAsJsonAsync(Url(salon, "resources"), new ResourceInput("Bella", [Guid.NewGuid()], true))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var created = await salon.Owner.Client.PostAsJsonAsync(Url(salon, "resources"), new ResourceInput("Bella", [salon.ServiceHour], true));
            created.StatusCode.Should().Be(HttpStatusCode.Created);
            var bella = (await created.Content.ReadFromJsonAsync<CreatedId>())!.Id;
            (await salon.Owner.Client.GetFromJsonAsync<List<ResourceDto>>(Url(salon, "resources")))!
                .Single(r => r.Id == bella).ServiceIds.Should().Equal(salon.ServiceHour);

            // A resource that already has appointments is deactivated rather than deleted, and stops being offered.
            var start = TestSalons.NextOpenDay(api);
            (await client.Client.PostAsJsonAsync("/api/v1/appointments", new BookingRequest(salon.Id, [salon.ServiceHour], bella, start, Phone, null)))
                .StatusCode.Should().Be(HttpStatusCode.Created);
            (await salon.Owner.Client.DeleteAsync(Url(salon, $"resources/{bella}"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await salon.Owner.Client.GetFromJsonAsync<List<ResourceDto>>(Url(salon, "resources")))!.Single(r => r.Id == bella).IsActive.Should().BeFalse();
            using var anonymous = api.CreateClient();
            (await anonymous.GetFromJsonAsync<BookingInfo>($"/api/v1/establishments/{salon.Slug}/booking"))!.Resources.Should().ContainSingle(r => r.Name == "Principal");

            // One without any appointment disappears for good.
            var spare = (await (await salon.Owner.Client.PostAsJsonAsync(Url(salon, "resources"), new ResourceInput("Spare", [], true))).Content.ReadFromJsonAsync<CreatedId>())!.Id;
            (await salon.Owner.Client.DeleteAsync(Url(salon, $"resources/{spare}"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await salon.Owner.Client.GetFromJsonAsync<List<ResourceDto>>(Url(salon, "resources")))!.Should().NotContain(r => r.Id == spare);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }

    [Fact]
    public async Task Closures_are_validated_and_listed()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin);

        try
        {
            var start = TestSalons.NextOpenDay(api, daysAhead: 5);

            (await salon.Owner.Client.PostAsJsonAsync(Url(salon, "time-off"), new TimeOffInput(null, start, start.AddHours(-1), null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await salon.Owner.Client.PostAsJsonAsync(Url(salon, "time-off"), new TimeOffInput(null, start, start.AddDays(400), null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await salon.Owner.Client.PostAsJsonAsync(Url(salon, "time-off"), new TimeOffInput(Guid.NewGuid(), start, start.AddHours(1), null))).StatusCode.Should().Be(HttpStatusCode.NotFound);

            var created = await salon.Owner.Client.PostAsJsonAsync(Url(salon, "time-off"), new TimeOffInput(null, start, start.AddDays(2), "Congés"));
            created.StatusCode.Should().Be(HttpStatusCode.Created);
            var list = (await salon.Owner.Client.GetFromJsonAsync<List<TimeOffDto>>(Url(salon, "time-off")))!;
            list.Should().ContainSingle(t => t.Reason == "Congés" && t.StartsAt == start);

            var id = list[0].Id;
            (await salon.Owner.Client.DeleteAsync(Url(salon, $"time-off/{id}"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await salon.Owner.Client.DeleteAsync(Url(salon, $"time-off/{id}"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }

    // ───────── Agenda and statistics ─────────

    [Fact]
    public async Task The_agenda_filters_by_status_and_period_and_the_statistics_count_activity()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin);
        using var client = await TestAccounts.ClientAsync(api);
        using var anonymous = api.CreateClient();

        try
        {
            var first = TestSalons.NextOpenDay(api, daysAhead: 2);
            var second = TestSalons.NextOpenDay(api, daysAhead: 9);
            (await BookAsync(client, salon, first)).StatusCode.Should().Be(HttpStatusCode.Created);
            var booked = await BookAsync(client, salon, second);
            var secondId = (await booked.Content.ReadFromJsonAsync<AppointmentDto>())!.Id;
            (await salon.Owner.Client.PostAsJsonAsync($"/api/v1/pro/appointments/{secondId}/confirm", new { })).StatusCode.Should().Be(HttpStatusCode.OK);

            var all = (await salon.Owner.Client.GetFromJsonAsync<List<PartnerAppointmentDto>>(Url(salon, "appointments")))!;
            all.Select(a => a.StartsAt).Should().Equal(first, second); // chronological

            (await salon.Owner.Client.GetFromJsonAsync<List<PartnerAppointmentDto>>(Url(salon, "appointments?status=Pending")))!.Should().ContainSingle();
            var window = await salon.Owner.Client.GetFromJsonAsync<List<PartnerAppointmentDto>>(
                Url(salon, $"appointments?from={Uri.EscapeDataString(second.AddHours(-1).ToString("O"))}&to={Uri.EscapeDataString(second.AddHours(3).ToString("O"))}"));
            window!.Should().ContainSingle(a => a.Id == secondId);
            (await salon.Owner.Client.GetAsync(Url(salon, "appointments?status=Nope"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // No-show is only possible once the appointment has started.
            (await salon.Owner.Client.PostAsJsonAsync($"/api/v1/pro/appointments/{secondId}/no-show", new { })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            await anonymous.PostAsJsonAsync($"/api/v1/establishments/{salon.Id}/events", new { type = "view" });
            await anonymous.PostAsJsonAsync($"/api/v1/establishments/{salon.Id}/events", new { type = "view" });
            await anonymous.PostAsJsonAsync($"/api/v1/establishments/{salon.Id}/events", new { type = "call" });
            await anonymous.PostAsJsonAsync($"/api/v1/establishments/{salon.Id}/events", new { type = "directions" });

            var today = api.Clock.GetUtcNow();
            var stats = (await salon.Owner.Client.GetFromJsonAsync<StatsDto>(
                Url(salon, $"stats?from={today.AddDays(-1):yyyy-MM-dd}&to={today.AddDays(30):yyyy-MM-dd}")))!;
            stats.Views.Should().Be(2);
            stats.CallClicks.Should().Be(1);
            stats.DirectionsClicks.Should().Be(1);
            stats.Appointments.Should().Contain("Pending", 1).And.Contain("Confirmed", 1);
            (await salon.Owner.Client.GetAsync(Url(salon, "stats?from=2026-01-10&to=2026-01-01"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }

    [Fact]
    public async Task Only_the_owner_can_use_the_agenda_of_an_establishment()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin);
        using var intruder = await TestAccounts.PartnerAsync(api, "Moussa", "Traoré");
        using var anonymous = api.CreateClient();

        try
        {
            foreach (var path in new[] { "booking-settings", "resources", "time-off", "appointments", "stats" })
            {
                (await intruder.Client.GetAsync(Url(salon, path))).StatusCode.Should().Be(HttpStatusCode.NotFound, path);
                (await anonymous.GetAsync(Url(salon, path))).StatusCode.Should().Be(HttpStatusCode.Unauthorized, path);
            }

            (await intruder.Client.PutAsJsonAsync(Url(salon, "booking-settings"), new BookingSettingsDto(false, false, 0, 60, 120, 15))).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await intruder.Client.PostAsJsonAsync(Url(salon, "resources"), new ResourceInput("Pirate", [], true))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }

    // ───────── Notification centre ─────────

    [Fact]
    public async Task The_notification_centre_lists_unread_messages_and_marks_them_read()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, autoConfirm: true);
        using var client = await TestAccounts.ClientAsync(api);
        using var other = await TestAccounts.ClientAsync(api);

        try
        {
            (await BookAsync(client, salon, TestSalons.NextOpenDay(api))).StatusCode.Should().Be(HttpStatusCode.Created);

            var list = (await client.Client.GetFromJsonAsync<NotificationList>("/api/v1/me/notifications"))!;
            list.Unread.Should().Be(1);
            var notification = list.Items.Should().ContainSingle().Subject;
            notification.Title.Should().Be("Rendez-vous confirmé");
            notification.IsRead.Should().BeFalse();

            (await other.Client.PostAsync($"/api/v1/me/notifications/{notification.Id}/read", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.Client.PostAsync($"/api/v1/me/notifications/{notification.Id}/read", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await client.Client.GetFromJsonAsync<NotificationList>("/api/v1/me/notifications"))!.Unread.Should().Be(0);

            (await BookAsync(client, salon, TestSalons.NextOpenDay(api, hour: 14))).StatusCode.Should().Be(HttpStatusCode.Created);
            (await client.Client.GetFromJsonAsync<NotificationList>("/api/v1/me/notifications"))!.Unread.Should().Be(1);
            (await client.Client.PostAsync("/api/v1/me/notifications/read-all", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
            var after = (await client.Client.GetFromJsonAsync<NotificationList>("/api/v1/me/notifications"))!;
            after.Unread.Should().Be(0);
            after.Items.Should().HaveCount(2).And.OnlyContain(n => n.IsRead);

            using var anonymous = api.CreateClient();
            (await anonymous.GetAsync("/api/v1/me/notifications")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }

    // ───────── Real time (SignalR) ─────────

    [Fact]
    public async Task Open_calendars_hear_about_a_taken_slot_without_reloading()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin);
        using var client = await TestAccounts.ClientAsync(api);
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = Connect("/hubs/availability");
        connection.On<string>("SlotsChanged", date => received.TrySetResult(date));

        try
        {
            await connection.StartAsync();
            await connection.InvokeAsync("Join", salon.Id);

            var start = TestSalons.NextOpenDay(api);
            (await BookAsync(client, salon, start)).StatusCode.Should().Be(HttpStatusCode.Created);

            (await received.Task.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(start.ToString("yyyy-MM-dd"));
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }

    [Fact]
    public async Task The_partner_agenda_receives_new_requests_live_and_only_for_its_own_establishments()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin);
        using var client = await TestAccounts.ClientAsync(api);
        using var stranger = await TestAccounts.PartnerAsync(api, "Moussa", "Traoré");
        var requested = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var owner = Connect("/hubs/pro", TokenOf(salon.Owner));
        await using var intruder = Connect("/hubs/pro", TokenOf(stranger));
        owner.On<Guid>(AppointmentEvents.Requested, id => requested.TrySetResult(id));
        owner.On<Guid>(AppointmentEvents.Cancelled, id => cancelled.TrySetResult(id));

        try
        {
            await owner.StartAsync();
            await owner.InvokeAsync("Join", salon.Id);
            await intruder.StartAsync();
            var refused = await Assert.ThrowsAsync<HubException>(() => intruder.InvokeAsync("Join", salon.Id));
            refused.Message.Should().Contain("introuvable");

            var booked = await BookAsync(client, salon, TestSalons.NextOpenDay(api));
            var id = (await booked.Content.ReadFromJsonAsync<AppointmentDto>())!.Id;
            (await requested.Task.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(id);

            (await client.Client.PostAsync($"/api/v1/appointments/{id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(id);

            // A hub that needs a partner role refuses everyone else.
            await using var anonymousConnection = Connect("/hubs/pro");
            await Assert.ThrowsAnyAsync<Exception>(() => anonymousConnection.StartAsync());
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }

    [Fact]
    public async Task Notifications_reach_the_signed_in_user_live()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin);
        using var client = await TestAccounts.ClientAsync(api);
        var received = new TaskCompletionSource<NotificationPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = Connect("/hubs/notifications", TokenOf(client));
        connection.On<NotificationPayload>("NotificationReceived", payload => received.TrySetResult(payload));

        try
        {
            await connection.StartAsync();
            var booked = await BookAsync(client, salon, TestSalons.NextOpenDay(api));
            var id = (await booked.Content.ReadFromJsonAsync<AppointmentDto>())!.Id;
            (await salon.Owner.Client.PostAsJsonAsync($"/api/v1/pro/appointments/{id}/confirm", new { })).StatusCode.Should().Be(HttpStatusCode.OK);

            var payload = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            payload.Title.Should().NotBeNullOrEmpty();
            payload.TemplateCode.Should().StartWith("appointment.");
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }
}

/// <summary>Creates a bare draft establishment for tests that need one that is not published.</summary>
internal sealed class PartnerOnboardingHelper(ApiFixture api)
{
    private sealed record Geo(Guid Id, string Name);

    private sealed record Country(Guid Id, string Name);

    private sealed record Category(Guid Id, string Name, string Slug, string Icon);

    public async Task<Guid> CreateDraftAsync(UserSession partner)
    {
        using var anonymous = api.CreateClient();
        var country = (await anonymous.GetFromJsonAsync<List<Country>>("/api/v1/geo/countries"))!.Single();
        var region = (await anonymous.GetFromJsonAsync<List<Geo>>($"/api/v1/geo/countries/{country.Id}/regions"))!.Single(r => r.Name.Contains("Abidjan", StringComparison.Ordinal));
        var city = (await anonymous.GetFromJsonAsync<List<Geo>>($"/api/v1/geo/regions/{region.Id}/cities"))!.Single();
        var commune = (await anonymous.GetFromJsonAsync<List<Geo>>($"/api/v1/geo/cities/{city.Id}/communes"))!.First();
        var category = (await anonymous.GetFromJsonAsync<List<Category>>("/api/v1/categories"))!.First();

        var response = await partner.Client.PostAsJsonAsync("/api/v1/pro/establishments", new EstablishmentInput(
            $"Brouillon {Guid.NewGuid():N}", "Un brouillon pour les tests.", "+22507000012", null, [category.Id], commune.Id, "Rue 1", 5.3, -4.0));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedId>())!.Id;
    }
}
