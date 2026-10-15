using KekeBeauty.SharedKernel;

namespace KekeBeauty.Modules.Billing.Domain;

public enum AppointmentPaymentKind
{
    /// <summary>A percentage of the price, paid to book.</summary>
    Deposit,

    /// <summary>The whole price, paid to book.</summary>
    Full,
}

public enum AppointmentPaymentStatus
{
    /// <summary>The client was sent to the payment page and has not paid yet.</summary>
    Awaiting,

    Paid,

    /// <summary>The provider refused the payment.</summary>
    Failed,

    /// <summary>The client never paid within the hold: the slot was released.</summary>
    Expired,

    /// <summary>A refund is due and its transfer has been started.</summary>
    RefundPending,

    Refunded,

    /// <summary>The refund transfer failed; an admin retries it from the back-office.</summary>
    RefundFailed,
}

/// <summary>
/// What a client pays online to book an appointment (deposit or full price): the money collected by the platform for an establishment.
/// The platform fee is frozen at payment time; <see cref="NetAmount"/> is what the establishment will be paid out.
/// </summary>
public sealed class AppointmentPayment : Entity
{
    public required Guid AppointmentId { get; init; }
    public required Guid EstablishmentId { get; init; }
    public required Guid ClientUserId { get; init; }
    public required AppointmentPaymentKind Kind { get; init; }

    /// <summary>What the client pays.</summary>
    public required decimal Amount { get; init; }

    public string CurrencyCode { get; init; } = "XOF";
    public decimal PlatformFee { get; init; }
    public decimal NetAmount { get; init; }
    public AppointmentPaymentStatus Status { get; set; } = AppointmentPaymentStatus.Awaiting;

    /// <summary>The gateway transaction (<see cref="Payment"/>) the client pays through.</summary>
    public Guid? PaymentId { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PaidAt { get; set; }

    /// <summary>The hosted payment page of the current gateway transaction, so a client who left can come back to it.</summary>
    public string? PaymentUrl { get; set; }

    /// <summary>Set once the establishment's share has been included in a payout.</summary>
    public Guid? PayoutId { get; set; }

    public string? RefundTransactionId { get; set; }
    public string? RefundProviderReference { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public string? RefundError { get; set; }
}

public enum PayoutStatus
{
    Pending,
    Succeeded,
    Failed,
}

/// <summary>A transfer of the money owed to an establishment (the sum of the net amounts of its completed, paid appointments).</summary>
public sealed class Payout : Entity
{
    public required Guid EstablishmentId { get; init; }
    public required decimal Amount { get; set; }
    public string CurrencyCode { get; init; } = "XOF";
    public required string Operator { get; init; }
    public required string Account { get; init; }
    public required string AccountName { get; init; }
    public required string Provider { get; init; }
    public required string TransactionId { get; init; }
    public string? ProviderReference { get; set; }
    public PayoutStatus Status { get; set; } = PayoutStatus.Pending;
    public string? Error { get; set; }
    public Guid? CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
