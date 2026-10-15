using Microsoft.Extensions.Configuration;

namespace KekeBeauty.Infrastructure.Partner;

/// <summary>
/// Turns a stored media key into a URL. Uploaded files (<c>uploads/...</c>) are served by the API; demonstration
/// visuals are static files served by the web front-end under <c>Media:BaseUrl</c>.
/// </summary>
public static class MediaUrls
{
    public const string UploadPrefix = "uploads/";
    public const string FilesRoute = "/api/v1/files";

    public static string BaseUrl(IConfiguration configuration) => (configuration["Media:BaseUrl"] ?? "/media").TrimEnd('/');

    public static string Build(string baseUrl, string key)
    {
        var trimmed = key.TrimStart('/');
        return trimmed.StartsWith(UploadPrefix, StringComparison.Ordinal) ? $"{FilesRoute}/{trimmed}" : $"{baseUrl}/{trimmed}";
    }
}
