using System.Globalization;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Search;
using KekeBeauty.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Growth;

/// <summary>A suggested establishment and why it is suggested, in words the visitor understands.</summary>
public sealed record RecommendationDto(EstablishmentSummary Establishment, string Reason);

/// <summary>
/// "Salons near you": the establishments around the visitor, ranked by distance, rating and — for a signed-in client — the
/// kinds of places she already goes to. Places she already visited are left out: the point is discovery.
/// </summary>
public sealed class RecommendationService(KekeBeautyDbContext db, EstablishmentQueries queries)
{
    public const int DefaultCount = 8;
    public const int MaxCount = 20;
    public const double RadiusKm = 15;
    private const int CandidatePool = 60;

    public async Task<IReadOnlyList<RecommendationDto>> RecommendAsync(Guid? clientId, double? latitude, double? longitude, int count, CancellationToken ct)
    {
        count = Math.Clamp(count, 1, MaxCount);
        var located = latitude is { } lat && longitude is { } lng && Math.Abs(lat) <= 90 && Math.Abs(lng) <= 180;

        var visited = new HashSet<Guid>();
        var liked = new Dictionary<string, int>(StringComparer.Ordinal);
        if (clientId is { } client)
        {
            visited = (await db.Appointments.AsNoTracking()
                .Where(a => a.ClientUserId == client && (a.Status == AppointmentStatus.Completed || a.Status == AppointmentStatus.Confirmed))
                .Select(a => a.EstablishmentId).Distinct().ToListAsync(ct)).ToHashSet();
            if (visited.Count > 0)
            {
                var slugs = await (from ec in db.EstablishmentCategories
                                   join c in db.BeautyCategories on ec.CategoryId equals c.Id
                                   where visited.Contains(ec.EstablishmentId)
                                   select c.Slug).ToListAsync(ct);
                liked = slugs.GroupBy(s => s).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            }
        }

        var page = await queries.SearchAsync(
            new EstablishmentSearchCriteria(null, null, null, located ? latitude : null, located ? longitude : null, located ? RadiusKm : null,
                null, null, located ? "distance" : "rating", 1, CandidatePool),
            ct);

        var categoryNames = page.Items.SelectMany(e => e.Categories).GroupBy(c => c.Slug).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
        return page.Items
            .Where(e => !visited.Contains(e.Id))
            .Select(e => (Establishment: e, Score: Score(e, liked, located)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Establishment.DistanceKm ?? double.MaxValue)
            .Take(count)
            .Select(x => new RecommendationDto(x.Establishment, Reason(x.Establishment, liked, categoryNames)))
            .ToList();
    }

    /// <summary>
    /// Closer is better (full marks under 1 km, nothing left at the edge of the radius), a good rating counts once there are
    /// a few reviews, and each kind of place the client already likes adds a bonus.
    /// </summary>
    public static double Score(EstablishmentSummary e, IReadOnlyDictionary<string, int> liked, bool located)
    {
        var proximity = located && e.DistanceKm is { } km ? Math.Clamp(1 - (Math.Max(km, 1) - 1) / (RadiusKm - 1), 0, 1) * 3 : 0;
        var quality = e.Rating is { } rating && e.ReviewCount > 0 ? (rating - 3) * Math.Min(e.ReviewCount, 10) / 10.0 : 0;
        var affinity = e.Categories.Sum(c => liked.TryGetValue(c.Slug, out var visits) ? Math.Min(visits, 3) * 0.75 : 0);
        var bookable = e.BookingAvailable ? 0.25 : 0;
        return proximity + quality + affinity + bookable;
    }

    private static string Reason(EstablishmentSummary e, IReadOnlyDictionary<string, int> liked, IReadOnlyDictionary<string, string> names)
    {
        var favourite = e.Categories.Where(c => liked.ContainsKey(c.Slug)).OrderByDescending(c => liked[c.Slug]).FirstOrDefault();
        if (favourite is not null)
        {
            return $"Vous aimez : {names.GetValueOrDefault(favourite.Slug) ?? favourite.Name}";
        }

        if (e.Rating is >= 4.5 && e.ReviewCount >= 3)
        {
            return $"Très bien noté ({e.Rating.Value.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',')}/5)";
        }

        // Formatted invariantly then given a French decimal comma: containers may run without culture data.
        return e.DistanceKm is { } km
            ? km < 1
                ? $"À {(Math.Round(km * 1000 / 50) * 50).ToString("0", CultureInfo.InvariantCulture)} m de vous"
                : $"À {km.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',')} km de vous"
            : "Nouveau à découvrir";
    }
}
