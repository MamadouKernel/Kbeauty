using KekeBeauty.SharedKernel;
using NetTopologySuite.Geometries;

namespace KekeBeauty.Modules.Catalog.Domain;

public enum EstablishmentStatus
{
    Draft,
    PendingReview,
    Active,
    Suspended,
    Rejected,
}

/// <summary>A beauty business listed in the directory (PRD §8.2). Only <see cref="EstablishmentStatus.Active"/> ones are public.</summary>
public enum EstablishmentPaymentMode
{
    /// <summary>Clients pay on the spot (the default).</summary>
    None,

    /// <summary>A percentage of the price is paid online to book; the rest on the spot.</summary>
    Deposit,

    /// <summary>The whole price is paid online to book.</summary>
    Full,
}

public sealed class Establishment : Entity
{
    /// <summary>Owning partner account; null for demonstration data.</summary>
    public Guid? OwnerUserId { get; set; }

    public required string Name { get; set; }
    public required string Slug { get; set; }
    public required string Description { get; set; }
    public required string CustomerServicePhone { get; set; }
    public string? Email { get; set; }

    /// <summary>Plain identifier: the Geography module is never navigated from here.</summary>
    public required Guid CommuneId { get; set; }

    public required string AddressLine { get; set; }

    /// <summary>WGS84 position (X = longitude, Y = latitude), stored as <c>geography(Point,4326)</c>.</summary>
    public required Point Location { get; set; }

    /// <summary>Derived by the database from <see cref="Location"/> (generated columns): read-only here.</summary>
    public double Latitude { get; private set; }

    /// <inheritdoc cref="Latitude"/>
    public double Longitude { get; private set; }

    public string Timezone { get; set; } = "Africa/Abidjan";
    public EstablishmentStatus Status { get; set; } = EstablishmentStatus.Draft;
    public bool IsFeatured { get; set; }
    public bool BookingEnabled { get; set; }

    /// <summary>Granted by the subscription plan (<c>booking</c> flag); booking is effective only when also enabled by the owner.</summary>
    public bool BookingAllowed { get; set; }

    /// <summary>Booking settings (PRD AGD-07): confirm requests automatically, lead time, horizon, cancellation window, grid step.</summary>
    public bool AutoConfirm { get; set; }

    public int MinLeadMinutes { get; set; } = 120;
    public int MaxHorizonDays { get; set; } = 60;
    public int CancelLimitMinutes { get; set; } = 120;
    public int SlotStepMinutes { get; set; } = 15;

    /// <summary>Online payment of appointments (effective only when the admins switched the feature on).</summary>
    public EstablishmentPaymentMode PaymentMode { get; set; } = EstablishmentPaymentMode.None;

    public int DepositPercent { get; set; } = 30;

    /// <summary>Where the establishment's share is paid out: a mobile-money operator id (wave-cote-divoire...), the account number and its holder.</summary>
    public string? PayoutOperator { get; set; }

    public string? PayoutAccount { get; set; }
    public string? PayoutAccountName { get; set; }

    /// <summary>
    /// How clients pay the establishment for the service itself. The platform is not involved: cash on the spot, or mobile money sent
    /// straight to the accounts below. (Only the subscription to Keke Beauty goes through the payment provider.)
    /// </summary>
    public bool AcceptsCash { get; set; } = true;

    public List<MobileMoneyAccount> MobileMoney { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<EstablishmentCategory> Categories { get; } = [];
}

/// <summary>A mobile-money account clients may pay the establishment on (operator id such as wave-cote-divoire, E.164 number, holder).</summary>
public sealed class MobileMoneyAccount
{
    public required string Operator { get; set; }
    public required string Account { get; set; }
    public required string HolderName { get; set; }
}

public sealed class EstablishmentCategory
{
    public Guid EstablishmentId { get; set; }
    public Guid CategoryId { get; set; }
}
