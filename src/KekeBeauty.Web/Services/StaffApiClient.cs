using System.Net.Http.Json;
using KekeBeauty.Web.Models;

namespace KekeBeauty.Web.Services;

/// <summary>Feature 018 (Parcours 5) : portail collaboratrice, meme pattern d'en-tete d'identite
/// que PartnerApiClient (X-Partner-Id) - ici X-Collaborateur-Id porte l'id_utilisateur.</summary>
public sealed class StaffApiClient
{
    private readonly HttpClient _httpClient;

    public StaffApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<(bool Success, List<PlanningItemDto> Items)> GetPlanningAsync(Guid idUtilisateur, CancellationToken cancellationToken) =>
        GetListAsync<PlanningItemDto>(idUtilisateur, "staff/planning", cancellationToken);

    public Task<(bool Success, List<CommissionItemDto> Items)> GetCommissionsAsync(Guid idUtilisateur, CancellationToken cancellationToken) =>
        GetListAsync<CommissionItemDto>(idUtilisateur, "staff/commissions", cancellationToken);

    public async Task<(bool Success, string? Notes)> GetFicheTechniqueAsync(Guid idUtilisateur, Guid idRdv, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"staff/rdv/{idRdv}/fiche-technique");
            request.Headers.Add("X-Collaborateur-Id", idUtilisateur.ToString());
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (false, null);
            }
            var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string?>>(cancellationToken);
            return (true, body?.GetValueOrDefault("notes"));
        }
        catch (Exception)
        {
            return (false, null);
        }
    }

    public async Task<bool> SaveFicheTechniqueAsync(Guid idUtilisateur, Guid idRdv, string notes, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, $"staff/rdv/{idRdv}/fiche-technique")
            {
                Content = JsonContent.Create(new { notes })
            };
            request.Headers.Add("X-Collaborateur-Id", idUtilisateur.ToString());
            var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<(bool Success,string? Status)> UpdateWorkSessionAsync(Guid idUtilisateur,Guid idRdv,string action,CancellationToken cancellationToken)
    {
        try{using var request=new HttpRequestMessage(HttpMethod.Post,$"staff/rdv/{idRdv}/session"){Content=JsonContent.Create(new{action})};request.Headers.Add("X-Collaborateur-Id",idUtilisateur.ToString());var response=await _httpClient.SendAsync(request,cancellationToken);Dictionary<string,string>? body=null;try{body=await response.Content.ReadFromJsonAsync<Dictionary<string,string>>(cancellationToken);}catch{}return(response.IsSuccessStatusCode,body?.GetValueOrDefault("status"));}catch{return(false,"network_error");}
    }
    private async Task<(bool Success, List<T> Items)> GetListAsync<T>(Guid idUtilisateur, string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Collaborateur-Id", idUtilisateur.ToString());
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
}
