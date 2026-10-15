using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KekeBeauty.SharedKernel;
using Microsoft.Extensions.Options;

namespace KekeBeauty.Infrastructure.Notifications;

public sealed record LeTextoResult(bool Success, string? Id = null, string? Error = null);

/// <summary>
/// Typed client of the LeTexto client API (https://apis.letexto.com/v1): single SMS, e-mail campaign, sender creation,
/// message status and balance. Authentication is a Bearer API key; errors come back as {statusCode, message, error}.
/// </summary>
public sealed class LeTextoClient(HttpClient http, IOptions<LeTextoOptions> options)
{
    private LeTextoOptions Settings => options.Value;

    /// <summary>POST /messages/send. <c>to</c> is the number in international form without the plus sign.</summary>
    public async Task<LeTextoResult> SendSmsAsync(string phone, string message, string? deliveryReportUrl, CancellationToken ct)
    {
        var to = PhoneNumbers.ToE164(phone);
        if (to is null)
        {
            return new LeTextoResult(false, Error: "Numéro de téléphone invalide.");
        }

        var body = new Dictionary<string, string>
        {
            ["from"] = Settings.SmsSender,
            ["to"] = PhoneNumbers.Digits(to),
            ["content"] = message,
        };
        if (deliveryReportUrl is not null)
        {
            body["dlrUrl"] = deliveryReportUrl;
            body["dlrMethod"] = "POST";
        }

        var (ok, json) = await PostAsync("/messages/send", body, ct);
        return Parse(ok, json, "id");
    }

    /// <summary>POST /campaigns/emails with a single recipient (the API has no single-message e-mail endpoint).</summary>
    public async Task<LeTextoResult> SendEmailAsync(string to, string subject, string content, CancellationToken ct)
    {
        var body = new Dictionary<string, object>
        {
            ["label"] = $"Keke Beauty {DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}",
            ["From"] = Settings.EmailFrom,
            ["Recipients"] = new[] { to },
            ["Subject"] = subject,
            ["content"] = content,
        };
        var (ok, json) = await PostAsync("/campaigns/emails", body, ct);
        return Parse(ok, json, "id");
    }

    /// <summary>POST /senders/create: registers a sender name (it still has to be approved by LeTexto).</summary>
    public async Task<LeTextoResult> CreateSenderAsync(string name, CancellationToken ct)
    {
        var (ok, json) = await PostAsync("/senders/create", new Dictionary<string, string> { ["name"] = name }, ct);
        return Parse(ok, json, "id");
    }

    /// <summary>GET /users/balance?token=. The shape of the answer is not documented, so the raw JSON is returned for display.</summary>
    public async Task<LeTextoResult> BalanceAsync(CancellationToken ct)
    {
        var (ok, json) = await GetAsync($"/users/balance?token={Uri.EscapeDataString(Settings.ApiKey)}", ct);
        return ok ? new LeTextoResult(true, json) : Parse(false, json, "id");
    }

    /// <summary>GET /messages/{id}/status?token=. Returns the raw answer, the status text is not documented.</summary>
    public async Task<LeTextoResult> MessageStatusAsync(string id, CancellationToken ct)
    {
        var (ok, json) = await GetAsync($"/messages/{Uri.EscapeDataString(id)}/status?token={Uri.EscapeDataString(Settings.ApiKey)}", ct);
        return ok ? new LeTextoResult(true, json) : Parse(false, json, "id");
    }

    private async Task<(bool Ok, string Body)> PostAsync(string path, object payload, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url(path)) { Content = JsonContent.Create(payload) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Settings.ApiKey);
        return await SendAsync(request, ct);
    }

    private async Task<(bool Ok, string Body)> GetAsync(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url(path));
        return await SendAsync(request, ct);
    }

    private async Task<(bool Ok, string Body)> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await http.SendAsync(request, ct);
        return (response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(ct));
    }

    private string Url(string path) => Settings.BaseUrl.TrimEnd('/') + path;

    /// <summary>Success looks like {"statut":"submitted","id":"..."} (or {"status":"CREATED","id":11}); failures carry a message (string or array).</summary>
    internal static LeTextoResult Parse(bool httpOk, string json, string idProperty)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!httpOk || (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("statusCode", out var code) && code.ValueKind == JsonValueKind.Number && code.GetInt32() >= 400))
            {
                return new LeTextoResult(false, Error: Describe(root));
            }

            var id = root.ValueKind == JsonValueKind.Object && root.TryGetProperty(idProperty, out var value)
                ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText()
                : null;
            return new LeTextoResult(true, id);
        }
        catch (JsonException)
        {
            return new LeTextoResult(false, Error: httpOk ? "Réponse inattendue de LeTexto." : "Erreur de LeTexto (réponse illisible).");
        }
    }

    private static string Describe(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("message", out var message))
        {
            return "Erreur de LeTexto.";
        }

        return message.ValueKind switch
        {
            JsonValueKind.Array => string.Join(" ", message.EnumerateArray().Select(m => m.GetString())),
            JsonValueKind.String => message.GetString() ?? "Erreur de LeTexto.",
            _ => "Erreur de LeTexto.",
        };
    }
}
