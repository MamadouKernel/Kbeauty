using System.Net.Http.Json;
using KekeBeauty.Web.Models;

namespace KekeBeauty.Web.Services;

public sealed class PartnerRdvApiClient
{
    private readonly HttpClient _httpClient;
    private readonly PartnerSessionService _session;

    public PartnerRdvApiClient(HttpClient httpClient, PartnerSessionService session)
    {
        _httpClient = httpClient;
        _session = session;
    }

    // Le jeton est attache ici (typed client, scope DI correct du circuit) plutot que dans
    // PartnerAuthHandler : IHttpClientFactory construit les DelegatingHandler via un scope DI
    // mis en cache/tourniquet distinct du circuit Blazor courant, donc ProtectedLocalStorage
    // (IJSRuntime) y echoue silencieusement - le jeton n'atteint jamais l'API (bug reproduit).
    private async Task AddTokenAsync(HttpRequestMessage request)
    {
        var token = await _session.GetTokenAsync();
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.TryAddWithoutValidation("X-Partner-Token", token);
        }
    }

    public async Task<(bool Success, List<RdvPartenaire> Rdvs)> ListAsync(Guid idPartner, Guid idEtablissement, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"partenaire/etablissements/{idEtablissement}/rdv");
            request.Headers.Add("X-Partner-Id", idPartner.ToString());
            await AddTokenAsync(request);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (false, []);
            }

            var rdvs = await response.Content.ReadFromJsonAsync<List<RdvPartenaire>>(cancellationToken);
            return (true, rdvs ?? []);
        }
        catch (Exception)
        {
            return (false, []);
        }
    }

    public Task<(bool Success, string? Status)> ConfirmerAsync(Guid idPartner, Guid idEtablissement, Guid idRdv, CancellationToken cancellationToken) =>
        SendDecisionAsync(idPartner, $"partenaire/etablissements/{idEtablissement}/rdv/{idRdv}/confirmer", null, cancellationToken);

    public Task<(bool Success, string? Status)> RefuserAsync(Guid idPartner, Guid idEtablissement, Guid idRdv, CancellationToken cancellationToken) =>
        SendDecisionAsync(idPartner, $"partenaire/etablissements/{idEtablissement}/rdv/{idRdv}/refuser", null, cancellationToken);

    public Task<(bool Success, string? Status)> ReprogrammerAsync(
        Guid idPartner, Guid idEtablissement, Guid idRdv, DateTimeOffset nouvelleDateHeureDebut, CancellationToken cancellationToken) =>
        SendDecisionAsync(idPartner, $"partenaire/etablissements/{idEtablissement}/rdv/{idRdv}/reprogrammer",
            new { dateHeureDebut = nouvelleDateHeureDebut }, cancellationToken);

    public Task<(bool Success,string? Status)> CreerComptoirAsync(Guid idPartner,Guid idEtablissement,Guid idPrestation,
        Guid? idCollaborateur,string nomClient,string telephoneClient,DateTimeOffset dateHeureDebut,string modePaiement,CancellationToken cancellationToken) =>
        SendDecisionAsync(idPartner,$"partenaire/etablissements/{idEtablissement}/rdv/comptoir",
            new{idPrestation,idCollaborateur,nomClient,telephoneClient,dateHeureDebut,modePaiement},cancellationToken);
    /// <summary>Feature 018 (Parcours 5) : assigne (ou desassigne si idCollaborateur == null) une
    /// collaboratrice au RDV (planning individuel).</summary>
    public Task<(bool Success, string? Status)> AssignerCollaborateurAsync(
        Guid idPartner, Guid idEtablissement, Guid idRdv, Guid? idCollaborateur, CancellationToken cancellationToken) =>
        SendDecisionAsync(idPartner, $"partenaire/etablissements/{idEtablissement}/rdv/{idRdv}/assigner",
            new { idCollaborateur }, cancellationToken);

    public async Task<(bool Success,QrVerificationDto? Result,string? Status)> VerifyQrAsync(Guid idPartner,string token,CancellationToken cancellationToken)
    {
        try{using var request=new HttpRequestMessage(HttpMethod.Get,$"rdv/qr/verify?token={Uri.EscapeDataString(token)}");request.Headers.Add("X-Partner-Id",idPartner.ToString());await AddTokenAsync(request);var response=await _httpClient.SendAsync(request,cancellationToken);if(!response.IsSuccessStatusCode)return(false,null,response.StatusCode.ToString());return(true,await response.Content.ReadFromJsonAsync<QrVerificationDto>(cancellationToken),"verified");}catch{return(false,null,"network_error");}
    }
    private async Task<(bool Success, string? Status)> SendDecisionAsync(Guid idPartner, string url, object? body, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }
            request.Headers.Add("X-Partner-Id", idPartner.ToString());
            await AddTokenAsync(request);

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
