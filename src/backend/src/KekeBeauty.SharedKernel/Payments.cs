namespace KekeBeauty.SharedKernel;

public sealed record PaymentRequest(
    string TransactionId,
    decimal Amount,
    string Currency,
    string Description,
    string CustomerName,
    string CustomerEmail,
    string CustomerPhone,
    string ReturnUrl,
    string NotifyUrl);

/// <summary>Where to send the customer, plus the provider's own identifier of the transaction when it has one (needed to query it later).</summary>
public sealed record PaymentSession(string PaymentUrl, string? ProviderReference = null);

public enum GatewayStatus
{
    Pending,
    Accepted,
    Refused,
}

/// <summary>What the provider itself says about a transaction (server-to-server), never what the browser claims.</summary>
public sealed record PaymentVerification(GatewayStatus Status, decimal Amount, string Currency, string? Method, string? Raw);

/// <summary>Raised when the provider cannot be reached or refuses to create a transaction.</summary>
public sealed class PaymentGatewayException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Payment aggregator port (PRD §10.2): WiniPayer or CinetPay in production, a simulated gateway in development.</summary>
public interface IPaymentGateway
{
    /// <summary>Lower-case provider key used in webhook URLs and stored with each payment.</summary>
    string Provider { get; }

    Task<PaymentSession> CreateAsync(PaymentRequest request, CancellationToken cancellationToken = default);

    /// <param name="transactionId">Our identifier, sent when the payment was created.</param>
    /// <param name="providerReference">The identifier the provider returned at creation, if any.</param>
    Task<PaymentVerification> VerifyAsync(string transactionId, string? providerReference = null, CancellationToken cancellationToken = default);

    /// <summary>Which of our transactions a notification (form fields and query string merged) is about.</summary>
    string? TransactionIdFrom(IReadOnlyDictionary<string, string> fields);

    /// <summary>Checks the signature of an incoming notification (fields as posted, token from the request header).</summary>
    bool IsNotificationAuthentic(IReadOnlyDictionary<string, string> fields, string? token);
}

public sealed record PayoutRequest(string TransactionId, string Operator, string AccountName, string Account, decimal Amount, string Description);

public enum PayoutState
{
    Pending,
    Succeeded,
    Failed,
}

/// <summary>What the provider says about a transfer (server-to-server).</summary>
public sealed record PayoutVerification(PayoutState State, string? Raw);

/// <summary>
/// Money-out port: refunds to a customer and payouts to an establishment, both mobile-money transfers. WiniPayer's transfer API
/// in production, an instant simulated one in development and tests.
/// </summary>
public interface IPayoutGateway
{
    string Provider { get; }

    /// <returns>The provider's own identifier of the transfer, used later to read its state.</returns>
    Task<string?> SendAsync(PayoutRequest request, CancellationToken cancellationToken = default);

    Task<PayoutVerification> VerifyAsync(string transactionId, string? providerReference, CancellationToken cancellationToken = default);
}
