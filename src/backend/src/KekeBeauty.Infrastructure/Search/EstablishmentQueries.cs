using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Modules.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NetTopologySuite.Geometries;

namespace KekeBeauty.Infrastructure.Search;

/// <summary>
/// Read-only queries that cross module boundaries (catalog + geography + scheduling). They live here, in the only
/// project that sees every schema, so the modules themselves never reference each other's tables.
/// </summary>
public sealed class EstablishmentQueries(KekeBeautyDbContext db, IConfiguration configuration, TimeProvider time)
{
    public const int MaxPageSize = 50;
    public const int Wgs84 = 4326;

    private readonly string _mediaBaseUrl = (configuration["Media:BaseUrl"] ?? "/media").TrimEnd('/');

    private sealed record Row(
        Guid Id,
        string Slug,
        string Name,
        bool IsFeatured,
        bool BookingEnabled,
        double Latitude,
        double Longitude,
        string CommuneName,
        string CityName,
        double? DistanceMeters,
        string Phone,
        string AddressLine);

    public async Task<EstablishmentPage> SearchAsync(EstablishmentSearchCriteria criteria, CancellationToken ct)
    {
        var listed =
            from e in db.Establishments.AsNoTracking()
            where e.Status == EstablishmentStatus.Active
            join commune in db.Communes on e.CommuneId equals commune.Id
            join city in db.Cities on EF.Property<Guid>(commune, "CityId") equals city.Id
            select new { Establishment = e, CommuneName = commune.Name, CityName = city.Name, CityId = city.Id };

        if (!string.IsNullOrWhiteSpace(criteria.Category))
        {
            var slug = criteria.Category.Trim();
            listed = listed.Where(x => db.EstablishmentCategories.Any(ec =>
                ec.EstablishmentId == x.Establishment.Id
                && db.BeautyCategories.Any(b => b.Id == ec.CategoryId && b.Slug == slug)));
        }

        if (criteria.CityId is { } cityId)
        {
            listed = listed.Where(x => x.CityId == cityId);
        }

        if (criteria.CommuneId is { } communeId)
        {
            listed = listed.Where(x => x.Establishment.CommuneId == communeId);
        }

        if (criteria.Bookable == true)
        {
            listed = listed.Where(x => (x.Establishment.BookingEnabled && x.Establishment.BookingAllowed));
        }

        if (!string.IsNullOrWhiteSpace(criteria.Text))
        {
            var pattern = $"%{EscapeLike(criteria.Text.Trim())}%";

            // Two index-friendly branches (trigram indexes on keke_unaccent(name)) merged by id: an OR with a correlated EXISTS
            // would force PostgreSQL to test every establishment one by one.
            var byName = db.Establishments
                .Where(e => EF.Functions.ILike(PostgresFunctions.Unaccent(e.Name), PostgresFunctions.Unaccent(pattern)))
                .Select(e => e.Id);
            var byService = db.Services
                .Where(s => s.IsVisible && s.DeletedAt == null
                    && EF.Functions.ILike(PostgresFunctions.Unaccent(s.Name), PostgresFunctions.Unaccent(pattern)))
                .Select(s => s.EstablishmentId);
            var matching = byName.Union(byService);
            listed = listed.Where(x => matching.Contains(x.Establishment.Id));
        }

        Point? origin = criteria.Latitude is { } latitude && criteria.Longitude is { } longitude
            ? new Point(longitude, latitude) { SRID = Wgs84 }
            : null;

        if (origin is not null && criteria.RadiusKm is { } radiusKm)
        {
            listed = listed.Where(x => x.Establishment.Location.IsWithinDistance(origin, radiusKm * 1000));
        }

        var total = await listed.CountAsync(ct);
        var skip = (criteria.Page - 1) * criteria.PageSize;

        List<Row> rows;
        if (origin is null)
        {
            var ordered = criteria.Sort switch
            {
                "recent" => listed.OrderByDescending(x => x.Establishment.IsFeatured).ThenByDescending(x => x.Establishment.CreatedAt),

                // Best rated first; establishments without review come last, the number of reviews breaks ties.
                "rating" => listed
                    .OrderByDescending(x => db.Reviews
                        .Where(r => r.EstablishmentId == x.Establishment.Id && r.Status == ReviewStatus.Published)
                        .Average(r => (double?)r.Rating) ?? 0)
                    .ThenByDescending(x => db.Reviews
                        .Count(r => r.EstablishmentId == x.Establishment.Id && r.Status == ReviewStatus.Published))
                    .ThenBy(x => x.Establishment.Name),
                _ => listed.OrderByDescending(x => x.Establishment.IsFeatured).ThenBy(x => x.Establishment.Name),
            };

            rows = await ordered.Skip(skip).Take(criteria.PageSize)
                .Select(x => new Row(
                    x.Establishment.Id, x.Establishment.Slug, x.Establishment.Name, x.Establishment.IsFeatured,
                    (x.Establishment.BookingEnabled && x.Establishment.BookingAllowed), x.Establishment.Latitude, x.Establishment.Longitude,
                    x.CommuneName, x.CityName, null, x.Establishment.CustomerServicePhone, x.Establishment.AddressLine))
                .ToListAsync(ct);
        }
        else
        {
            var ordered = criteria.Sort switch
            {
                "distance" => listed.OrderBy(x => x.Establishment.Location.Distance(origin)),
                "name" => listed.OrderBy(x => x.Establishment.Name),
                "rating" => listed
                    .OrderByDescending(x => db.Reviews
                        .Where(r => r.EstablishmentId == x.Establishment.Id && r.Status == ReviewStatus.Published)
                        .Average(r => (double?)r.Rating) ?? 0)
                    .ThenBy(x => x.Establishment.Location.Distance(origin)),
                _ => listed.OrderByDescending(x => x.Establishment.IsFeatured).ThenBy(x => x.Establishment.Location.Distance(origin)),
            };

            rows = await ordered.Skip(skip).Take(criteria.PageSize)
                .Select(x => new Row(
                    x.Establishment.Id, x.Establishment.Slug, x.Establishment.Name, x.Establishment.IsFeatured,
                    (x.Establishment.BookingEnabled && x.Establishment.BookingAllowed), x.Establishment.Latitude, x.Establishment.Longitude,
                    x.CommuneName, x.CityName, x.Establishment.Location.Distance(origin),
                    x.Establishment.CustomerServicePhone, x.Establishment.AddressLine))
                .ToListAsync(ct);
        }

        var items = await Enrich(rows, ct);
        return new EstablishmentPage(items, total, criteria.Page, criteria.PageSize);
    }

