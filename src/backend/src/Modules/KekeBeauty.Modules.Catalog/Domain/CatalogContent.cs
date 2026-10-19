using KekeBeauty.SharedKernel;

namespace KekeBeauty.Modules.Catalog.Domain;

public enum MediaKind
{
    Photo,
    Video,
}

/// <summary>Photo or short video of an establishment. Keys are relative; the public base URL is configuration.</summary>
public sealed class Media : Entity
{
    public required Guid EstablishmentId { get; set; }
    public required MediaKind Kind { get; set; }
    public required string StorageKey { get; set; }
    public string? ThumbKey { get; set; }
    public string? PosterKey { get; set; }
    public string? AltText { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? DurationSeconds { get; set; }
    public int SortOrder { get; set; }
    public bool IsCover { get; set; }
}

public enum PriceType
{
    Fixed,
    From,
}

public sealed class ServiceGroup : Entity
{
    public required Guid EstablishmentId { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>A bookable or displayed treatment (PRD SVC-01): fixed or "from" price, duration in minutes.</summary>
public sealed class Service : Entity
{
    public required Guid EstablishmentId { get; set; }
    public Guid? GroupId { get; set; }
    public required Guid CategoryId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required decimal Price { get; set; }
    public PriceType PriceType { get; set; } = PriceType.Fixed;
    public string CurrencyCode { get; set; } = "XOF";
    public required int DurationMinutes { get; set; }
    public bool IsBookable { get; set; } = true;
    public bool IsVisible { get; set; } = true;
    public int SortOrder { get; set; }
    public int PromoPercent { get; set; }
    public DateTimeOffset? PromoEndsAt { get; set; }
    public string? Perks { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public bool HasActivePromo(DateTimeOffset now) =>
        PromoPercent is > 0 and <= 90 && (PromoEndsAt is null || PromoEndsAt > now);

    public decimal EffectivePrice(DateTimeOffset now) =>
        HasActivePromo(now)
            ? decimal.Round(Price * (100 - PromoPercent) / 100m, 0, MidpointRounding.AwayFromZero)
            : Price;

    public IReadOnlyList<string> PerksList() =>
        string.IsNullOrWhiteSpace(Perks)
            ? []
            : Perks.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Daily counters feeding the partner statistics (PRD AGD-08).</summary>
public sealed class EstablishmentStatsDaily
{
    public Guid EstablishmentId { get; set; }
    public DateOnly Day { get; set; }
    public int Views { get; set; }
    public int CallClicks { get; set; }
    public int DirectionsClicks { get; set; }
}
