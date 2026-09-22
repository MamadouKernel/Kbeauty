using System.Net.Http.Json;
using KekeBeauty.Web.Models;

namespace KekeBeauty.Web.Services;

public sealed class DirectoryApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ClientSessionService _session;

    public DirectoryApiClient(HttpClient httpClient, ClientSessionService session)
    {
        _httpClient = httpClient;
        _session = session;
    }

    // Le jeton est attache ici (typed client, scope DI correct du circuit) plutot que dans
    // ClientAuthHandler : IHttpClientFactory construit les DelegatingHandler via un scope DI mis
    // en cache/tourniquet distinct du circuit Blazor courant, donc ProtectedLocalStorage
    // (IJSRuntime) y echoue silencieusement - le jeton n'atteint jamais l'API.
    private async Task AddTokenAsync(HttpRequestMessage request)
    {
        var token = await _session.GetTokenAsync();
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.TryAddWithoutValidation("X-Client-Token", token);
        }
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

    public async Task<(bool Success, List<EtablissementSummary> Results)> GetFavorisAsync(Guid idClient, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "etablissements/favoris");
            request.Headers.Add("X-Client-Id", idClient.ToString());
            await AddTokenAsync(request);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return (false, []);
            return (true, await response.Content.ReadFromJsonAsync<List<EtablissementSummary>>(cancellationToken) ?? []);
        }
        catch (Exception) { return (false, []); }
    }
    /// <summary>Feature 014 (US2) : avis publics d'un etablissement (note moyenne + liste). Jamais
    /// de valeur inventee : NoteMoyenne reste null tant qu'aucun avis reel n'existe.</summary>
    public async Task<(bool Success, AvisEtablissement? Avis)> GetAvisAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync($"etablissements/{idEtablissement}/avis", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (false, null);
            }

            return (true, await response.Content.ReadFromJsonAsync<AvisEtablissement>(cancellationToken));
        }
        catch (Exception)
        {
            return (false, null);
        }
    }

    /// <summary>Distingue "introuvable" (404, Success=true/Detail=null) d'une panne reseau/API
    /// (Success=false) pour que l'appelant affiche le bon message (FR-008). idClient optionnel
    /// (endpoint public) : si fourni, EstFavori reflete l'etat reel pour ce client.</summary>
    public async Task<(bool Success, EtablissementDetail? Detail)> GetDetailAsync(Guid id, Guid? idClient, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"etablissements/{id}");
            if (idClient is not null)
            {
                request.Headers.Add("X-Client-Id", idClient.Value.ToString());
                await AddTokenAsync(request);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
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

    /// <summary>Parcours 1 : favoris client. Retourne le nouvel etat (true = ajoute au favoris).</summary>
    public async Task<(bool Success, bool EstFavori)> ToggleFavoriAsync(Guid idClient, Guid idEtablissement, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"etablissements/{idEtablissement}/favori");
            request.Headers.Add("X-Client-Id", idClient.ToString());
            await AddTokenAsync(request);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (false, false);
            }

            var body = await response.Content.ReadFromJsonAsync<Dictionary<string, bool>>(cancellationToken);
            return (true, body?.GetValueOrDefault("estFavori") ?? false);
        }
        catch (Exception)
        {
            return (false, false);
        }
    }
}
