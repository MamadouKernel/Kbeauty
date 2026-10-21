using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KekeBeauty.Modules.Billing.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.Extensions.Options;

namespace KekeBeauty.Infrastructure.Billing;

/// <summary>
/// WiniPayer standard checkout (docs.winipayer.com, API 2.0). Flow: create an invoice (the answer carries the hosted payment page and
/// the invoice uuid), send the customer there, then trust only what <c>checkout/standard/detail/{uuid}</c> says.
/// The documentation does not define the callback body, so a callback is only a trigger: it carries our own transaction id and an
/// HMAC of it (keyed with the merchant token), and the outcome is always read back from WiniPayer.
/// </summary>
public sealed class WinipayGateway(HttpClient http, IOptions<PaymentsOptions> options) : IPaymentGateway
{
    /// <summary><c>state</c> values meaning that the payment will not happen (only <c>success</c> is documented; others are read as pending).</summary>
    private static readonly string[] FailedStates =
        ["failed", "failure", "cancel", "canceled", "cancelled", "expired", "refused", "rejected", "declined", "error"];

    private WinipayOptions Settings => options.Value.Winipay;

    public string Provider => PaymentsOptions.WinipayProvider;

    public string? TransactionIdFrom(IReadOnlyDictionary<string, string> fields) => fields.GetValueOrDefault("tx");

    public async Task<PaymentSession> CreateAsync(PaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0 || request.Amount != decimal.Truncate(request.Amount))
        {
            throw new PaymentGatewayException($"WiniPayer takes whole amounts in XOF ({request.Amount}).");
        }

        var separator = request.NotifyUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        using var form = new MultipartFormDataContent
        {
            { new StringContent(Settings.Env), "env" },
            { new StringContent(((long)request.Amount).ToString(CultureInfo.InvariantCulture)), "amount" },
            { new StringContent(OneLine(request.Description)), "description" },
            { new StringContent(JsonSerializer.Serialize(new { tx = request.TransactionId })), "custom_data" },
            { new StringContent("false"), "client_pay_fee" },
            { new StringContent(request.ReturnUrl + (request.ReturnUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "cancelled=1"), "cancel_url" },
            { new StringContent(request.ReturnUrl), "return_url" },
            {
                new StringContent($"{request.NotifyUrl}{separator}tx={Uri.EscapeDataString(request.TransactionId)}&sig={Sign(request.TransactionId)}"),
                "callback_url"
            },
        };

        using var document = await PostAsync("checkout/standard/create", form, cancellationToken);
        var root = document.RootElement;
        if (Succeeded(root)
            && root.TryGetProperty("results", out var results)
            && results.ValueKind == JsonValueKind.Object
            && Text(results, "checkout_process") is { Length: > 0 } pageUrl)
        {
            return new PaymentSession(pageUrl, Text(results, "uuid"));
        }

        Telemetry.ProviderErrors.Add(1, new KeyValuePair<string, object?>("provider", Provider), new KeyValuePair<string, object?>("operation", "refused"));
        throw new PaymentGatewayException($"WiniPayer refused the invoice ({Describe(root)}).");
    }

    public async Task<PaymentVerification> VerifyAsync(string transactionId, string? providerReference = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerReference))
        {
            // The invoice was never created at WiniPayer: nothing can have been paid.
            return new PaymentVerification(GatewayStatus.Pending, 0, string.Empty, null, "no provider reference");
        }

        using var form = new MultipartFormDataContent { { new StringContent(Settings.Env), "env" } };
        using var document = await PostAsync($"checkout/standard/detail/{Uri.EscapeDataString(providerReference)}", form, cancellationToken);
        var root = document.RootElement;
        if (!Succeeded(root)
            || !root.TryGetProperty("results", out var results)
            || !results.TryGetProperty("invoice", out var invoice)
            || invoice.ValueKind != JsonValueKind.Object)
        {
            // "Invalid or missing invoice" (unknown or expired): treated as not paid; the reconciliation retries and gives up after a week.
            return new PaymentVerification(GatewayStatus.Pending, 0, string.Empty, null, Truncate(Describe(root)));
        }

        var state = Text(invoice, "state")?.Trim().ToLowerInvariant() ?? string.Empty;
        var status = state == "success"
            ? GatewayStatus.Accepted
            : FailedStates.Contains(state) ? GatewayStatus.Refused : GatewayStatus.Pending;
        // amount_init is what we asked for; amount may include fees the customer chose to carry.
        decimal.TryParse(Text(invoice, "amount_init") ?? Text(invoice, "amount"), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount);
        var currency = (Text(invoice, "currency") ?? string.Empty).ToUpperInvariant();
        var operatorId = Text(invoice, "operator");
        return new PaymentVerification(status, amount, currency, operatorId, Truncate($"state={state}; operator={operatorId}; ref={Text(invoice, "operator_ref")}"));
    }

    public bool IsNotificationAuthentic(IReadOnlyDictionary<string, string> fields, string? token)
    {
        var transactionId = TransactionIdFrom(fields);
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(transactionId) || string.IsNullOrEmpty(Settings.Token))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Sign(transactionId)), Encoding.UTF8.GetBytes(token.Trim().ToLowerInvariant()));
    }

    /// <summary>HMAC-SHA256 (hex) of our transaction id, keyed with the merchant token: proves a callback URL is one we issued.</summary>
    public string Sign(string transactionId) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Settings.Token), Encoding.UTF8.GetBytes(transactionId)));

    public static PaymentMethod MapMethod(string? operatorId)
    {
        var id = operatorId?.Trim().ToLowerInvariant() ?? string.Empty;
        return id switch
        {
            _ when id.Contains("wave", StringComparison.Ordinal) => PaymentMethod.Wave,
            _ when id.Contains("orange", StringComparison.Ordinal) => PaymentMethod.OrangeMoney,
            _ when id.Contains("mtn", StringComparison.Ordinal) => PaymentMethod.Mtn,
            _ when id.Contains("moov", StringComparison.Ordinal) => PaymentMethod.Moov,
            _ when id.Contains("visa", StringComparison.Ordinal) || id.Contains("master", StringComparison.Ordinal) || id.Contains("card", StringComparison.Ordinal) => PaymentMethod.Card,
            _ => PaymentMethod.Other,
        };
    }

    private async Task<JsonDocument> PostAsync(string path, HttpContent content, CancellationToken ct)
    {
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, $"{Settings.BaseUrl.TrimEnd('/')}/{path}") { Content = content };
            message.Headers.Add("X-Merchant-Apply", Settings.ApplyKey);
            message.Headers.Add("X-Merchant-Token", Settings.Token);
            message.Headers.Accept.ParseAdd("application/json");
            using var response = await http.SendAsync(message, ct);
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            Telemetry.ProviderErrors.Add(1, new KeyValuePair<string, object?>("provider", Provider), new KeyValuePair<string, object?>("operation", path.Split('/')[1]));
            throw new PaymentGatewayException("WiniPayer is unreachable or answered with an unreadable response.", ex);
        }
    }

    private static bool Succeeded(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;

    private static string Describe(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
        {
            return $"{Text(errors, "code")} {Text(errors, "key")} {Text(errors, "msg")}".Trim();
        }

        return "no error detail";
    }

    private static string OneLine(string text) => string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string Truncate(string text) => text.Length <= 500 ? text : text[..500];
}
