using System.Net.Http.Json;
using KekeBeauty.Web.Models;

namespace KekeBeauty.Web.Services;

public sealed class RdvApiClient
{
    private readonly HttpClient _httpClient;

    public RdvApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<(bool Success, List<CreneauOccupe> Creneaux)> GetCreneauxAsync(Guid idEtablissement, DateOnly date, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"etablissements/{idEtablissement}/creneaux?date={date:yyyy-MM-dd}";
            var creneaux = await _httpClient.GetFromJsonAsync<List<CreneauOccupe>>(url, cancellationToken);
            return (true, creneaux ?? []);
        }
        catch (Exception)
        {
            return (false, []);
        }
    }

    public async Task<(bool Success, string Status, RequestRdvResponse? Rdv)> RequestRdvAsync(
        Guid idClient, Guid idEtablissement, Guid idPrestation, DateTimeOffset dateHeureDebut, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "rdv")
            {
                Content = JsonContent.Create(new { idEtablissement, idPrestation, dateHeureDebut })
            };
            request.Headers.Add("X-Client-Id", idClient.ToString());

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<RequestRdvResponse>(cancellationToken);
                return (true, body?.Statut ?? "created", body);
            }

            var errorBody = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken);
            return (false, errorBody?.GetValueOrDefault("status") ?? "error", null);
        }
        catch (Exception)
        {
            return (false, "network_error", null);
        }
    }

    public async Task<RdvStatutResponse?> GetStatutAsync(Guid idClient, Guid idRdv, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"rdv/{idRdv}");
            request.Headers.Add("X-Client-Id", idClient.ToString());

            var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<RdvStatutResponse>(cancellationToken)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
