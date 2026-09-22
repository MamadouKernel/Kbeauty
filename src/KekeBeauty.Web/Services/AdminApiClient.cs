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
    public async Task<(bool Success,string? Token,string? Role)> LoginAsync(string email,string password,CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "admin/auth/login") { Content=JsonContent.Create(new { email, motDePasse=password }) };
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if(!response.IsSuccessStatusCode)return(false,null,null);
            var body=await response.Content.ReadFromJsonAsync<Dictionary<string,string>>(cancellationToken);
            return(true,body?.GetValueOrDefault("token"),body?.GetValueOrDefault("role"));
        }
        catch (Exception)
        {
            return (false,null,null);
        }
    }

    /// <summary>Feature 017 : tableau de bord global (lecture seule, agregations SQL reelles).</summary>
    public async Task<(bool Success, AdminStatistiquesGlobalesDto? Stats)> GetStatistiquesGlobalesAsync(string apiKey, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "admin/statistiques");
            request.Headers.Add("X-Admin-Token", apiKey);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (false, null);
            }
            return (true, await response.Content.ReadFromJsonAsync<AdminStatistiquesGlobalesDto>(cancellationToken));
        }
        catch (Exception)
        {
            return (false, null);
        }
    }

    public Task<(bool Success, List<ApplicationSummary> Dossiers)> ListDossiersAsync(string apiKey, string statut, CancellationToken cancellationToken) =>
        GetListAsync<ApplicationSummary>(apiKey, $"admin/applications?statut={Uri.EscapeDataString(statut)}", cancellationToken);

    public async Task<(bool Success, ApplicationDetail? Detail)> GetDossierAsync(string apiKey, Guid id, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"admin/applications/{id}");
            request.Headers.Add("X-Admin-Token", apiKey);
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

    public async Task<(bool Success, string? DataUrl, string? ContentType)> GetDossierFilePreviewAsync(
        string apiKey, Guid id, string type, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"admin/applications/{id}/files/{type}");
            request.Headers.Add("X-Admin-Token", apiKey);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return (false, null, null);
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            return (true, $"data:{contentType};base64,{Convert.ToBase64String(bytes)}", contentType);
        }
        catch (Exception)
        {
            return (false, null, null);
        }
    }
    public Task<(bool Success, string? Status)> ValiderDossierAsync(string apiKey, Guid id, CancellationToken cancellationToken) =>
        PostAsync(apiKey, $"admin/applications/{id}/validate", null, cancellationToken);

    public Task<(bool Success, string? Status)> RejeterDossierAsync(string apiKey, Guid id, string motif, CancellationToken cancellationToken) =>
        PostAsync(apiKey, $"admin/applications/{id}/reject", new { motif }, cancellationToken);

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
            request.Headers.Add("X-Admin-Token", apiKey);
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

    public Task<(bool Success, List<ConfigurationFormuleDto> Formules)> ListerFormulesAsync(string apiKey, CancellationToken cancellationToken) =>
        GetListAsync<ConfigurationFormuleDto>(apiKey, "admin/formules", cancellationToken);

    public async Task<(bool Success, string? Status)> UpdateFormuleAsync(
        string apiKey, ConfigurationFormuleDto formule, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, $"admin/formules/{formule.Formule}")
            {
                Content = JsonContent.Create(new
                {
                    formule.Libelle,
                    formule.EstActif,
                    formule.OrdreAffichage,
                    formule.TarifMensuel,
                    formule.TarifAnnuel,
                    formule.LimitePrestations,
                    formule.LimiteRdvMensuels,
                    formule.PaiementMobile,
                    formule.GestionEquipe,
                    formule.StatistiquesAvancees,
                    formule.Avantages,
                    formule.PromoPourcentage,
                    formule.PromoFin,
                })
            };
            request.Headers.Add("X-Admin-Token", apiKey);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode ? (true, null) : (false, "configuration_invalide");
        }
        catch (Exception)
        {
            return (false, "network_error");
        }
    }

    public async Task<(bool Success, string? Status)> CreateFormuleAsync(
        string apiKey, ConfigurationFormuleDto formule, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "admin/formules")
            {
                Content = JsonContent.Create(new
                {
                    formule.Formule,
                    formule.Libelle,
                    formule.OrdreAffichage,
                    formule.TarifMensuel,
                    formule.TarifAnnuel,
                    formule.LimitePrestations,
                    formule.LimiteRdvMensuels,
                    formule.PaiementMobile,
                    formule.GestionEquipe,
                    formule.StatistiquesAvancees,
                    formule.Avantages,
                    formule.PromoPourcentage,
                    formule.PromoFin,
                })
            };
            request.Headers.Add("X-Admin-Token", apiKey);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) return (true, null);
            var errorBody = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken);
            return (false, errorBody?.GetValueOrDefault("status") ?? "error");
        }
        catch (Exception)
        {
            return (false, "network_error");
        }
    }

    public Task<(bool Success, string? Status)> SetFormuleParDefautAsync(string apiKey, string formule, CancellationToken cancellationToken) =>
        PutAsync(apiKey, $"admin/formules/{formule}/defaut", null, cancellationToken);

    public async Task<(bool Success, string? Status)> DeleteFormuleAsync(string apiKey, string formule, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"admin/formules/{formule}");
            request.Headers.Add("X-Admin-Token", apiKey);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) return (true, null);
            var errorBody = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken);
            return (false, errorBody?.GetValueOrDefault("status") ?? "error");
        }
        catch (Exception)
        {
            return (false, "network_error");
        }
    }

    private async Task<(bool Success, string? Status)> PutAsync(string apiKey, string url, object? body, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, url);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }
            request.Headers.Add("X-Admin-Token", apiKey);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) return (true, null);
            var errorBody = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken);
            return (false, errorBody?.GetValueOrDefault("status") ?? "error");
        }
        catch (Exception)
        {
            return (false, "network_error");
        }
    }

    public Task<(bool Success, List<LitigeItem> Litiges)> ListerLitigesAsync(string apiKey, string? statut, CancellationToken cancellationToken)
    {
        var url = "admin/litiges" + (string.IsNullOrWhiteSpace(statut) ? "" : $"?statut={Uri.EscapeDataString(statut)}");
        return GetListAsync<LitigeItem>(apiKey, url, cancellationToken);
    }

    public Task<(bool Success, string? Status)> ResoudreLitigeAsync(string apiKey, Guid idLitige, string resolution, CancellationToken cancellationToken) =>
        PostAsync(apiKey, $"admin/litiges/{idLitige}/resoudre", new { resolution }, cancellationToken);

    public Task<(bool Success, List<DemandeRemboursementItem> Demandes)> ListerRemboursementsAsync(string apiKey, CancellationToken cancellationToken) =>
        GetListAsync<DemandeRemboursementItem>(apiKey, "admin/remboursements", cancellationToken);

    public Task<(bool Success,List<AdminUserItemDto> Items)> SearchUsersAsync(string token,string? q,CancellationToken ct)=>GetListAsync<AdminUserItemDto>(token,"admin/gestion/utilisateurs?q="+Uri.EscapeDataString(q??""),ct);
    public Task<(bool Success,List<AdminShopItemDto> Items)> SearchShopsAsync(string token,string? q,CancellationToken ct)=>GetListAsync<AdminShopItemDto>(token,"admin/gestion/etablissements?q="+Uri.EscapeDataString(q??""),ct);
    public Task<(bool Success,List<AdminAuditItemDto> Items)> ListAuditAsync(string token,CancellationToken ct)=>GetListAsync<AdminAuditItemDto>(token,"admin/gestion/journal",ct);
    public Task<(bool Success,List<AdminAccountItemDto> Items)> ListAdminAccountsAsync(string token,CancellationToken ct)=>GetListAsync<AdminAccountItemDto>(token,"admin/comptes",ct);
    public async Task<bool> CreateAdminAsync(string token,string nom,string email,string password,string role,CancellationToken ct){using var r=new HttpRequestMessage(HttpMethod.Post,"admin/comptes"){Content=JsonContent.Create(new{nom,email,motDePasse=password,role})};r.Headers.Add("X-Admin-Token",token);return (await _httpClient.SendAsync(r,ct)).IsSuccessStatusCode;}
    public async Task<bool> SetAdminActiveAsync(string token,Guid id,bool active,CancellationToken ct){using var r=new HttpRequestMessage(HttpMethod.Put,$"admin/comptes/{id}/actif"){Content=JsonContent.Create(new{actif=active})};r.Headers.Add("X-Admin-Token",token);return (await _httpClient.SendAsync(r,ct)).IsSuccessStatusCode;}

    public Task<(bool Success, string? Status)> ExecuterRemboursementWaveAsync(string apiKey, Guid idRdv, CancellationToken cancellationToken) =>
        PostAsync(apiKey, $"admin/remboursements/{idRdv}/executer-wave", new { }, cancellationToken);

    public Task<(bool Success, string? Status)> TraiterRemboursementAsync(string apiKey, Guid idRdv, string referenceRemboursement, CancellationToken cancellationToken) =>
        PostAsync(apiKey, $"admin/remboursements/{idRdv}/traiter", new { referenceRemboursement }, cancellationToken);

    private async Task<(bool Success, List<T> Items)> GetListAsync<T>(string apiKey, string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Admin-Token", apiKey);
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
            request.Headers.Add("X-Admin-Token", apiKey);
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
