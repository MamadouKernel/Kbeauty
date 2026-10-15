using KekeBeauty.Infrastructure.Billing;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Catalog.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace KekeBeauty.Infrastructure.Partner;

/// <summary>Photo and video gallery of an establishment (PRD SVC-03): upload, delete, cover and order. Videos sit beside the photos and never become the cover.</summary>
public sealed class PartnerMediaService(KekeBeautyDbContext db, IFileStorage storage, IConfiguration configuration, EntitlementService entitlements)
{
    private const int DefaultMaxVideoMegabytes = 50;

    private static readonly Error MediaNotFound = new("media.not_found", "Média introuvable.");

    private readonly string _mediaBaseUrl = MediaUrls.BaseUrl(configuration);

    public async Task<Result<PartnerMediaItem>> AddPhotoAsync(Guid userId, Guid establishmentId, Stream content, CancellationToken ct)
    {
        var status = await PartnerSupport.OwnedBy(db, userId, establishmentId).Select(e => (EstablishmentStatus?)e.Status).FirstOrDefaultAsync(ct);
        if (status is null)
        {
            return Result.Failure<PartnerMediaItem>(PartnerErrors.NotFound);
        }

        if (!PartnerSupport.IsEditable(status.Value))
        {
            return Result.Failure<PartnerMediaItem>(PartnerErrors.Locked);
        }

        var existing = await db.MediaItems.Where(m => m.EstablishmentId == establishmentId && m.Kind == MediaKind.Photo)
            .Select(m => m.SortOrder).ToListAsync(ct);
        var maxPhotos = (await entitlements.EffectiveAsync(establishmentId, ct)).Features.MaxMedia;
        if (existing.Count >= maxPhotos)
        {
            return Result.Failure<PartnerMediaItem>(new Error("media.limit_reached", $"Limite de {maxPhotos} photos atteinte pour votre formule."));
        }

        var medium = await ImageProcessor.ProcessAsync(content, ImageProcessor.MediumWidth, ct);
        if (medium.IsFailure)
        {
            return Result.Failure<PartnerMediaItem>(medium.Error);
        }

        using var mediumStream = new MemoryStream(medium.Value.Bytes);
        var thumb = await ImageProcessor.ProcessAsync(mediumStream, ImageProcessor.ThumbWidth, ct);
        if (thumb.IsFailure)
        {
            return Result.Failure<PartnerMediaItem>(thumb.Error);
        }

        var fileId = Guid.CreateVersion7();
        var key = $"{MediaUrls.UploadPrefix}{establishmentId}/{fileId}.webp";
        var thumbKey = $"{MediaUrls.UploadPrefix}{establishmentId}/{fileId}_thumb.webp";
        await PutAsync(key, medium.Value.Bytes, ct);
        await PutAsync(thumbKey, thumb.Value.Bytes, ct);

        var media = new Media
        {
            EstablishmentId = establishmentId,
            Kind = MediaKind.Photo,
            StorageKey = key,
            ThumbKey = thumbKey,
            Width = medium.Value.Width,
            Height = medium.Value.Height,
            SortOrder = existing.Count == 0 ? 0 : existing.Max() + 1,
            IsCover = existing.Count == 0,
        };

        try
        {
            db.MediaItems.Add(media);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await DeleteQuietlyAsync(key, thumbKey);
            throw;
        }

        return Result.Success(ToItem(media));
    }

