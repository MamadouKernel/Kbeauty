using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Notifications.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KekeBeauty.Infrastructure.Notifications;

/// <summary>
/// Checks the <c>X-Zavu-Signature</c> header (<c>t=&lt;unix seconds&gt;,v1=&lt;hex&gt;</c> and/or <c>v2=&lt;hex&gt;</c>). <c>v2</c> signs
/// <c>"{t}.{body}"</c>, <c>v1</c> the body alone; both are HMAC-SHA256 in lowercase hex. A call older than five minutes or more than a
/// minute in the future is refused, and the comparison is constant-time.
/// </summary>
public static class ZavuSignature
{
    public const string HeaderName = "X-Zavu-Signature";
    public const int MaxAgeSeconds = 300;
    public const int MaxFutureSeconds = 60;

    public static bool IsValid(ReadOnlySpan<byte> rawBody, string? header, string secret, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(header) || secret.Length == 0)
        {
            return false;
        }

        long? timestamp = null;
        string? v1 = null;
        string? v2 = null;
        foreach (var piece in header.Split(','))
        {
            var index = piece.IndexOf('=');
            if (index <= 0)
            {
                continue;
            }

            var key = piece[..index].Trim();
            var value = piece[(index + 1)..].Trim();
            switch (key)
            {
                case "t" when long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed):
                    timestamp = parsed;
                    break;
                case "v1":
                    v1 = value;
                    break;
                case "v2":
                    v2 = value;
                    break;
            }
        }

        if (timestamp is not { } t)
        {
            return false;
        }

        var age = now.ToUnixTimeSeconds() - t;
        if (age > MaxAgeSeconds || age < -MaxFutureSeconds)
        {
            return false;
        }

        var received = v2 ?? v1;
        if (string.IsNullOrEmpty(received))
        {
            return false;
        }

        var key2 = Encoding.UTF8.GetBytes(secret);
        byte[] expected;
        if (v2 is not null)
        {
            var prefix = Encoding.UTF8.GetBytes($"{t}.");
            var signed = new byte[prefix.Length + rawBody.Length];
            prefix.CopyTo(signed, 0);
            rawBody.CopyTo(signed.AsSpan(prefix.Length));
            expected = HMACSHA256.HashData(key2, signed);
        }
        else
        {
            expected = HMACSHA256.HashData(key2, rawBody);
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Convert.ToHexStringLower(expected)), Encoding.ASCII.GetBytes(received.ToLowerInvariant()));
    }
}

public enum ZavuWebhookResult
{
    Rejected,
    Processed,
    Duplicate,
    Ignored,
    Malformed,
}

/// <summary>
/// Applies Zavu webhook events: delivery reports on the WhatsApp notifications we sent, and a "STOP" reply that switches WhatsApp
/// off for that person. Delivery is at-least-once, so an event id is remembered for a day and a repeat is acknowledged without effect
/// (the status changes are idempotent anyway, so a second server that did not see the id does no harm).
/// </summary>
public sealed class ZavuWebhookHandler(KekeBeautyDbContext db, WhatsAppConsentService consents, IMemoryCache cache, IOptions<ZavuOptions> options, TimeProvider time, ILogger<ZavuWebhookHandler> logger)
{
    public const int MaxBodyBytes = 64 * 1024;
    private static readonly string[] StopWords = ["stop", "arret", "arrêt", "arrete", "arrêter", "desabonner", "désabonner"];

