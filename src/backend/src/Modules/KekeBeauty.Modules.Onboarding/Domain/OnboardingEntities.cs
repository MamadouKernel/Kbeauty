using KekeBeauty.SharedKernel;

namespace KekeBeauty.Modules.Onboarding.Domain;

public enum IdentityDocumentType
{
    NationalId,
    Passport,
    DrivingLicense,
    ConsularCard,
}

/// <summary>Review state shared by the owner's identity file and each establishment submission (PRD ONB-02).</summary>
public enum KycStatus
{
    Draft,
    PendingReview,
    Approved,
    Rejected,
    ChangesRequested,
}

public enum DocumentSide
{
    Front,
    Back,
    Selfie,
}

/// <summary>
/// Identity of a partner owner (PRD §8.2 <c>kyc_identities</c>). One per user: once approved it is reused for every
/// establishment the owner creates (ONB-04). Document files live in the private bucket; only their keys are stored.
/// </summary>
public sealed class KycIdentity : Entity
{
    public required Guid UserId { get; set; }
    public string HolderName { get; set; } = string.Empty;
    public IdentityDocumentType DocumentType { get; set; } = IdentityDocumentType.NationalId;
    public string DocumentNumber { get; set; } = string.Empty;
    public DateOnly? DocumentExpiry { get; set; }
    public string? FrontKey { get; set; }
    public string? BackKey { get; set; }
    public string? SelfieKey { get; set; }
    public KycStatus Status { get; set; } = KycStatus.Draft;
    public DateTimeOffset? SubmittedAt { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }

    /// <summary>When the document images were deleted at the end of the retention period set by the admins; null while they are kept.</summary>
    public DateTimeOffset? DocumentsPurgedAt { get; set; }

    /// <summary>Rejection reason or requested corrections, shown to the partner.</summary>
    public string? ReviewComment { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public bool IsEditable => Status is KycStatus.Draft or KycStatus.ChangesRequested or KycStatus.Rejected;

    public bool HasRequiredDocuments => FrontKey is not null && BackKey is not null;

    public string? KeyOf(DocumentSide side) => side switch
    {
        DocumentSide.Front => FrontKey,
        DocumentSide.Back => BackKey,
        _ => SelfieKey,
    };
}

/// <summary>One submission of an establishment to the admin team (PRD §8.2 <c>establishment_reviews</c>).</summary>
public sealed class EstablishmentReview : Entity
{
    public required Guid EstablishmentId { get; set; }
    public KycStatus Status { get; set; } = KycStatus.PendingReview;
    public string? Comment { get; set; }
    public DateTimeOffset SubmittedAt { get; init; } = DateTimeOffset.UtcNow;
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}
