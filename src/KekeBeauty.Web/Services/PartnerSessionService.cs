using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.JSInterop;

namespace KekeBeauty.Web.Services;

/// <summary>
/// Conserve l'identifiant gerant obtenu apres OTP (meme pattern que ClientSessionService, 010),
/// avec sa propre cle de stockage pour ne pas entrer en collision avec la session client si le
/// meme navigateur est utilise pour les deux roles en test.
/// </summary>
public sealed class PartnerSessionService
{
    private const string StorageKey = "keke-partner-id";
    private const string TokenKey = "keke-partner-token";
    private const string DeviceTrustKey = "keke-partner-device-trust";
    private readonly ProtectedLocalStorage _storage;
    private readonly IJSRuntime _js;

    public PartnerSessionService(ProtectedLocalStorage storage, IJSRuntime js)
    {
        _storage = storage;
        _js = js;
    }

    public async Task<Guid?> GetPartnerIdAsync()
    {
        try
        {
            var result = await _storage.GetAsync<Guid>(StorageKey);
            return result.Success ? result.Value : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<string?> GetTokenAsync(){try{var r=await _storage.GetAsync<string>(TokenKey);return r.Success?r.Value:null;}catch{return null;}}
    public async Task SetPartnerIdAsync(Guid idUtilisateur, string? token = null)
    {
        await _storage.SetAsync(StorageKey, idUtilisateur);
        if(!string.IsNullOrWhiteSpace(token))await _storage.SetAsync(TokenKey,token);
        try { await _js.InvokeVoidAsync("kekeInactivity.sessionStarted"); } catch { }
    }

    public async Task ClearAsync()
    {
        await _storage.DeleteAsync(StorageKey);
        await _storage.DeleteAsync(TokenKey);
        try { await _js.InvokeVoidAsync("kekeInactivity.sessionEnded"); } catch { }
    }

    /// <summary>2FA par etape : jeton "appareil de confiance" (60 jours), permet de sauter le
    /// step-up sur les connexions Google suivantes depuis ce navigateur.</summary>
    public async Task<string?> GetDeviceTrustTokenAsync()
    {
        try { var result = await _storage.GetAsync<string>(DeviceTrustKey); return result.Success ? result.Value : null; }
        catch { return null; }
    }

    public async Task SetDeviceTrustTokenAsync(string token) => await _storage.SetAsync(DeviceTrustKey, token);
}
