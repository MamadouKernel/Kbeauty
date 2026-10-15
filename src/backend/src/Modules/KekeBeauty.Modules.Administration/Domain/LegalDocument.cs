using KekeBeauty.SharedKernel;

namespace KekeBeauty.Modules.Administration.Domain;

/// <summary>
/// A legal text shown to visitors (terms of use, privacy policy, legal notice) and edited by the admin team.
/// The body is a small subset of Markdown: <c>## </c> headings, <c>- </c> list items and blank-line separated paragraphs.
/// </summary>
public sealed class LegalDocument : Entity
{
    public required string Slug { get; init; }
    public required string Title { get; set; }
    public required string Body { get; set; }

    /// <summary>Incremented at every save; each version is kept in <see cref="LegalDocumentVersion"/>.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Unpublished documents are not shown to visitors (the legal notice starts that way until it is filled in).</summary>
    public bool IsPublished { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>Immutable copy of a legal text at the time it was saved, so that what visitors saw can always be proven.</summary>
public sealed class LegalDocumentVersion : Entity
{
    public required string Slug { get; init; }
    public required int Version { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public required bool WasPublished { get; init; }
    public required DateTimeOffset SavedAt { get; init; }
    public Guid? SavedBy { get; init; }
}
