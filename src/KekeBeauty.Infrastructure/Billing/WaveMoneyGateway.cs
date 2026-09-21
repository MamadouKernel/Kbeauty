using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KekeBeauty.Application.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KekeBeauty.Infrastructure.Billing;

public sealed class WaveMoneyGateway : IWaveMoneyGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly string? _signingSecret;
    private readonly ILogger<WaveMoneyGateway> _logger;

    public WaveMoneyGateway(HttpClient httpClient, IConfiguration configuration, ILogger<WaveMoneyGateway> logger)
    {
        _httpClient = httpClient;
        _apiKey = configuration["Payments:Wave:ApiKey"];
        _signingSecret = configuration["Payments:Wave:SigningSecret"];
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public Task<WaveOperationResult> RefundCheckoutAsync(string checkoutId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(checkoutId))
            return Task.FromResult(new WaveOperationResult(false, "checkout_id_required"));

        return SendAsync(HttpMethod.Post, $"v1/checkout/sessions/{Uri.EscapeDataString(checkoutId)}/refund", null, null, cancellationToken);
    }

    public Task<WaveOperationResult> CreatePayoutAsync(string mobile, string name, decimal amount, string clientReference, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (amount <= 0 || string.IsNullOrWhiteSpace(mobile) || string.IsNullOrWhiteSpace(idempotencyKey))
            return Task.FromResult(new WaveOperationResult(false, "invalid_payout"));

        var body = JsonSerializer.Serialize(new
        {
            currency = "XOF",
            receive_amount = amount.ToString("0.##", CultureInfo.InvariantCulture),
            mobile,
            name,
            client_reference = clientReference,
            payment_reason = "Pourboire Keke Beauty"
        }, JsonOptions);

        return SendAsync(HttpMethod.Post, "v1/payout", body, idempotencyKey, cancellationToken);
    }

    private async Task<WaveOperationResult> SendAsync(HttpMethod method, string path, string? body, string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey)) return WaveOperationResult.NotConfigured();

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        if (!string.IsNullOrWhiteSpace(idempotencyKey)) request.Headers.TryAddWithoutValidation("idempotency-key", idempotencyKey);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        AddSignature(request, body ?? string.Empty);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Wave {Path} a echoue ({StatusCode}): {Response}", path, (int)response.StatusCode, responseBody);
                return new(false, "wave_rejected", Error: responseBody);
            }

            string? reference = null;
            string status = "succeeded";
            if (!string.IsNullOrWhiteSpace(responseBody))
            {
                using var json = JsonDocument.Parse(responseBody);
                if (json.RootElement.TryGetProperty("id", out var id)) reference = id.GetString();
                if (json.RootElement.TryGetProperty("status", out var state)) status = state.GetString() ?? status;
            }
            return new(status is "succeeded" or "success" or "processing", status, reference);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogError(ex, "Erreur de communication avec Wave pour {Path}", path);
            return new(false, "wave_unavailable", Error: ex.Message);
        }
    }

    private void AddSignature(HttpRequestMessage request, string body)
    {
        if (string.IsNullOrWhiteSpace(_signingSecret)) return;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var bytes = HMACSHA256.HashData(Encoding.UTF8.GetBytes(_signingSecret), Encoding.UTF8.GetBytes(timestamp + body));
        request.Headers.TryAddWithoutValidation("Wave-Signature", $"t={timestamp},v1={Convert.ToHexString(bytes).ToLowerInvariant()}");
    }
}
