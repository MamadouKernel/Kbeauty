using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Notifications.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KekeBeauty.Infrastructure.Notifications;

public sealed record ChannelState(bool Enabled, bool ProviderReady, string Provider, string? Detail = null);

public sealed record ChannelsStatus(ChannelState Sms, ChannelState WhatsApp, ChannelState Push, ChannelState Email, string? LeTextoBalance);

public sealed record PushConfigDto(bool Enabled, string? PublicKey);

public sealed record PushKeysInput(string? P256dh, string? Auth);

public sealed record PushSubscribeInput(string? Endpoint, PushKeysInput? Keys, string? UserAgent);

public sealed record NotificationPreferencesDto(bool Push, bool Sms, bool WhatsApp, bool Reminders, bool Email = true);

public sealed record SmsTestInput(string? Phone);

public sealed record SenderInput(string? Name);

/// <summary>
/// What the notification channels can do today (for the back-office), the browsers' Web Push subscriptions, each person's
/// opt-outs, and the delivery reports LeTexto posts back.
/// </summary>
public sealed class ChannelService(
    KekeBeautyDbContext db,
    AppSettingsService settings,
    VapidKeyStore vapid,
    ISmsSender sms,
    IOptions<LeTextoOptions> leTexto,
    IOptions<WhatsAppOptions> whatsApp,
    IOptions<SmtpOptions> smtp,
    IConfiguration configuration,
    IServiceProvider services,
    TimeProvider time)
{
    public const int MaxSubscriptionsPerUser = 10;

    private static readonly Error InvalidSubscription = new("push.invalid_subscription", "Abonnement de notification invalide.");
    private static readonly Error ProviderNotReady = new("channel.not_ready", "Le fournisseur n'est pas configuré ou activé.");

    // ───────── Back-office ─────────

    public async Task<ChannelsStatus> StatusAsync(CancellationToken ct)
    {
        var current = await settings.GetAsync(ct);
        var lt = leTexto.Value;
        var emailProvider = string.Equals(configuration["Email:Provider"], "LeTexto", StringComparison.OrdinalIgnoreCase) && lt.EmailReady
            ? "LeTexto"
            : string.IsNullOrWhiteSpace(smtp.Value.Host) ? "Journal" : "SMTP";

        string? balance = null;
        if (lt.Enabled && lt.HasApiKey && services.GetService(typeof(LeTextoClient)) is LeTextoClient client)
        {
            try
            {
                var result = await client.BalanceAsync(ct);
                balance = result.Success ? result.Id : null;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                balance = null;
            }
        }

        return new ChannelsStatus(
            new ChannelState(current.SmsEnabled, lt.SmsReady, lt.SmsReady ? "LeTexto" : "Journal", lt.SmsReady ? null : "Expéditeur ou clé API manquants, ou LeTexto désactivé : les SMS sont seulement journalisés."),
            new ChannelState(current.WhatsAppEnabled, whatsApp.Value.IsConfigured, whatsApp.Value.Provider, "Aucun fournisseur WhatsApp n'est branché : les messages sont seulement journalisés."),
            new ChannelState(current.PushEnabled, true, "Web Push (VAPID)"),
            new ChannelState(current.EmailEnabled, emailProvider != "Journal", emailProvider),
            balance);
    }

    public async Task<Result> SendTestSmsAsync(string? phone, CancellationToken ct)
    {
        if (!leTexto.Value.SmsReady)
        {
            return Result.Failure(ProviderNotReady);
        }

        var result = await sms.SendAsync(phone ?? string.Empty, "Keke Beauty : SMS de test.", ct);
        return result.Success ? Result.Success() : Result.Failure(new Error("channel.send_failed", result.Error ?? "Envoi refusé."));
    }

    public async Task<Result<string>> CreateSenderAsync(string? name, CancellationToken ct)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > LeTextoOptions.MaxSenderLength)
        {
            return Result.Failure<string>(new Error("validation.failed", $"Le nom d'expéditeur doit compter de 1 à {LeTextoOptions.MaxSenderLength} caractères."));
        }

        if (!leTexto.Value.Enabled || !leTexto.Value.HasApiKey || services.GetService(typeof(LeTextoClient)) is not LeTextoClient client)
        {
            return Result.Failure<string>(ProviderNotReady);
        }

        var result = await client.CreateSenderAsync(trimmed, ct);
        return result.Success ? Result.Success(result.Id ?? trimmed) : Result.Failure<string>(new Error("channel.send_failed", result.Error ?? "Création refusée."));
    }

    // ───────── Web Push ─────────

    public async Task<PushConfigDto> PushConfigAsync(CancellationToken ct)
    {
        var current = await settings.GetAsync(ct);
        return new PushConfigDto(current.PushEnabled, current.PushEnabled ? (await vapid.GetAsync(ct)).PublicKey : null);
    }

    private static readonly string[] AllowedPushHostSuffixes =
    [
        ".googleapis.com",
        ".google.com",
        ".mozilla.com",
        ".push.apple.com",
        ".notify.windows.com",
        ".wns.windows.com",
        "push.example",
    ];

    private static bool IsAllowedPushHost(Uri uri)
    {
        if (uri.IsLoopback || !uri.IsDefaultPort || System.Net.IPAddress.TryParse(uri.Host, out _))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        if (!host.Contains('.') || host == "localhost" || host.EndsWith(".local", StringComparison.Ordinal)
            || host.EndsWith(".internal", StringComparison.Ordinal) || host.EndsWith(".localhost", StringComparison.Ordinal))
        {
            return false;
        }

        return AllowedPushHostSuffixes.Any(suffix =>
            suffix.StartsWith('.')
                ? host.EndsWith(suffix, StringComparison.Ordinal)
                : host.Equals(suffix, StringComparison.Ordinal) || host.EndsWith("." + suffix, StringComparison.Ordinal));
    }

    public async Task<Result> SubscribeAsync(Guid userId, PushSubscribeInput input, CancellationToken ct)
    {
        var endpoint = input.Endpoint?.Trim() ?? string.Empty;
        var p256dh = input.Keys?.P256dh?.Trim() ?? string.Empty;
        var auth = input.Keys?.Auth?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || endpoint.Length > 600
            || !IsAllowedPushHost(uri) || p256dh.Length is < 20 or > 200 || auth.Length is < 8 or > 100)
        {
            return Result.Failure(InvalidSubscription);
        }

        var agent = input.UserAgent is { Length: > 0 } ua ? ua[..Math.Min(ua.Length, 256)] : null;
        var existing = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint, ct);
        if (existing is not null)
        {
            // The same browser signed in as someone else: the subscription follows the current account.
            db.PushSubscriptions.Remove(existing);
            await db.SaveChangesAsync(ct);
        }

        var mine = await db.PushSubscriptions.Where(s => s.UserId == userId).OrderBy(s => s.CreatedAt).ToListAsync(ct);
        foreach (var stale in mine.Take(Math.Max(0, mine.Count - (MaxSubscriptionsPerUser - 1))))
        {
            db.PushSubscriptions.Remove(stale);
        }

        db.PushSubscriptions.Add(new PushSubscription { UserId = userId, Endpoint = endpoint, P256dh = p256dh, Auth = auth, UserAgent = agent, CreatedAt = time.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task UnsubscribeAsync(Guid userId, string? endpoint, CancellationToken ct)
    {
        var query = db.PushSubscriptions.Where(s => s.UserId == userId);
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            var target = endpoint.Trim();
            query = query.Where(s => s.Endpoint == target);
        }

        await query.ExecuteDeleteAsync(ct);
    }

    // ───────── Preferences ─────────

    public async Task<NotificationPreferencesDto> PreferencesAsync(Guid userId, CancellationToken ct)
    {
        var row = await db.NotificationPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        return row is null ? new NotificationPreferencesDto(true, true, true, true, true) : new NotificationPreferencesDto(row.Push, row.Sms, row.WhatsApp, row.Reminders, row.Email);
    }

    public async Task<NotificationPreferencesDto> SavePreferencesAsync(Guid userId, NotificationPreferencesDto input, CancellationToken ct)
    {
        var row = await db.NotificationPreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (row is null)
        {
            row = new NotificationPreference { UserId = userId };
            db.NotificationPreferences.Add(row);
        }

        row.Push = input.Push;
        row.Sms = input.Sms;
        row.WhatsApp = input.WhatsApp;
        row.Email = input.Email;
        row.Reminders = input.Reminders;
        row.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return input;
    }

    // ───────── Delivery reports ─────────

    /// <summary>
    /// Applies a LeTexto delivery report (PENDING, SENT, DELIVERED, FAILED) to the SMS it concerns. Reports for unknown messages are
    /// ignored, and a final state is never downgraded by a late "SENT".
    /// </summary>
    public async Task<bool> ApplyDeliveryReportAsync(string? providerMessageId, string? status, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(providerMessageId) || string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        var row = await db.Notifications.FirstOrDefaultAsync(n => n.Channel == NotificationChannel.Sms && n.ProviderMessageId == providerMessageId, ct);
        if (row is null)
        {
            return false;
        }

        switch (status.Trim().ToUpperInvariant())
        {
            case "DELIVERED":
                row.Status = DeliveryStatus.Delivered;
                row.Error = null;
                break;
            case "FAILED" when row.Status != DeliveryStatus.Delivered:
                row.Status = DeliveryStatus.Failed;
                row.Error = "Le SMS n'a pas pu être délivré (rapport de l'opérateur).";
                break;
            default:
                return true;
        }

        await db.SaveChangesAsync(ct);
        return true;
    }
}
