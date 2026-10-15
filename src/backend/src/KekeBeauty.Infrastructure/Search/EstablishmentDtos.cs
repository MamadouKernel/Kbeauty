namespace KekeBeauty.Infrastructure.Search;

public sealed record CategoryRef(string Name, string Slug);

/// <summary>Card shown in search results (list and map).</summary>
public sealed record EstablishmentSummary(
    Guid Id,
    string Slug,
    string Name,
    string? CoverUrl,
    IReadOnlyList<CategoryRef> Categories,
    string CommuneName,
    string CityName,
    double? DistanceKm,
    double Latitude,
    double Longitude,
    decimal? StartingPrice,
    string? CurrencyCode,
    bool IsFeatured,
    bool BookingAvailable,
    string Phone,
    string AddressLine,
    int ServiceCount,
    double? Rating = null,
    int ReviewCount = 0);

public sealed record EstablishmentPage(IReadOnlyList<EstablishmentSummary> Items, int Total, int Page, int PageSize);

public sealed record MediaItem(string Kind, string Url, string? PosterUrl, string? Alt, int? DurationSeconds);

public sealed record ServiceItem(Guid Id, string Name, string? Description, decimal Price, string PriceType, string CurrencyCode, int DurationMinutes);

/// <summary>Services under a heading; <see cref="Name"/> is null for services without a group.</summary>
public sealed record ServiceGroupItem(string? Name, IReadOnlyList<ServiceItem> Services);

/// <summary>Opening range; <see cref="DayOfWeek"/> follows .NET (0 = Sunday … 6 = Saturday), times are <c>HH:mm</c> local time.</summary>
public sealed record OpeningHourItem(int DayOfWeek, string OpensAt, string ClosesAt);

public sealed record EstablishmentDetails(
    Guid Id,
    string Slug,
    string Name,
    string Description,
    IReadOnlyList<CategoryRef> Categories,
    string Phone,
    string? Email,
    string AddressLine,
    string CommuneName,
    string CityName,
    string RegionName,
    double Latitude,
    double Longitude,
    string Timezone,
    bool BookingAvailable,
    bool IsFeatured,
    IReadOnlyList<MediaItem> Media,
    IReadOnlyList<ServiceGroupItem> ServiceGroups,
    IReadOnlyList<OpeningHourItem> OpeningHours,
    AcceptedPaymentsInfo AcceptedPayments,
    double? Rating = null,
    int ReviewCount = 0);

/// <summary>How a client pays the establishment: cash, and/or mobile money straight to these accounts (the platform is not involved).</summary>
public sealed record AcceptedPaymentsInfo(bool Cash, IReadOnlyList<MobileMoneyInfo> MobileMoney);

public sealed record MobileMoneyInfo(string Operator, string Account, string HolderName);

public sealed record EstablishmentSearchCriteria(
    string? Category,
    Guid? CityId,
    Guid? CommuneId,
    double? Latitude,
    double? Longitude,
    double? RadiusKm,
    string? Text,
    bool? Bookable,
    string? Sort,
    int Page,
    int PageSize);

public enum EstablishmentEventKind
{
    View,
    Call,
    Directions,
}
