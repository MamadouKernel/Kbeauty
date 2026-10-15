using System.Collections.Concurrent;
using System.Text.Json;
using KekeBeauty.Modules.Billing.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.Extensions.Options;

namespace KekeBeauty.Infrastructure.Billing;

/// <summary>Mobile-money operators a transfer can go to (WiniPayer identifiers) and how a payment method maps back to one.</summary>
public static class PayoutOperators
{
    public static readonly IReadOnlyList<string> All =
        ["wave-cote-divoire", "orange-money-cote-divoire", "mtn-cote-divoire", "moov-cote-divoire"];

    public static bool IsKnown(string? id) => id is not null && All.Contains(id);

    /// <summary>The operator to refund through, from the method the customer paid with; null for cards (refunded manually).</summary>
    public static string? For(PaymentMethod method) => method switch
    {
        PaymentMethod.Wave => "wave-cote-divoire",
        PaymentMethod.OrangeMoney => "orange-money-cote-divoire",
        PaymentMethod.Mtn => "mtn-cote-divoire",
        PaymentMethod.Moov => "moov-cote-divoire",
        _ => null,
    };
}

/// <summary>
/// WiniPayer standard transfer (docs.winipayer.com, API 2.0 "payout"): <c>payout/standard/create</c> then <c>payout/standard/detail/{uuid}</c>.
/// Transfers exist only in the PROD environment. A transfer counts as done only when every recipient reads <c>success</c>.
/// </summary>
public sealed class WinipayPayoutGateway(HttpClient http, IOptions<PaymentsOptions> options) : IPayoutGateway
{
    private static readonly string[] FailedStates = ["failed", "failure", "cancel", "canceled", "cancelled", "expired", "refused", "rejected", "declined", "error"];

    private WinipayOptions Settings => options.Value.Winipay;

    public string Provider => PaymentsOptions.WinipayProvider;

    public async Task<string?> SendAsync(PayoutRequest request, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(Settings.Env, "prod", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentGatewayException("Les transferts WiniPayer ne sont disponibles qu'en environnement de production.");
        }

        if (request.Amount <= 0 || request.Amount != decimal.Truncate(request.Amount))
        {
            throw new PaymentGatewayException($"WiniPayer takes whole amounts in XOF ({request.Amount}).");
        }

        var recipients = JsonSerializer.Serialize(new[] { new { name = request.AccountName, account = request.Account, amount = (long)request.Amount } });
        using var form = new MultipartFormDataContent
        {
            { new StringContent(Settings.Env), "env" },
            { new StringContent(request.Operator), "operator" },
            { new StringContent(request.Description.ReplaceLineEndings(" ")), "description" },
            { new StringContent(JsonSerializer.Serialize(new { tx = request.TransactionId })), "custom_data" },
            { new StringContent(recipients), "recipients" },
        };

        using var document = await PostAsync("payout/standard/create", form, cancellationToken);
        var root = document.RootElement;
        if (Succeeded(root) && root.TryGetProperty("results", out var results) && Text(results, "uuid") is { Length: > 0 } uuid)
        {
            return uuid;
        }

        throw new PaymentGatewayException($"WiniPayer refused the transfer ({Describe(root)}).");
    }

    public async Task<PayoutVerification> VerifyAsync(string transactionId, string? providerReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerReference))
        {
            return new PayoutVerification(PayoutState.Pending, "no provider reference");
        }

        using var form = new MultipartFormDataContent { { new StringContent(Settings.Env), "env" } };
        using var document = await PostAsync($"payout/standard/detail/{Uri.EscapeDataString(providerReference)}", form, cancellationToken);
        var root = document.RootElement;
        if (!Succeeded(root) || !root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Object)
        {
            return new PayoutVerification(PayoutState.Pending, Describe(root));
        }

        return new PayoutVerification(StateOf(results), Truncate(results.ToString()));
    }

    /// <summary>Failed if the transfer or any recipient failed; succeeded when every recipient (or, without recipients, the transfer) is <c>success</c>.</summary>
    internal static PayoutState StateOf(JsonElement transfer)
    {
        var states = new List<string>();
        if (transfer.TryGetProperty("recipients", out var recipients) && recipients.ValueKind == JsonValueKind.Array)
        {
            states.AddRange(recipients.EnumerateArray().Select(r => (Text(r, "state") ?? string.Empty).Trim().ToLowerInvariant()));
        }

        var overall = (Text(transfer, "state") ?? string.Empty).Trim().ToLowerInvariant();
        if (FailedStates.Contains(overall) || states.Any(FailedStates.Contains))
        {
            return PayoutState.Failed;
        }

        var all = states.Count > 0 ? states : [overall];
        return all.All(s => s == "success") ? PayoutState.Succeeded : PayoutState.Pending;
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
            throw new PaymentGatewayException("WiniPayer is unreachable or answered with an unreadable response.", ex);
        }
    }

    private static bool Succeeded(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;

    private static string Describe(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object
            ? $"{Text(errors, "code")} {Text(errors, "key")} {Text(errors, "msg")}".Trim()
            : "no error detail";

    private static string Truncate(string text) => text.Length <= 500 ? text : text[..500];
}

/// <summary>Development and test transfer gateway: succeeds instantly unless told otherwise. Refused in production like the fake payment gateway.</summary>
public sealed class FakePayoutGateway : IPayoutGateway
{
    private readonly ConcurrentDictionary<string, PayoutState> _transfers = new();

    public string Provider => PaymentsOptions.FakeProvider;

    /// <summary>Every transfer sent, for tests.</summary>
    public ConcurrentQueue<PayoutRequest> Sent { get; } = new();

    /// <summary>Test hook: transfers are refused at creation (provider refusal).</summary>
    public bool Refuse { get; set; }

    /// <summary>Test hook: the state a new transfer takes (Succeeded by default, Pending to simulate a slow operator).</summary>
    public PayoutState NextState { get; set; } = PayoutState.Succeeded;

    public Task<string?> SendAsync(PayoutRequest request, CancellationToken cancellationToken = default)
    {
        if (Refuse)
        {
            throw new PaymentGatewayException("fake: transfer refused");
        }

        Sent.Enqueue(request);
        _transfers[request.TransactionId] = NextState;
        return Task.FromResult<string?>($"fake-{request.TransactionId}");
    }

    public Task<PayoutVerification> VerifyAsync(string transactionId, string? providerReference, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PayoutVerification(_transfers.GetValueOrDefault(transactionId, PayoutState.Pending), "fake"));

    /// <summary>Test hook: the operator finally answers.</summary>
    public void Settle(string transactionId, PayoutState state) => _transfers[transactionId] = state;
}
