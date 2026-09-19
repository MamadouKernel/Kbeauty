using System.Net.Http.Json;
using KekeBeauty.Web.Models;

namespace KekeBeauty.Web.Services;

public sealed class AdminApiClient
{
    private readonly HttpClient _httpClient;

    public AdminApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>Verifie la validite de la cle en tentant un appel admin reel. Success=false si la
    /// cle est invalide/absente ou l'API indisponible (l'API ne distingue pas les deux cas non plus).</summary>
    public async Task<bool> VerifyApiKeyAsync(string apiKey, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "admin/abonnements");
            request.Headers.Add("X-Admin-Api-Key", apiKey);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public Task<(bool Success, List<ApplicationSummary> Dossiers)> ListDossiersAsync(string apiKey, string statut, CancellationToken cancellationToken) =>
        GetListAsync<ApplicationSummary>(apiKey, $"admin/applications?statut={Uri.EscapeDataString(statut)}", cancellationToken);

    public async Task<(bool Success, ApplicationDetail? Detail)> GetDossierAsync(string apiKey, Guid id, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"admin/applications/{id}");
            request.Headers.Add("X-Admin-Api-Key", apiKey);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return (true, null);
            }
            if (!response.IsSuccessStatusCode)
            {
                return (false, null);
            }
            return (true, await response.Content.ReadFromJsonAsync<ApplicationDetail>(cancellationToken));
        }
        catch (Exception)
        {
            return (false, null);
        }
    }

    public Task<(bool Success, string? Status)> ValiderDossierAsync(string apiKey, Guid id, CancellationToken cancellationToken) =>
        PostAsync(apiKey, $"admin/applications/{id}/validate", null, cancellationToken);

    public Task<(bool Success, string? Status)> RejeterDossierAsync(string apiKey, Guid id, CancellationToken cancellationToken) =>
        PostAsync(apiKey, $"admin/applications/{id}/reject", null, cancellationToken);

    public Task<(bool Success, string? Status)> SuspendreUtilisateurAsync(string apiKey, Guid id, CancellationToken cancellationToken) =>
        PostAsync(apiKey, $"admin/utilisateurs/{id}/suspendre", null, cancellationToken);

    public Task<(bool Success, string? Status)> ReactiverUtilisateurAsync(string apiKey, Guid id, CancellationToken cancellationToken) =>
        PostAsync(apiKey, $"admin/utilisateurs/{id}/reactiver", null, cancellationToken);

    public Task<(bool Success, string? Status)> SuspendreEtablissementAsync(string apiKey, Guid id, CancellationToken cancellationToken) =>
        PostAsync(apiKey, $"admin/etablissements/{id}/suspendre", null, cancellationToken);

    public Task<(bool Success, string? Status)> ReactiverEtablissementAsync(string apiKey, Guid id, CancellationToken cancellationToken) =>
        PostAsync(apiKey, $"admin/etablissements/{id}/reactiver", null, cancellationToken);

    public Task<(bool Success, List<AbonnementResume> Abonnements)> ListAbonnementsAsync(string apiKey, string? statut, CancellationToken cancellationToken)
    {
        var url = "admin/abonnements" + (string.IsNullOrWhiteSpace(statut) ? "" : $"?statut={Uri.EscapeDataString(statut)}");
        return GetListAsync<AbonnementResume>(apiKey, url, cancellationToken);
    }

    public async Task<(bool Success, bool NotificationEnvoyee)> RelancerAsync(string apiKey, Guid idAbonnement, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"admin/abonnements/{idAbonnement}/relance");
            request.Headers.Add("X-Admin-Api-Key", apiKey);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (false, false);
            }
            var body = await response.Content.ReadFromJsonAsync<Dictionary<string, bool>>(cancellationToken);
            return (true, body?.GetValueOrDefault("notificationEnvoyee") ?? false);
        }
        catch (Exception)
        {
            return (false, false);
        }
    }

    public Task<(bool Success, List<TarifStandard> Tarifs)> ListerTarifsAsync(string apiKey, CancellationToken cancellationToken) =>
        GetListAsync<TarifStandard>(apiKey, "admin/tarifs", cancellationToken);

    public async Task<(bool Success, string? Status)> UpdateTarifAsync(string apiKey, string periodicite, decimal montant, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, $"admin/tarifs/{periodicite}")
            {
                Content = JsonContent.Create(new { montant })
            };
            request.Headers.Add("X-Admin-Api-Key", apiKey);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode ? (true, null) : (false, "invalid_montant");
        }
        catch (Exception)
        {
            return (false, "network_error");
        }
    }

    private async Task<(bool Success, List<T> Items)> GetListAsync<T>(string apiKey, string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Admin-Api-Key", apiKey);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (false, []);
            }
            var items = await response.Content.ReadFromJsonAsync<List<T>>(cancellationToken);
            return (true, items ?? []);
        }
        catch (Exception)
        {
            return (false, []);
        }
    }

    private async Task<(bool Success, string? Status)> PostAsync(string apiKey, string url, object? body, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }
            request.Headers.Add("X-Admin-Api-Key", apiKey);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return (true, null);
            }
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return (false, "not_found");
            }
            var errorBody = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken);
            return (false, errorBody?.GetValueOrDefault("status") ?? "error");
        }
        catch (Exception)
        {
            return (false, "network_error");
        }
    }
}
