using System.Net.Http.Json;
using KekeBeauty.Web.Models;

namespace KekeBeauty.Web.Services;

public sealed class DirectoryApiClient
{
    private readonly HttpClient _httpClient;

    public DirectoryApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>Retourne une liste vide en cas d'erreur reseau/API (FR-008 gere par l'appelant via le booleen out).</summary>
    public async Task<(bool Success, List<EtablissementSummary> Results)> SearchAsync(
        string? categorie, string? commune, CancellationToken cancellationToken)
    {
        try
        {
            var query = new List<string>();
            if (!string.IsNullOrWhiteSpace(categorie)) query.Add($"categorie={Uri.EscapeDataString(categorie)}");
            if (!string.IsNullOrWhiteSpace(commune)) query.Add($"commune={Uri.EscapeDataString(commune)}");
            var url = "etablissements" + (query.Count > 0 ? "?" + string.Join("&", query) : "");

            var results = await _httpClient.GetFromJsonAsync<List<EtablissementSummary>>(url, cancellationToken);
            return (true, results ?? []);
        }
        catch (Exception)
        {
            return (false, []);
        }
    }

    /// <summary>Distingue "introuvable" (404, Success=true/Detail=null) d'une panne reseau/API
    /// (Success=false) pour que l'appelant affiche le bon message (FR-008).</summary>
    public async Task<(bool Success, EtablissementDetail? Detail)> GetDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync($"etablissements/{id}", cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return (true, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                return (false, null);
            }

            return (true, await response.Content.ReadFromJsonAsync<EtablissementDetail>(cancellationToken));
        }
        catch (Exception)
        {
            return (false, null);
        }
    }
}
