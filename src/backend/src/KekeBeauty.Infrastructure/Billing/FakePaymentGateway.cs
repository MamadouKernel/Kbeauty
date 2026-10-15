using System.Collections.Concurrent;
using KekeBeauty.SharedKernel;
using Microsoft.Extensions.Options;

namespace KekeBeauty.Infrastructure.Billing;

/// <summary>
/// Development and test gateway: payments are "made" on a simulated page (or through the simulate endpoint) and then
/// confirmed through the same notification and verification path as the real provider. Refused in production.
/// </summary>
public sealed class FakePaymentGateway(IOptions<PaymentsOptions> options) : IPaymentGateway
{
    private sealed record Transaction(decimal Amount, string Currency, GatewayStatus Status, string Method);

    private readonly ConcurrentDictionary<string, Transaction> _transactions = new();

    public string Provider => PaymentsOptions.FakeProvider;

    public Task<PaymentSession> CreateAsync(PaymentRequest request, CancellationToken cancellationToken = default)
    {
        _transactions[request.TransactionId] = new Transaction(request.Amount, request.Currency, GatewayStatus.Pending, "Wave");
        var url = $"{options.Value.WebBaseUrl.TrimEnd('/')}/paiement/simulation?tx={Uri.EscapeDataString(request.TransactionId)}&back={Uri.EscapeDataString(request.ReturnUrl)}";
        return Task.FromResult(new PaymentSession(url));
    }

    public string? TransactionIdFrom(IReadOnlyDictionary<string, string> fields) => fields.GetValueOrDefault("cpm_trans_id");

    public Task<PaymentVerification> VerifyAsync(string transactionId, string? providerReference = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(_transactions.TryGetValue(transactionId, out var transaction)
            ? new PaymentVerification(transaction.Status, transaction.Amount, transaction.Currency, transaction.Method, "fake")
            : new PaymentVerification(GatewayStatus.Pending, 0, string.Empty, null, "fake: unknown transaction"));

    public bool IsNotificationAuthentic(IReadOnlyDictionary<string, string> fields, string? token) =>
        token is not null && token == options.Value.FakeSecret;

    /// <summary>What the customer did on the simulated payment page.</summary>
    public bool Settle(string transactionId, GatewayStatus status)
    {
        if (!_transactions.TryGetValue(transactionId, out var transaction))
        {
            return false;
        }

        _transactions[transactionId] = transaction with { Status = status };
        return true;
    }

    /// <summary>Test hook: pretend the provider charged a different amount than requested.</summary>
    public void OverrideAmount(string transactionId, decimal amount)
    {
        if (_transactions.TryGetValue(transactionId, out var transaction))
        {
            _transactions[transactionId] = transaction with { Amount = amount };
        }
    }
}
