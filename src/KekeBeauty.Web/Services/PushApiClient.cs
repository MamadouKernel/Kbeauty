using System.Net.Http.Json;

namespace KekeBeauty.Web.Services;

public sealed class PushApiClient
{
    private readonly HttpClient _httpClient;

    public PushApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<(bool Success, string? PublicKey)> GetVapidPublicKeyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync("push/vapid-public-key", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (false, null);
            }

            var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken);
            return (true, body?.GetValueOrDefault("publicKey"));
        }
        catch (Exception)
        {
            return (false, null);
        }
    }

    public async Task<bool> SubscribeAsync(Guid idClient, string endpoint, string p256dh, string auth, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "push/subscribe")
            {
                Content = JsonContent.Create(new { endpoint, p256dh, auth })
            };
            request.Headers.Add("X-Client-Id", idClient.ToString());

            var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
