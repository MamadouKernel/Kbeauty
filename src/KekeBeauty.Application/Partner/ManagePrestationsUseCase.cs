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
}
