using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Administration.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Admin;

public sealed record LegalDocumentDto(string Slug, string Title, string Body, int Version, DateTimeOffset UpdatedAt);

public sealed record LegalDocumentAdminDto(string Slug, string Title, string Body, int Version, bool IsPublished, DateTimeOffset UpdatedAt);

public sealed record LegalDocumentSummary(string Slug, string Title, int Version, bool IsPublished, DateTimeOffset UpdatedAt);

public sealed record LegalVersionDto(int Version, string Title, bool WasPublished, DateTimeOffset SavedAt, Guid? SavedBy);

public sealed record LegalDocumentInput(string? Title, string? Body, bool Published);

/// <summary>Legal texts the admins maintain: public reading of the published version, editing with a full version history.</summary>
public sealed class LegalDocumentService(KekeBeautyDbContext db, TimeProvider time)
{
    public const int MaxBodyLength = 60000;
    public const string Placeholder = "[à compléter]";

    private static readonly Error NotFound = new("legal.not_found", "Document introuvable.");

    public async Task<LegalDocumentDto?> GetPublishedAsync(string slug, CancellationToken ct) =>
        await db.LegalDocuments.AsNoTracking()
            .Where(d => d.Slug == slug && d.IsPublished)
            .Select(d => new LegalDocumentDto(d.Slug, d.Title, d.Body, d.Version, d.UpdatedAt))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<LegalDocumentSummary>> ListAsync(CancellationToken ct) =>
        await db.LegalDocuments.AsNoTracking().OrderBy(d => d.Slug)
            .Select(d => new LegalDocumentSummary(d.Slug, d.Title, d.Version, d.IsPublished, d.UpdatedAt))
            .ToListAsync(ct);

    public async Task<Result<LegalDocumentAdminDto>> GetAsync(string slug, CancellationToken ct)
    {
        var document = await db.LegalDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Slug == slug, ct);
        return document is null
            ? Result.Failure<LegalDocumentAdminDto>(NotFound)
            : Result.Success(ToAdminDto(document));
    }

    public async Task<IReadOnlyList<LegalVersionDto>> VersionsAsync(string slug, CancellationToken ct) =>
        await db.LegalDocumentVersions.AsNoTracking().Where(v => v.Slug == slug).OrderByDescending(v => v.Version)
            .Select(v => new LegalVersionDto(v.Version, v.Title, v.WasPublished, v.SavedAt, v.SavedBy))
            .ToListAsync(ct);

    public async Task<Result<LegalDocumentAdminDto>> UpdateAsync(AdminContext admin, string slug, LegalDocumentInput input, CancellationToken ct)
    {
        var title = input.Title?.Trim() ?? string.Empty;
        var body = (input.Body ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (title.Length is < 3 or > 120)
        {
            return Result.Failure<LegalDocumentAdminDto>(AdminErrors.Invalid("Le titre doit compter de 3 à 120 caractères."));
        }

        if (body.Length is < 20 or > MaxBodyLength)
        {
            return Result.Failure<LegalDocumentAdminDto>(AdminErrors.Invalid($"Le texte doit compter de 20 à {MaxBodyLength} caractères."));
        }

        var document = await db.LegalDocuments.FirstOrDefaultAsync(d => d.Slug == slug, ct);
        if (document is null)
        {
            return Result.Failure<LegalDocumentAdminDto>(NotFound);
        }

        if (input.Published && body.Contains(Placeholder, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<LegalDocumentAdminDto>(AdminErrors.Invalid($"Le texte contient encore des mentions « {Placeholder} » : renseignez-les avant de le publier."));
        }

        var now = time.GetUtcNow();
        document.Title = title;
        document.Body = body;
        document.IsPublished = input.Published;
        document.Version++;
        document.UpdatedAt = now;
        document.UpdatedBy = admin.ActorId;
        db.LegalDocumentVersions.Add(new LegalDocumentVersion
        {
            Slug = slug,
            Version = document.Version,
            Title = title,
            Body = body,
            WasPublished = input.Published,
            SavedAt = now,
            SavedBy = admin.ActorId,
        });
        Audit.Add(db, admin, time, "legal.update", "LegalDocument", document.Id, new { slug, document.Version, input.Published });
        await db.SaveChangesAsync(ct);
        return Result.Success(ToAdminDto(document));
    }

    /// <summary>Seeds the starting texts once; existing documents (possibly edited) are never overwritten.</summary>
    public static async Task SeedAsync(KekeBeautyDbContext db, TimeProvider time, CancellationToken ct)
    {
        var existing = await db.LegalDocuments.Select(d => d.Slug).ToListAsync(ct);
        var now = time.GetUtcNow();
        foreach (var seed in LegalDefaults.All.Where(s => !existing.Contains(s.Slug)))
        {
            var body = seed.Body.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
            db.LegalDocuments.Add(new LegalDocument { Slug = seed.Slug, Title = seed.Title, Body = body, IsPublished = seed.Published, UpdatedAt = now });
            db.LegalDocumentVersions.Add(new LegalDocumentVersion { Slug = seed.Slug, Version = 1, Title = seed.Title, Body = body, WasPublished = seed.Published, SavedAt = now });
        }

        await db.SaveChangesAsync(ct);
    }

    private static LegalDocumentAdminDto ToAdminDto(LegalDocument d) => new(d.Slug, d.Title, d.Body, d.Version, d.IsPublished, d.UpdatedAt);
}
