namespace KekeBeauty.Application.Geo;

public sealed record GeoSuggestion(string DisplayName, decimal Latitude, decimal Longitude);

public interface IGeocodingService
{
    /// <summary>Geocodage direct (adresse/lieu -> coordonnees), via Nominatim, borne a la Cote
    /// d'Ivoire. Retourne au plus 5 suggestions.</summary>
    Task<IReadOnlyList<GeoSuggestion>> SearchAsync(string query, CancellationToken cancellationToken);

    /// <summary>Geocodage inverse (coordonnees -> adresse lisible), via Nominatim.</summary>
    Task<string?> ReverseAsync(decimal latitude, decimal longitude, CancellationToken cancellationToken);
}
