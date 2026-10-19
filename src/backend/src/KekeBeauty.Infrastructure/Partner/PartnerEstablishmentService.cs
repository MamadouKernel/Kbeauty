using KekeBeauty.Infrastructure.Billing;
using System.Globalization;
using System.Net.Mail;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Search;
using KekeBeauty.Modules.Catalog.Domain;
using KekeBeauty.Modules.Onboarding.Domain;
using KekeBeauty.Modules.Scheduling.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NetTopologySuite.Geometries;

namespace KekeBeauty.Infrastructure.Partner;

/// <summary>Partner area: create and edit one's establishments, opening hours, and submit them for review (PRD ONB-01/04).</summary>
public sealed class PartnerEstablishmentService(KekeBeautyDbContext db, IConfiguration configuration, TimeProvider time, EntitlementService entitlements)
{
    public const int MaxCategories = 3;
    public const int MaxEstablishmentsPerOwner = 10;
    private const int MaxRangesPerDay = 4;

    private readonly string _mediaBaseUrl = MediaUrls.BaseUrl(configuration);

    public async Task<IReadOnlyList<PartnerEstablishmentSummary>> ListAsync(Guid userId, CancellationToken ct)
    {
        var rows = await db.Establishments.AsNoTracking()
            .Where(e => e.OwnerUserId == userId)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new { e.Id, e.Slug, e.Name, e.Status, CategoryCount = e.Categories.Count })
            .ToListAsync(ct);
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(r => r.Id).ToList();
        var identity = await db.KycIdentities.AsNoTracking().FirstOrDefaultAsync(k => k.UserId == userId, ct);
        var hours = await CountsAsync(db.OpeningHours.Where(h => ids.Contains(h.EstablishmentId)).Select(h => h.EstablishmentId), ct);
        var photos = await CountsAsync(db.MediaItems.Where(m => ids.Contains(m.EstablishmentId) && m.Kind == MediaKind.Photo).Select(m => m.EstablishmentId), ct);
        var covers = (await db.MediaItems.AsNoTracking()
                .Where(m => ids.Contains(m.EstablishmentId) && m.Kind == MediaKind.Photo)
                .OrderByDescending(m => m.IsCover).ThenBy(m => m.SortOrder)
                .Select(m => new { m.EstablishmentId, m.ThumbKey, m.StorageKey })
                .ToListAsync(ct))
            .GroupBy(m => m.EstablishmentId)
            .ToDictionary(g => g.Key, g => g.First());
        var comments = (await db.EstablishmentReviews.AsNoTracking()
                .Where(r => ids.Contains(r.EstablishmentId))
                .OrderByDescending(r => r.SubmittedAt)
                .Select(r => new { r.EstablishmentId, r.Comment })
                .ToListAsync(ct))
            .GroupBy(r => r.EstablishmentId)
            .ToDictionary(g => g.Key, g => g.First().Comment);

