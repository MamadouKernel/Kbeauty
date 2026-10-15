using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Notifications.Domain;
using KekeBeauty.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class BookingTests(ApiFixture api)
{
    private const string Phone = "07 00 00 12 34";

    private static string Day(DateTimeOffset instant) => instant.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<string> CodeOfAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("code").GetString()!;
    }

    private static async Task<AvailabilityResponse> AvailabilityAsync(HttpClient client, Salon salon, DateTimeOffset day, params Guid[] services)
    {
        var query = string.Join('&', services.Select(s => $"serviceIds={s}"));
        return (await client.GetFromJsonAsync<AvailabilityResponse>(
            $"/api/v1/establishments/{salon.Id}/availability?from={Day(day)}&to={Day(day)}&{query}"))!;
    }

    private static bool IsFree(AvailabilityResponse response, DateTimeOffset start) =>
        response.Days.Single().Slots.Single(s => s.StartsAt == start).Available;

    private static Task<HttpResponseMessage> BookAsync(UserSession client, Salon salon, DateTimeOffset start, Guid? service = null, Guid? resource = null) =>
        client.Client.PostAsJsonAsync("/api/v1/appointments", new BookingRequest(salon.Id, [service ?? salon.ServiceHour], resource, start, Phone, "Merci !"));

    private static async Task<AppointmentDto> BookedAsync(UserSession client, Salon salon, DateTimeOffset start, Guid? service = null)
    {
        var response = await BookAsync(client, salon, start, service);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<AppointmentDto>())!;
    }

    private static Task<HttpResponseMessage> PartnerActAsync(Salon salon, Guid appointmentId, string action, object? body = null) =>
        salon.Owner.Client.PostAsJsonAsync($"/api/v1/pro/appointments/{appointmentId}/{action}", body ?? new { });

    private static async Task<AppointmentDto> MineAsync(UserSession client, Guid appointmentId) =>
        (await client.Client.GetFromJsonAsync<List<AppointmentDto>>("/api/v1/me/appointments"))!.Single(a => a.Id == appointmentId);

    private async Task<bool> HasNotificationAsync(Guid userId, string template)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
        return await db.Notifications.AnyAsync(n => n.UserId == userId && n.TemplateCode == template && n.Channel == NotificationChannel.InApp);
    }

    [Fact]
    public async Task A_booking_is_requested_confirmed_and_both_sides_are_notified()
    {
        await TestChannels.EnableSmsAsync(api);
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin);
        using var client = await TestAccounts.ClientAsync(api);
        using var anonymous = api.CreateClient();

        try
        {
            var start = TestSalons.NextOpenDay(api);
            IsFree(await AvailabilityAsync(anonymous, salon, start, salon.ServiceHour), start).Should().BeTrue();

            var booked = await BookedAsync(client, salon, start);
            booked.Status.Should().Be("Pending");
            booked.TotalPrice.Should().Be(10000);
            booked.EndsAt.Should().Be(start.AddHours(1));
            booked.Lines.Should().ContainSingle(l => l.Name == "Coupe longue" && l.DurationMinutes == 60);
            booked.ExpiresAt.Should().Be(api.Clock.GetUtcNow().AddHours(24));

            // The requested slot (and every slot overlapping it) is now red for everyone.
            var after = await AvailabilityAsync(anonymous, salon, start, salon.ServiceHour);
            IsFree(after, start).Should().BeFalse();
            IsFree(after, start.AddMinutes(-30)).Should().BeFalse();
            IsFree(after, start.AddHours(1)).Should().BeTrue();
            IsFree(after, start.AddHours(-1)).Should().BeTrue();

            var pending = (await salon.Owner.Client.GetFromJsonAsync<List<PartnerAppointmentDto>>(
                $"/api/v1/pro/establishments/{salon.Id}/appointments?status=Pending"))!.Should().ContainSingle().Subject;
            pending.ClientName.Should().Be("Awa Koné");
            pending.ClientPhone.Should().Be("0700001234");
            (await HasNotificationAsync(salon.Owner.UserId, "appointment.requested.partner")).Should().BeTrue();
            (await HasNotificationAsync(client.UserId, "appointment.requested")).Should().BeTrue();

            var confirm = await PartnerActAsync(salon, booked.Id, "confirm");
            confirm.StatusCode.Should().Be(HttpStatusCode.OK);

            (await MineAsync(client, booked.Id)).Status.Should().Be("Confirmed");
            (await HasNotificationAsync(client.UserId, "appointment.confirmed")).Should().BeTrue();
            api.Sms.Messages.Should().Contain(m => m.Phone == "0700001234" && m.Message.Contains("confirmé", StringComparison.Ordinal));

            await using var scope = api.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
            (await db.AppointmentHistory.Where(h => h.AppointmentId == booked.Id).OrderBy(h => h.At).Select(h => h.ToStatus).ToListAsync())
                .Should().Equal(AppointmentStatus.Pending, AppointmentStatus.Confirmed);
            var sms = await db.Notifications.Where(n => n.UserId == client.UserId && n.Channel == NotificationChannel.Sms).ToListAsync();
            sms.Should().NotBeEmpty().And.OnlyContain(n => n.Status == DeliveryStatus.Sent && n.Attempts == 1);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }

    [Fact]
    public async Task Two_clients_racing_for_the_same_slot_cannot_both_win()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin);
        using var first = await TestAccounts.ClientAsync(api);
        using var second = await TestAccounts.ClientAsync(api);

        try
        {
            foreach (var hour in new[] { 10, 11, 12, 13 })
            {
                var start = TestSalons.NextOpenDay(api, hour: hour);

                var results = await Task.WhenAll(BookAsync(first, salon, start), BookAsync(second, salon, start));

                var bodies = await Task.WhenAll(results.Select(r => r.Content.ReadAsStringAsync()));
                results.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.Created, HttpStatusCode.Conflict], string.Join(" | ", bodies));
            }

            await using var scope = api.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
            (await db.Appointments.CountAsync(a => a.EstablishmentId == salon.Id)).Should().Be(4);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }

    [Fact]
    public async Task The_database_itself_refuses_overlapping_active_appointments()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin);
        using var client = await TestAccounts.ClientAsync(api);

        try
        {
            var resources = (await salon.Owner.Client.GetFromJsonAsync<List<ResourceDto>>($"/api/v1/pro/establishments/{salon.Id}/resources"))!;
            var start = TestSalons.NextOpenDay(api);

            Appointment Make(DateTimeOffset at, int minutes, AppointmentStatus status) => new()
            {
                EstablishmentId = salon.Id,
                ClientUserId = client.UserId,
                ResourceId = resources[0].Id,
                StartsAt = at,
                EndsAt = at.AddMinutes(minutes),
                Status = status,
            };

            await using var scope = api.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
            var first = Make(start, 60, AppointmentStatus.Pending);
            db.Appointments.Add(first);
            await db.SaveChangesAsync();

            // Back-to-back is fine; an overlap is not, whatever the application checked before.
            db.Appointments.Add(Make(start.AddHours(1), 60, AppointmentStatus.Confirmed));
            await db.SaveChangesAsync();

            db.Appointments.Add(Make(start.AddMinutes(30), 60, AppointmentStatus.Pending));
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            AppointmentWorkflow.IsOverlapViolation(failure).Should().BeTrue();

            // A cancelled appointment no longer holds the slot.
            db.ChangeTracker.Clear();
            await db.Appointments.Where(a => a.Id == first.Id).ExecuteUpdateAsync(set => set.SetProperty(a => a.Status, AppointmentStatus.CancelledByClient));
            db.Appointments.Add(Make(start.AddMinutes(30), 30, AppointmentStatus.Pending));
            await db.SaveChangesAsync();
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }

    [Fact]
    public async Task Without_a_preference_the_next_free_resource_is_used_until_all_are_taken()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin);
        using var first = await TestAccounts.ClientAsync(api);
        using var second = await TestAccounts.ClientAsync(api);
        using var third = await TestAccounts.ClientAsync(api);

        try
        {
            var created = await salon.Owner.Client.PostAsJsonAsync(
                $"/api/v1/pro/establishments/{salon.Id}/resources", new ResourceInput("Bella", [], true));
            created.StatusCode.Should().Be(HttpStatusCode.Created);
            var bella = (await created.Content.ReadFromJsonAsync<KekeBeauty.Api.Endpoints.CreatedId>())!.Id;

            var info = (await api.CreateClient().GetFromJsonAsync<BookingInfo>($"/api/v1/establishments/{salon.Slug}/booking"))!;
            info.Resources.Select(r => r.Name).Should().Equal("Principal", "Bella");

            var start = TestSalons.NextOpenDay(api);
            var one = await BookedAsync(first, salon, start);
            var two = await BookedAsync(second, salon, start);
            new[] { one.ResourceName, two.ResourceName }.Should().BeEquivalentTo("Principal", "Bella");
            (await BookAsync(third, salon, start)).StatusCode.Should().Be(HttpStatusCode.Conflict);

            // Asking for a specific professional only looks at that professional's calendar.
            var later = start.AddHours(2);
            (await BookAsync(third, salon, later, resource: bella)).StatusCode.Should().Be(HttpStatusCode.Created);
            (await BookAsync(first, salon, later, resource: bella)).StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await BookAsync(first, salon, later)).StatusCode.Should().Be(HttpStatusCode.Created); // the other resource is free
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }

    [Fact]
    public async Task A_resource_only_offers_the_services_it_performs()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin);
        using var client = await TestAccounts.ClientAsync(api);

        try
        {
            var baseUrl = $"/api/v1/pro/establishments/{salon.Id}/resources";
            var main = (await salon.Owner.Client.GetFromJsonAsync<List<ResourceDto>>(baseUrl))![0];
            (await salon.Owner.Client.PutAsJsonAsync($"{baseUrl}/{main.Id}", new ResourceInput("Principal", [salon.ServiceHalfHour], true)))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);

            var start = TestSalons.NextOpenDay(api);
            // The only resource no longer performs the one-hour service: nothing can be booked for it.
            var closed = await BookAsync(client, salon, start);
            closed.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await CodeOfAsync(closed)).Should().Be("booking.closed");
            (await BookAsync(client, salon, start, service: salon.ServiceHalfHour)).StatusCode.Should().Be(HttpStatusCode.Created);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }

    [Fact]
    public async Task Several_services_add_up_their_durations_and_prices()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin);
        using var client = await TestAccounts.ClientAsync(api);
        using var anonymous = api.CreateClient();

        try
        {
            var start = TestSalons.NextOpenDay(api);
            var availability = await AvailabilityAsync(anonymous, salon, start, salon.ServiceHour, salon.ServiceHalfHour);
            availability.DurationMinutes.Should().Be(90);
            availability.TotalPrice.Should().Be(15000);
            // 09:00-18:00 with 90 minutes: the last start is 16:30.
            availability.Days.Single().Slots.Last().Time.Should().Be("16:30");

            var response = await client.Client.PostAsJsonAsync("/api/v1/appointments",
                new BookingRequest(salon.Id, [salon.ServiceHour, salon.ServiceHalfHour], null, start, Phone, null));
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var booked = (await response.Content.ReadFromJsonAsync<AppointmentDto>())!;
            booked.EndsAt.Should().Be(start.AddMinutes(90));
            booked.TotalPrice.Should().Be(15000);
            booked.Lines.Should().HaveCount(2);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }

    [Fact]
    public async Task Automatic_confirmation_books_straight_away()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, autoConfirm: true);
        using var client = await TestAccounts.ClientAsync(api);

        try
        {
            var booked = await BookedAsync(client, salon, TestSalons.NextOpenDay(api));

            booked.Status.Should().Be("Confirmed");
            booked.ExpiresAt.Should().BeNull();
            (await HasNotificationAsync(client.UserId, "appointment.confirmed")).Should().BeTrue();
            (await HasNotificationAsync(salon.Owner.UserId, "appointment.confirmed.partner")).Should().BeTrue();
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }

    [Fact]
    public async Task Invalid_bookings_are_refused()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin);
        using var client = await TestAccounts.ClientAsync(api);
        using var anonymous = api.CreateClient();

        try
        {
            var start = TestSalons.NextOpenDay(api);
            BookingRequest Valid() => new(salon.Id, [salon.ServiceHour], null, start, Phone, null);

            (await anonymous.PostAsJsonAsync("/api/v1/appointments", Valid())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await client.Client.PostAsJsonAsync("/api/v1/appointments", Valid() with { Phone = "abc" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await client.Client.PostAsJsonAsync("/api/v1/appointments", Valid() with { ServiceIds = [Guid.NewGuid()] })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await client.Client.PostAsJsonAsync("/api/v1/appointments", Valid() with { ServiceIds = [] })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await client.Client.PostAsJsonAsync("/api/v1/appointments", Valid() with { Note = new string('x', 501) })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await client.Client.PostAsJsonAsync("/api/v1/appointments", Valid() with { EstablishmentId = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.NotFound);

            // Off the 30-minute grid, in the past, outside opening hours, on a closed day: no slot, no booking.
            foreach (var impossible in new[] { start.AddMinutes(7), start.AddDays(-30), start.AddHours(10), start.AddDays(NextSundayOffset(start)) })
            {
                var response = await client.Client.PostAsJsonAsync("/api/v1/appointments", Valid() with { StartsAt = impossible });
                response.StatusCode.Should().Be(HttpStatusCode.Conflict, $"{impossible:u} is not bookable");
                (await CodeOfAsync(response)).Should().Be("slot.unavailable");
            }

            var farFuture = await client.Client.PostAsJsonAsync("/api/v1/appointments", Valid() with { StartsAt = start.AddDays(90) });
            farFuture.StatusCode.Should().Be(HttpStatusCode.Conflict);

            // Once the owner switches booking off, the calendar and the booking page close.
            var settings = (await salon.Owner.Client.GetFromJsonAsync<BookingSettingsDto>($"/api/v1/pro/establishments/{salon.Id}/booking-settings"))!;
            (await salon.Owner.Client.PutAsJsonAsync($"/api/v1/pro/establishments/{salon.Id}/booking-settings", settings with { BookingEnabled = false }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            (await anonymous.GetAsync($"/api/v1/establishments/{salon.Slug}/booking")).StatusCode.Should().Be(HttpStatusCode.Conflict);
            var closed = await anonymous.GetAsync($"/api/v1/establishments/{salon.Id}/availability?from={Day(start)}&to={Day(start)}&serviceIds={salon.ServiceHour}");
            closed.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await BookAsync(client, salon, start)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }

    private static int NextSundayOffset(DateTimeOffset from) => ((int)DayOfWeek.Sunday - (int)from.DayOfWeek + 7) % 7;

    [Fact]
    public async Task The_availability_range_is_validated()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin);
        using var anonymous = api.CreateClient();

        try
        {
            var start = TestSalons.NextOpenDay(api);
            var url = $"/api/v1/establishments/{salon.Id}/availability";

            (await anonymous.GetAsync($"{url}?from={Day(start)}&to={Day(start.AddDays(-1))}&serviceIds={salon.ServiceHour}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await anonymous.GetAsync($"{url}?from={Day(start)}&to={Day(start.AddDays(100))}&serviceIds={salon.ServiceHour}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await anonymous.GetAsync($"{url}?from={Day(start)}&to={Day(start)}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await anonymous.GetAsync($"{url}?from={Day(start)}&to={Day(start)}&serviceIds={Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await anonymous.GetAsync($"/api/v1/establishments/{Guid.NewGuid()}/availability?from={Day(start)}&to={Day(start)}&serviceIds={salon.ServiceHour}"))
                .StatusCode.Should().Be(HttpStatusCode.NotFound);

            var week = await anonymous.GetFromJsonAsync<AvailabilityResponse>($"{url}?from={Day(start)}&to={Day(start.AddDays(6))}&serviceIds={salon.ServiceHour}");
            week!.Days.Should().HaveCount(7);
            week.Days.Where(d => !d.IsOpen).Should().ContainSingle(); // Sunday
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }

    [Fact]
    public async Task Closures_turn_slots_red_and_reopen_when_removed()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin);
        using var anonymous = api.CreateClient();

        try
        {
            var start = TestSalons.NextOpenDay(api);
            var closure = await salon.Owner.Client.PostAsJsonAsync(
                $"/api/v1/pro/establishments/{salon.Id}/time-off", new TimeOffInput(null, start, start.AddHours(2), "Formation"));
            closure.StatusCode.Should().Be(HttpStatusCode.Created);
            var closureId = (await closure.Content.ReadFromJsonAsync<KekeBeauty.Api.Endpoints.CreatedId>())!.Id;

            var closed = await AvailabilityAsync(anonymous, salon, start, salon.ServiceHour);
            IsFree(closed, start).Should().BeFalse();
            IsFree(closed, start.AddMinutes(30)).Should().BeFalse();
            IsFree(closed, start.AddMinutes(-30)).Should().BeFalse(); // 09:30-10:30 overlaps the closure
            IsFree(closed, start.AddHours(2)).Should().BeTrue();
            (await BookAsync(await TestAccounts.ClientAsync(api), salon, start)).StatusCode.Should().Be(HttpStatusCode.Conflict);

            (await salon.Owner.Client.DeleteAsync($"/api/v1/pro/establishments/{salon.Id}/time-off/{closureId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
            IsFree(await AvailabilityAsync(anonymous, salon, start, salon.ServiceHour), start).Should().BeTrue();
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }

    [Fact]
    public async Task A_client_can_cancel_until_the_cancellation_limit()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, autoConfirm: true);
        using var client = await TestAccounts.ClientAsync(api);
        using var other = await TestAccounts.ClientAsync(api);
        using var anonymous = api.CreateClient();

        try
        {
            var start = TestSalons.NextOpenDay(api, daysAhead: 3);
            var booked = await BookedAsync(client, salon, start);
            booked.CanCancel.Should().BeTrue();

            (await other.Client.PostAsync($"/api/v1/appointments/{booked.Id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

            var cancelled = await client.Client.PostAsync($"/api/v1/appointments/{booked.Id}/cancel", null);
            cancelled.StatusCode.Should().Be(HttpStatusCode.OK);
            (await cancelled.Content.ReadFromJsonAsync<AppointmentDto>())!.Status.Should().Be("CancelledByClient");
            IsFree(await AvailabilityAsync(anonymous, salon, start, salon.ServiceHour), start).Should().BeTrue();
            (await HasNotificationAsync(salon.Owner.UserId, "appointment.cancelled_by_client.partner")).Should().BeTrue();

            var again = await client.Client.PostAsync($"/api/v1/appointments/{booked.Id}/cancel", null);
            again.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await CodeOfAsync(again)).Should().Be("appointment.invalid_transition");

            // A 7-day cancellation window makes an appointment three days away impossible to cancel online.
            var settings = (await salon.Owner.Client.GetFromJsonAsync<BookingSettingsDto>($"/api/v1/pro/establishments/{salon.Id}/booking-settings"))!;
            (await salon.Owner.Client.PutAsJsonAsync($"/api/v1/pro/establishments/{salon.Id}/booking-settings", settings with { CancelLimitMinutes = 7 * 24 * 60 }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            var late = await BookedAsync(client, salon, start);
            late.CanCancel.Should().BeFalse();
            var refused = await client.Client.PostAsync($"/api/v1/appointments/{late.Id}/cancel", null);
            refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await CodeOfAsync(refused)).Should().Be("appointment.too_late");
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }

    [Fact]
    public async Task The_partner_can_reject_or_cancel_and_the_client_hears_about_it()
    {
        await TestChannels.EnableSmsAsync(api);
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin);
        using var client = await TestAccounts.ClientAsync(api);
        using var stranger = await TestAccounts.PartnerAsync(api, "Moussa", "Traoré");

        try
        {
            var start = TestSalons.NextOpenDay(api);
            var request = await BookedAsync(client, salon, start);

            (await stranger.Client.PostAsJsonAsync($"/api/v1/pro/appointments/{request.Id}/confirm", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.Client.PostAsJsonAsync($"/api/v1/pro/appointments/{request.Id}/confirm", new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            (await PartnerActAsync(salon, request.Id, "reject", new RejectInput("Fermé ce jour-là"))).StatusCode.Should().Be(HttpStatusCode.OK);
            var rejected = await MineAsync(client, request.Id);
            rejected.Status.Should().Be("Rejected");
            rejected.RejectionReason.Should().Be("Fermé ce jour-là");
            api.Sms.Messages.Should().Contain(m => m.Message.Contains("Fermé ce jour-là", StringComparison.Ordinal));
            (await PartnerActAsync(salon, request.Id, "confirm")).StatusCode.Should().Be(HttpStatusCode.Conflict); // final

            var next = await BookedAsync(client, salon, start); // the slot is free again
            (await PartnerActAsync(salon, next.Id, "confirm")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await PartnerActAsync(salon, next.Id, "cancel", new CancelInput("Imprévu"))).StatusCode.Should().Be(HttpStatusCode.OK);
            (await MineAsync(client, next.Id)).Status.Should().Be("CancelledByEstablishment");
            (await HasNotificationAsync(client.UserId, "appointment.cancelled_by_establishment")).Should().BeTrue();
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }

    [Fact]
    public async Task A_reschedule_proposal_can_be_accepted_or_declined()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, autoConfirm: true);
        using var client = await TestAccounts.ClientAsync(api);
        using var rival = await TestAccounts.ClientAsync(api);
        using var anonymous = api.CreateClient();

        try
        {
            var start = TestSalons.NextOpenDay(api);
            var later = start.AddHours(4);
            var booked = await BookedAsync(client, salon, start);
            await BookedAsync(rival, salon, later.AddHours(2)); // occupies 16:00

            // The proposed slot must itself be free.
            (await PartnerActAsync(salon, booked.Id, "propose-reschedule", new RescheduleProposal(later.AddHours(2), null)))
                .StatusCode.Should().Be(HttpStatusCode.Conflict);

            var proposed = await PartnerActAsync(salon, booked.Id, "propose-reschedule", new RescheduleProposal(later, "Retard de la coiffeuse"));
            proposed.StatusCode.Should().Be(HttpStatusCode.OK);
            var mine = await MineAsync(client, booked.Id);
            mine.Status.Should().Be("RescheduleProposed");
            mine.ProposedStartsAt.Should().Be(later);
            mine.CanCancel.Should().BeTrue();
            (await HasNotificationAsync(client.UserId, "appointment.reschedule_proposed")).Should().BeTrue();

            // The original slot stays held until the client answers.
            IsFree(await AvailabilityAsync(anonymous, salon, start, salon.ServiceHour), start).Should().BeFalse();

            var accepted = await client.Client.PostAsync($"/api/v1/appointments/{booked.Id}/reschedule/accept", null);
            accepted.StatusCode.Should().Be(HttpStatusCode.OK);
            var moved = (await accepted.Content.ReadFromJsonAsync<AppointmentDto>())!;
            moved.Status.Should().Be("Confirmed");
            moved.StartsAt.Should().Be(later);
            moved.EndsAt.Should().Be(later.AddHours(1));
            moved.ProposedStartsAt.Should().BeNull();

            var availability = await AvailabilityAsync(anonymous, salon, start, salon.ServiceHour);
            IsFree(availability, start).Should().BeTrue();
            IsFree(availability, later).Should().BeFalse();
            (await HasNotificationAsync(salon.Owner.UserId, "appointment.reschedule_accepted.partner")).Should().BeTrue();

            // Declining a new proposal cancels the appointment and frees the slot.
            (await PartnerActAsync(salon, booked.Id, "propose-reschedule", new RescheduleProposal(start, null))).StatusCode.Should().Be(HttpStatusCode.OK);
            var declined = await client.Client.PostAsync($"/api/v1/appointments/{booked.Id}/reschedule/decline", null);
            declined.StatusCode.Should().Be(HttpStatusCode.OK);
            (await declined.Content.ReadFromJsonAsync<AppointmentDto>())!.Status.Should().Be("CancelledByClient");
            (await client.Client.PostAsync($"/api/v1/appointments/{booked.Id}/reschedule/accept", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }

    [Fact]
    public async Task Unanswered_requests_expire_and_finished_appointments_complete()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin);
        using var client = await TestAccounts.ClientAsync(api);
        var maintenance = api.Services.GetRequiredService<AppointmentMaintenance>();

        try
        {
            var start = TestSalons.NextOpenDay(api, daysAhead: 4);
            var pending = await BookedAsync(client, salon, start);
            var confirmed = await BookedAsync(client, salon, start.AddHours(2));
            (await PartnerActAsync(salon, confirmed.Id, "confirm")).StatusCode.Should().Be(HttpStatusCode.OK);

            api.Clock.Advance(TimeSpan.FromHours(23));
            (await maintenance.RunOnceAsync(CancellationToken.None)).Should().Be(0);

            api.Clock.Advance(TimeSpan.FromHours(2)); // 25 h after the request
            (await maintenance.RunOnceAsync(CancellationToken.None)).Should().Be(1);

            await using (var scope = api.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
                (await db.Appointments.SingleAsync(a => a.Id == pending.Id)).Status.Should().Be(AppointmentStatus.Expired);
                (await db.Appointments.SingleAsync(a => a.Id == confirmed.Id)).Status.Should().Be(AppointmentStatus.Confirmed);
                (await db.Notifications.AnyAsync(n => n.UserId == client.UserId && n.TemplateCode == "appointment.expired")).Should().BeTrue();
            }

            api.Clock.Advance(TimeSpan.FromDays(5));
            (await maintenance.RunOnceAsync(CancellationToken.None)).Should().Be(1);

            await using var final = api.Services.CreateAsyncScope();
            var context = final.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
            (await context.Appointments.SingleAsync(a => a.Id == confirmed.Id)).Status.Should().Be(AppointmentStatus.Completed);
            (await maintenance.RunOnceAsync(CancellationToken.None)).Should().Be(0); // idempotent
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }

    [Fact]
    public async Task An_unanswered_reschedule_proposal_is_cancelled_once_its_time_has_come()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, autoConfirm: true);
        using var client = await TestAccounts.ClientAsync(api);
        var maintenance = api.Services.GetRequiredService<AppointmentMaintenance>();

        try
        {
            var start = TestSalons.NextOpenDay(api, daysAhead: 2);
            var booked = await BookedAsync(client, salon, start);
            var proposed = start.AddHours(3);
            (await PartnerActAsync(salon, booked.Id, "propose-reschedule", new RescheduleProposal(proposed, null))).StatusCode.Should().Be(HttpStatusCode.OK);

            api.Clock.Advance(proposed - api.Clock.GetUtcNow() + TimeSpan.FromMinutes(1));
            (await maintenance.RunOnceAsync(CancellationToken.None)).Should().Be(1);

            await using var scope = api.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
            (await db.Appointments.SingleAsync(a => a.Id == booked.Id)).Status.Should().Be(AppointmentStatus.CancelledByClient);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }

    [Fact]
    public async Task Appointments_are_private_to_their_client_and_establishment()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin);
        using var client = await TestAccounts.ClientAsync(api);
        using var other = await TestAccounts.ClientAsync(api);
        using var anonymous = api.CreateClient();

        try
        {
            var booked = await BookedAsync(client, salon, TestSalons.NextOpenDay(api));

            (await other.Client.GetFromJsonAsync<List<AppointmentDto>>("/api/v1/me/appointments"))!.Should().NotContain(a => a.Id == booked.Id);
            (await anonymous.GetAsync("/api/v1/me/appointments")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await other.Client.PostAsync($"/api/v1/appointments/{booked.Id}/reschedule/decline", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await other.Client.GetAsync($"/api/v1/pro/establishments/{salon.Id}/appointments")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
            salon.Owner.Dispose();
        }
    }
}
