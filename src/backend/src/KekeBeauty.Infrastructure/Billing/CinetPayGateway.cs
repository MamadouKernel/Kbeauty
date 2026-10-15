using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KekeBeauty.Modules.Billing.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.Extensions.Options;

namespace KekeBeauty.Infrastructure.Billing;

/// <summary>
/// CinetPay v2 (Mobile Money Côte d'Ivoire and cards). Written from the public documentation; it has not been run
/// against the sandbox yet (no keys were available), so exercise it with test credentials before going live.
/// </summary>
public sealed class CinetPayGateway(HttpClient http, IOptions<PaymentsOptions> options) : IPaymentGateway
{
    /// <summary>Order of the notification fields signed by CinetPay (HMAC-SHA256, header <c>x-token</c>).</summary>
    private static readonly string[] SignedFields =
    [
        "cpm_site_id", "cpm_trans_id", "cpm_trans_date", "cpm_amount", "cpm_currency", "signature", "payment_method",
        "cel_phone_num", "cpm_phone_prefixe", "cpm_language", "cpm_version", "cpm_payment_config", "cpm_page_action",
        "cpm_custom", "cpm_designation", "cpm_error_message",
    ];

    private CinetPayOptions Settings => options.Value.CinetPay;

    public string Provider => PaymentsOptions.CinetPayProvider;

    /// <summary>CinetPay refuses amounts that are not a multiple of 5 (and below 100 in XOF).</summary>
    public static bool IsAcceptedAmount(decimal amount) => amount >= MinimumAmount && amount % 5 == 0;

    private const int MinimumAmount = 100;

    /// <summary>The documentation advises against <c>#</c>, <c>/</c>, <c>$</c>, <c>_</c> and <c>&amp;</c> in values: establishment names may hold them.</summary>
    public static string Clean(string text, int maxLength = 255)
    {
        var cleaned = new string(text.Select(c => c is '#' or '/' or '$' or '_' or '&' or '\r' or '\n' ? ' ' : c).ToArray());
        cleaned = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }

    public async Task<PaymentSession> CreateAsync(PaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsAcceptedAmount(request.Amount))
        {
            throw new PaymentGatewayException($"CinetPay only accepts amounts that are multiples of 5 and at least {MinimumAmount} ({request.Amount}).");
        }

        var (first, last) = SplitName(Clean(request.CustomerName));
        var body = new Dictionary<string, object>
        {
            ["apikey"] = Settings.ApiKey,
            ["site_id"] = Settings.SiteId,
            ["transaction_id"] = request.TransactionId,
            ["amount"] = (long)Math.Round(request.Amount, MidpointRounding.AwayFromZero),
            ["currency"] = request.Currency,
            ["description"] = Clean(request.Description),
            ["notify_url"] = request.NotifyUrl,
            ["return_url"] = request.ReturnUrl,
            ["channels"] = "ALL",
            ["lang"] = "fr",
            ["customer_name"] = first,
            ["customer_surname"] = last,
            ["customer_email"] = request.CustomerEmail,
            ["customer_phone_number"] = request.CustomerPhone,
            ["customer_address"] = "Abidjan",
            ["customer_city"] = "Abidjan",
            ["customer_country"] = "CI",
            ["customer_state"] = "CI",
            ["customer_zip_code"] = "00225",
        };

        using var document = await PostAsync("payment", body, cancellationToken);
        var root = document.RootElement;
        if (root.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("payment_url", out var url)
            && url.GetString() is { Length: > 0 } paymentUrl)
        {
            return new PaymentSession(paymentUrl);
        }

        throw new PaymentGatewayException($"CinetPay refused the transaction ({Describe(root)}).");
    }

    public string? TransactionIdFrom(IReadOnlyDictionary<string, string> fields) => fields.GetValueOrDefault("cpm_trans_id");

    public async Task<PaymentVerification> VerifyAsync(string transactionId, string? providerReference = null, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object>
        {
            ["apikey"] = Settings.ApiKey,
            ["site_id"] = Settings.SiteId,
            ["transaction_id"] = transactionId,
        };

        using var document = await PostAsync("payment/check", body, cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            // "Transaction not found" and similar: nothing was paid (yet).
            return new PaymentVerification(GatewayStatus.Pending, 0, string.Empty, null, root.GetRawText());
        }

        var status = Text(data, "status")?.ToUpperInvariant() switch
        {
            "ACCEPTED" => GatewayStatus.Accepted,
            "REFUSED" or "CANCELED" or "CANCELLED" => GatewayStatus.Refused,
            _ => GatewayStatus.Pending,
        };
        decimal.TryParse(Text(data, "amount"), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount);
        return new PaymentVerification(status, amount, Text(data, "currency") ?? string.Empty, Text(data, "payment_method"), root.GetRawText());
    }

    public bool IsNotificationAuthentic(IReadOnlyDictionary<string, string> fields, string? token)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(Settings.SecretKey))
        {
            return false;
        }

        var expected = ComputeToken(Settings.SecretKey, fields);
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(token.Trim().ToLowerInvariant()));
    }

    /// <summary>HMAC-SHA256 (hex, lower case) of the concatenated notification fields.</summary>
    public static string ComputeToken(string secret, IReadOnlyDictionary<string, string> fields)
    {
        var data = string.Concat(SignedFields.Select(name => fields.GetValueOrDefault(name, string.Empty)));
        return Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(data)));
    }

    public static PaymentMethod MapMethod(string? code) => code?.Trim().ToUpperInvariant() switch
    {
        "OM" or "OMCI" or "ORANGE" or "ORANGEMONEY" => PaymentMethod.OrangeMoney,
        "MOOV" or "FLOOZ" => PaymentMethod.Moov,
        "MTN" or "MOMO" => PaymentMethod.Mtn,
        "WAVE" or "WAVECI" => PaymentMethod.Wave,
        "VISA" or "MASTERCARD" or "CARD" or "CREDIT_CARD" => PaymentMethod.Card,
        _ => PaymentMethod.Other,
    };

    private async Task<JsonDocument> PostAsync(string path, object body, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync($"{Settings.BaseUrl.TrimEnd('/')}/{path}", body, ct);
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            throw new PaymentGatewayException("CinetPay is unreachable or answered with an unreadable response.", ex);
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString() : null;

    private static string Describe(JsonElement root) => $"{Text(root, "code")} {Text(root, "message")}".Trim();

    private static (string First, string Last) SplitName(string name)
    {
        var parts = name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 ? (parts[0], parts[1]) : (parts.FirstOrDefault() ?? "Client", "Keke");
    }
}
