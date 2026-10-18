using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Infrastructure.Search;
using KekeBeauty.Modules.Catalog.Domain;
using KekeBeauty.Modules.Scheduling.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace KekeBeauty.Infrastructure.Reviews;

/// <summary>Errors of the clients' reviews ("opinions"); not to be confused with the administrators' review of KYC files.</summary>
public static class OpinionErrors
{
    public static readonly Error NotFound = new("opinion.not_found", "Avis introuvable.");
    public static readonly Error NotReviewable = new("opinion.not_reviewable", "Vous pourrez donner votre avis une fois le rendez-vous terminé.");
    public static readonly Error AlreadyReviewed = new("opinion.already_reviewed", "Vous avez déjà donné votre avis sur ce rendez-vous.");
    public static readonly Error TooLate = new("opinion.too_late", "Le délai pour donner un avis (90 jours après le rendez-vous) est dépassé.");
    public static readonly Error EstablishmentNotFound = new("establishment.not_found", "Établissement introuvable.");
    public static readonly Error TooManyFavorites = new("favorite.limit_reached", "Vous avez atteint la limite de 200 favoris.");

    public static Error Invalid(string message) => new("validation.failed", message);
}

/// <summary>A public review. The author appears as a first name and the initial of the last name only.</summary>
public sealed record ReviewDto(
    Guid Id,
    int Rating,
    string? Comment,
    string AuthorName,
    DateTimeOffset CreatedAt,
    string? ReplyText,
    DateTimeOffset? RepliedAt,
    string Status = "Published",
    IReadOnlyList<ReviewPhotoDto>? Photos = null);

/// <summary>Average, number and how many reviews each star count got (<c>Distribution[0]</c> = 1 star … <c>[4]</c> = 5 stars).</summary>
public sealed record RatingSummary(double? Average, int Count, IReadOnlyList<int> Distribution);

public sealed record ReviewsPage(RatingSummary Summary, IReadOnlyList<ReviewDto> Items, int Page, int PageSize, int Total);

public sealed record ReviewInput(int Rating, string? Comment);

public sealed record ReplyInput(string Text);

public sealed record HideReviewInput(string Reason);

/// <summary>A review the signed-in client wrote: lets the appointments page know which visits still wait for an opinion.</summary>
public sealed record MyReviewDto(Guid ReviewId, Guid AppointmentId, Guid EstablishmentId, int Rating);

/// <summary>Clients' reviews (a real, completed visit is required) and the establishment's answers.</summary>
public sealed class OpinionService(KekeBeautyDbContext db, TimeProvider time, IConfiguration configuration)
{
    private readonly string _mediaBaseUrl = MediaUrls.BaseUrl(configuration);

    public const int MaxCommentLength = 1000;
    public const int MaxReplyLength = 600;
    public const int PageSizeDefault = 10;
    private static readonly TimeSpan ReviewWindow = TimeSpan.FromDays(90);

    public async Task<Result<ReviewDto>> CreateAsync(Guid userId, Guid appointmentId, ReviewInput input, CancellationToken ct)
    {
        var appointment = await db.Appointments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == appointmentId && a.ClientUserId == userId, ct);
        if (appointment is null)
        {
            return Result.Failure<ReviewDto>(SchedulingErrors.AppointmentNotFound);
        }

        if (appointment.Status != AppointmentStatus.Completed)
        {
            return Result.Failure<ReviewDto>(OpinionErrors.NotReviewable);
        }

        var now = time.GetUtcNow();
        if (now - appointment.EndsAt > ReviewWindow)
        {
            return Result.Failure<ReviewDto>(OpinionErrors.TooLate);
        }

        if (input.Rating is < 1 or > 5)
        {
            return Result.Failure<ReviewDto>(OpinionErrors.Invalid("La note va de 1 à 5 étoiles."));
        }

        var comment = input.Comment?.Trim();
        if (comment is { Length: > MaxCommentLength })
        {
            return Result.Failure<ReviewDto>(OpinionErrors.Invalid($"Le commentaire ne peut pas dépasser {MaxCommentLength} caractères."));
        }

        if (await db.Reviews.AnyAsync(r => r.AppointmentId == appointmentId, ct))
        {
            return Result.Failure<ReviewDto>(OpinionErrors.AlreadyReviewed);
        }

