using System.Net.Http.Json;
using KekeBeauty.Web.Models;

namespace KekeBeauty.Web.Services;

public sealed class PartnerRdvApiClient
{
    private readonly HttpClient _httpClient;

    public PartnerRdvApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<(bool Success, List<RdvPartenaire> Rdvs)> ListAsync(Guid idPartner, Guid idEtablissement, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"partenaire/etablissements/{idEtablissement}/rdv");
            request.Headers.Add("X-Partner-Id", idPartner.ToString());

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