    /// <summary>Stores a short MP4/WebM clip. The container is recognised from its first bytes, not from the file name.</summary>
    public async Task<Result<PartnerMediaItem>> AddVideoAsync(
        Guid userId, Guid establishmentId, Stream video, Stream? poster, int? durationSeconds, CancellationToken ct)
    {
        var status = await PartnerSupport.OwnedBy(db, userId, establishmentId).Select(e => (EstablishmentStatus?)e.Status).FirstOrDefaultAsync(ct);
        if (status is null)
        {
            return Result.Failure<PartnerMediaItem>(PartnerErrors.NotFound);
        }

        if (!PartnerSupport.IsEditable(status.Value))
        {
            return Result.Failure<PartnerMediaItem>(PartnerErrors.Locked);
        }

        var features = (await entitlements.EffectiveAsync(establishmentId, ct)).Features;
        if (!features.Videos || features.MaxVideos <= 0)
        {
            return Result.Failure<PartnerMediaItem>(new Error("plan.feature_required", "Les vidéos ne sont pas incluses dans votre formule."));
        }

        var existing = await db.MediaItems.Where(m => m.EstablishmentId == establishmentId && m.Kind == MediaKind.Video)
            .Select(m => m.SortOrder).ToListAsync(ct);
        if (existing.Count >= features.MaxVideos)
        {
            return Result.Failure<PartnerMediaItem>(new Error("media.limit_reached", $"Limite de {features.MaxVideos} vidéos atteinte pour votre formule."));
        }

        var maxBytes = (long)configuration.GetValue("Media:MaxVideoMegabytes", DefaultMaxVideoMegabytes) * 1024 * 1024;
        var header = new byte[VideoSniffer.HeaderLength];
        var read = 0;
        while (read < header.Length)
        {
            var n = await video.ReadAsync(header.AsMemory(read), ct);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        var detected = VideoSniffer.Detect(header.AsSpan(0, read));
        if (detected is null)
        {
            return Result.Failure<PartnerMediaItem>(new Error("media.unsupported_video", "Format de vidéo non pris en charge (MP4 ou WebM uniquement)."));
        }

        string? posterKey = null;
        string? thumbKey = null;
        var fileId = Guid.CreateVersion7();
        var key = $"{MediaUrls.UploadPrefix}{establishmentId}/{fileId}.{detected.Extension}";
        try
        {
            if (poster is not null)
            {
                var posterImage = await ImageProcessor.ProcessAsync(poster, ImageProcessor.MediumWidth, ct);
                if (posterImage.IsFailure)
                {
                    return Result.Failure<PartnerMediaItem>(posterImage.Error);
                }

                using var posterStream = new MemoryStream(posterImage.Value.Bytes);
                var thumb = await ImageProcessor.ProcessAsync(posterStream, ImageProcessor.ThumbWidth, ct);
                if (thumb.IsFailure)
                {
                    return Result.Failure<PartnerMediaItem>(thumb.Error);
                }

                posterKey = $"{MediaUrls.UploadPrefix}{establishmentId}/{fileId}_poster.webp";
                thumbKey = $"{MediaUrls.UploadPrefix}{establishmentId}/{fileId}_thumb.webp";
                await PutAsync(posterKey, posterImage.Value.Bytes, ct);
                await PutAsync(thumbKey, thumb.Value.Bytes, ct);
            }

            // S3 needs the length up front, so the upload (capped at maxBytes) is spooled to a temp file first.
            var spool = Path.Combine(Path.GetTempPath(), $"kekevideo-{fileId:N}.tmp");
            try
            {
                await using (var copy = File.Create(spool))
                {
                    using var limited = new LengthLimitedStream(video, header.AsMemory(0, read), maxBytes);
                    await limited.CopyToAsync(copy, ct);
                }

                await using var spooled = File.OpenRead(spool);
                await storage.PutAsync(StorageBuckets.PublicMedia, key, spooled, detected.ContentType, ct);
            }
            finally
            {
                File.Delete(spool);
            }
        }
        catch (LengthLimitedStream.TooLargeException)
        {
            await DeleteQuietlyAsync(key, posterKey, thumbKey);
            return Result.Failure<PartnerMediaItem>(new Error("media.video_too_large", $"La vidéo dépasse {maxBytes / 1024 / 1024} Mo."));
        }
        catch
        {
            await DeleteQuietlyAsync(key, posterKey, thumbKey);
            throw;
        }

        var media = new Media
        {
            EstablishmentId = establishmentId,
            Kind = MediaKind.Video,
            StorageKey = key,
            ThumbKey = thumbKey,
            PosterKey = posterKey,
            DurationSeconds = durationSeconds is > 0 and <= 3600 ? durationSeconds : null,
            SortOrder = existing.Count == 0 ? 0 : existing.Max() + 1,
        };

        try
        {
            db.MediaItems.Add(media);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await DeleteQuietlyAsync(key, posterKey, thumbKey);
            throw;
        }

        return Result.Success(ToItem(media));
    }

    public async Task<Result> DeleteAsync(Guid userId, Guid establishmentId, Guid mediaId, CancellationToken ct)
    {
        var status = await PartnerSupport.OwnedBy(db, userId, establishmentId).Select(e => (EstablishmentStatus?)e.Status).FirstOrDefaultAsync(ct);
        if (status is null)
        {
            return Result.Failure(PartnerErrors.NotFound);
        }

        if (!PartnerSupport.IsEditable(status.Value))
        {
            return Result.Failure(PartnerErrors.Locked);
        }

        var all = await db.MediaItems.Where(m => m.EstablishmentId == establishmentId)
            .OrderBy(m => m.SortOrder).ToListAsync(ct);
        var photos = all.Where(m => m.Kind == MediaKind.Photo).ToList();
        var media = all.FirstOrDefault(m => m.Id == mediaId);
        if (media is null)
        {
            return Result.Failure(MediaNotFound);
        }

        if (media.Kind == MediaKind.Photo && status == EstablishmentStatus.Active && photos.Count == 1)
        {
            return Result.Failure(new Error("media.last_photo", "Une fiche publiée doit garder au moins une photo."));
        }

        db.MediaItems.Remove(media);
        if (media.IsCover && media.Kind == MediaKind.Photo)
        {
            var next = photos.FirstOrDefault(m => m.Id != mediaId);
            if (next is not null)
            {
                next.IsCover = true;
            }
        }

        await db.SaveChangesAsync(ct);
        await DeleteQuietlyAsync(media.StorageKey, media.ThumbKey, media.PosterKey);
        return Result.Success();
    }

    public async Task<Result> SetCoverAsync(Guid userId, Guid establishmentId, Guid mediaId, CancellationToken ct)
    {
        var owned = await PartnerSupport.OwnedBy(db, userId, establishmentId).Select(e => (EstablishmentStatus?)e.Status).FirstOrDefaultAsync(ct);
        if (owned is null)
        {
            return Result.Failure(PartnerErrors.NotFound);
        }

        if (!PartnerSupport.IsEditable(owned.Value))
        {
            return Result.Failure(PartnerErrors.Locked);
        }

        var photos = await db.MediaItems.Where(m => m.EstablishmentId == establishmentId && m.Kind == MediaKind.Photo).ToListAsync(ct);
        if (photos.All(m => m.Id != mediaId))
        {
            return Result.Failure(MediaNotFound);
        }

        foreach (var photo in photos)
        {
            photo.IsCover = photo.Id == mediaId;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> ReorderAsync(Guid userId, Guid establishmentId, OrderInput input, CancellationToken ct)
    {
        var owned = await PartnerSupport.OwnedBy(db, userId, establishmentId).Select(e => (EstablishmentStatus?)e.Status).FirstOrDefaultAsync(ct);
        if (owned is null)
        {
            return Result.Failure(PartnerErrors.NotFound);
        }

        if (!PartnerSupport.IsEditable(owned.Value))
        {
            return Result.Failure(PartnerErrors.Locked);
        }

        var photos = await db.MediaItems.Where(m => m.EstablishmentId == establishmentId && m.Kind == MediaKind.Photo).ToListAsync(ct);
        var ids = input.Ids ?? [];
        if (ids.Distinct().Count() != ids.Count || ids.Any(id => photos.All(p => p.Id != id)))
        {
            return Result.Failure(PartnerErrors.Invalid("Liste d'identifiants invalide."));
        }

        var position = 0;
        foreach (var id in ids)
        {
            photos.First(p => p.Id == id).SortOrder = position++;
        }

        foreach (var rest in photos.Where(p => !ids.Contains(p.Id)).OrderBy(p => p.SortOrder))
        {
            rest.SortOrder = position++;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private PartnerMediaItem ToItem(Media media) => new(
        media.Id,
        MediaUrls.Build(_mediaBaseUrl, media.StorageKey),
        media.ThumbKey is null ? null : MediaUrls.Build(_mediaBaseUrl, media.ThumbKey),
        media.IsCover,
        media.SortOrder,
        media.Kind.ToString(),
        media.PosterKey is null ? null : MediaUrls.Build(_mediaBaseUrl, media.PosterKey),
        media.DurationSeconds);

    private async Task PutAsync(string key, byte[] bytes, CancellationToken ct)
    {
        using var stream = new MemoryStream(bytes);
        await storage.PutAsync(StorageBuckets.PublicMedia, key, stream, ImageProcessor.WebpContentType, ct);
    }

    private async Task DeleteQuietlyAsync(params string?[] keys)
    {
        // Orphaned objects are harmless; a storage hiccup must not fail a request whose database change succeeded.
        foreach (var k in keys.Where(k => k is not null))
        {
            try
            {
                await storage.DeleteAsync(StorageBuckets.PublicMedia, k!);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Intentionally ignored (see above).
            }
        }
    }
}

/// <summary>Replays the already-read header, then the rest of the upload, and fails once more than the limit has been read.</summary>
internal sealed class LengthLimitedStream(Stream inner, ReadOnlyMemory<byte> head, long limit) : Stream
{
    private int _headRead;
    private long _total;

    public sealed class TooLargeException : Exception;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int n;
        if (_headRead < head.Length)
        {
            n = Math.Min(buffer.Length, head.Length - _headRead);
            head.Slice(_headRead, n).CopyTo(buffer);
            _headRead += n;
        }
        else
        {
            n = await inner.ReadAsync(buffer, cancellationToken);
        }

        _total += n;
        return _total > limit ? throw new TooLargeException() : n;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
