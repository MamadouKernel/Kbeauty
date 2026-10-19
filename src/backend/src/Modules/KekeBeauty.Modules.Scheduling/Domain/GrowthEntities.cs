using KekeBeauty.SharedKernel;

namespace KekeBeauty.Modules.Scheduling.Domain;

public enum WaitlistStatus
{
    /// <summary>The client waits to be told that a slot freed up on that day.</summary>
    Waiting,

    /// <summary>A slot freed up and the client was told; the entry is spent.</summary>
    Notified,

    /// <summary>The client left the list.</summary>
    Cancelled,
}

/// <summary>
/// A client who found no free slot on a day asks to be told when one frees up there (an appointment of that day is cancelled,
/// refused or expires). Everyone waiting is told at once: the first one to book gets the slot.
/// </summary>
public sealed class WaitlistEntry : Entity
{
    public required Guid EstablishmentId { get; init; }
    public required Guid ClientUserId { get; init; }

    /// <summary>The day wanted, in the establishment's time zone.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>The service the client was looking for, for the establishment's information only.</summary>
    public Guid? ServiceId { get; init; }

    public WaitlistStatus Status { get; set; } = WaitlistStatus.Waiting;
    public DateTimeOffset? NotifiedAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// The loyalty card of an establishment: points earned on each finished visit, a reward once the threshold is reached.
/// The establishment grants the reward itself (it is paid directly), and records it so the points are spent.
/// </summary>
public sealed class LoyaltyProgram : Entity
{
    public required Guid EstablishmentId { get; init; }
    public bool IsActive { get; set; }

    /// <summary>Points earned for every full 1 000 of the price of a finished visit.</summary>
    public int PointsPer1000 { get; set; }

    /// <summary>Points needed for one reward.</summary>
    public int RewardThreshold { get; set; }

    /// <summary>What the client gets, in the establishment's words ("-5 000 F sur votre prochaine prestation").</summary>
    public required string RewardDescription { get; set; }

    /// <summary>Visits that started before this moment earn nothing: the card starts when the program is first switched on.</summary>
    public DateTimeOffset? ActiveSince { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public enum LoyaltyTransactionKind
{
    Earn,
    Redeem,
}

/// <summary>One line of a client's loyalty card in one establishment; the balance is the sum of the points.</summary>
public sealed class LoyaltyTransaction : Entity
{
    public required Guid EstablishmentId { get; init; }
    public required Guid ClientUserId { get; init; }
    public required LoyaltyTransactionKind Kind { get; init; }

    /// <summary>Positive when earned, negative when spent.</summary>
    public required int Points { get; init; }

    /// <summary>The finished visit that earned the points (one earning per visit).</summary>
    public Guid? AppointmentId { get; init; }

    public string? Note { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public enum GiftCardStatus
{
    Active,
    Cancelled,
}

/// <summary>
/// A gift card sold by an establishment (which collects the money itself): a code, an amount, and a balance spent over one or
/// more visits until it expires. The platform only keeps the books.
/// </summary>
public sealed class GiftCard : Entity
{
    public required Guid EstablishmentId { get; init; }

    /// <summary>What is printed on the card and typed at the counter, e.g. <c>KB-7HQ4-M2XP</c>.</summary>
    public required string Code { get; init; }

    public required decimal InitialAmount { get; init; }
    public decimal Balance { get; set; }
    public string CurrencyCode { get; init; } = "XOF";
    public required string RecipientName { get; init; }
    public string? BuyerName { get; init; }
    public string? Message { get; init; }
    public GiftCardStatus Status { get; set; } = GiftCardStatus.Active;
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>The client who added the card to her account, to follow its balance.</summary>
    public Guid? HolderUserId { get; set; }

    public required Guid CreatedByUserId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>An amount taken off a gift card at the counter.</summary>
public sealed class GiftCardRedemption : Entity
{
    public required Guid GiftCardId { get; init; }
    public required decimal Amount { get; init; }
    public Guid? AppointmentId { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public enum GroupEventType
{
    Wedding,
    Birthday,
    Baptism,
    Corporate,
    Other,
}

public enum GroupBookingStatus
{
    /// <summary>Waiting for the establishment's answer.</summary>
    Pending,

    /// <summary>The establishment agreed, with a quote and a message.</summary>
    Accepted,

    Declined,
    CancelledByClient,
}

/// <summary>
/// A request to welcome a group (a wedding, a birthday): too many people and services for the slot grid, so it is a
/// conversation — the client asks, the establishment answers with a quote or declines.
/// </summary>
public sealed class GroupBookingRequest : Entity
{
    public required Guid EstablishmentId { get; init; }
    public required Guid ClientUserId { get; init; }
    public required GroupEventType EventType { get; init; }
    public required DateTimeOffset DesiredStartsAt { get; init; }
    public required int GroupSize { get; init; }
    public required string Services { get; init; }
    public string? Message { get; init; }
    public required string Phone { get; init; }
    public GroupBookingStatus Status { get; set; } = GroupBookingStatus.Pending;
    public decimal? QuotedPrice { get; set; }
    public string? PartnerMessage { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