    public async Task<ZavuWebhookResult> HandleAsync(byte[] rawBody, string? signature, CancellationToken ct)
    {
        if (!ZavuSignature.IsValid(rawBody, signature, options.Value.WebhookSecret, time.GetUtcNow()))
        {
            Telemetry.WhatsAppWebhooks.Add(1, new KeyValuePair<string, object?>("result", "rejected_signature"));
            return ZavuWebhookResult.Rejected;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(rawBody);
        }
        catch (JsonException)
        {
            return ZavuWebhookResult.Malformed;
        }

        using (document)
        {
            var root = document.RootElement;
            var type = Text(root, "type");
            var eventId = Text(root, "id");
            if (type is null || eventId is null || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                return ZavuWebhookResult.Malformed;
            }

            var key = $"zavu:event:{eventId}";
            if (cache.TryGetValue(key, out _))
            {
                Telemetry.WhatsAppWebhooks.Add(1, new KeyValuePair<string, object?>("result", "duplicate"));
                return ZavuWebhookResult.Duplicate;
            }

            var result = type switch
            {
                "message.delivered" or "message.read" => await ApplyStatusAsync(Text(data, "messageId"), DeliveryStatus.Delivered, null, ct),
                "message.failed" => await ApplyStatusAsync(Text(data, "messageId"), DeliveryStatus.Failed, Text(data, "errorCode"), ct),
                "message.inbound" => await ApplyInboundAsync(data, ct),
                "template.status_changed" => LogTemplate(data),
                _ => ZavuWebhookResult.Ignored,
            };

            cache.Set(key, true, TimeSpan.FromDays(1));
            Telemetry.WhatsAppWebhooks.Add(1, new KeyValuePair<string, object?>("result", result == ZavuWebhookResult.Processed ? "processed" : "ignored"));
            return result;
        }
    }

    private async Task<ZavuWebhookResult> ApplyStatusAsync(string? messageId, DeliveryStatus status, string? errorCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return ZavuWebhookResult.Malformed;
        }

        var row = await db.Notifications.FirstOrDefaultAsync(n => n.Channel == NotificationChannel.WhatsApp && n.ProviderMessageId == messageId, ct);
        if (row is null)
        {
            return ZavuWebhookResult.Ignored;
        }

        // A final "delivered" is never downgraded by a late failure report, and a failure is not overwritten by a late "read".
        if (status == DeliveryStatus.Delivered && row.Status != DeliveryStatus.Failed)
        {
            row.Status = DeliveryStatus.Delivered;
            row.Error = null;
        }
        else if (status == DeliveryStatus.Failed && row.Status != DeliveryStatus.Delivered)
        {
            row.Status = DeliveryStatus.Failed;
            row.Error = $"WhatsApp n'a pas pu délivrer le message{(string.IsNullOrWhiteSpace(errorCode) ? string.Empty : $" (code {errorCode.Trim()[..Math.Min(errorCode.Trim().Length, 60)]})")}.";
        }
        else
        {
            return ZavuWebhookResult.Ignored;
        }

        await db.SaveChangesAsync(ct);
        return ZavuWebhookResult.Processed;
    }

    /// <summary>"STOP" (and its French equivalents) from a known number switches WhatsApp notifications off for that person.</summary>
    private async Task<ZavuWebhookResult> ApplyInboundAsync(JsonElement data, CancellationToken ct)
    {
        var text = Text(data, "text")?.Trim().Trim('.', '!', ' ').ToLowerInvariant();
        if (text is null || !StopWords.Contains(text) || PhoneNumbers.ToE164(Text(data, "from")) is not { } phone)
        {
            return ZavuWebhookResult.Ignored;
        }

        var userId = await db.Users.Where(u => u.PhoneNumber == phone).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        if (userId is null)
        {
            return ZavuWebhookResult.Ignored;
        }

        var preferences = await db.NotificationPreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (preferences is null)
        {
            db.NotificationPreferences.Add(new NotificationPreference { UserId = userId.Value, WhatsApp = false, UpdatedAt = time.GetUtcNow() });
        }
        else
        {
            preferences.WhatsApp = false;
            preferences.UpdatedAt = time.GetUtcNow();
        }

        await db.SaveChangesAsync(ct);

        // The register keeps the proof of the withdrawal: every agreement this number still had is closed, with where it happened.
        await consents.WithdrawAllAsync(userId.Value, phone, "stop", ct);
        logger.LogInformation("WhatsApp notifications switched off after a STOP reply from {Phone}", PhoneNumbers.Mask(phone));
        return ZavuWebhookResult.Processed;
    }

    private ZavuWebhookResult LogTemplate(JsonElement data)
    {
        logger.LogInformation(
            "WhatsApp template {Template} ({Language}) is now {Status}{Reason}",
            Text(data, "templateName") ?? Text(data, "name"), Text(data, "language"), Text(data, "currentStatus"),
            Text(data, "rejectionReason") is { Length: > 0 } reason ? $": {reason}" : string.Empty);
        return ZavuWebhookResult.Processed;
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
