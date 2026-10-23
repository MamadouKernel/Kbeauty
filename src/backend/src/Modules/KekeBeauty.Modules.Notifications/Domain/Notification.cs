using KekeBeauty.SharedKernel;

namespace KekeBeauty.Modules.Notifications.Domain;

public enum NotificationChannel
{
    InApp,
    Sms,
    WhatsApp,
    Push,

    /// <summary>E-mail through the SMTP relay (MailKit).</summary>
    Email,
}

public enum DeliveryStatus
{
    Pending,
    Sent,

    /// <summary>Confirmed by the provider's delivery report (SMS).</summary>
    Delivered,

    Failed,
}

/// <summary>
/// One message to one user on one channel (PRD §8.2 <c>notifications</c>). In-app rows are the notification centre;
/// SMS rows double as the delivery log with the provider outcome.
/// </summary>
public sealed class Notification : Entity
{
    public required Guid UserId { get; init; }
    public required NotificationChannel Channel { get; init; }
    public required string TemplateCode { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;
    public int Attempts { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}

/// <summary>A browser or installed web app that agreed to receive Web Push notifications for a user.</summary>
public sealed class PushSubscription : Entity
{
    public required Guid UserId { get; init; }

    /// <summary>The push service URL: unique, and the key by which a re-subscription replaces the previous row.</summary>
    public required string Endpoint { get; set; }

    public required string P256dh { get; set; }
    public required string Auth { get; set; }
    public string? UserAgent { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSuccessAt { get; set; }
}

/// <summary>What a user accepts to receive. Without a row everything is allowed; the admins' channel switches still apply first.</summary>
public sealed class NotificationPreference
{
    public required Guid UserId { get; init; }
    public bool Push { get; set; } = true;
    public bool Sms { get; set; } = true;
    public bool WhatsApp { get; set; } = true;
    public bool Email { get; set; } = true;

    /// <summary>Reminders before an appointment (the in-app notification is always kept).</summary>
    public bool Reminders { get; set; } = true;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
