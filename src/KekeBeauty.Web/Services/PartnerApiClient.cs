using System.Net.Http.Json;
using KekeBeauty.Web.Models;

namespace KekeBeauty.Web.Services;

public sealed class PartnerApiClient
{
    private readonly HttpClient _httpClient;

    public PartnerApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<(bool Success, List<EtablissementGere> Etablissements)> GetMesEtablissementsAsync(Guid idPartner, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "partenaire/etablissements");
            request.Headers.Add("X-Partner-Id", idPartner.ToString());

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (false, []);
            }

            var etablissements = await response.Content.ReadFromJsonAsync<List<EtablissementGere>>(cancellationToken);
            return (true, etablissements ?? []);
        }
        catch (Exception)
        {
            return (false, []);
        }
    }

    public async Task<(bool Success, EtablissementDetail? Detail)> GetDetailAsync(Guid idEtablissement, CancellationToken cancellationToken)
    {
        // Reutilise l'endpoint annuaire public (005) : suffisant pour lire prestations/medias.
        // Les etablissements en attente de validation KYC ou suspendus n'y apparaitront pas -
        // limitation connue, voir tasks.md Polish pour une future evolution si necessaire.
        try
        {
            var response = await _httpClient.GetAsync($"etablissements/{idEtablissement}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (true, null);
            }

            return (true, await response.Content.ReadFromJsonAsync<EtablissementDetail>(cancellationToken));
        }
        catch (Exception)
        {
            return (false, null);
        }
    }

    public async Task<(bool Success, string? Status)> AddPrestationAsync(
        Guid idPartner, Guid idEtablissement, string libelle, decimal tarif, short dureeMinutes, CancellationToken cancellationToken)
    {
        return await SendPartnerRequestAsync(idPartner, HttpMethod.Post, $"partenaire/etablissements/{idEtablissement}/prestations",
            new { libellePrestation = libelle, tarif, dureeMinutes }, cancellationToken);
    }

    public async Task<(bool Success, string? Status)> UpdatePrestationAsync(
        Guid idPartner, Guid idEtablissement, Guid idPrestation, string libelle, decimal tarif, short dureeMinutes, CancellationToken cancellationToken)
    {
        return await SendPartnerRequestAsync(idPartner, HttpMethod.Put, $"partenaire/etablissements/{idEtablissement}/prestations/{idPrestation}",
            new { libellePrestation = libelle, tarif, dureeMinutes }, cancellationToken);
    }

    public async Task<(bool Success, string? Status)> DeletePrestationAsync(
        Guid idPartner, Guid idEtablissement, Guid idPrestation, CancellationToken cancellationToken)
    {
        return await SendPartnerRequestAsync(idPartner, HttpMethod.Delete, $"partenaire/etablissements/{idEtablissement}/prestations/{idPrestation}",
            body: null, cancellationToken);
    }

    public async Task<(bool Success, string? Status)> AssignCategoryAsync(
        Guid idPartner, Guid idEtablissement, string libelleCategorie, CancellationToken cancellationToken)
    {
        return await SendPartnerRequestAsync(idPartner, HttpMethod.Post, $"partenaire/etablissements/{idEtablissement}/categories",
            new { libelleCategorie }, cancellationToken);
    }

    private async Task<(bool Success, string? Status)> SendPartnerRequestAsync(
        Guid idPartner, HttpMethod method, string url, object? body, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, url);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }
            request.Headers.Add("X-Partner-Id", idPartner.ToString());

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return (true, null);
            }

            var errorBody = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken);
            return (false, errorBody?.GetValueOrDefault("status") ?? response.StatusCode.ToString());
        }
        catch (Exception)
        {
            return (false, "network_error");
        }
    }
}
