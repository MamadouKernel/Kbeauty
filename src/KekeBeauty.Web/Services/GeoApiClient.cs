using System.Net.Http.Json;

namespace KekeBeauty.Web.Services;

public sealed record GeoSuggestionDto(string DisplayName, decimal Latitude, decimal Longitude);

/// <summary>Proxy vers /geo/* (recherche/reverse geocoding OpenStreetMap-Nominatim, cote API).</summary>
public sealed class GeoApiClient
{
    private readonly HttpClient _httpClient;

    public GeoApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<GeoSuggestionDto>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 3)
        {
            return [];
        }

        try
        {
            var results = await _httpClient.GetFromJsonAsync<List<GeoSuggestionDto>>(
                $"geo/search?q={Uri.EscapeDataString(query.Trim())}", cancellationToken);
            return results ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<string?> ReverseAsync(decimal latitude, decimal longitude, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<Dictionary<string, string?>>(
                $"geo/reverse?lat={latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}&lng={longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                cancellationToken);
            return response?.GetValueOrDefault("address");
        }
        catch
        {
            return null;
        }
    }
}
