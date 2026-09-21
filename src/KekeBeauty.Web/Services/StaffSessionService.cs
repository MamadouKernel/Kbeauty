using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.JSInterop;

namespace KekeBeauty.Web.Services;

public sealed class StaffSessionService
{
    private const string StorageKey = "keke-staff-id";
    private const string TokenKey = "keke-staff-token";
    private readonly ProtectedLocalStorage _storage;
    private readonly IJSRuntime _js;
    public StaffSessionService(ProtectedLocalStorage storage, IJSRuntime js) { _storage = storage; _js = js; }
    public async Task<Guid?> GetStaffIdAsync()
    {
        try { var result = await _storage.GetAsync<Guid>(StorageKey); return result.Success ? result.Value : null; }
        catch { return null; }
    }
    public async Task<string?> GetTokenAsync()
    {
        try { var result = await _storage.GetAsync<string>(TokenKey); return result.Success ? result.Value : null; }
        catch { return null; }
    }
    public async Task SetStaffIdAsync(Guid idUtilisateur, string? token = null)
    {
        await _storage.SetAsync(StorageKey, idUtilisateur);
        if (!string.IsNullOrWhiteSpace(token)) await _storage.SetAsync(TokenKey, token);
        try { await _js.InvokeVoidAsync("kekeInactivity.sessionStarted"); } catch { }
    }
    public async Task ClearAsync()
    {
        await _storage.DeleteAsync(StorageKey);
        await _storage.DeleteAsync(TokenKey);
        try { await _js.InvokeVoidAsync("kekeInactivity.sessionEnded"); } catch { }
    }
}