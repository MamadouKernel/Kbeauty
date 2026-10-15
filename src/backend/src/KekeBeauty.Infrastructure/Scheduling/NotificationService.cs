using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Notifications;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Notifications.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KekeBeauty.Infrastructure.Scheduling;

/// <summary>
/// Delivers one message to one user on every channel that applies: the in-app notification centre (always), then Web Push,
/// WhatsApp and SMS. A channel is used only when the admins switched it on and the person did not opt out; WhatsApp falls
/// back to SMS when it fails. External deliveries are attempted up to three times (PRD NOT-01, NOT-04).
/// </summary>
public sealed class NotificationService(
    KekeBeautyDbContext db,
    ISmsSender sms,
    IWhatsAppSender whatsApp,
    IPushSender push,
    IRealtimeNotifier realtime,
    AppSettingsService settings,
    IOptions<WhatsAppOptions> whatsAppOptions,
    TimeProvider time,
    ILogger<NotificationService> logger)
{
    public const int MaxSmsAttempts = 3;
    private const int ListLimit = 30;

    private sealed record Delivery(bool Success, string? ProviderMessageId, string? Error);

    public async Task NotifyAsync(
        Guid userId,
        string templateCode,
        string title,
        string body,
        string? phone,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? variables = null,
        bool isReminder = false)
    {
        // An admin-edited template (PRD NOT-02) replaces the built-in wording when the sender provides its variables.
        if (variables is not null
            && await db.NotificationTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Code == templateCode, ct) is { } custom)
        {
            title = TemplateRenderer.Render(custom.Title, variables);
            body = TemplateRenderer.Render(custom.Body, variables);
        }

        var now = time.GetUtcNow();
        var inApp = new Notification
        {
            UserId = userId,
            Channel = NotificationChannel.InApp,
            TemplateCode = templateCode,
            Title = title,
            Body = body,
            Status = DeliveryStatus.Sent,
            Attempts = 1,
            CreatedAt = now,
            SentAt = now,
        };
        db.Notifications.Add(inApp);
        await db.SaveChangesAsync(ct);

        try
        {
            await realtime.NotificationReceivedAsync(userId, new NotificationPayload(inApp.Id, title, body, templateCode, now), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Live notification push failed for {TemplateCode}", templateCode);
        }

        var channels = await settings.GetAsync(ct);
        var preferences = await db.NotificationPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct)
            ?? new NotificationPreference { UserId = userId };
        if (isReminder && !preferences.Reminders)
        {
            return;
        }

        if (channels.PushEnabled && preferences.Push)
        {
            await DeliverPushAsync(userId, templateCode, title, body, ct);
        }

        if (string.IsNullOrWhiteSpace(phone))
        {
            return;
        }

        var viaWhatsApp = false;
        if (channels.WhatsAppEnabled && preferences.WhatsApp)
        {
            var language = whatsAppOptions.Value.Language;
            var template = whatsAppOptions.Value.NotificationTemplate;
            viaWhatsApp = await DeliverExternalAsync(userId, NotificationChannel.WhatsApp, templateCode, title, body, now, ct, async () =>
            {
                var result = await whatsApp.SendTemplateAsync(phone, template, language, [title, body], false, ct);
                return new Delivery(result.Success, result.ProviderMessageId, result.Error);
            });
        }

        if (!viaWhatsApp && channels.SmsEnabled && preferences.Sms)
        {
            await DeliverExternalAsync(userId, NotificationChannel.Sms, templateCode, title, body, now, ct, async () =>
            {
                var result = await sms.SendAsync(phone, body, ct);
                return new Delivery(result.Success, result.ProviderMessageId, result.Error);
            });
        }
    }

    public async Task<NotificationList> ListAsync(Guid userId, CancellationToken ct)
    {
        var inApp = db.Notifications.AsNoTracking().Where(n => n.UserId == userId && n.Channel == NotificationChannel.InApp);
        var items = await inApp.OrderByDescending(n => n.CreatedAt).Take(ListLimit)
            .Select(n => new NotificationDto(n.Id, n.Title, n.Body, n.TemplateCode, n.CreatedAt, n.ReadAt != null))
            .ToListAsync(ct);
        var unread = await inApp.CountAsync(n => n.ReadAt == null, ct);
        return new NotificationList(items, unread);
    }

    public async Task<bool> MarkReadAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var changed = await db.Notifications
            .Where(n => n.Id == id && n.UserId == userId && n.Channel == NotificationChannel.InApp && n.ReadAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.ReadAt, now), ct);
        return changed > 0 || await db.Notifications.AnyAsync(n => n.Id == id && n.UserId == userId, ct);
    }

    public Task<int> MarkAllReadAsync(Guid userId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return db.Notifications
            .Where(n => n.UserId == userId && n.Channel == NotificationChannel.InApp && n.ReadAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.ReadAt, now), ct);
    }

    /// <summary>Where a tap on the notification leads: the appointments for booking events, the partner space for billing.</summary>
    public static string TargetUrl(string templateCode)
    {
        if (templateCode.StartsWith("appointment", StringComparison.Ordinal))
        {
            return templateCode.EndsWith(".partner", StringComparison.Ordinal) ? "/pro" : "/compte/rendez-vous";
        }

        return templateCode.StartsWith("billing", StringComparison.Ordinal) ? "/pro" : "/";
    }

    private async Task DeliverPushAsync(Guid userId, string templateCode, string title, string body, CancellationToken ct)
    {
        var subscriptions = await db.PushSubscriptions.Where(s => s.UserId == userId).ToListAsync(ct);
        if (subscriptions.Count == 0)
        {
            return;
        }

        var row = new Notification
        {
            UserId = userId,
            Channel = NotificationChannel.Push,
            TemplateCode = templateCode,
            Title = title,
            Body = body,
            CreatedAt = time.GetUtcNow(),
            Attempts = 1,
        };
        db.Notifications.Add(row);

        var message = new PushMessage(title, body, TargetUrl(templateCode), templateCode);
        var delivered = 0;
        foreach (var subscription in subscriptions)
        {
            var outcome = await push.SendAsync(new PushSubscriptionInfo(subscription.Endpoint, subscription.P256dh, subscription.Auth), message, ct);
            switch (outcome)
            {
                case PushOutcome.Sent:
                    delivered++;
                    subscription.LastSuccessAt = time.GetUtcNow();
                    break;
                case PushOutcome.Gone:
                    db.PushSubscriptions.Remove(subscription);
                    break;
            }
        }

        row.Status = delivered > 0 ? DeliveryStatus.Sent : DeliveryStatus.Failed;
        row.SentAt = delivered > 0 ? time.GetUtcNow() : null;
        row.Error = delivered > 0 ? null : "Aucun appareil n'a accepté la notification.";
        await db.SaveChangesAsync(ct);
    }

    private async Task<bool> DeliverExternalAsync(
        Guid userId,
        NotificationChannel channel,
        string templateCode,
        string title,
        string body,
        DateTimeOffset createdAt,
        CancellationToken ct,
        Func<Task<Delivery>> send)
    {
        var row = new Notification
        {
            UserId = userId,
            Channel = channel,
            TemplateCode = templateCode,
            Title = title,
            Body = body,
            CreatedAt = createdAt,
        };
        db.Notifications.Add(row);
        await db.SaveChangesAsync(ct);

        for (var attempt = 1; attempt <= MaxSmsAttempts; attempt++)
        {
            row.Attempts = attempt;
            try
            {
                var result = await send();
                if (result.Success)
                {
                    row.Status = DeliveryStatus.Sent;
                    row.ProviderMessageId = result.ProviderMessageId;
                    row.SentAt = time.GetUtcNow();
                    row.Error = null;
                    break;
                }

                row.Error = result.Error;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                row.Error = ex.Message;
            }

            row.Status = DeliveryStatus.Failed;
            if (attempt < MaxSmsAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), time, ct);
            }
        }

        if (row.Status == DeliveryStatus.Failed)
        {
            logger.LogWarning("{Channel} message {TemplateCode} could not be delivered after {Attempts} attempts", channel, templateCode, row.Attempts);
        }

        await db.SaveChangesAsync(ct);
        return row.Status == DeliveryStatus.Sent;
    }
}