    public async Task<EstablishmentDetails?> GetBySlugAsync(string slug, CancellationToken ct)
    {
        var head = await (
            from e in db.Establishments.AsNoTracking()
            where e.Slug == slug && e.Status == EstablishmentStatus.Active
            join commune in db.Communes on e.CommuneId equals commune.Id
            join city in db.Cities on EF.Property<Guid>(commune, "CityId") equals city.Id
            join region in db.Regions on EF.Property<Guid>(city, "RegionId") equals region.Id
            select new { Establishment = e, CommuneName = commune.Name, CityName = city.Name, RegionName = region.Name })
            .FirstOrDefaultAsync(ct);

        if (head is null)
        {
            return null;
        }

        var e1 = head.Establishment;
        var categories = await CategoriesOf([e1.Id], ct);

        var media = (await db.MediaItems.AsNoTracking()
                .Where(m => m.EstablishmentId == e1.Id)
                .OrderBy(m => m.Kind == MediaKind.Video).ThenByDescending(m => m.IsCover).ThenBy(m => m.SortOrder)
                .ToListAsync(ct))
            .Select(m => new MediaItem(
                m.Kind.ToString(),
                MediaUrl(m.StorageKey),
                m.PosterKey is null ? null : MediaUrl(m.PosterKey),
                m.AltText,
                m.DurationSeconds))
            .ToList();

        var groups = await db.ServiceGroups.AsNoTracking().Where(g => g.EstablishmentId == e1.Id).OrderBy(g => g.SortOrder).ToListAsync(ct);
        var services = await db.Services.AsNoTracking()
            .Where(s => s.EstablishmentId == e1.Id && s.IsVisible && s.DeletedAt == null)
            .OrderBy(s => s.SortOrder)
            .ToListAsync(ct);

        var now = time.GetUtcNow();
        ServiceItem ToItem(Service s) =>
            new(s.Id, s.Name, s.Description, s.Price, s.PriceType.ToString(), s.CurrencyCode, s.DurationMinutes,
                s.HasActivePromo(now) ? s.PromoPercent : 0,
                s.HasActivePromo(now) ? s.PromoEndsAt : null,
                s.EffectivePrice(now),
                s.PerksList());

        var serviceGroups = new List<ServiceGroupItem>();
        var ungrouped = services.Where(s => s.GroupId is null).Select(ToItem).ToList();
        if (ungrouped.Count > 0)
        {
            serviceGroups.Add(new ServiceGroupItem(null, ungrouped));
        }

        foreach (var group in groups)
        {
            var inGroup = services.Where(s => s.GroupId == group.Id).Select(ToItem).ToList();
            if (inGroup.Count > 0)
            {
                serviceGroups.Add(new ServiceGroupItem(group.Name, inGroup));
            }
        }

        var hours = (await db.OpeningHours.AsNoTracking().Where(h => h.EstablishmentId == e1.Id).ToListAsync(ct))
            .OrderBy(h => ((int)h.DayOfWeek + 6) % 7) // Monday first
            .ThenBy(h => h.OpensAt)
            .Select(h => new OpeningHourItem((int)h.DayOfWeek, h.OpensAt.ToString("HH:mm"), h.ClosesAt.ToString("HH:mm")))
            .ToList();

        return new EstablishmentDetails(
            e1.Id, e1.Slug, e1.Name, e1.Description,
            categories.GetValueOrDefault(e1.Id) ?? [],
            e1.CustomerServicePhone, e1.Email, e1.AddressLine,
            head.CommuneName, head.CityName, head.RegionName,
            e1.Latitude, e1.Longitude, e1.Timezone,
            e1.BookingEnabled && e1.BookingAllowed, e1.IsFeatured,
            media, serviceGroups, hours,
            new AcceptedPaymentsInfo(e1.AcceptsCash, e1.MobileMoney.Select(m => new MobileMoneyInfo(m.Operator, m.Account, m.HolderName)).ToList()),
            (await RatingsAsync([e1.Id], ct)).TryGetValue(e1.Id, out var rating) ? rating.Average : null,
            rating.Count);
    }

