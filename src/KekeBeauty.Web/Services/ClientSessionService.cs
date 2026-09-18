using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace KekeBeauty.Web.Services;

/// <summary>
/// Conserve l'identifiant client obtenu apres OTP (voir research.md Decision 2) : pas de vraie
/// session/JWT, coherent avec la dette technique deja documentee cote API (X-Client-Id).
/// </summary>
public sealed class ClientSessionService
{
    private const string StorageKey = "keke-client-id";
    private readonly ProtectedLocalStorage _storage;

    public ClientSessionService(ProtectedLocalStorage storage)
    {
        _storage = storage;
    }

    public async Task<Guid?> GetClientIdAsync()
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

    public async Task SetClientIdAsync(Guid idUtilisateur) =>
        await _storage.SetAsync(StorageKey, idUtilisateur);

    public async Task ClearAsync() =>
        await _storage.DeleteAsync(StorageKey);
}
