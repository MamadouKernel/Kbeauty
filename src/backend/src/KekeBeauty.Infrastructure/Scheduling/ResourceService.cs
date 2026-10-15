using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Scheduling.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Scheduling;

/// <summary>Partner configuration of who serves clients and when the establishment is closed (PRD AGD-04, AGD-05).</summary>
public sealed class ResourceService(KekeBeautyDbContext db, TimeProvider time, KekeBeauty.Infrastructure.Billing.EntitlementService entitlements)
{
    public const int MaxResources = 20;
    public const string DefaultResourceName = "Principal";

    private static readonly Error ResourceNotFound = new("resource.not_found", "Ressource introuvable.");
    private static readonly Error TimeOffNotFound = new("timeoff.not_found", "Indisponibilité introuvable.");
    private static readonly Error LastResource = new("resource.last_active", "La réservation en ligne est activée : gardez au moins une ressource active.");

    /// <summary>
    /// Every bookable establishment needs one resource ("by default a single resource is created automatically").
    /// Called when a file is approved and whenever the partner opens the agenda; idempotent.
    /// </summary>
    public static async Task EnsureDefaultAsync(KekeBeautyDbContext context, Guid establishmentId, DateTimeOffset now, CancellationToken ct)
    {
        if (await context.Resources.AnyAsync(r => r.EstablishmentId == establishmentId, ct))
        {
            return;
        }

        context.Resources.Add(new Resource { EstablishmentId = establishmentId, Name = DefaultResourceName, CreatedAt = now });
        await context.SaveChangesAsync(ct);
    }

    // ───────── Resources ─────────

    public async Task<Result<IReadOnlyList<ResourceDto>>> ListAsync(Guid userId, Guid establishmentId, CancellationToken ct)
    {
        if (!await Owns(userId, establishmentId, ct))
        {
            return Result.Failure<IReadOnlyList<ResourceDto>>(PartnerErrors.NotFound);
        }

        await EnsureDefaultAsync(db, establishmentId, time.GetUtcNow(), ct);
        var resources = await db.Resources.AsNoTracking().Where(r => r.EstablishmentId == establishmentId)
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).ToListAsync(ct);
        var ids = resources.Select(r => r.Id).ToList();
        var links = (await db.ResourceServiceLinks.AsNoTracking().Where(l => ids.Contains(l.ResourceId)).ToListAsync(ct))
            .GroupBy(l => l.ResourceId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(l => l.ServiceId).ToList());