    /// <summary>Atomically increments a daily counter. Returns false when the establishment is unknown or not public.</summary>
    public async Task<bool> RecordEventAsync(Guid establishmentId, EstablishmentEventKind kind, CancellationToken ct)
    {
        var isPublic = await db.Establishments.AnyAsync(e => e.Id == establishmentId && e.Status == EstablishmentStatus.Active, ct);
        if (!isPublic)
        {
            return false;
        }

        var day = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var affected = kind switch
        {
            EstablishmentEventKind.View => await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO catalog.establishment_stats_daily (establishment_id, day, views, call_clicks, directions_clicks)
                VALUES ({establishmentId}, {day}, 1, 0, 0)
                ON CONFLICT (establishment_id, day) DO UPDATE SET views = establishment_stats_daily.views + 1
                """, ct),
            EstablishmentEventKind.Call => await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO catalog.establishment_stats_daily (establishment_id, day, views, call_clicks, directions_clicks)
                VALUES ({establishmentId}, {day}, 0, 1, 0)
                ON CONFLICT (establishment_id, day) DO UPDATE SET call_clicks = establishment_stats_daily.call_clicks + 1
                """, ct),
            _ => await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO catalog.establishment_stats_daily (establishment_id, day, views, call_clicks, directions_clicks)
                VALUES ({establishmentId}, {day}, 0, 0, 1)
                ON CONFLICT (establishment_id, day) DO UPDATE SET directions_clicks = establishment_stats_daily.directions_clicks + 1
                """, ct),
        };

        return affected > 0;
    }

    /// <summary>Escapes the LIKE wildcards so user input is matched literally.</summary>
    public static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    private string MediaUrl(string key) => MediaUrls.Build(_mediaBaseUrl, key);

    private async Task<Dictionary<Guid, List<CategoryRef>>> CategoriesOf(List<Guid> ids, CancellationToken ct)
    {
        var rows = await (
            from ec in db.EstablishmentCategories.AsNoTracking()
            join b in db.BeautyCategories.AsNoTracking() on ec.CategoryId equals b.Id
            where ids.Contains(ec.EstablishmentId)
            orderby b.SortOrder
            select new { ec.EstablishmentId, b.Name, b.Slug })
            .ToListAsync(ct);

        return rows.GroupBy(r => r.EstablishmentId)
            .ToDictionary(g => g.Key, g => g.Select(r => new CategoryRef(r.Name, r.Slug)).ToList());
    }

    /// <summary>Average (one decimal) and number of published reviews per establishment; establishments without review are absent.</summary>
    private async Task<Dictionary<Guid, (double? Average, int Count)>> RatingsAsync(IReadOnlyList<Guid> ids, CancellationToken ct) =>
        (await db.Reviews.AsNoTracking()
            .Where(r => ids.Contains(r.EstablishmentId) && r.Status == ReviewStatus.Published)
            .GroupBy(r => r.EstablishmentId)
            .Select(g => new { Id = g.Key, Average = g.Average(r => (double)r.Rating), Count = g.Count() })
            .ToListAsync(ct))
        .ToDictionary(x => x.Id, x => ((double?)Math.Round(x.Average, 1), x.Count));

    /// <summary>The cards of the given establishments (the signed-in client's favorites), in the order of <paramref name="ids"/>; unpublished ones are left out.</summary>
    public async Task<IReadOnlyList<EstablishmentSummary>> SummariesAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await (
            from e in db.Establishments.AsNoTracking()
            where ids.Contains(e.Id) && e.Status == EstablishmentStatus.Active
            join commune in db.Communes on e.CommuneId equals commune.Id
            join city in db.Cities on EF.Property<Guid>(commune, "CityId") equals city.Id
            select new Row(
                e.Id, e.Slug, e.Name, e.IsFeatured, (e.BookingEnabled && e.BookingAllowed), e.Latitude, e.Longitude,
                commune.Name, city.Name, null, e.CustomerServicePhone, e.AddressLine))
            .ToListAsync(ct);
        var position = ids.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
        return (await Enrich(rows, ct)).OrderBy(item => position[item.Id]).ToList();
    }

    private async Task<List<EstablishmentSummary>> Enrich(List<Row> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(r => r.Id).ToList();
        var categories = await CategoriesOf(ids, ct);

        var covers = (await db.MediaItems.AsNoTracking()
                .Where(m => ids.Contains(m.EstablishmentId) && m.Kind == MediaKind.Photo)
                .OrderByDescending(m => m.IsCover).ThenBy(m => m.SortOrder)
                .Select(m => new { m.EstablishmentId, m.StorageKey })
                .ToListAsync(ct))
            .GroupBy(m => m.EstablishmentId)
            .ToDictionary(g => g.Key, g => g.First().StorageKey);

        var prices = (await db.Services.AsNoTracking()
                .Where(s => ids.Contains(s.EstablishmentId) && s.IsVisible && s.DeletedAt == null)
                .GroupBy(s => s.EstablishmentId)
                .Select(g => new { Id = g.Key, Min = g.Min(s => s.Price), Currency = g.Min(s => s.CurrencyCode), Count = g.Count() })
                .ToListAsync(ct))
            .ToDictionary(p => p.Id);

        var ratings = await RatingsAsync(ids, ct);

        return rows.Select(r => new EstablishmentSummary(
                r.Id,
                r.Slug,
                r.Name,
                covers.TryGetValue(r.Id, out var cover) ? MediaUrl(cover) : null,
                categories.GetValueOrDefault(r.Id) ?? [],
                r.CommuneName,
                r.CityName,
                r.DistanceMeters is { } meters ? Math.Round(meters / 1000, 1) : null,
                r.Latitude,
                r.Longitude,
                prices.TryGetValue(r.Id, out var price) ? price.Min : null,
                prices.TryGetValue(r.Id, out var p2) ? p2.Currency : null,
                r.IsFeatured,
                r.BookingEnabled,
                r.Phone,
                r.AddressLine,
                prices.TryGetValue(r.Id, out var p3) ? p3.Count : 0,
                ratings.TryGetValue(r.Id, out var rating) ? rating.Average : null,
                ratings.TryGetValue(r.Id, out var rated) ? rated.Count : 0))
            .ToList();
    }
}
