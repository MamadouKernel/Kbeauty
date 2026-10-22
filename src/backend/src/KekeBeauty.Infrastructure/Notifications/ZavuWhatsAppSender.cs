using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using KekeBeauty.SharedKernel;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KekeBeauty.Infrastructure.Notifications;

/// <summary>
/// Sends WhatsApp template messages through Zavu (<c>POST /v1/messages</c>, API v1). A template is looked up by name among the
/// templates Meta has approved; a missing or unapproved template fails visibly. Failures are returned, never thrown: the caller
/// records them on the notification and may fall back to SMS.
/// </summary>
public sealed partial class ZavuWhatsAppSender(HttpClient http, IMemoryCache cache, IOptions<ZavuOptions> options, ILogger<ZavuWhatsAppSender> logger) : IWhatsAppSender
{
    private const int MaxTemplatePages = 5;

    /// <summary>WhatsApp refuses template parameters with line breaks, tabs or runs of more than four spaces.</summary>
    [GeneratedRegex(@"[\r\n\t]+| {5,}")]
    private static partial Regex ForbiddenWhitespace();

    private ZavuOptions Settings => options.Value;

    public async Task<WhatsAppResult> SendTemplateAsync(string phone, string template, string language, IReadOnlyList<string> parameters, bool withCodeButton, CancellationToken cancellationToken = default)
    {
        if (!Settings.HasApiKey)
        {
            Telemetry.WhatsAppSends.Add(1, new KeyValuePair<string, object?>("outcome", "failed"));
            return new WhatsAppResult(false, Error: "Zavu n'est pas configuré (clé d'API absente).");
        }

        try
        {
            var templateId = await ResolveTemplateAsync(template, language, cancellationToken);
            if (templateId is null)
            {
                Telemetry.WhatsAppSends.Add(1, new KeyValuePair<string, object?>("outcome", "template_missing"));
                return new WhatsAppResult(false, Error: $"Le modèle WhatsApp « {template} » n'est pas approuvé chez Meta.");
            }

            // Positional variables, as Zavu submits templates to Meta: "1" is the first {{1}}. An authentication template's
            // copy-code button repeats the first variable on its own (Zavu documents no separate button value for OTP).
            var variables = parameters.Select((value, index) => (Key: (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), Value: Clean(value)))
                .ToDictionary(p => p.Key, p => p.Value);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
            {
                Content = JsonContent.Create(new
                {
                    to = phone,
                    channel = "whatsapp",
                    messageType = "template",
                    content = new { templateId, templateVariables = variables },
                    // A one-time code must not silently turn into an SMS the user did not ask for.
                    fallbackEnabled = false,
                }),
            };
            using var response = await http.SendAsync(request, cancellationToken);
            return await ReadAnswerAsync(response, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Zavu is unreachable");
            Telemetry.WhatsAppSends.Add(1, new KeyValuePair<string, object?>("outcome", "failed"));
            return new WhatsAppResult(false, Error: "Zavu est momentanément injoignable.");
        }
    }

    /// <summary>Values go into a template body: no line break, tab or long run of spaces, and a sane length.</summary>
    public static string Clean(string value)
    {
        var cleaned = ForbiddenWhitespace().Replace(value ?? string.Empty, " · ").Trim();
        return cleaned.Length > 1000 ? cleaned[..1000] : cleaned;
    }

    private async Task<WhatsAppResult> ReadAnswerAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string? body = null;
        try
        {
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException)
        {
        }

        if (response.IsSuccessStatusCode)
        {
            var id = Find(body, "message", "id");
            if (id is null)
            {
                logger.LogWarning("Zavu accepted a message without returning its id");
            }

            Telemetry.WhatsAppSends.Add(1, new KeyValuePair<string, object?>("outcome", "sent"));
            return new WhatsAppResult(true, id);
        }

        var code = Find(body, "code") ?? Find(body, "error", "code") ?? ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture);
        logger.LogWarning("Zavu refused a WhatsApp message: HTTP {Status}, code {Code}", (int)response.StatusCode, code);

        // The Free plan's monthly allowance (a2p_limit_exceeded) and the daily ceiling both answer 429: the alert is on this outcome.
        var outcome = response.StatusCode == HttpStatusCode.TooManyRequests ? "quota_exceeded" : "failed";
        Telemetry.WhatsAppSends.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        Telemetry.ProviderErrors.Add(1, new KeyValuePair<string, object?>("provider", "zavu"), new KeyValuePair<string, object?>("operation", "send"));
        return new WhatsAppResult(false, Error: Describe(response.StatusCode, code));
    }

    private static string Describe(HttpStatusCode status, string code) => code switch
    {
        "a2p_limit_exceeded" => "Le quota mensuel WhatsApp de Zavu est atteint.",
        "daily_limit_exceeded" => "Le plafond quotidien WhatsApp de Zavu est atteint.",
        "whatsapp_window_closed" => "La fenêtre de 24 h WhatsApp est fermée : un modèle approuvé est nécessaire.",
        "sending_suspended" => "L'envoi est suspendu chez Zavu : contactez leur support.",
        _ when status == HttpStatusCode.Unauthorized => "La clé d'API Zavu est refusée.",
        _ when status == HttpStatusCode.Forbidden => "Zavu refuse cet envoi (destinataire non autorisé en mode test, ou compte restreint).",
        _ => $"Zavu a refusé le message (code {code}).",
    };

    private async Task<string?> ResolveTemplateAsync(string name, string language, CancellationToken ct)
    {
        var key = $"zavu:template:{name}:{language}";
        if (cache.TryGetValue(key, out string? known) && known is not null)
        {
            return known;
        }

        string? cursor = null;
        for (var page = 0; page < MaxTemplatePages; page++)
        {
            var url = "/v1/templates?limit=100" + (cursor is null ? string.Empty : "&cursor=" + Uri.EscapeDataString(cursor));
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Zavu template list refused: HTTP {Status}", (int)response.StatusCode);
                return null;
            }

            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = json.RootElement;
            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (Text(item, "name") == name && Text(item, "status") == "approved" && Text(item, "language") is { } lang
                        && lang.StartsWith(language, StringComparison.OrdinalIgnoreCase) && Text(item, "id") is { Length: > 0 } id)
                    {
                        cache.Set(key, id, TimeSpan.FromMinutes(Math.Max(1, Settings.TemplateCacheMinutes)));
                        return id;
                    }
                }
            }

            cursor = Text(root, "nextCursor");
            if (string.IsNullOrEmpty(cursor))
            {
                break;
            }
        }

        return null;
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? Find(string? body, params string[] path)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(body);
            var current = json.RootElement;
            foreach (var step in path)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(step, out current))
                {
                    return null;
                }
            }

            return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Configures the typed client: base address, bearer key and the optional sender header.</summary>
    public static void Configure(HttpClient client, ZavuOptions settings)
    {
        client.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        if (settings.SenderId.Length > 0)
        {
            client.DefaultRequestHeaders.Add("Zavu-Sender", settings.SenderId);
        }
    }
}
