using KekeBeauty.SharedKernel;

namespace KekeBeauty.Modules.Catalog.Domain;

public enum ReviewStatus
{
    Published,

    /// <summary>Removed from the public page by an administrator (abuse, insult, off-topic); kept for the audit trail.</summary>
    Hidden,
}

/// <summary>
/// A client's opinion on an establishment. Only a client whose appointment was completed can write one, and only once for that
/// appointment, so every review stands for a real visit.
/// </summary>
public sealed class Review : Entity
{
    public required Guid EstablishmentId { get; init; }
    public required Guid UserId { get; init; }
    public required Guid AppointmentId { get; init; }

    /// <summary>1 to 5 stars.</summary>
    public required int Rating { get; init; }

    public string? Comment { get; init; }
    public ReviewStatus Status { get; set; } = ReviewStatus.Published;
    public string? HiddenReason { get; set; }

    /// <summary>The establishment's public answer.</summary>
    public string? ReplyText { get; set; }

    public DateTimeOffset? RepliedAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public enum ReviewPhotoKind
{
    Before,
    After,
    Other,
}

/// <summary>A photo a client attached to her review, usually the before and the after of a treatment.</summary>
public sealed class ReviewPhoto : Entity
{
    public required Guid ReviewId { get; init; }
    public required ReviewPhotoKind Kind { get; init; }
    public required string StorageKey { get; init; }
    public string? ThumbKey { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>An establishment a client saved to find again.</summary>
public sealed class Favorite
{
    public Guid UserId { get; set; }
    public Guid EstablishmentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
