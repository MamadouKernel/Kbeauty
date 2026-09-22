using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.JSInterop;

namespace KekeBeauty.Web.Services;

/// <summary>
/// Conserve le jeton de session signé du compte administrateur.
/// </summary>
public sealed class AdminSessionService
{
    private const string StorageKey = "keke-admin-token";
    private const string RoleStorageKey = "keke-admin-role";
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

    /// <summary>Rôle admin (ex. SUPER_ADMIN) persisté au login. Voir AdminAuthController.Login.</summary>
    public async Task<string?> GetRoleAsync()
    {
        try
        {
            var result = await _storage.GetAsync<string>(RoleStorageKey);
            return result.Success ? result.Value : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task SetApiKeyAsync(string apiKey, string? role = null)
    {
        await _storage.SetAsync(StorageKey, apiKey);
        if (role is not null) await _storage.SetAsync(RoleStorageKey, role);
        try { await _js.InvokeVoidAsync("kekeInactivity.sessionStarted"); } catch { }
    }

    public async Task ClearAsync()
    {
        await _storage.DeleteAsync(StorageKey);
        await _storage.DeleteAsync(RoleStorageKey);
        try { await _js.InvokeVoidAsync("kekeInactivity.sessionEnded"); } catch { }
    }
}
