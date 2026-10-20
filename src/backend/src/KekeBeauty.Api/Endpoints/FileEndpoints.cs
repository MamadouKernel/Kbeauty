using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.SharedKernel;

namespace KekeBeauty.Api.Endpoints;

/// <summary>Public photos uploaded by partners. Only the <c>uploads/</c> namespace of the public bucket is reachable.</summary>
public static class FileEndpoints
{
    public static IEndpointRouteBuilder MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/files/{**key}", async (string key, IFileStorage storage, HttpContext http, CancellationToken ct) =>
            {
                if (!key.StartsWith(MediaUrls.UploadPrefix, StringComparison.Ordinal) || key.Contains("..", StringComparison.Ordinal) || key.Contains('\\'))
                {
                    return Results.NotFound();
                }

                // File names embed a unique id, so a given URL never changes content.
                http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                http.Response.Headers.XContentTypeOptions = "nosniff";

                var videoType = VideoSniffer.ContentTypeForKey(key);
                if (videoType is not null)
                {
                    return await StreamVideoAsync(key, videoType, storage, http, ct);
                }

                var file = await storage.OpenReadAsync(StorageBuckets.PublicMedia, key, ct);
                return file is null ? Results.NotFound() : Results.Stream(file.Content, ImageProcessor.WebpContentType);
            })
            .WithTags("Files")
            .AllowAnonymous()
            .Produces(StatusCodes.Status200OK, contentType: ImageProcessor.WebpContentType)
            .Produces(StatusCodes.Status206PartialContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>Videos are served in pieces (HTTP range requests) so that players can start, seek and never download more than needed.</summary>
    private static async Task<IResult> StreamVideoAsync(string key, string contentType, IFileStorage storage, HttpContext http, CancellationToken ct)
    {
        var info = await storage.StatAsync(StorageBuckets.PublicMedia, key, ct);
        if (info is null)
        {
            return Results.NotFound();
        }

        http.Response.Headers.AcceptRanges = "bytes";
        long from = 0;
        var to = info.Length - 1;
        var partial = false;
        if (http.Request.Headers.Range.ToString() is { Length: > 0 } header)
        {
            if (!TryParseRange(header, info.Length, out from, out to))
            {
                http.Response.Headers.ContentRange = $"bytes */{info.Length}";
                return Results.StatusCode(StatusCodes.Status416RangeNotSatisfiable);
            }

            partial = true;
        }

        var stream = await storage.OpenRangeAsync(StorageBuckets.PublicMedia, key, from, to, ct);
        if (stream is null)
        {
            return Results.NotFound();
        }

        http.Response.StatusCode = partial ? StatusCodes.Status206PartialContent : StatusCodes.Status200OK;
        http.Response.ContentType = contentType;
        http.Response.ContentLength = to - from + 1;
        if (partial)
        {
            http.Response.Headers.ContentRange = $"bytes {from}-{to}/{info.Length}";
        }

        await using (stream)
        {
            await stream.CopyToAsync(http.Response.Body, ct);
        }

        return Results.Empty;
    }

    /// <summary>One range only (<c>bytes=a-b</c>, <c>bytes=a-</c> or <c>bytes=-n</c>); anything else is refused.</summary>
    public static bool TryParseRange(string header, long length, out long from, out long to)
    {
        from = 0;
        to = length - 1;
        const string prefix = "bytes=";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || header.Contains(',', StringComparison.Ordinal) || length <= 0)
        {
            return false;
        }

        var parts = header[prefix.Length..].Split('-', 2);
        if (parts.Length != 2)
        {
            return false;
        }

        if (parts[0].Length == 0)
        {
            if (!long.TryParse(parts[1], out var suffix) || suffix <= 0)
            {
                return false;
            }

            from = Math.Max(0, length - suffix);
            return true;
        }

        if (!long.TryParse(parts[0], out from) || from < 0 || from >= length)
        {
            return false;
        }

        if (parts[1].Length > 0)
        {
            if (!long.TryParse(parts[1], out to) || to < from)
            {
                return false;
            }

            to = Math.Min(to, length - 1);
        }

        return true;
    }
}