        var review = new Review
        {
            EstablishmentId = appointment.EstablishmentId,
            UserId = userId,
            AppointmentId = appointmentId,
            Rating = input.Rating,
            Comment = string.IsNullOrEmpty(comment) ? null : comment,
            CreatedAt = now,
        };
        db.Reviews.Add(review);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Result.Failure<ReviewDto>(OpinionErrors.AlreadyReviewed); // two clicks at once: the unique index decided
        }

        var author = await AuthorsAsync([userId], ct);
        return Result.Success(ToDto(review, author));
    }

    public async Task<Result<ReviewsPage>> ListPublicAsync(string slug, int page, int pageSize, CancellationToken ct)
    {
        var establishmentId = await db.Establishments.AsNoTracking()
            .Where(e => e.Slug == slug && e.Status == EstablishmentStatus.Active).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
        if (establishmentId is not { } id)
        {
            return Result.Failure<ReviewsPage>(OpinionErrors.EstablishmentNotFound);
        }

        return Result.Success(await PageAsync(id, includeHidden: false, page, pageSize, ct));
    }

    public async Task<Result<ReviewsPage>> ListForOwnerAsync(Guid ownerId, Guid establishmentId, int page, int pageSize, CancellationToken ct)
    {
        if (!await PartnerSupport.OwnedBy(db, ownerId, establishmentId).AnyAsync(ct))
        {
            return Result.Failure<ReviewsPage>(PartnerErrors.NotFound);
        }

        return Result.Success(await PageAsync(establishmentId, includeHidden: true, page, pageSize, ct));
    }

    public async Task<IReadOnlyList<MyReviewDto>> MineAsync(Guid userId, CancellationToken ct) =>
        await db.Reviews.AsNoTracking().Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt).Take(200)
            .Select(r => new MyReviewDto(r.Id, r.AppointmentId, r.EstablishmentId, r.Rating))
            .ToListAsync(ct);

    public async Task<Result<ReviewDto>> ReplyAsync(Guid ownerId, Guid reviewId, ReplyInput input, CancellationToken ct)
    {
        var review = await (
            from r in db.Reviews
            where r.Id == reviewId && db.Establishments.Any(e => e.Id == r.EstablishmentId && e.OwnerUserId == ownerId)
            select r).FirstOrDefaultAsync(ct);
        if (review is null || review.Status != ReviewStatus.Published)
        {
            return Result.Failure<ReviewDto>(OpinionErrors.NotFound);
        }

        var text = input.Text?.Trim() ?? string.Empty;
        if (text.Length is < 1 or > MaxReplyLength)
        {
            return Result.Failure<ReviewDto>(OpinionErrors.Invalid($"La réponse comporte de 1 à {MaxReplyLength} caractères."));
        }

        review.ReplyText = text;
        review.RepliedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(review, await AuthorsAsync([review.UserId], ct)));
    }

    /// <summary>Hides (or restores) a review: abuse stays out of the public page, the audit log keeps who decided and why.</summary>
    public async Task<Result> SetHiddenAsync(Guid adminId, Guid reviewId, bool hidden, string? reason, CancellationToken ct)
    {
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId, ct);
        if (review is null)
        {
            return Result.Failure(OpinionErrors.NotFound);
        }

        var why = reason?.Trim();
        if (hidden && (string.IsNullOrEmpty(why) || why.Length > 300))
        {
            return Result.Failure(OpinionErrors.Invalid("Indiquez le motif (300 caractères au plus)."));
        }

        review.Status = hidden ? ReviewStatus.Hidden : ReviewStatus.Published;
        review.HiddenReason = hidden ? why : null;
        Admin.Audit.Add(db, new Admin.AdminContext(adminId, false, null), time, hidden ? "review.hide" : "review.restore", "Review", review.Id,
            new { review.EstablishmentId, reason = why });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<ReviewsPage> PageAsync(Guid establishmentId, bool includeHidden, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var published = db.Reviews.AsNoTracking().Where(r => r.EstablishmentId == establishmentId && r.Status == ReviewStatus.Published);
        var counts = await published.GroupBy(r => r.Rating).Select(g => new { Rating = g.Key, Count = g.Count() }).ToListAsync(ct);
        var distribution = Enumerable.Range(1, 5).Select(star => counts.FirstOrDefault(c => c.Rating == star)?.Count ?? 0).ToList();
        var total = distribution.Sum();
        var summary = new RatingSummary(
            total == 0 ? null : Math.Round(distribution.Select((n, i) => n * (i + 1.0)).Sum() / total, 1),
            total,
            distribution);

        var listed = db.Reviews.AsNoTracking().Where(r => r.EstablishmentId == establishmentId && (includeHidden || r.Status == ReviewStatus.Published));
        var listedTotal = await listed.CountAsync(ct);
        var items = await listed.OrderByDescending(r => r.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var authors = await AuthorsAsync(items.Select(r => r.UserId).Distinct().ToList(), ct);
        var photos = await ReviewPhotoService.ForReviewsAsync(db, _mediaBaseUrl, items.Select(r => r.Id).ToList(), ct);
        return new ReviewsPage(summary, items.Select(r => ToDto(r, authors, photos)).ToList(), page, pageSize, listedTotal);
    }

    private async Task<Dictionary<Guid, string>> AuthorsAsync(IReadOnlyList<Guid> userIds, CancellationToken ct) =>
        (await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.FirstName, u.LastName }).ToListAsync(ct))
        .ToDictionary(u => u.Id, u => DisplayName(u.FirstName, u.LastName));

    /// <summary>"Awa K." — never the full name; a deleted account (names erased) reads "Ancien client".</summary>
    public static string DisplayName(string firstName, string lastName)
    {
        var first = firstName.Trim();
        if (first.Length == 0)
        {
            return "Ancien client";
        }

        var initial = lastName.Trim();
        return initial.Length == 0 ? first : $"{first} {char.ToUpperInvariant(initial[0])}.";
    }

    private static ReviewDto ToDto(Review r, IReadOnlyDictionary<Guid, string> authors, IReadOnlyDictionary<Guid, IReadOnlyList<ReviewPhotoDto>>? photos = null) =>
        new(r.Id, r.Rating, r.Status == ReviewStatus.Hidden ? null : r.Comment, authors.GetValueOrDefault(r.UserId, "Ancien client"), r.CreatedAt,
            r.ReplyText, r.RepliedAt, r.Status.ToString(), r.Status == ReviewStatus.Hidden ? null : photos?.GetValueOrDefault(r.Id));
}

