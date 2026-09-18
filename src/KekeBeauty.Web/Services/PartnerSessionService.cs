using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace KekeBeauty.Web.Services;

/// <summary>
/// Conserve l'identifiant gerant obtenu apres OTP (meme pattern que ClientSessionService, 010),
/// avec sa propre cle de stockage pour ne pas entrer en collision avec la session client si le
/// meme navigateur est utilise pour les deux roles en test.
/// </summary>
public sealed class PartnerSessionService
{
    private const string StorageKey = "keke-partner-id";
    private readonly ProtectedLocalStorage _storage;

    public PartnerSessionService(ProtectedLocalStorage storage)
    {
        _storage = storage;
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

    public async Task SetPartnerIdAsync(Guid idUtilisateur) =>
        await _storage.SetAsync(StorageKey, idUtilisateur);

    public async Task ClearAsync() =>
        await _storage.DeleteAsync(StorageKey);
}
