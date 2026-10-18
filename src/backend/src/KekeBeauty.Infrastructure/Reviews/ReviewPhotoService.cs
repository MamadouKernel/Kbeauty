using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Modules.Catalog.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace KekeBeauty.Infrastructure.Reviews;

/// <summary>A photo attached to a review, with the addresses of the full-size and the thumbnail images.</summary>
public sealed record ReviewPhotoDto(Guid Id, string Kind, string Url, string? ThumbUrl);

/// <summary>Before and after photos a client attaches to her own review: at most <see cref="MaxPhotos"/>, processed like any gallery photo.</summary>
public sealed class ReviewPhotoService(KekeBeautyDbContext db, IFileStorage storage, IConfiguration configuration)
{
    public const int MaxPhotos = 4;

    private static readonly Error PhotoNotFound = new("photo.not_found", "Photo introuvable.");
    private static readonly Error LimitReached = new("photo.limit_reached", $"Un avis peut comporter {MaxPhotos} photos au plus.");

    private readonly string _mediaBaseUrl = MediaUrls.BaseUrl(configuration);

    public async Task<Result<ReviewPhotoDto>> AddAsync(Guid userId, Guid reviewId, string? kindText, Stream content, CancellationToken ct)
    {
        var review = await db.Reviews.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reviewId && r.UserId == userId, ct);
        if (review is null)
        {
            return Result.Failure<ReviewPhotoDto>(OpinionErrors.NotFound);
        }

        if (!Enum.TryParse<ReviewPhotoKind>(kindText, ignoreCase: true, out var kind) || !Enum.IsDefined(kind))
        {
            return Result.Failure<ReviewPhotoDto>(OpinionErrors.Invalid("Type de photo inconnu (Before, After ou Other)."));
        }

        if (await db.ReviewPhotos.CountAsync(p => p.ReviewId == reviewId, ct) >= MaxPhotos)
        {
            return Result.Failure<ReviewPhotoDto>(LimitReached);
        }

        var medium = await ImageProcessor.ProcessAsync(content, ImageProcessor.MediumWidth, ct);
        if (medium.IsFailure)
        {
            return Result.Failure<ReviewPhotoDto>(medium.Error);
        }

        using var mediumStream = new MemoryStream(medium.Value.Bytes);
        var thumb = await ImageProcessor.ProcessAsync(mediumStream, ImageProcessor.ThumbWidth, ct);
        if (thumb.IsFailure)
        {
            return Result.Failure<ReviewPhotoDto>(thumb.Error);
        }

        var fileId = Guid.CreateVersion7();
        var key = $"{MediaUrls.UploadPrefix}reviews/{reviewId}/{fileId}.webp";
        var thumbKey = $"{MediaUrls.UploadPrefix}reviews/{reviewId}/{fileId}_thumb.webp";
        await PutAsync(key, medium.Value.Bytes, ct);
        await PutAsync(thumbKey, thumb.Value.Bytes, ct);

        var photo = new ReviewPhoto { ReviewId = reviewId, Kind = kind, StorageKey = key, ThumbKey = thumbKey, CreatedAt = DateTimeOffset.UtcNow };
        try
        {
            db.ReviewPhotos.Add(photo);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await DeleteQuietlyAsync(key, thumbKey);
            throw;
        }

        return Result.Success(ToDto(photo, _mediaBaseUrl));
    }

    public async Task<Result> DeleteAsync(Guid userId, Guid reviewId, Guid photoId, CancellationToken ct)
    {
        var photo = await (from p in db.ReviewPhotos
                           join r in db.Reviews on p.ReviewId equals r.Id
                           where p.Id == photoId && p.ReviewId == reviewId && r.UserId == userId
                           select p).FirstOrDefaultAsync(ct);
        if (photo is null)
        {
            return Result.Failure(PhotoNotFound);
        }

        db.ReviewPhotos.Remove(photo);
        await db.SaveChangesAsync(ct);
        await DeleteQuietlyAsync(photo.StorageKey, photo.ThumbKey);
        return Result.Success();
    }

    /// <summary>The photos of a set of reviews, grouped by review.</summary>
    public static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ReviewPhotoDto>>> ForReviewsAsync(
        KekeBeautyDbContext db, string mediaBaseUrl, IReadOnlyCollection<Guid> reviewIds, CancellationToken ct)
    {
        if (reviewIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<ReviewPhotoDto>>();
        }

        var rows = await db.ReviewPhotos.AsNoTracking().Where(p => reviewIds.Contains(p.ReviewId)).OrderBy(p => p.CreatedAt).ToListAsync(ct);
        return rows.GroupBy(p => p.ReviewId).ToDictionary(g => g.Key, g => (IReadOnlyList<ReviewPhotoDto>)g.Select(p => ToDto(p, mediaBaseUrl)).ToList());
    }

    private static ReviewPhotoDto ToDto(ReviewPhoto photo, string baseUrl) =>
        new(photo.Id, photo.Kind.ToString(), MediaUrls.Build(baseUrl, photo.StorageKey), photo.ThumbKey is null ? null : MediaUrls.Build(baseUrl, photo.ThumbKey));

    private async Task PutAsync(string key, byte[] bytes, CancellationToken ct)
    {
        using var stream = new MemoryStream(bytes);
        await storage.PutAsync(StorageBuckets.PublicMedia, key, stream, ImageProcessor.WebpContentType, ct);
    }

    private async Task DeleteQuietlyAsync(params string?[] keys)
    {
        // Orphaned objects are harmless; a storage hiccup must not fail a request whose database change succeeded.
        foreach (var key in keys.Where(k => k is not null))
        {
            try
            {
                await storage.DeleteAsync(StorageBuckets.PublicMedia, key!);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // ignored on purpose
            }
        }
    }
}
