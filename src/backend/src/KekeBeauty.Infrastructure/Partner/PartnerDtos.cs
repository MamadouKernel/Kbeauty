namespace KekeBeauty.Infrastructure.Partner;

// ───────── Establishment ─────────

/// <summary>Steps 1 and 2 of the onboarding wizard: basic information and location (PRD ONB-01).</summary>
public sealed record EstablishmentInput(
    string Name,
    string Description,
    string CustomerServicePhone,
    string? Email,
    IReadOnlyList<Guid> CategoryIds,
    Guid CommuneId,
    string AddressLine,
    double Latitude,
    double Longitude);

public sealed record OpeningHourInput(int DayOfWeek, string OpensAt, string ClosesAt);

public sealed record MissingItem(string Code, string Label);

public sealed record PartnerEstablishmentSummary(
    Guid Id,
    string Slug,
    string Name,
    string Status,
    string? ReviewComment,
    string? CoverUrl,
    IReadOnlyList<MissingItem> Missing);

public sealed record PartnerServiceItem(
    Guid Id,
    Guid? GroupId,
    Guid CategoryId,
    string Name,
    string? Description,
    decimal Price,
    string PriceType,
    string CurrencyCode,
    int DurationMinutes,
    bool IsBookable,
    bool IsVisible,
    int PromoPercent = 0,
    DateTimeOffset? PromoEndsAt = null,
    decimal EffectivePrice = 0,
    IReadOnlyList<string>? Perks = null);

public sealed record PartnerGroupItem(Guid Id, string Name);

public sealed record PartnerMediaItem(Guid Id, string Url, string? ThumbUrl, bool IsCover, int SortOrder, string Kind = "Photo", string? PosterUrl = null, int? DurationSeconds = null);

public sealed record ReviewInfo(string Status, string? Comment, DateTimeOffset SubmittedAt, DateTimeOffset? ReviewedAt);

public sealed record PartnerEstablishmentDetail(
    Guid Id,
    string Slug,
    string Name,
    string Status,
    bool IsEditable,
    string Description,
    string CustomerServicePhone,
    string? Email,
    IReadOnlyList<Guid> CategoryIds,
    Guid RegionId,
    Guid CityId,
    Guid CommuneId,
    string AddressLine,
    double Latitude,
    double Longitude,
    IReadOnlyList<KekeBeauty.Infrastructure.Search.OpeningHourItem> OpeningHours,
    IReadOnlyList<PartnerGroupItem> Groups,
    IReadOnlyList<PartnerServiceItem> Services,
    IReadOnlyList<PartnerMediaItem> Media,
    int MaxPhotos,
    ReviewInfo? LatestReview,
    IReadOnlyList<MissingItem> Missing,
    IReadOnlyList<PartnerMediaItem>? Videos = null,
    bool VideosAllowed = false,
    int MaxVideos = 3,
    int? MaxServices = null);

public sealed record ServiceInput(
    string Name,
    string? Description,
    decimal Price,
    string PriceType,
    int DurationMinutes,
    Guid CategoryId,
    Guid? GroupId,
    bool IsBookable,
    bool IsVisible,
    int PromoPercent = 0,
    DateTimeOffset? PromoEndsAt = null,
    IReadOnlyList<string>? Perks = null);

public sealed record GroupInput(string Name);

public sealed record OrderInput(IReadOnlyList<Guid> Ids);

// ───────── KYC ─────────

public sealed record KycInput(string HolderName, string DocumentType, string DocumentNumber, DateOnly? DocumentExpiry);

public sealed record KycView(
    string Status,
    bool IsEditable,
    string HolderName,
    string DocumentType,
    string DocumentNumber,
    DateOnly? DocumentExpiry,
    bool HasFront,
    bool HasBack,
    bool HasSelfie,
    string? ReviewComment);

// ───────── Admin review ─────────

public sealed record QueueItem(
    Guid ReviewId,
    Guid EstablishmentId,
    string EstablishmentName,
    string OwnerName,
    string OwnerEmail,
    string CommuneName,
    string Status,
    DateTimeOffset SubmittedAt);

public sealed record QueuePage(IReadOnlyList<QueueItem> Items, int Total, int Page, int PageSize);

public sealed record KycCheck(string Code, string Level, string Message);

public sealed record ReviewIdentity(
    string Status,
    string HolderName,
    string DocumentType,
    string DocumentNumber,
    DateOnly? DocumentExpiry,
    bool HasFront,
    bool HasBack,
    bool HasSelfie);

public sealed record ReviewDetail(
    Guid ReviewId,
    string Status,
    string? Comment,
    DateTimeOffset SubmittedAt,
    Guid EstablishmentId,
    string EstablishmentName,
    string Description,
    string CustomerServicePhone,
    string? Email,
    string AddressLine,
    string CommuneName,
    string CityName,
    double Latitude,
    double Longitude,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> PhotoUrls,
    string OwnerName,
    string OwnerEmail,
    ReviewIdentity? Identity,
    IReadOnlyList<KycCheck> Checks);

public sealed record DecisionInput(string Decision, string? Comment);
