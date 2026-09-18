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

    /// <summary>typeCompte : 0=Client, 1=Partenaire, 2=Admin (KekeBeauty.Domain.Entities.TypeCompte,
    /// serialise en numerique par defaut par System.Text.Json cote API).</summary>
    public async Task<(bool Success, string Status, string? Message)> RequestOtpAsync(string telephone, int typeCompte, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("auth/otp/request", new { telephone, typeCompte }, cancellationToken);
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
        string telephone, string code, int typeCompte, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("auth/otp/verify", new { telephone, code, typeCompte }, cancellationToken);
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
