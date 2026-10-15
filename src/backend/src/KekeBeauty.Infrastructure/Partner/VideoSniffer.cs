namespace KekeBeauty.Infrastructure.Partner;

/// <summary>
/// Recognises the video containers accepted on listings (MP4 and WebM) from their first bytes, whatever the file name or declared type say.
/// Nothing else is stored, and the type served later comes from the extension we chose, never from the upload.
/// </summary>
public static class VideoSniffer
{
    public const string Mp4 = "video/mp4";
    public const string WebM = "video/webm";
    public const int HeaderLength = 12;

    // The "ftyp" brands of video files. Image brands (heic, avif, mif1...) share the container but must be refused.
    private static readonly string[] Mp4Brands = ["isom", "iso2", "iso3", "iso4", "iso5", "iso6", "mp41", "mp42", "avc1", "dash", "MSNV", "M4V "];

    public sealed record Detected(string ContentType, string Extension);

    public static Detected? Detect(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 4 && header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3)
        {
            return new Detected(WebM, "webm");
        }

        if (header.Length >= HeaderLength && header[4] == (byte)'f' && header[5] == (byte)'t' && header[6] == (byte)'y' && header[7] == (byte)'p')
        {
            var brand = System.Text.Encoding.ASCII.GetString(header.Slice(8, 4));
            return Mp4Brands.Contains(brand, StringComparer.Ordinal) ? new Detected(Mp4, "mp4") : null;
        }

        return null;
    }

    /// <summary>The type to serve for a stored key, or null when the key is not a video.</summary>
    public static string? ContentTypeForKey(string key) =>
        key.EndsWith(".mp4", StringComparison.Ordinal) ? Mp4
        : key.EndsWith(".webm", StringComparison.Ordinal) ? WebM
        : null;
}
