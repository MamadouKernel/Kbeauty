using System.Net.Http.Json;
using KekeBeauty.Web.Models;

namespace KekeBeauty.Web.Services;

public sealed class AuthApiClient
{
    private readonly HttpClient _httpClient;

    public AuthApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<(bool Success, string Status, string? Message)> RequestOtpAsync(string telephone, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("auth/otp/request", new { telephone }, cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<RequestOtpResponse>(cancellationToken);
            return response.IsSuccessStatusCode
                ? (true, body?.Status ?? "sent", body?.Message)
                : (false, body?.Status ?? "error", body?.Message ?? "Erreur inattendue.");
        }
        catch (Exception)
        {
            return (false, "network_error", "Le service d'authentification est momentanément indisponible.");
        }
    }

    public async Task<(bool Success, string Status, Guid? IdUtilisateur)> VerifyOtpAsync(
        string telephone, string code, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("auth/otp/verify", new { telephone, code }, cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<VerifyOtpResponse>(cancellationToken);
            return response.IsSuccessStatusCode
                ? (true, body?.Status ?? "verified", body?.IdUtilisateur)
                : (false, body?.Status ?? "error", null);
        }
        catch (Exception)
        {
            return (false, "network_error", null);
        }
    }
}