        return Result.Success<IReadOnlyList<ResourceDto>>(
            resources.Select(r => new ResourceDto(r.Id, r.Name, r.IsActive, links.GetValueOrDefault(r.Id) ?? [])).ToList());
    }

    public async Task<Result<Guid>> CreateAsync(Guid userId, Guid establishmentId, ResourceInput input, CancellationToken ct)
    {
        if (!await Owns(userId, establishmentId, ct))
        {
            return Result.Failure<Guid>(PartnerErrors.NotFound);
        }

        var invalid = await ValidateAsync(establishmentId, input, ct);
        if (invalid is not null)
        {
            return Result.Failure<Guid>(invalid);
        }

        var existing = await db.Resources.CountAsync(r => r.EstablishmentId == establishmentId, ct);
        if (existing >= MaxResources)
        {
            return Result.Failure<Guid>(SchedulingErrors.Invalid($"Au plus {MaxResources} ressources."));
        }

        if ((await entitlements.EffectiveAsync(establishmentId, ct)).Features.MaxResources is { } planLimit && existing >= planLimit)
        {
            return Result.Failure<Guid>(new Error("resource.limit_reached", $"Votre formule autorise {planLimit} ressource(s) : passez à une formule supérieure pour en ajouter."));
        }

        var resource = new Resource { EstablishmentId = establishmentId, Name = input.Name.Trim(), IsActive = input.IsActive, CreatedAt = time.GetUtcNow() };
        db.Resources.Add(resource);
        AddLinks(resource.Id, input.ServiceIds);
        await db.SaveChangesAsync(ct);
        return Result.Success(resource.Id);
    }

    public async Task<Result> UpdateAsync(Guid userId, Guid establishmentId, Guid resourceId, ResourceInput input, CancellationToken ct)
    {
        if (!await Owns(userId, establishmentId, ct))
        {
            return Result.Failure(PartnerErrors.NotFound);
        }

        var resource = await db.Resources.FirstOrDefaultAsync(r => r.Id == resourceId && r.EstablishmentId == establishmentId, ct);
        if (resource is null)
        {
            return Result.Failure(ResourceNotFound);
        }

        var invalid = await ValidateAsync(establishmentId, input, ct);
        if (invalid is not null)
        {
            return Result.Failure(invalid);
        }

        if (!input.IsActive && resource.IsActive && await WouldRemoveLastActiveAsync(establishmentId, resourceId, ct))
        {
            return Result.Failure(LastResource);
        }

        resource.Name = input.Name.Trim();
        resource.IsActive = input.IsActive;
        await db.ResourceServiceLinks.Where(l => l.ResourceId == resourceId).ExecuteDeleteAsync(ct);
        AddLinks(resourceId, input.ServiceIds);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>A resource with appointments in its history is deactivated instead of deleted.</summary>
    public async Task<Result> DeleteAsync(Guid userId, Guid establishmentId, Guid resourceId, CancellationToken ct)
    {
        if (!await Owns(userId, establishmentId, ct))
        {
            return Result.Failure(PartnerErrors.NotFound);
        }

        var resource = await db.Resources.FirstOrDefaultAsync(r => r.Id == resourceId && r.EstablishmentId == establishmentId, ct);
        if (resource is null)
        {
            return Result.Failure(ResourceNotFound);
        }

        if (resource.IsActive && await WouldRemoveLastActiveAsync(establishmentId, resourceId, ct))
        {
            return Result.Failure(LastResource);
        }

        if (await db.Appointments.AnyAsync(a => a.ResourceId == resourceId, ct))
        {
            resource.IsActive = false;
            await db.SaveChangesAsync(ct);
        }
        else
        {
            db.Resources.Remove(resource);
            await db.SaveChangesAsync(ct);
        }

        return Result.Success();
    }

    // ───────── Time off ─────────

    public async Task<Result<IReadOnlyList<TimeOffDto>>> ListTimeOffAsync(Guid userId, Guid establishmentId, CancellationToken ct)
    {
        if (!await Owns(userId, establishmentId, ct))
        {
            return Result.Failure<IReadOnlyList<TimeOffDto>>(PartnerErrors.NotFound);
        }

        var since = time.GetUtcNow().AddDays(-1);
        var items = await db.TimeOffs.AsNoTracking()
            .Where(t => t.EstablishmentId == establishmentId && t.EndsAt >= since)
            .OrderBy(t => t.StartsAt)
            .Select(t => new TimeOffDto(t.Id, t.ResourceId, t.StartsAt, t.EndsAt, t.Reason))
            .ToListAsync(ct);
        return Result.Success<IReadOnlyList<TimeOffDto>>(items);
    }

    public async Task<Result<Guid>> CreateTimeOffAsync(Guid userId, Guid establishmentId, TimeOffInput input, CancellationToken ct)
    {
        if (!await Owns(userId, establishmentId, ct))
        {
            return Result.Failure<Guid>(PartnerErrors.NotFound);
        }

        if (input.EndsAt <= input.StartsAt || input.EndsAt - input.StartsAt > TimeSpan.FromDays(366))
        {
            return Result.Failure<Guid>(SchedulingErrors.Invalid("La fin doit suivre le début, sur 366 jours au plus."));
        }

        if (input.Reason is { Length: > 250 })
        {
            return Result.Failure<Guid>(SchedulingErrors.Invalid("Le motif est limité à 250 caractères."));
        }

        if (input.ResourceId is { } resourceId && !await db.Resources.AnyAsync(r => r.Id == resourceId && r.EstablishmentId == establishmentId, ct))
        {
            return Result.Failure<Guid>(ResourceNotFound);
        }

        var timeOff = new TimeOff
        {
            EstablishmentId = establishmentId,
            ResourceId = input.ResourceId,
            StartsAt = input.StartsAt,
            EndsAt = input.EndsAt,
            Reason = string.IsNullOrWhiteSpace(input.Reason) ? null : input.Reason.Trim(),
        };
        db.TimeOffs.Add(timeOff);
        await db.SaveChangesAsync(ct);
        return Result.Success(timeOff.Id);
    }

    public async Task<Result> DeleteTimeOffAsync(Guid userId, Guid establishmentId, Guid timeOffId, CancellationToken ct)
    {
        if (!await Owns(userId, establishmentId, ct))
        {
            return Result.Failure(PartnerErrors.NotFound);
        }

        var removed = await db.TimeOffs.Where(t => t.Id == timeOffId && t.EstablishmentId == establishmentId).ExecuteDeleteAsync(ct);
        return removed == 0 ? Result.Failure(TimeOffNotFound) : Result.Success();
    }

    // ───────── Helpers ─────────

    private Task<bool> Owns(Guid userId, Guid establishmentId, CancellationToken ct) =>
        PartnerSupport.OwnedBy(db, userId, establishmentId).AnyAsync(ct);

    private void AddLinks(Guid resourceId, IReadOnlyList<Guid> serviceIds)
    {
        foreach (var serviceId in serviceIds.Distinct())
        {
            db.ResourceServiceLinks.Add(new ResourceServiceLink { ResourceId = resourceId, ServiceId = serviceId });
        }
    }

    private async Task<Error?> ValidateAsync(Guid establishmentId, ResourceInput input, CancellationToken ct)
    {
        var name = input.Name?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > 120)
        {
            return SchedulingErrors.Invalid("Le nom doit contenir entre 2 et 120 caractères.");
        }

        var ids = input.ServiceIds?.Distinct().ToList() ?? [];
        var known = await db.Services.CountAsync(s => s.EstablishmentId == establishmentId && s.DeletedAt == null && ids.Contains(s.Id), ct);
        return known == ids.Count ? null : SchedulingErrors.Invalid("Prestation inconnue.");
    }

    private async Task<bool> WouldRemoveLastActiveAsync(Guid establishmentId, Guid resourceId, CancellationToken ct)
    {
        var bookingEnabled = await db.Establishments.AsNoTracking().Where(e => e.Id == establishmentId).Select(e => e.BookingEnabled).FirstAsync(ct);
        return bookingEnabled && !await db.Resources.AnyAsync(r => r.EstablishmentId == establishmentId && r.IsActive && r.Id != resourceId, ct);
    }
}