/// <summary>The establishments a client saved.</summary>
public sealed class FavoriteService(KekeBeautyDbContext db, EstablishmentQueries queries, TimeProvider time)
{
    public const int MaxFavorites = 200;

    public async Task<Result> AddAsync(Guid userId, Guid establishmentId, CancellationToken ct)
    {
        if (!await db.Establishments.AsNoTracking().AnyAsync(e => e.Id == establishmentId && e.Status == EstablishmentStatus.Active, ct))
        {
            return Result.Failure(OpinionErrors.EstablishmentNotFound);
        }

        if (await db.Favorites.AnyAsync(f => f.UserId == userId && f.EstablishmentId == establishmentId, ct))
        {
            return Result.Success(); // saving twice is not an error
        }

        if (await db.Favorites.CountAsync(f => f.UserId == userId, ct) >= MaxFavorites)
        {
            return Result.Failure(OpinionErrors.TooManyFavorites);
        }

        db.Favorites.Add(new Favorite { UserId = userId, EstablishmentId = establishmentId, CreatedAt = time.GetUtcNow() });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Saved by a parallel request: same outcome.
        }

        return Result.Success();
    }

    public async Task RemoveAsync(Guid userId, Guid establishmentId, CancellationToken ct) =>
        await db.Favorites.Where(f => f.UserId == userId && f.EstablishmentId == establishmentId).ExecuteDeleteAsync(ct);

    public async Task<IReadOnlyList<Guid>> IdsAsync(Guid userId, CancellationToken ct) =>
        await db.Favorites.AsNoTracking().Where(f => f.UserId == userId).OrderByDescending(f => f.CreatedAt).Select(f => f.EstablishmentId).ToListAsync(ct);

    public async Task<IReadOnlyList<EstablishmentSummary>> ListAsync(Guid userId, CancellationToken ct) =>
        await queries.SummariesAsync(await IdsAsync(userId, ct), ct);
}
