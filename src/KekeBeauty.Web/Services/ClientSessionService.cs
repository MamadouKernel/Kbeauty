using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.JSInterop;

namespace KekeBeauty.Web.Services;

public sealed class ClientSessionService
{
    private const string StorageKey = "keke-client-id";
    private const string TokenKey = "keke-client-token";
    private const string DeviceTrustKey = "keke-client-device-trust";
    private readonly ProtectedLocalStorage _storage;
    private readonly IJSRuntime _js;
    public ClientSessionService(ProtectedLocalStorage storage, IJSRuntime js) { _storage = storage; _js = js; }
    public async Task<Guid?> GetClientIdAsync()
    {
        try { var result = await _storage.GetAsync<Guid>(StorageKey); return result.Success ? result.Value : null; }
        catch { return null; }
    }
    public async Task<string?> GetTokenAsync()
    {
        try { var result = await _storage.GetAsync<string>(TokenKey); return result.Success ? result.Value : null; }
        catch { return null; }
    }
    public async Task SetClientIdAsync(Guid idUtilisateur, string? token = null)
    {
        await _storage.SetAsync(StorageKey, idUtilisateur);
        if (!string.IsNullOrWhiteSpace(token)) await _storage.SetAsync(TokenKey, token);
        try { await _js.InvokeVoidAsync("kekeInactivity.sessionStarted"); } catch { }
    }

    /// <summary>2FA par etape : jeton "appareil de confiance" (60 jours), permet de sauter le
    /// step-up sur les connexions Google suivantes depuis ce navigateur.</summary>
    public async Task<string?> GetDeviceTrustTokenAsync()
    {
        try { var result = await _storage.GetAsync<string>(DeviceTrustKey); return result.Success ? result.Value : null; }
        catch { return null; }
    }

    public async Task SetDeviceTrustTokenAsync(string token) => await _storage.SetAsync(DeviceTrustKey, token);
    public async Task ClearAsync()
    {
        await _storage.DeleteAsync(StorageKey);
        await _storage.DeleteAsync(TokenKey);
        try { await _js.InvokeVoidAsync("kekeInactivity.sessionEnded"); } catch { }
    }
}