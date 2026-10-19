using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Notifications;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Identity.Application;
using KekeBeauty.Modules.Notifications.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

/// <summary>SMS, WhatsApp, Web Push, delivery reports, reminders and WhatsApp sign-in: all off until the admins switch them on.</summary>
[Collection(ApiCollection.Name)]
public sealed class MessagingChannelTests(ApiFixture api)
{
    private const string Phone = "0700001234";
    private const string ValidP256dh = "BNcRdreALRFXTkOOUHK1EtK2wtaz5Ry4YfYCA_0QTpQtUbVlUls0VJXg7A8u-Ts1XbjhazAkj7I99e8QcYP7DkM";
    private const string ValidAuth = "tBHItJI5svbpez7KI4CCXg";

    private async Task ResetChannelsAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
        await db.AppSettings.ExecuteDeleteAsync();
    }

    private async Task NotifyAsync(Guid userId, string template = "appointment.requested", string? phone = Phone, bool isReminder = false)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NotificationService>()
            .NotifyAsync(userId, template, "Titre", $"Corps {Guid.NewGuid():N}", phone, CancellationToken.None, isReminder: isReminder);
    }

    private async Task<List<Notification>> RowsAsync(Guid userId, NotificationChannel channel)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
        return await db.Notifications.AsNoTracking().Where(n => n.UserId == userId && n.Channel == channel).ToListAsync();
    }

    // ───────── Admin switches and user preferences ─────────

    [Fact]
    public async Task Every_external_channel_is_off_by_default_and_only_the_in_app_notification_is_kept()
    {
        await ResetChannelsAsync();
        using var user = await TestAccounts.ClientAsync(api);
        var smsBefore = api.Sms.Messages.Count;

        await NotifyAsync(user.UserId);

        (await RowsAsync(user.UserId, NotificationChannel.InApp)).Should().HaveCount(1);
        (await RowsAsync(user.UserId, NotificationChannel.Sms)).Should().BeEmpty();
        (await RowsAsync(user.UserId, NotificationChannel.WhatsApp)).Should().BeEmpty();
        (await RowsAsync(user.UserId, NotificationChannel.Push)).Should().BeEmpty();
        api.Sms.Messages.Count.Should().Be(smsBefore);
    }

    [Fact]
    public async Task Sms_are_sent_once_the_admins_switch_them_on_and_never_to_someone_who_opted_out()
    {
        await ResetChannelsAsync();
        using var user = await TestAccounts.ClientAsync(api);
        using var optedOut = await TestAccounts.ClientAsync(api);
        try
        {
            await TestChannels.EnableSmsAsync(api);
            (await optedOut.Client.PutAsJsonAsync("/api/v1/me/notification-preferences", new NotificationPreferencesDto(true, false, true, true))).StatusCode.Should().Be(HttpStatusCode.OK);

            await NotifyAsync(user.UserId);
            await NotifyAsync(optedOut.UserId);

            var rows = await RowsAsync(user.UserId, NotificationChannel.Sms);
            rows.Should().ContainSingle().Which.Status.Should().Be(DeliveryStatus.Sent);
            (await RowsAsync(optedOut.UserId, NotificationChannel.Sms)).Should().BeEmpty();
            (await RowsAsync(optedOut.UserId, NotificationChannel.InApp)).Should().HaveCount(1, "the in-app notification is always kept");
        }
        finally
        {
            await ResetChannelsAsync();
        }
    }

    [Fact]
    public async Task WhatsApp_is_tried_first_and_the_sms_is_only_a_fallback()
    {
        await ResetChannelsAsync();
        using var worked = await TestAccounts.ClientAsync(api);
        using var failed = await TestAccounts.ClientAsync(api);
        var whatsApp = api.Services.GetRequiredService<CapturingWhatsAppSender>();
        try
        {
            await TestChannels.EnableSmsAsync(api);
            await TestChannels.SetAsync(api, AppSettingsService.WhatsAppEnabledKey, true);

            await NotifyAsync(worked.UserId);
            (await RowsAsync(worked.UserId, NotificationChannel.WhatsApp)).Should().ContainSingle().Which.Status.Should().Be(DeliveryStatus.Sent);
            (await RowsAsync(worked.UserId, NotificationChannel.Sms)).Should().BeEmpty();

            // Failed deliveries are retried after a short pause measured on the (fake) clock, so the test moves it forward.
            whatsApp.Succeed = false;
            var sending = NotifyAsync(failed.UserId);
            while (!sending.IsCompleted)
            {
                api.Clock.Advance(TimeSpan.FromSeconds(1));
                await Task.Delay(10);
            }

            await sending;
            var attempts = await RowsAsync(failed.UserId, NotificationChannel.WhatsApp);
            attempts.Should().ContainSingle().Which.Should().Match<Notification>(n => n.Status == DeliveryStatus.Failed && n.Attempts == NotificationService.MaxSmsAttempts);
            (await RowsAsync(failed.UserId, NotificationChannel.Sms)).Should().ContainSingle().Which.Status.Should().Be(DeliveryStatus.Sent);
        }
        finally
        {
            whatsApp.Succeed = true;
            await ResetChannelsAsync();
        }
    }

    // ───────── Web Push ─────────

    private static PushSubscribeInput Subscription(string endpoint) => new(endpoint, new PushKeysInput(ValidP256dh, ValidAuth), "test-agent");

    [Fact]
    public async Task Web_push_needs_a_valid_subscription_a_stable_server_key_and_the_admins_switch()
    {
        await ResetChannelsAsync();
        using var user = await TestAccounts.ClientAsync(api);
        using var anonymous = api.CreateClient();
        try
        {
            (await anonymous.GetAsync("/api/v1/me/push/config")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await user.Client.GetFromJsonAsync<PushConfigDto>("/api/v1/me/push/config"))!.Should().Match<PushConfigDto>(c => !c.Enabled && c.PublicKey == null);

            await TestChannels.SetAsync(api, AppSettingsService.PushEnabledKey, true);
            var config = (await user.Client.GetFromJsonAsync<PushConfigDto>("/api/v1/me/push/config"))!;
            config.Enabled.Should().BeTrue();
            config.PublicKey.Should().NotBeNullOrEmpty().And.HaveLength(87, "an uncompressed P-256 point in base64url");
            (await user.Client.GetFromJsonAsync<PushConfigDto>("/api/v1/me/push/config"))!.PublicKey.Should().Be(config.PublicKey);

            foreach (var bad in new[]
            {
                new PushSubscribeInput("http://insecure.example/x", new PushKeysInput(ValidP256dh, ValidAuth), null),
                new PushSubscribeInput("https://push.example/x", new PushKeysInput("x", "y"), null),
                new PushSubscribeInput("https://push.example/x", null, null),
            })
            {
                (await user.Client.PostAsJsonAsync("/api/v1/me/push/subscribe", bad)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            }
        }
        finally
        {
            await ResetChannelsAsync();
        }
    }

    [Fact]
    public async Task A_notification_reaches_every_subscribed_browser_and_dead_subscriptions_are_removed()
    {
        await ResetChannelsAsync();
        using var user = await TestAccounts.ClientAsync(api);
        using var optedOut = await TestAccounts.ClientAsync(api);
        var sender = api.Services.GetRequiredService<CapturingPushSender>();
        var endpoint = $"https://push.example/{Guid.NewGuid():N}";
        try
        {
            await TestChannels.SetAsync(api, AppSettingsService.PushEnabledKey, true);
            (await user.Client.PostAsJsonAsync("/api/v1/me/push/subscribe", Subscription(endpoint))).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await user.Client.PostAsJsonAsync("/api/v1/me/push/subscribe", Subscription(endpoint))).StatusCode.Should().Be(HttpStatusCode.NoContent, "subscribing twice replaces the row");
            (await optedOut.Client.PostAsJsonAsync("/api/v1/me/push/subscribe", Subscription($"https://push.example/{Guid.NewGuid():N}"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
            await optedOut.Client.PutAsJsonAsync("/api/v1/me/notification-preferences", new NotificationPreferencesDto(false, true, true, true));

            await NotifyAsync(user.UserId, "appointment.confirmed", phone: null);
            await NotifyAsync(optedOut.UserId, "appointment.confirmed", phone: null);

            var delivered = sender.Messages.Where(m => m.Endpoint == endpoint).ToList();
            delivered.Should().ContainSingle().Which.Message.Should().Match<PushMessage>(m => m.Title == "Titre" && m.Url == "/compte/rendez-vous");
            (await RowsAsync(user.UserId, NotificationChannel.Push)).Should().ContainSingle().Which.Status.Should().Be(DeliveryStatus.Sent);
            (await RowsAsync(optedOut.UserId, NotificationChannel.Push)).Should().BeEmpty();

            // The browser dropped the subscription: it is deleted instead of being retried forever.
            sender.Outcome = PushOutcome.Gone;
            await NotifyAsync(user.UserId, "appointment.confirmed", phone: null);
            await using var scope = api.Services.CreateAsyncScope();
            (await scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>().PushSubscriptions.AnyAsync(s => s.Endpoint == endpoint)).Should().BeFalse();
        }
        finally
        {
            sender.Outcome = PushOutcome.Sent;
            await ResetChannelsAsync();
        }
    }

    // ───────── LeTexto delivery reports ─────────

    [Fact]
    public async Task Delivery_reports_update_the_sms_status_only_with_the_shared_secret_and_never_downgrade()
    {
        using var user = await TestAccounts.ClientAsync(api);
        var providerId = $"lt-{Guid.NewGuid():N}";
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
            db.Notifications.Add(new Notification
            {
                UserId = user.UserId,
                Channel = NotificationChannel.Sms,
                TemplateCode = "t",
                Title = "t",
                Body = "b",
                Status = DeliveryStatus.Sent,
                ProviderMessageId = providerId,
            });
            await db.SaveChangesAsync();
        }

        using var client = api.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/webhooks/sms/letexto?sig=wrong", new { id = providerId, statuts = "DELIVERED" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/v1/webhooks/sms/letexto", new { id = providerId, statuts = "DELIVERED" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RowsAsync(user.UserId, NotificationChannel.Sms)).Single().Status.Should().Be(DeliveryStatus.Sent);

        (await client.PostAsJsonAsync("/api/v1/webhooks/sms/letexto?sig=test-dlr-secret", new { id = providerId, statuts = "DELIVERED" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await RowsAsync(user.UserId, NotificationChannel.Sms)).Single().Status.Should().Be(DeliveryStatus.Delivered);

        (await client.GetAsync($"/api/v1/webhooks/sms/letexto?sig=test-dlr-secret&id={providerId}&statuts=FAILED")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await RowsAsync(user.UserId, NotificationChannel.Sms)).Single().Status.Should().Be(DeliveryStatus.Delivered, "a delivered message cannot become failed");
        (await client.PostAsJsonAsync("/api/v1/webhooks/sms/letexto?sig=test-dlr-secret", new { id = "unknown", statuts = "FAILED" })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ───────── Back-office channel panel ─────────

    [Fact]
    public async Task The_back_office_shows_each_channel_and_refuses_sms_tests_while_the_provider_is_off()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        using var client = await TestAccounts.ClientAsync(api);

        (await client.Client.GetAsync("/api/v1/admin/channels")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var status = (await admin.Client.GetFromJsonAsync<ChannelsStatus>("/api/v1/admin/channels"))!;
        status.Sms.ProviderReady.Should().BeFalse("LeTexto is configured but disabled");
        status.Sms.Provider.Should().Be("Journal");
        status.WhatsApp.ProviderReady.Should().BeFalse();
        status.Push.ProviderReady.Should().BeTrue();

        (await admin.Client.PostAsJsonAsync("/api/v1/admin/channels/sms/test", new SmsTestInput(Phone))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.Client.PostAsJsonAsync("/api/v1/admin/channels/letexto/senders", new SenderInput("Keke"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.Client.PostAsJsonAsync("/api/v1/admin/channels/letexto/senders", new SenderInput("Un nom beaucoup trop long"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ───────── Reminders ─────────

    [Fact]
    public async Task A_confirmed_appointment_gets_one_reminder_before_it_and_the_client_can_opt_out_of_the_external_ones()
    {
        await ResetChannelsAsync();
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, autoConfirm: true);
        using var client = await TestAccounts.ClientAsync(api);
        try
        {
            await TestChannels.EnableSmsAsync(api);
            var start = TestSalons.NextOpenDay(api, daysAhead: 3);
            var response = await client.Client.PostAsJsonAsync("/api/v1/appointments", new BookingRequest(salon.Id, [salon.ServiceHour], null, start, Phone, null));
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var booked = (await response.Content.ReadFromJsonAsync<AppointmentDto>())!;
            booked.Status.Should().Be("Confirmed");

            // Booked a long time ago, and the appointment is now within the next 24 hours.
            await using (var scope = api.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
                var createdAt = start.AddDays(-5);
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE scheduling.appointments SET created_at = {createdAt} WHERE id = {booked.Id}");
            }

            api.Clock.Advance(start - api.Clock.GetUtcNow() - TimeSpan.FromHours(20));
            var reminders = api.Services.GetRequiredService<ReminderMaintenance>();

            await reminders.RunOnceAsync(CancellationToken.None);
            await reminders.RunOnceAsync(CancellationToken.None);

            await using var check = api.Services.CreateAsyncScope();
            var checkDb = check.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
            (await checkDb.Notifications.CountAsync(n => n.UserId == client.UserId && n.TemplateCode == "appointment.reminder" && n.Channel == NotificationChannel.InApp))
                .Should().Be(1, "each reminder is sent once");
            api.Sms.Messages.Should().Contain(m => m.Phone == Phone && m.Message.StartsWith("Rappel", StringComparison.Ordinal));
            var saved = await checkDb.Appointments.AsNoTracking().SingleAsync(a => a.Id == booked.Id);
            saved.FirstReminderSentAt.Should().NotBeNull();

            // Someone who turned reminders off keeps the in-app notification but gets nothing outside.
            // (The account is created now: the clock moved days ahead, so older access tokens have expired.)
            using var optedOut = await TestAccounts.ClientAsync(api);
            (await optedOut.Client.PutAsJsonAsync("/api/v1/me/notification-preferences", new NotificationPreferencesDto(true, true, true, false))).StatusCode.Should().Be(HttpStatusCode.OK);
            await NotifyAsync(optedOut.UserId, "appointment.reminder", isReminder: true);
            (await RowsAsync(optedOut.UserId, NotificationChannel.InApp)).Should().HaveCount(1);
            (await RowsAsync(optedOut.UserId, NotificationChannel.Sms)).Should().BeEmpty();
        }
        finally
        {
            await ResetChannelsAsync();
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }

    // ───────── WhatsApp sign-in ─────────

    [Fact]
    public async Task WhatsApp_sign_in_is_unavailable_while_it_is_switched_off()
    {
        using var anonymous = api.CreateClient();
        using var user = await TestAccounts.ClientAsync(api);

        var providers = await anonymous.GetFromJsonAsync<ProvidersResponse>("/api/v1/auth/providers");
        providers!.WhatsApp.Enabled.Should().BeFalse();
        (await anonymous.PostAsJsonAsync("/api/v1/auth/whatsapp/request", new WhatsAppRequest(Phone))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anonymous.PostAsJsonAsync("/api/v1/auth/whatsapp/verify", new WhatsAppVerifyRequest(Phone, "123456"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await user.Client.PostAsJsonAsync("/api/v1/me/whatsapp/link", new WhatsAppRequest(Phone))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await user.Client.GetFromJsonAsync<WhatsAppLinkStatus>("/api/v1/me/whatsapp"))!.Available.Should().BeFalse();
    }

    [Fact]
    public async Task A_number_is_linked_by_a_whatsapp_code_and_then_works_as_a_first_sign_in_factor()
    {
        await using var enabled = api.WithWebHostBuilder(builder => builder.UseSetting("Auth:WhatsApp:Enabled", "true"));
        var whatsApp = enabled.Services.GetRequiredService<CapturingWhatsAppSender>();
        // Tokens are issued with the main host's (fake) clock: the second host should read the same time. When the main
        // clock has not been advanced yet it may sit a few milliseconds behind this freshly created host; that tiny gap is
        // well within the 30 s token clock-skew, so a backwards SetUtcNow is simply skipped.
        try
        {
            enabled.Services.GetRequiredService<Microsoft.Extensions.Time.Testing.FakeTimeProvider>().SetUtcNow(api.Clock.GetUtcNow());
        }
        catch (ArgumentOutOfRangeException)
        {
            // The main clock is behind this host by less than the clock skew: leave the host's clock as it is.
        }
        using var user = await TestAccounts.ClientAsync(api);
        using var other = await TestAccounts.ClientAsync(api);
        var number = $"+22507{Random.Shared.Next(10_000_000, 99_999_999)}";

        HttpClient As(UserSession session)
        {
            var client = enabled.CreateClient();
            client.DefaultRequestHeaders.Authorization = session.Client.DefaultRequestHeaders.Authorization;
            return client;
        }

        string LastCode() => whatsApp.Messages.Last(m => m.Phone == number).Parameters[0];
        string WrongCode(string right) => right == "000000" ? "111111" : "000000";

        using var mine = As(user);
        (await mine.GetFromJsonAsync<WhatsAppLinkStatus>("/api/v1/me/whatsapp"))!.Should().Match<WhatsAppLinkStatus>(s => s.Available && !s.Linked);
        (await mine.PostAsJsonAsync("/api/v1/me/whatsapp/link", new WhatsAppRequest("pas un numero"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Linking: a wrong code is refused, the right one links the number.
        (await mine.PostAsJsonAsync("/api/v1/me/whatsapp/link", new WhatsAppRequest(number))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var code = LastCode();
        code.Should().MatchRegex("^[0-9]{6}$");
        (await mine.PostAsJsonAsync("/api/v1/me/whatsapp/verify", new PhoneCodeRequest(WrongCode(code)))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await mine.PostAsJsonAsync("/api/v1/me/whatsapp/verify", new PhoneCodeRequest(code))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await mine.GetFromJsonAsync<WhatsAppLinkStatus>("/api/v1/me/whatsapp"))!.Linked.Should().BeTrue();

        // Another account cannot take the same number.
        using var theirs = As(other);
        (await theirs.PostAsJsonAsync("/api/v1/me/whatsapp/link", new WhatsAppRequest(number))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Signing in: unknown numbers get the same answer as known ones and nothing is sent to them.
        using var anonymous = enabled.CreateClient();
        var before = whatsApp.Messages.Count;
        (await anonymous.PostAsJsonAsync("/api/v1/auth/whatsapp/request", new WhatsAppRequest("+2250799999999"))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        whatsApp.Messages.Count.Should().Be(before);

        (await anonymous.PostAsJsonAsync("/api/v1/auth/whatsapp/request", new WhatsAppRequest(number))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var loginCode = LastCode();
        (await anonymous.PostAsJsonAsync("/api/v1/auth/whatsapp/verify", new WhatsAppVerifyRequest(number, WrongCode(loginCode)))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var verified = await anonymous.PostAsJsonAsync("/api/v1/auth/whatsapp/verify", new WhatsAppVerifyRequest(number, loginCode));
        verified.StatusCode.Should().Be(HttpStatusCode.OK);
        var challenge = (await verified.Content.ReadFromJsonAsync<ChallengeResponse>())!;
        challenge.Status.Should().Be(ChallengeStatus.PasswordOnly, "WhatsApp replaces the password; a plain client needs no authenticator code");
        (await anonymous.PostAsJsonAsync("/api/v1/auth/whatsapp/verify", new WhatsAppVerifyRequest(number, loginCode))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "a code works once");

        // Codes per number are capped, whatever the number of requests.
        for (var i = 0; i < 6; i++)
        {
            await anonymous.PostAsJsonAsync("/api/v1/auth/whatsapp/request", new WhatsAppRequest(number));
        }

        whatsApp.Messages.Count(m => m.Phone == number).Should().BeLessThanOrEqualTo(1 + 3, "one link code plus at most three sign-in codes");

        (await mine.DeleteAsync("/api/v1/me/whatsapp")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await mine.GetFromJsonAsync<WhatsAppLinkStatus>("/api/v1/me/whatsapp"))!.Linked.Should().BeFalse();
    }
}
