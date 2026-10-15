using KekeBeauty.SharedKernel;
using SkiaSharp;

namespace KekeBeauty.Infrastructure.Partner;

/// <summary>An image re-encoded as WebP (EXIF and any appended payload are dropped by the re-encoding).</summary>
public sealed record ProcessedImage(byte[] Bytes, int Width, int Height);

/// <summary>
/// Validates uploads by their real content (never the client-supplied type or file name) and re-encodes them.
/// Only static JPEG, PNG and WebP are accepted; dimensions are read before decoding to refuse decompression bombs.
/// </summary>
public static class ImageProcessor
{
    public const long MaxUploadBytes = 10 * 1024 * 1024;
    public const int MaxSourceDimension = 12_000;
    public const long MaxSourcePixels = 40_000_000;
    public const int MediumWidth = 1600;
    public const int ThumbWidth = 400;
    public const int DocumentWidth = 2400;
    public const int WebpQuality = 82;
    public const string WebpContentType = "image/webp";

    private static readonly Error TooLarge = new("file.too_large", "Le fichier dépasse 10 Mo.");
    private static readonly Error Unsupported = new("file.unsupported", "Format non pris en charge : utilisez une image JPEG, PNG ou WebP valide.");

    public static async Task<Result<ProcessedImage>> ProcessAsync(Stream source, int maxWidth, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxUploadBytes)
            {
                return Result.Failure<ProcessedImage>(TooLarge);
            }

            buffer.Write(chunk, 0, read);
        }

        var bytes = buffer.ToArray();
        return await Task.Run(() => Reencode(bytes, maxWidth), ct);
    }

    private static Result<ProcessedImage> Reencode(byte[] bytes, int maxWidth)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null
            || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp)
            || codec.FrameCount > 1)
        {
            return Result.Failure<ProcessedImage>(Unsupported);
        }

        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0
            || info.Width > MaxSourceDimension || info.Height > MaxSourceDimension
            || (long)info.Width * info.Height > MaxSourcePixels)
        {
            return Result.Failure<ProcessedImage>(Unsupported);
        }

        using var decoded = SKBitmap.Decode(codec);
        if (decoded is null)
        {
            return Result.Failure<ProcessedImage>(Unsupported);
        }

        using var oriented = Orient(decoded, codec.EncodedOrigin);
        var target = oriented;
        SKBitmap? resized = null;
        try
        {
            if (oriented.Width > maxWidth)
            {
                var height = Math.Max(1, (int)Math.Round((double)oriented.Height * maxWidth / oriented.Width));
                resized = oriented.Resize(new SKImageInfo(maxWidth, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                target = resized ?? oriented;
            }

            using var image = SKImage.FromBitmap(target);
            using var encoded = image.Encode(SKEncodedImageFormat.Webp, WebpQuality);
            return encoded is null
                ? Result.Failure<ProcessedImage>(Unsupported)
                : Result.Success(new ProcessedImage(encoded.ToArray(), target.Width, target.Height));
        }
        finally
        {
            resized?.Dispose();
        }
    }

    /// <summary>Applies the EXIF orientation so phone photos are stored upright (the metadata itself is dropped).</summary>
    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default)
        {
            return source.Copy();
        }

        float w = source.Width, h = source.Height;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var matrix = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            _ => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
        };

        var result = new SKBitmap(swap ? source.Height : source.Width, swap ? source.Width : source.Height);
        using var canvas = new SKCanvas(result);
        canvas.Concat(in matrix);
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), null);
        return result;
    }
}