        return rows.Select(r => new PartnerEstablishmentSummary(
                r.Id,
                r.Slug,
                r.Name,
                r.Status.ToString(),
                comments.GetValueOrDefault(r.Id),
                covers.TryGetValue(r.Id, out var cover) ? MediaUrls.Build(_mediaBaseUrl, cover.ThumbKey ?? cover.StorageKey) : null,
                r.Status is EstablishmentStatus.Draft
                    ? PartnerSupport.ComputeMissing(r.CategoryCount, hours.GetValueOrDefault(r.Id), photos.GetValueOrDefault(r.Id), identity)
                    : []))
            .ToList();
    }

    public async Task<PartnerEstablishmentDetail?> GetAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var e = await db.Establishments.AsNoTracking().Include(x => x.Categories)
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == userId, ct);
        if (e is null)
        {
            return null;
        }

        var place = await (
            from commune in db.Communes
            where commune.Id == e.CommuneId
            join city in db.Cities on EF.Property<Guid>(commune, "CityId") equals city.Id
            select new { CityId = city.Id, RegionId = EF.Property<Guid>(city, "RegionId") })
            .FirstOrDefaultAsync(ct);

        var hours = (await db.OpeningHours.AsNoTracking().Where(h => h.EstablishmentId == id).ToListAsync(ct))
            .OrderBy(h => ((int)h.DayOfWeek + 6) % 7).ThenBy(h => h.OpensAt)
            .Select(h => new OpeningHourItem((int)h.DayOfWeek, h.OpensAt.ToString("HH:mm", CultureInfo.InvariantCulture), h.ClosesAt.ToString("HH:mm", CultureInfo.InvariantCulture)))
            .ToList();
        var groups = await db.ServiceGroups.AsNoTracking().Where(g => g.EstablishmentId == id).OrderBy(g => g.SortOrder)
            .Select(g => new PartnerGroupItem(g.Id, g.Name)).ToListAsync(ct);
        var services = await db.Services.AsNoTracking().Where(s => s.EstablishmentId == id && s.DeletedAt == null).OrderBy(s => s.SortOrder)
            .ToListAsync(ct);
        var media = (await db.MediaItems.AsNoTracking().Where(m => m.EstablishmentId == id && m.Kind == MediaKind.Photo)
                .OrderByDescending(m => m.IsCover).ThenBy(m => m.SortOrder).ToListAsync(ct))
            .Select(m => new PartnerMediaItem(
                m.Id,
                MediaUrls.Build(_mediaBaseUrl, m.StorageKey),
                m.ThumbKey is null ? null : MediaUrls.Build(_mediaBaseUrl, m.ThumbKey),
                m.IsCover,
                m.SortOrder))
            .ToList();
        var videos = (await db.MediaItems.AsNoTracking().Where(m => m.EstablishmentId == id && m.Kind == MediaKind.Video)
                .OrderBy(m => m.SortOrder).ToListAsync(ct))
            .Select(m => new PartnerMediaItem(
                m.Id,
                MediaUrls.Build(_mediaBaseUrl, m.StorageKey),
                m.ThumbKey is null ? null : MediaUrls.Build(_mediaBaseUrl, m.ThumbKey),
                false,
                m.SortOrder,
                "Video",
                m.PosterKey is null ? null : MediaUrls.Build(_mediaBaseUrl, m.PosterKey),
                m.DurationSeconds))
            .ToList();
        var identity = await db.KycIdentities.AsNoTracking().FirstOrDefaultAsync(k => k.UserId == userId, ct);
        var review = await db.EstablishmentReviews.AsNoTracking().Where(r => r.EstablishmentId == id)
            .OrderByDescending(r => r.SubmittedAt).FirstOrDefaultAsync(ct);

        var now = time.GetUtcNow();
        var (_, features) = await entitlements.EffectiveAsync(id, ct);
        return new PartnerEstablishmentDetail(
            e.Id, e.Slug, e.Name, e.Status.ToString(), PartnerSupport.IsEditable(e.Status),
            e.Description, e.CustomerServicePhone, e.Email,
            e.Categories.Select(c => c.CategoryId).ToList(),
            place?.RegionId ?? Guid.Empty, place?.CityId ?? Guid.Empty, e.CommuneId, e.AddressLine, e.Latitude, e.Longitude,
            hours,
            groups,
            services.Select(s => new PartnerServiceItem(s.Id, s.GroupId, s.CategoryId, s.Name, s.Description, s.Price, s.PriceType.ToString(),
                s.CurrencyCode, s.DurationMinutes, s.IsBookable, s.IsVisible, s.PromoPercent, s.PromoEndsAt, s.EffectivePrice(now), s.PerksList())).ToList(),
            media,
            features.MaxMedia,
            review is null ? null : new ReviewInfo(review.Status.ToString(), review.Comment, review.SubmittedAt, review.ReviewedAt),
            PartnerSupport.ComputeMissing(e.Categories.Count, hours.Count, media.Count, identity),
            videos,
            features.Videos,
            features.MaxVideos,
            features.MaxServices);
    }

    public async Task<Result<Guid>> CreateAsync(Guid userId, EstablishmentInput input, CancellationToken ct)
    {
        var invalid = await ValidateAsync(input, ct);
        if (invalid is not null)
        {
            return Result.Failure<Guid>(invalid);
        }

        if (await db.Establishments.CountAsync(e => e.OwnerUserId == userId, ct) >= MaxEstablishmentsPerOwner)
        {
            return Result.Failure<Guid>(PartnerErrors.Invalid($"Vous ne pouvez pas gérer plus de {MaxEstablishmentsPerOwner} établissements."));
        }

        var establishment = new Establishment
        {
            OwnerUserId = userId,
            Name = input.Name.Trim(),
            Slug = await UniqueSlugAsync(input.Name, ct),
            Description = input.Description.Trim(),
            CustomerServicePhone = PartnerText.NormalizePhone(input.CustomerServicePhone)!,
            Email = string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim(),
            CommuneId = input.CommuneId,
            AddressLine = input.AddressLine.Trim(),
            Location = ToPoint(input),
            Status = EstablishmentStatus.Draft,
            CreatedAt = time.GetUtcNow(),
        };
        foreach (var categoryId in input.CategoryIds.Distinct())
        {
            establishment.Categories.Add(new EstablishmentCategory { EstablishmentId = establishment.Id, CategoryId = categoryId });
        }

        db.Establishments.Add(establishment);
        await db.SaveChangesAsync(ct);
        return Result.Success(establishment.Id);
    }

    public async Task<Result> UpdateAsync(Guid userId, Guid id, EstablishmentInput input, CancellationToken ct)
    {
        var e = await PartnerSupport.OwnedBy(db, userId, id).Include(x => x.Categories).FirstOrDefaultAsync(ct);
        if (e is null)
        {
            return Result.Failure(PartnerErrors.NotFound);
        }

        if (!PartnerSupport.IsEditable(e.Status))
        {
            return Result.Failure(PartnerErrors.Locked);
        }

        var invalid = await ValidateAsync(input, ct);
        if (invalid is not null)
        {
            return Result.Failure(invalid);
        }

        e.Name = input.Name.Trim();
        e.Description = input.Description.Trim();
        e.CustomerServicePhone = PartnerText.NormalizePhone(input.CustomerServicePhone)!;
        e.Email = string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim();
        e.CommuneId = input.CommuneId;
        e.AddressLine = input.AddressLine.Trim();
        e.Location = ToPoint(input);

        var wanted = input.CategoryIds.Distinct().ToHashSet();
        foreach (var stale in e.Categories.Where(c => !wanted.Contains(c.CategoryId)).ToList())
        {
            e.Categories.Remove(stale);
        }

        foreach (var categoryId in wanted.Where(w => e.Categories.All(c => c.CategoryId != w)))
        {
            e.Categories.Add(new EstablishmentCategory { EstablishmentId = e.Id, CategoryId = categoryId });
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SetOpeningHoursAsync(Guid userId, Guid id, IReadOnlyList<OpeningHourInput> input, CancellationToken ct)
    {
        var e = await PartnerSupport.OwnedBy(db, userId, id).Select(x => new { x.Id, x.Status }).FirstOrDefaultAsync(ct);
        if (e is null)
        {
            return Result.Failure(PartnerErrors.NotFound);
        }

        if (!PartnerSupport.IsEditable(e.Status))
        {
            return Result.Failure(PartnerErrors.Locked);
        }

        var parsed = new List<OpeningHour>();
        foreach (var item in input)
        {
            if (item.DayOfWeek is < 0 or > 6
                || !TimeOnly.TryParseExact(item.OpensAt, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var opens)
                || !TimeOnly.TryParseExact(item.ClosesAt, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var closes)
                || opens >= closes)
            {
                return Result.Failure(PartnerErrors.Invalid("Chaque plage doit avoir un jour valide et une heure de fermeture postérieure à l'ouverture (format HH:mm)."));
            }

            parsed.Add(new OpeningHour { EstablishmentId = id, DayOfWeek = (DayOfWeek)item.DayOfWeek, OpensAt = opens, ClosesAt = closes });
        }

        foreach (var day in parsed.GroupBy(h => h.DayOfWeek))
        {
            var ranges = day.OrderBy(h => h.OpensAt).ToList();
            if (ranges.Count > MaxRangesPerDay)
            {
                return Result.Failure(PartnerErrors.Invalid($"Au plus {MaxRangesPerDay} plages par jour."));
            }

            for (var i = 1; i < ranges.Count; i++)
            {
                if (ranges[i].OpensAt < ranges[i - 1].ClosesAt)
                {
                    return Result.Failure(PartnerErrors.Invalid("Les plages d'un même jour ne doivent pas se chevaucher."));
                }
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.OpeningHours.Where(h => h.EstablishmentId == id).ExecuteDeleteAsync(ct);
        db.OpeningHours.AddRange(parsed);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success();
    }

    /// <summary>Sends the file to the admin team: establishment and (if not yet approved) identity go to <c>PendingReview</c>.</summary>
    public async Task<Result<ReviewInfo>> SubmitAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var e = await PartnerSupport.OwnedBy(db, userId, id).Include(x => x.Categories).FirstOrDefaultAsync(ct);
        if (e is null)
        {
            return Result.Failure<ReviewInfo>(PartnerErrors.NotFound);
        }

        if (e.Status != EstablishmentStatus.Draft)
        {
            return Result.Failure<ReviewInfo>(PartnerErrors.Locked);
        }

        var identity = await db.KycIdentities.FirstOrDefaultAsync(k => k.UserId == userId, ct);
        var hourCount = await db.OpeningHours.CountAsync(h => h.EstablishmentId == id, ct);
        var photoCount = await db.MediaItems.CountAsync(m => m.EstablishmentId == id && m.Kind == MediaKind.Photo, ct);
        if (PartnerSupport.ComputeMissing(e.Categories.Count, hourCount, photoCount, identity).Count > 0)
        {
            return Result.Failure<ReviewInfo>(PartnerErrors.Incomplete);
        }

        var now = time.GetUtcNow();
        e.Status = EstablishmentStatus.PendingReview;
        if (identity is { IsEditable: true })
        {
            identity.Status = KycStatus.PendingReview;
            identity.SubmittedAt = now;
            identity.ReviewComment = null;
        }

        var review = new EstablishmentReview { EstablishmentId = id, Status = KycStatus.PendingReview };
        db.EstablishmentReviews.Add(review);
        await db.SaveChangesAsync(ct);
        return Result.Success(new ReviewInfo(review.Status.ToString(), null, review.SubmittedAt, null));
    }

    private async Task<Error?> ValidateAsync(EstablishmentInput input, CancellationToken ct)
    {
        var name = input.Name?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > 150)
        {
            return PartnerErrors.Invalid("Le nom doit contenir entre 2 et 150 caractères.");
        }

        var description = input.Description?.Trim() ?? string.Empty;
        if (description.Length is < 10 or > 4000)
        {
            return PartnerErrors.Invalid("La description doit contenir entre 10 et 4000 caractères.");
        }

        if (PartnerText.NormalizePhone(input.CustomerServicePhone) is null)
        {
            return PartnerErrors.Invalid("Numéro de téléphone invalide (8 à 15 chiffres, « + » facultatif).");
        }

        if (!string.IsNullOrWhiteSpace(input.Email) && (input.Email.Length > 254 || !MailAddress.TryCreate(input.Email.Trim(), out _)))
        {
            return PartnerErrors.Invalid("Adresse e-mail invalide.");
        }

        var address = input.AddressLine?.Trim() ?? string.Empty;
        if (address.Length is < 3 or > 250)
        {
            return PartnerErrors.Invalid("L'adresse doit contenir entre 3 et 250 caractères.");
        }

        if (double.IsNaN(input.Latitude) || double.IsNaN(input.Longitude)
            || input.Latitude is < -90 or > 90 || input.Longitude is < -180 or > 180)
        {
            return PartnerErrors.Invalid("Position GPS invalide.");
        }

        var categories = input.CategoryIds?.Distinct().ToList() ?? [];
        if (categories.Count is < 1 or > MaxCategories)
        {
            return PartnerErrors.Invalid($"Choisissez entre 1 et {MaxCategories} catégories.");
        }

        if (await db.BeautyCategories.CountAsync(c => c.IsActive && categories.Contains(c.Id), ct) != categories.Count)
        {
            return PartnerErrors.Invalid("Catégorie inconnue.");
        }

        return await db.Communes.AnyAsync(c => c.Id == input.CommuneId && c.IsActive, ct)
            ? null
            : PartnerErrors.Invalid("Commune inconnue.");
    }

    private async Task<string> UniqueSlugAsync(string name, CancellationToken ct)
    {
        var baseSlug = PartnerText.Slugify(name);
        var slug = baseSlug;
        for (var attempt = 2; await db.Establishments.AnyAsync(e => e.Slug == slug, ct); attempt++)
        {
            slug = $"{baseSlug}-{attempt}";
        }

        return slug;
    }

    private static Point ToPoint(EstablishmentInput input) =>
        new(input.Longitude, input.Latitude) { SRID = EstablishmentQueries.Wgs84 };

    private static async Task<Dictionary<Guid, int>> CountsAsync(IQueryable<Guid> ids, CancellationToken ct) =>
        (await ids.GroupBy(x => x).Select(g => new { Id = g.Key, Count = g.Count() }).ToListAsync(ct))
        .ToDictionary(x => x.Id, x => x.Count);
}
