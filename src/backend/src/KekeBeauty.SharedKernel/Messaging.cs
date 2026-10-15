namespace KekeBeauty.SharedKernel;

public sealed record SmsResult(bool Success, string? ProviderMessageId = null, string? Error = null);

/// <summary>SMS port (PRD §10.1). Development uses a logging implementation; Infobip or Twilio plug in here.</summary>
public interface ISmsSender
{
    Task<SmsResult> SendAsync(string phone, string message, CancellationToken cancellationToken = default);
}

public sealed record WhatsAppResult(bool Success, string? ProviderMessageId = null, string? Error = null);

/// <summary>
/// WhatsApp Business port: messages outside a 24-hour customer window must be pre-approved templates, so the sender
/// takes a template name and its body parameters. <paramref name="withCodeButton"/> marks an authentication template
/// whose "copy code" button repeats the first parameter.
/// </summary>
public interface IWhatsAppSender
{
    Task<WhatsAppResult> SendTemplateAsync(string phone, string template, string language, IReadOnlyList<string> parameters, bool withCodeButton, CancellationToken cancellationToken = default);
}

public sealed record PushSubscriptionInfo(string Endpoint, string P256dh, string Auth);

public sealed record PushMessage(string Title, string Body, string Url, string Tag);

public enum PushOutcome
{
    Sent,

    /// <summary>The browser dropped the subscription (HTTP 404/410): it must be deleted.</summary>
    Gone,

    Failed,
}

/// <summary>Web Push (RFC 8030 with VAPID): delivers a notification to a browser or installed web app.</summary>
public interface IPushSender
{
    Task<PushOutcome> SendAsync(PushSubscriptionInfo subscription, PushMessage message, CancellationToken cancellationToken = default);
}

public sealed record NotificationPayload(Guid Id, string Title, string Body, string TemplateCode, DateTimeOffset CreatedAt);

public static class AppointmentEvents
{
    public const string Requested = "AppointmentRequested";
    public const string Cancelled = "AppointmentCancelled";
    public const string Updated = "AppointmentUpdated";
}

/// <summary>
/// Pushes live updates to connected browsers (SignalR hubs, PRD §9). The Api project provides the real
/// implementation; everywhere else (tests, background tools) the no-op below is used.
/// </summary>
public interface IRealtimeNotifier
{
    Task SlotsChangedAsync(Guid establishmentId, DateOnly date, CancellationToken cancellationToken = default);

    Task AppointmentChangedAsync(Guid establishmentId, string kind, Guid appointmentId, CancellationToken cancellationToken = default);

    Task NotificationReceivedAsync(Guid userId, NotificationPayload notification, CancellationToken cancellationToken = default);
}

public sealed class NullRealtimeNotifier : IRealtimeNotifier
{
    public Task SlotsChangedAsync(Guid establishmentId, DateOnly date, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task AppointmentChangedAsync(Guid establishmentId, string kind, Guid appointmentId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotificationReceivedAsync(Guid userId, NotificationPayload notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
