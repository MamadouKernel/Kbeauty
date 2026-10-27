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

public enum ConsentPurpose
{
    /// <summary>Confirmations, reminders and other messages about the person's own appointments (WhatsApp utility templates).</summary>
    Service,

    /// <summary>Invitations and offers (WhatsApp marketing templates): a separate, explicit agreement.</summary>
    Marketing,
}

/// <summary>
/// One entry of the WhatsApp consent register: someone agreed to, or withdrew from, a kind of WhatsApp message for one number. The
/// register is append-only (the proof Meta may ask for): each row keeps the exact text that was shown, its version, when and where.
/// The current state is the latest row for the user, number and purpose.
/// </summary>
public sealed class WhatsAppConsent : Entity
{
    public required Guid UserId { get; init; }

    /// <summary>The number the agreement is about, E.164.</summary>
    public required string Phone { get; init; }

    public required ConsentPurpose Purpose { get; init; }
    public required bool Granted { get; init; }
    public required string TextVersion { get; init; }

    /// <summary>The exact wording the person agreed to (a later change of the text never rewrites the proof).</summary>
    public required string TextSnapshot { get; init; }

    /// <summary>Where it happened: settings (the person ticked or unticked the box), stop (replied STOP).</summary>
    public required string Source { get; init; }

    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
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
