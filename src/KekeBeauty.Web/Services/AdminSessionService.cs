using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace KekeBeauty.Web.Services;

/// <summary>
/// Conserve la cle API admin (X-Admin-Api-Key). Different de ClientSessionService/
/// PartnerSessionService : pas d'idUtilisateur admin cote API (dette technique documentee depuis
/// 004, AdminApiKeyFilter compare une cle statique, pas d'entite utilisateur authentifiee).
/// </summary>
public sealed class AdminSessionService
{
    private const string StorageKey = "keke-admin-key";
    private readonly ProtectedLocalStorage _storage;

    public AdminSessionService(ProtectedLocalStorage storage)
    {
        _storage = storage;
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

    public async Task SetApiKeyAsync(string apiKey) =>
        await _storage.SetAsync(StorageKey, apiKey);

    public async Task ClearAsync() =>
        await _storage.DeleteAsync(StorageKey);
}
