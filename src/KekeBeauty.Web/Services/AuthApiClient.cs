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
        var (success, status, idUtilisateur, _, _) = await VerifyOtpDetailedAsync(telephone, code, typeCompte, cancellationToken);
        return (success, status, idUtilisateur);
    }

    /// <summary>Feature 018 (Parcours 5) : variante exposant idCollaborateur, necessaire pour
    /// distinguer un compte COLLABORATEUR verifie mais non reference par un gerant.</summary>
    public async Task<(bool Success, string Status, Guid? IdUtilisateur, Guid? IdCollaborateur, string? SessionToken)> VerifyOtpDetailedAsync(
        string telephone, string code, int typeCompte, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("auth/otp/verify", new { telephone, code, typeCompte }, cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<VerifyOtpResponse>(cancellationToken);
            return response.IsSuccessStatusCode
                ? (true, body?.Status ?? "verified", body?.IdUtilisateur, body?.IdCollaborateur, body?.SessionToken)
                : (false, body?.Status ?? "error", null, null, null);
        }
        catch (Exception)
        {
            return (false, "network_error", null, null, null);
        }
    }

    public async Task<(bool Success, string Status, Guid? IdUtilisateur, string? SessionToken)> LoginWithGoogleAsync(
        string credential, CancellationToken cancellationToken, string? role = null)
    {
        var (success, body) = await LoginWithGoogleDetailedAsync(credential, cancellationToken, role, null);
        return (success, body?.Status ?? "error", body?.IdUtilisateur, body?.SessionToken);
    }

    /// <summary>2FA par etape (client/partenaire) : variante exposant le corps complet, necessaire
    /// pour distinguer un login termine ("verified") d'un login qui requiert un code de verification
    /// ("step_up_required") sur un appareil non reconnu.</summary>
    public async Task<(bool Success, GoogleLoginResponse? Body)> LoginWithGoogleDetailedAsync(
        string credential, CancellationToken cancellationToken, string? role, string? deviceTrustToken)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                "auth/google/login", new { credential, role, deviceTrustToken }, cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<GoogleLoginResponse>(cancellationToken);
            return (response.IsSuccessStatusCode, body);
        }
        catch (Exception)
        {
            return (false, null);
        }
    }

    public async Task<(bool Success, GoogleLoginResponse? Body)> VerifyStepUpAsync(string ticket, string code, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("auth/google/step-up/verify", new { ticket, code }, cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<GoogleLoginResponse>(cancellationToken);
            return (response.IsSuccessStatusCode, body);
        }
        catch (Exception)
        {
            return (false, null);
        }
    }

    public async Task<(bool Success, string Status, Guid? IdUtilisateur, string? OnboardingToken)> LoginPartnerWithGoogleAsync(
        string credential, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("auth/google/login", new { credential, role = "partenaire" }, cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<GoogleLoginResponse>(cancellationToken);
            return (response.IsSuccessStatusCode, body?.Status ?? "error", body?.IdUtilisateur, body?.OnboardingToken);
        }
        catch (Exception)
        {
            return (false, "network_error", null, null);
        }
    }
}
