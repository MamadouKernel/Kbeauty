using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.JSInterop;

namespace KekeBeauty.Web.Services;

/// <summary>
/// Conserve le jeton de session signé du compte administrateur.
/// </summary>
public sealed class AdminSessionService
{
    private const string StorageKey = "keke-admin-token";
    private readonly ProtectedLocalStorage _storage;
    private readonly IJSRuntime _js;

    public AdminSessionService(ProtectedLocalStorage storage, IJSRuntime js)
    {
        _storage = storage;
        _js = js;
    }

    public async Task<string?> GetApiKeyAsync()
    {
        try
        {
            var result = await _storage.GetAsync<string>(StorageKey);
            return result.Success ? result.Value : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task SetApiKeyAsync(string apiKey)
    {
        await _storage.SetAsync(StorageKey, apiKey);
        try { await _js.InvokeVoidAsync("kekeInactivity.sessionStarted"); } catch { }
    }

    public async Task ClearAsync()
    {
        await _storage.DeleteAsync(StorageKey);
        try { await _js.InvokeVoidAsync("kekeInactivity.sessionEnded"); } catch { }
    }
}
