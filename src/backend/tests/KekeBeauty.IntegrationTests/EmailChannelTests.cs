using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Notifications;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Identity.Application;
using KekeBeauty.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KekeBeauty.IntegrationTests;

/// <summary>Notifications by e-mail through the SMTP relay (MailKit): who gets one, who never does, and what is recorded.</summary>
[Collection(ApiCollection.Name)]
public sealed class EmailChannelTests(ApiFixture api)
{
    private async Task ResetChannelsAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>().AppSettings.ExecuteDeleteAsync();
        api.Emails.Refuse = false;
    }

    private async Task NotifyAsync(Guid userId, string template = "appointment.confirmed", string title = "Rendez-vous confirmé", bool isReminder = false)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NotificationService>()
            .NotifyAsync(userId, template, title, $"Corps {template} {Guid.NewGuid():N}", null, CancellationToken.None, isReminder: isReminder);
    }

    private async Task<List<Notification>> RowsAsync(Guid userId)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>().Notifications.AsNoTracking()
            .Where(n => n.UserId == userId && n.Channel == NotificationChannel.Email).ToListAsync();
    }

    private IEnumerable<EmailMessage> Sent(string to, string subjectPart) =>
        api.Emails.Messages.Where(m => m.To == to && m.Subject.Contains(subjectPart, StringComparison.Ordinal));

    [Fact]
    public async Task A_confirmed_account_gets_the_notification_by_e_mail_with_a_link_and_the_delivery_is_recorded()
    {
        await ResetChannelsAsync();
        using var user = await TestAccounts.ClientAsync(api);

        await NotifyAsync(user.UserId);

        var mail = Sent(user.Email, "Rendez-vous confirmé").Should().ContainSingle().Subject;
        mail.Subject.Should().Be("Rendez-vous confirmé — Keke Beauty");
        mail.TextBody.Should().Contain("Bonjour Awa").And.Contain("Corps appointment.confirmed").And.Contain("/compte/rendez-vous").And.Contain("/compte");
        (await RowsAsync(user.UserId)).Should().ContainSingle().Which.Should().Match<Notification>(n => n.Status == DeliveryStatus.Sent && n.Attempts == 1);
    }

    [Fact]
    public async Task Nothing_is_sent_to_someone_who_opted_out_nor_when_the_admins_switch_e_mail_off()
    {
        await ResetChannelsAsync();
        using var optedOut = await TestAccounts.ClientAsync(api);
        using var other = await TestAccounts.ClientAsync(api);
        try
        {
            (await optedOut.Client.PutAsJsonAsync("/api/v1/me/notification-preferences", new NotificationPreferencesDto(true, true, true, true, Email: false))).StatusCode.Should().Be(HttpStatusCode.OK);
            await NotifyAsync(optedOut.UserId);
            Sent(optedOut.Email, "Rendez-vous confirmé").Should().BeEmpty();
            (await RowsAsync(optedOut.UserId)).Should().BeEmpty();

            await TestChannels.SetAsync(api, AppSettingsService.EmailEnabledKey, false);
            await NotifyAsync(other.UserId);
            Sent(other.Email, "Rendez-vous confirmé").Should().BeEmpty();
        }
        finally
        {
            await ResetChannelsAsync();
        }
    }

    [Fact]
    public async Task Reminders_follow_the_reminder_preference_and_billing_messages_are_never_doubled()
    {
        await ResetChannelsAsync();
        using var user = await TestAccounts.ClientAsync(api);
        using var quiet = await TestAccounts.ClientAsync(api);
        (await quiet.Client.PutAsJsonAsync("/api/v1/me/notification-preferences", new NotificationPreferencesDto(true, true, true, Reminders: false))).StatusCode.Should().Be(HttpStatusCode.OK);

        await NotifyAsync(user.UserId, "appointment.reminder", "Rappel de rendez-vous", isReminder: true);
        await NotifyAsync(quiet.UserId, "appointment.reminder", "Rappel de rendez-vous", isReminder: true);
        await NotifyAsync(user.UserId, "billing.payment_failed", "Paiement non abouti");

        Sent(user.Email, "Rappel de rendez-vous").Should().ContainSingle();
        Sent(quiet.Email, "Rappel de rendez-vous").Should().BeEmpty();
        Sent(user.Email, "Paiement non abouti").Should().BeEmpty();
    }

    [Fact]
    public async Task An_unconfirmed_address_receives_nothing()
    {
        await ResetChannelsAsync();
        using var client = api.CreateClient();
        var email = $"unconfirmed{Guid.NewGuid():N}@example.com";
        (await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, TestAccounts.Password, "Awa", "Koné", "Client", true))).EnsureSuccessStatusCode();
        Guid userId;
        await using (var scope = api.Services.CreateAsyncScope())
        {
            userId = await scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>().Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        }

        await NotifyAsync(userId);

        api.Emails.Messages.Where(m => m.To == email && m.Subject.Contains("Rendez-vous confirmé")).Should().BeEmpty();
        (await RowsAsync(userId)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_relay_that_refuses_the_message_is_recorded_as_a_failure_after_three_attempts()
    {
        await ResetChannelsAsync();
        using var user = await TestAccounts.ClientAsync(api);
        try
        {
            api.Emails.Refuse = true;
            var sending = NotifyAsync(user.UserId);
            while (!sending.IsCompleted)
            {
                api.Clock.Advance(TimeSpan.FromSeconds(1));
                await Task.Delay(10);
            }

            await sending;
            (await RowsAsync(user.UserId)).Should().ContainSingle().Which.Should().Match<Notification>(n => n.Status == DeliveryStatus.Failed && n.Attempts == NotificationService.MaxSmsAttempts && n.Error != null);
            Sent(user.Email, "Rendez-vous confirmé").Should().BeEmpty();
        }
        finally
        {
            api.Emails.Refuse = false;
        }
    }

    [Fact]
    public async Task The_preferences_carry_the_e_mail_choice_and_default_to_on()
    {
        using var user = await TestAccounts.ClientAsync(api);

        (await user.Client.GetFromJsonAsync<NotificationPreferencesDto>("/api/v1/me/notification-preferences"))!.Email.Should().BeTrue();
        (await user.Client.PutAsJsonAsync("/api/v1/me/notification-preferences", new NotificationPreferencesDto(true, true, true, true, Email: false))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await user.Client.GetFromJsonAsync<NotificationPreferencesDto>("/api/v1/me/notification-preferences"))!.Email.Should().BeFalse();
    }
}
