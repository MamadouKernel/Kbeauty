using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Billing.Domain;
using KekeBeauty.Modules.Catalog.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace KekeBeauty.Infrastructure.Admin;

public sealed record AdminEstablishmentDto(
    Guid Id,
    string Name,
    string Slug,
    string Status,
    string? OwnerEmail,
    string PlanCode,
    bool IsFeatured,
    bool BookingAllowed,
    DateTimeOffset CreatedAt);

public sealed record AdminMediaDto(Guid Id, string Url, bool IsCover, string? AltText, string Kind = "Photo");

public sealed record AdminEstablishmentDetail(
    AdminEstablishmentDto Establishment,
    IReadOnlyList<AdminMediaDto> Media,
    IReadOnlyList<AuditEntryDto> History);

public sealed record AdminEstablishmentFilter(string? Query, string? Status);

/// <summary>Moderation of listings: suspend or hide a file, remove a non-compliant photo, keep the history (PRD ADM-02).</summary>
public sealed class AdminModerationService(
    KekeBeautyDbContext db,
    AuditQueryService audit,
    NotificationService notifications,
    IFileStorage storage,
    IConfiguration configuration,
    TimeProvider time)
{
    private readonly string _mediaBaseUrl = MediaUrls.BaseUrl(configuration);

    public async Task<PageOf<AdminEstablishmentDto>> ListAsync(AdminEstablishmentFilter filter, int? page, int? pageSize, CancellationToken ct)
    {
        var (number, size) = AdminPaging.Normalize(page, pageSize);
        var query = db.Establishments.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var like = AdminPaging.Like(filter.Query);
            query = query.Where(e => EF.Functions.ILike(e.Name, like) || EF.Functions.ILike(e.Slug, like));
        }

        if (Enum.TryParse<EstablishmentStatus>(filter.Status, ignoreCase: true, out var status))
        {
            query = query.Where(e => e.Status == status);
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(e => e.CreatedAt).ThenBy(e => e.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
        return new PageOf<AdminEstablishmentDto>(await ToDtosAsync(rows, ct), total, number, size);
    }

    public async Task<Result<AdminEstablishmentDetail>> GetAsync(Guid establishmentId, CancellationToken ct)
    {
        var e = await db.Establishments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == establishmentId, ct);
        if (e is null)
        {
            return Result.Failure<AdminEstablishmentDetail>(AdminErrors.EstablishmentNotFound);
        }

        var media = (await db.MediaItems.AsNoTracking().Where(m => m.EstablishmentId == establishmentId)
                .OrderBy(m => m.Kind == MediaKind.Video).ThenBy(m => m.SortOrder).ToListAsync(ct))
            .Select(m => new AdminMediaDto(m.Id, MediaUrls.Build(_mediaBaseUrl, m.ThumbKey ?? m.PosterKey ?? m.StorageKey), m.IsCover, m.AltText, m.Kind.ToString()))
            .ToList();
        var history = await audit.ListAsync(new AuditFilter(null, "Establishment", establishmentId, null, null, null), 1, 50, ct);
        return Result.Success(new AdminEstablishmentDetail((await ToDtosAsync([e], ct))[0], media, history.Items));
    }

    public Task<Result> SuspendAsync(AdminContext admin, Guid establishmentId, string? reason, CancellationToken ct) =>
        ChangeStatusAsync(admin, establishmentId, reason, suspend: true, ct);

    public Task<Result> ReactivateAsync(AdminContext admin, Guid establishmentId, string? reason, CancellationToken ct) =>
        ChangeStatusAsync(admin, establishmentId, reason, suspend: false, ct);

    public async Task<Result> RemoveMediaAsync(AdminContext admin, Guid establishmentId, Guid mediaId, string? rawReason, CancellationToken ct)
    {
        var reason = AdminErrors.CleanReason(rawReason);
        if (reason is null)
        {
            return Result.Failure(AdminErrors.ReasonRequired);
        }

        var establishment = await db.Establishments.AsNoTracking().Where(e => e.Id == establishmentId)
            .Select(e => new { e.Name, e.OwnerUserId }).FirstOrDefaultAsync(ct);
        if (establishment is null)
        {
            return Result.Failure(AdminErrors.EstablishmentNotFound);
        }

        var photos = await db.MediaItems.Where(m => m.EstablishmentId == establishmentId)
            .OrderBy(m => m.SortOrder).ToListAsync(ct);
        var media = photos.FirstOrDefault(m => m.Id == mediaId);
        if (media is null)
        {
            return Result.Failure(AdminErrors.MediaNotFound);
        }

        db.MediaItems.Remove(media);
        if (media.IsCover && photos.FirstOrDefault(m => m.Id != mediaId && m.Kind == MediaKind.Photo) is { } next)
        {
            next.IsCover = true;
        }

        Audit.Add(db, admin, time, "establishment.media.remove", "Establishment", establishmentId, new { mediaId, reason });
        await db.SaveChangesAsync(ct);
        await DeleteQuietlyAsync(media.StorageKey, media.ThumbKey, media.PosterKey);

        await NotifyOwnerAsync(
            establishment.OwnerUserId,
            "moderation.media_removed",
            media.Kind == MediaKind.Video ? "Vidéo retirée" : "Photo retirée",
            $"Un média de « {establishment.Name} » a été retiré par la modération. Motif : {reason}",
            ct);
        return Result.Success();
    }

    private async Task<Result> ChangeStatusAsync(AdminContext admin, Guid establishmentId, string? rawReason, bool suspend, CancellationToken ct)
    {
        var reason = AdminErrors.CleanReason(rawReason);
        if (reason is null)
        {
            return Result.Failure(AdminErrors.ReasonRequired);
        }

        var e = await db.Establishments.FirstOrDefaultAsync(x => x.Id == establishmentId, ct);
        if (e is null)
        {
            return Result.Failure(AdminErrors.EstablishmentNotFound);
        }

        var allowed = suspend ? e.Status == EstablishmentStatus.Active : e.Status == EstablishmentStatus.Suspended;
        if (!allowed)
        {
            return Result.Failure(AdminErrors.InvalidTransition);
        }

        e.Status = suspend ? EstablishmentStatus.Suspended : EstablishmentStatus.Active;
        Audit.Add(db, admin, time, suspend ? "establishment.suspend" : "establishment.reactivate", "Establishment", establishmentId, new { reason });
        await db.SaveChangesAsync(ct);

        await NotifyOwnerAsync(
            e.OwnerUserId,
            suspend ? "moderation.suspended" : "moderation.reactivated",
            suspend ? "Fiche suspendue" : "Fiche rétablie",
            suspend
                ? $"« {e.Name} » n'est plus visible dans l'annuaire. Motif : {reason}"
                : $"« {e.Name} » est de nouveau visible dans l'annuaire.",
            ct);
        return Result.Success();
    }

    private async Task<List<AdminEstablishmentDto>> ToDtosAsync(IReadOnlyList<Establishment> rows, CancellationToken ct)
    {
        var ids = rows.Select(r => r.Id).ToList();
        var ownerIds = rows.Where(r => r.OwnerUserId != null).Select(r => r.OwnerUserId!.Value).Distinct().ToList();
        var owners = await db.Users.AsNoTracking().Where(u => ownerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, ct);
        var plans = await (
            from s in db.Subscriptions.AsNoTracking()
            where ids.Contains(s.EstablishmentId) && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing)
            join p in db.Plans.AsNoTracking() on s.PlanId equals p.Id
            select new { s.EstablishmentId, p.Code })
            .ToListAsync(ct);
        var planByEstablishment = plans.GroupBy(x => x.EstablishmentId).ToDictionary(g => g.Key, g => g.First().Code);

        return rows.Select(e => new AdminEstablishmentDto(
            e.Id,
            e.Name,
            e.Slug,
            e.Status.ToString(),
            e.OwnerUserId is { } owner ? owners.GetValueOrDefault(owner) : null,
            planByEstablishment.GetValueOrDefault(e.Id) ?? EntitlementCodes.Free,
            e.IsFeatured,
            e.BookingAllowed,
            e.CreatedAt)).ToList();
    }

    private async Task NotifyOwnerAsync(Guid? ownerId, string template, string title, string body, CancellationToken ct)
    {
        if (ownerId is { } id)
        {
            await notifications.NotifyAsync(id, template, title, body, null, ct);
        }
    }

    private async Task DeleteQuietlyAsync(params string?[] keys)
    {
        foreach (var k in keys.Where(k => k is not null))
        {
            try
            {
                await storage.DeleteAsync(StorageBuckets.PublicMedia, k!);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // An orphaned object is harmless; the database change already succeeded.
            }
        }
    }
}

internal static class EntitlementCodes
{
    public const string Free = "free";
}
