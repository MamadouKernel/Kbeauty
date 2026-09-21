namespace KekeBeauty.Application.Partner;

public sealed class ManagePrestationsUseCase
{
    private readonly IPartnerPrestationRepository _repository;

    public ManagePrestationsUseCase(IPartnerPrestationRepository repository)
    {
        _repository = repository;
    }

    public Task<Guid> AddAsync(Guid idEtablissement, string libelle, decimal tarif, short dureeMinutes, CancellationToken cancellationToken) =>
        _repository.AddAsync(idEtablissement, libelle, tarif, dureeMinutes, cancellationToken);

    public Task<bool> UpdateAsync(Guid idEtablissement, Guid idPrestation, string libelle, decimal tarif, short dureeMinutes, CancellationToken cancellationToken) =>
        _repository.UpdateAsync(idEtablissement, idPrestation, libelle, tarif, dureeMinutes, cancellationToken);

    public Task<bool> DeleteAsync(Guid idEtablissement, Guid idPrestation, CancellationToken cancellationToken) =>
        _repository.DeleteAsync(idEtablissement, idPrestation, cancellationToken);

    public Task<bool> UpdateModePaiementServiceAsync(Guid idEtablissement, string modePaiementService, bool paiementWave, bool paiementOrangeMoney, bool paiementMoovMoney, CancellationToken cancellationToken) =>
        _repository.UpdateModePaiementServiceAsync(idEtablissement, modePaiementService, paiementWave, paiementOrangeMoney, paiementMoovMoney, cancellationToken);

    public Task<bool> UpdateProfilBoutiqueAsync(Guid idEtablissement, string nom, string? description,
        string telephone, decimal latitude, decimal longitude, string? horaires, CancellationToken cancellationToken) =>
        _repository.UpdateProfilBoutiqueAsync(idEtablissement, nom, description, telephone, latitude, longitude, horaires, cancellationToken);
    public Task<IReadOnlyList<IndisponibiliteRow>> ListIndisponibilitesAsync(Guid id, CancellationToken ct) => _repository.ListIndisponibilitesAsync(id, ct);
    public Task<Guid> AddIndisponibiliteAsync(Guid id, DateTimeOffset debut, DateTimeOffset fin, string? motif, CancellationToken ct) => _repository.AddIndisponibiliteAsync(id, debut, fin, motif, ct);
    public Task<bool> DeleteIndisponibiliteAsync(Guid id, Guid indispo, CancellationToken ct) => _repository.DeleteIndisponibiliteAsync(id, indispo, ct);
    public Task<Guid> AddMediaAsync(Guid id,string path,short ordre,CancellationToken ct)=>_repository.AddMediaAsync(id,path,ordre,ct);
    public Task<bool> DeleteMediaAsync(Guid id,Guid media,CancellationToken ct)=>_repository.DeleteMediaAsync(id,media,ct);
}
