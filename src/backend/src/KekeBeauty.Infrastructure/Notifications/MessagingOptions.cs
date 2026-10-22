namespace KekeBeauty.Infrastructure.Notifications;

/// <summary>
/// LeTexto (Arolitec) messaging gateway: SMS and e-mail. It is configured but <b>off</b> until <c>LeTexto:Enabled</c> is
/// switched on, and the admins still decide per channel whether messages are sent (back-office, Paramètres).
/// </summary>
public sealed class LeTextoOptions
{
    public const string SectionName = "LeTexto";
    public const int MaxSenderLength = 11;

    /// <summary>Master switch. While false the log-only senders are used, whatever the other values are.</summary>
    public bool Enabled { get; init; }

    /// <summary>API key generated in the LeTexto application (section "API développeur"). Never committed.</summary>
    public string ApiKey { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = "https://apis.letexto.com/v1";

    /// <summary>SMS sender name (at most 11 characters). LeTexto senders are created in the LeTexto application or by POST /senders/create.</summary>
    public string SmsSender { get; init; } = string.Empty;

    /// <summary>E-mail sender address of the e-mail campaigns.</summary>
    public string EmailFrom { get; init; } = string.Empty;

    /// <summary>Public base URL of this API (for example https://api.kekebeauty.example): LeTexto posts delivery reports there.</summary>
    public string PublicApiUrl { get; init; } = string.Empty;

    /// <summary>Shared secret carried by the delivery-report URL (<c>?sig=</c>): LeTexto does not sign its reports.</summary>
    public string WebhookSecret { get; init; } = string.Empty;

    public bool HasApiKey => ApiKey.Length > 0;

    public bool SmsReady => Enabled && HasApiKey && SmsSender.Length is > 0 and <= MaxSenderLength;

    public bool EmailReady => Enabled && HasApiKey && EmailFrom.Contains('@', StringComparison.Ordinal);

    /// <summary>Delivery reports need both a public URL and a secret; without them statuses stay at "sent".</summary>
    public string? DeliveryReportUrl => PublicApiUrl.Length > 0 && WebhookSecret.Length > 0
        ? $"{PublicApiUrl.TrimEnd('/')}/api/v1/webhooks/sms/letexto?sig={Uri.EscapeDataString(WebhookSecret)}"
        : null;
}

/// <summary>
/// WhatsApp provider selection. <c>Log</c> (default) only logs; <c>Zavu</c> sends approved Meta templates through Zavu (https://docs.zavu.dev).
/// LeTexto's API documentation has no WhatsApp endpoint.
/// </summary>
public sealed class WhatsAppOptions
{
    public const string SectionName = "WhatsApp";

    public string Provider { get; init; } = "Log";
    public string Language { get; init; } = "fr";

    /// <summary>Authentication template: one body parameter (the code) and a copy-code button.</summary>
    public string OtpTemplate { get; init; } = "keke_otp";

    /// <summary>Utility template with two body parameters: title and message.</summary>
    public string NotificationTemplate { get; init; } = "keke_notification";

    public bool IsConfigured => false;
}

/// <summary>
/// Zavu (WhatsApp through the Meta Cloud API). The API key is a secret (<c>.env</c> or the secrets directory, never the repository).
/// <c>zv_test_</c> keys use Zavu's sandbox number and only reach phones verified in the Zavu dashboard; <c>zv_live_</c> keys use the
/// connected WhatsApp Business number.
/// </summary>
public sealed class ZavuOptions
{
    public const string SectionName = "Zavu";
    public const string TestKeyPrefix = "zv_test_";

    public string ApiKey { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = "https://api.zavu.dev";

    /// <summary>Optional <c>Zavu-Sender</c> header: the sender (WhatsApp account) to use instead of the project's default.</summary>
    public string SenderId { get; init; } = string.Empty;

    /// <summary>Signing secret of the webhook (shown once by Zavu): without it every webhook call is refused.</summary>
    public string WebhookSecret { get; init; } = string.Empty;

    /// <summary>How long a resolved template id is remembered.</summary>
    public int TemplateCacheMinutes { get; init; } = 10;

    public bool HasApiKey => ApiKey.Length > 0;

    public bool IsTestKey => ApiKey.StartsWith(TestKeyPrefix, StringComparison.Ordinal);
}

/// <summary>Web Push settings. Keys are optional: without them a key pair is generated once and kept in the database.</summary>
public sealed class PushOptions
{
    public const string SectionName = "Push";

    public string PublicKey { get; init; } = string.Empty;
    public string PrivateKey { get; init; } = string.Empty;
    public string Subject { get; init; } = "mailto:contact@kekebeauty.example";
}
