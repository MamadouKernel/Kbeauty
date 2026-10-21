using System.Globalization;
using System.Text.Json;
using KekeBeauty.SharedKernel;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace KekeBeauty.Infrastructure.Geo;

public sealed class GeocodingOptions
{
    public const string SectionName = "Geo";

    /// <summary>Address search server: OpenStreetMap's Nominatim by default, or one of our own.</summary>
    public string BaseUrl { get; set; } = "https://nominatim.openstreetmap.org";

    /// <summary>Nominatim's usage policy requires a User-Agent that identifies the application and a way to reach its operator.</summary>
    public string UserAgent { get; set; } = "KekeBeauty/1.0 (+https://kekebeauty.example; contact configured by the operator)";

    /// <summary>Comma-separated ISO country codes the search is limited to.</summary>
    public string CountryCodes { get; set; } = "ci";

    /// <summary>Minimum delay between two calls to the server (the public Nominatim allows one per second).</summary>
    public int MinIntervalMilliseconds { get; set; } = 1100;

    public int CacheHours { get; set; } = 24;
}

public sealed record GeoPlace(string Label, double Latitude, double Longitude);

/// <summary>
/// Address search and reverse lookup for the partner address form (V1 <c>geo/search</c> and <c>geo/reverse</c>). The browser never
/// talks to a third party: the API does, politely (identified, one call at a time, at most one per second, answers cached).
/// </summary>
public sealed class GeocodingService(IHttpClientFactory clients, IMemoryCache cache, IOptions<GeocodingOptions> options)
{
    public const string ClientName = "geocoding";
    public const int MinQueryLength = 3;
    public const int MaxQueryLength = 120;
    private const int MaxResults = 5;

    private static readonly Error ProviderError = new("geo.provider_error", "La recherche d'adresse est momentanément indisponible. Placez le repère sur la carte.");

    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTimeOffset lastCall = DateTimeOffset.MinValue;

    public async Task<Result<IReadOnlyList<GeoPlace>>> SearchAsync(string? query, CancellationToken ct)
    {
        var text = Normalize(query);
        if (text is null)
        {
            return Result.Failure<IReadOnlyList<GeoPlace>>(new Error("validation.failed", $"Saisissez entre {MinQueryLength} et {MaxQueryLength} caractères."));
        }

        var o = options.Value;
        var url = $"search?format=jsonv2&limit={MaxResults}&addressdetails=0&accept-language=fr&countrycodes={Uri.EscapeDataString(o.CountryCodes)}&q={Uri.EscapeDataString(text)}";
        return await CachedAsync($"geo:s:{text.ToLowerInvariant()}", url, ParseSearch, ct);
    }

    public async Task<Result<IReadOnlyList<GeoPlace>>> ReverseAsync(double latitude, double longitude, CancellationToken ct)
    {
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            return Result.Failure<IReadOnlyList<GeoPlace>>(new Error("validation.failed", "Coordonnées invalides."));
        }

        // About a metre: precise enough for an address, and it makes neighbouring clicks share one cached answer.
        var lat = Math.Round(latitude, 5);
        var lon = Math.Round(longitude, 5);
        var url = $"reverse?format=jsonv2&zoom=18&addressdetails=0&accept-language=fr&lat={lat.ToString(CultureInfo.InvariantCulture)}&lon={lon.ToString(CultureInfo.InvariantCulture)}";
        return await CachedAsync($"geo:r:{lat.ToString(CultureInfo.InvariantCulture)}:{lon.ToString(CultureInfo.InvariantCulture)}", url, ParseReverse, ct);
    }

    /// <summary>Trimmed, whitespace collapsed, control characters refused; <c>null</c> when the length is out of bounds.</summary>
    public static string? Normalize(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var cleaned = string.Join(' ', query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return cleaned.Length is < MinQueryLength or > MaxQueryLength || cleaned.Any(char.IsControl) ? null : cleaned;
    }

    private async Task<Result<IReadOnlyList<GeoPlace>>> CachedAsync(string key, string relativeUrl, Func<JsonElement, IReadOnlyList<GeoPlace>> parse, CancellationToken ct)
    {
        if (cache.TryGetValue(key, out IReadOnlyList<GeoPlace>? hit) && hit is not null)
        {
            return Result.Success(hit);
        }

        var o = options.Value;
        await gate.WaitAsync(ct);
        try
        {
            // Another caller may have filled the cache while we were waiting for our turn.
            if (cache.TryGetValue(key, out hit) && hit is not null)
            {
                return Result.Success(hit);
            }

            var wait = lastCall + TimeSpan.FromMilliseconds(o.MinIntervalMilliseconds) - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct);
            }

            try
            {
                using var client = clients.CreateClient(ClientName);
                using var response = await client.GetAsync(relativeUrl, ct);
                lastCall = DateTimeOffset.UtcNow;
                if (!response.IsSuccessStatusCode)
                {
                    return Result.Failure<IReadOnlyList<GeoPlace>>(ProviderError);
                }

                await using var body = await response.Content.ReadAsStreamAsync(ct);
                using var json = await JsonDocument.ParseAsync(body, cancellationToken: ct);
                var places = parse(json.RootElement);
                cache.Set(key, places, TimeSpan.FromHours(o.CacheHours));
                return Result.Success(places);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                lastCall = DateTimeOffset.UtcNow;
                return Result.Failure<IReadOnlyList<GeoPlace>>(ProviderError);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static IReadOnlyList<GeoPlace> ParseSearch(JsonElement root)
    {
        var places = new List<GeoPlace>();
        if (root.ValueKind != JsonValueKind.Array)
        {
            return places;
        }

        foreach (var item in root.EnumerateArray())
        {
            if (ToPlace(item) is { } place)
            {
                places.Add(place);
            }

            if (places.Count == MaxResults)
            {
                break;
            }
        }

        return places;
    }

    private static IReadOnlyList<GeoPlace> ParseReverse(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && ToPlace(root) is { } place ? [place] : [];

    private static GeoPlace? ToPlace(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("display_name", out var name) || name.ValueKind != JsonValueKind.String
            || !TryNumber(item, "lat", out var lat) || !TryNumber(item, "lon", out var lon)
            || lat is < -90 or > 90 || lon is < -180 or > 180)
        {
            return null;
        }

        var label = name.GetString()!.Trim();
        return label.Length == 0 ? null : new GeoPlace(label.Length > 300 ? label[..300] : label, lat, lon);
    }

    private static bool TryNumber(JsonElement item, string property, out double value)
    {
        value = 0;
        if (!item.TryGetProperty(property, out var element))
        {
            return false;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value),
            JsonValueKind.Number => element.TryGetDouble(out value) && double.IsFinite(value),
            _ => false,
        };
    }
}
