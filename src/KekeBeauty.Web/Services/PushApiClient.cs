using System.Net.Http.Json;

namespace KekeBeauty.Web.Services;

public sealed class PushApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ClientSessionService _session;

    public PushApiClient(HttpClient httpClient, ClientSessionService session)
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
            await AddTokenAsync(request);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
