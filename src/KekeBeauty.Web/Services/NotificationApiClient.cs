using System.Net.Http.Json;
using KekeBeauty.Web.Models;

namespace KekeBeauty.Web.Services;

public sealed class NotificationApiClient
{
    private readonly HttpClient _httpClient;

    public NotificationApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<(bool Success, List<NotificationItem> Notifications)> ListerAsync(Guid idUtilisateur, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "notifications");
            request.Headers.Add("X-Client-Id", idUtilisateur.ToString());

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (false, []);
            }

            var items = await response.Content.ReadFromJsonAsync<List<NotificationItem>>(cancellationToken);
            return (true, items ?? []);
        }
        catch (Exception)
        {
            return (false, []);
        }
    }

    public async Task<(bool Success, int NonLues)> CompterNonLuesAsync(Guid idUtilisateur, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "notifications/compteur");
            request.Headers.Add("X-Client-Id", idUtilisateur.ToString());

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (false, 0);
            }

            var body = await response.Content.ReadFromJsonAsync<Dictionary<string, int>>(cancellationToken);
            return (true, body?.GetValueOrDefault("nonLues") ?? 0);
        }
        catch (Exception)
        {
            return (false, 0);
        }
    }

    public async Task MarquerLuesAsync(Guid idUtilisateur, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "notifications/lues");
            request.Headers.Add("X-Client-Id", idUtilisateur.ToString());
            await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception)
        {
            // best-effort : un echec de marquage-lu n'est pas bloquant pour l'affichage.
        }
    }
}
