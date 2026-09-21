using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace KekeBeauty.Web.Services;

public sealed class ClientBookingFlowState
{
    public Guid IdRdv { get; set; }
    public Guid IdEtablissement { get; set; }
    public string NomEtablissement { get; set; } = string.Empty;
    public string LibellePrestation { get; set; } = string.Empty;
    public DateTimeOffset DateHeureDebut { get; set; }
    public decimal Montant { get; set; }
    public string StatutRdv { get; set; } = string.Empty;
    public string? StatutPaiement { get; set; }
    public string? LienPaiement { get; set; }
}

public sealed class ClientBookingFlowService
{
    private const string StorageKey = "keke-client-booking";
    private readonly ProtectedSessionStorage _storage;

    public ClientBookingFlowService(ProtectedSessionStorage storage) => _storage = storage;

    public async Task SaveAsync(ClientBookingFlowState state) => await _storage.SetAsync(StorageKey, state);

    public async Task<ClientBookingFlowState?> GetAsync()
    {
        try
        {
            var result = await _storage.GetAsync<ClientBookingFlowState>(StorageKey);
            return result.Success ? result.Value : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task ClearAsync() => await _storage.DeleteAsync(StorageKey);
}
