using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using KekeBeauty.Application.Geo;
using Microsoft.Extensions.Caching.Memory;

namespace KekeBeauty.Infrastructure.Geo;

/// <summary>Client Nominatim (OpenStreetMap) pour le geocodage direct/inverse, borne a la Cote
/// d'Ivoire. Respecte la politique d'usage Nominatim : User-Agent identifiant l'app (configure sur
/// le HttpClient, voir Program.cs), resultats mis en cache (evite les appels repetes pour la meme
/// requete) - jamais d'appel en boucle/bulk depuis le code serveur.</summary>
public sealed class NominatimGeocodingService : IGeocodingService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;

    public NominatimGeocodingService(HttpClient httpClient, IMemoryCache cache)
    {
        _httpClient = httpClient;
        _cache = cache;
    }

    public async Task<IReadOnlyList<GeoSuggestion>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var normalized = query.Trim();
        if (normalized.Length < 3)
        {
            return [];
        }

        var cacheKey = $"geo:search:{normalized.ToLowerInvariant()}";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<GeoSuggestion>? cached) && cached is not null)
        {
            return cached;
        }

        // countrycodes=ci : borne les resultats a la Cote d'Ivoire pour eviter le bruit d'adresses
        // homonymes ailleurs dans le monde.
        var url = "search?format=jsonv2&countrycodes=ci&limit=5&q=" + Uri.EscapeDataString(normalized);
        var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var payload = await response.Content.ReadFromJsonAsync<List<NominatimResult>>(cancellationToken: cancellationToken) ?? [];
        var results = payload
            .Where(r => r.Lat is not null && r.Lon is not null)
            .Select(r => new GeoSuggestion(
                r.DisplayName ?? normalized,
                decimal.Parse(r.Lat!, CultureInfo.InvariantCulture),
                decimal.Parse(r.Lon!, CultureInfo.InvariantCulture)))
            .ToList();

        _cache.Set(cacheKey, (IReadOnlyList<GeoSuggestion>)results, CacheDuration);
        return results;
    }

    public async Task<string?> ReverseAsync(decimal latitude, decimal longitude, CancellationToken cancellationToken)
    {
        var cacheKey = $"geo:reverse:{latitude:F6}:{longitude:F6}";
        if (_cache.TryGetValue(cacheKey, out string? cached))
        {
            return cached;
        }

        var url = $"reverse?format=jsonv2&lat={latitude.ToString(CultureInfo.InvariantCulture)}&lon={longitude.ToString(CultureInfo.InvariantCulture)}";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<NominatimResult>(cancellationToken: cancellationToken);
        var address = payload?.DisplayName;
        _cache.Set(cacheKey, address, CacheDuration);
        return address;
    }

    private sealed class NominatimResult
    {
        [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
        [JsonPropertyName("lat")] public string? Lat { get; set; }
        [JsonPropertyName("lon")] public string? Lon { get; set; }
    }
}
