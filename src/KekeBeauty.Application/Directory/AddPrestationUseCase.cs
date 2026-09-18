namespace KekeBeauty.Application.Directory;

public sealed record AddPrestationResult(bool Success, string Status, Guid? IdPrestation = null);

public sealed class AddPrestationUseCase
{
    private readonly IPrestationRepository _prestationRepository;
    private readonly ICategorieRepository _categorieRepository;

    public AddPrestationUseCase(IPrestationRepository prestationRepository, ICategorieRepository categorieRepository)
    {
        _prestationRepository = prestationRepository;
        _categorieRepository = categorieRepository;
    }

    public async Task<AddPrestationResult> ExecuteAsync(
        Guid idEtablissement, string libellePrestation, decimal tarif, short dureeMinutes, CancellationToken cancellationToken)
    {
        if (!await _categorieRepository.EtablissementExistsAsync(idEtablissement, cancellationToken))
        {
            return new AddPrestationResult(false, "not_found");
        }

        var idPrestation = await _prestationRepository.AddAsync(idEtablissement, libellePrestation, tarif, dureeMinutes, cancellationToken);
        return new AddPrestationResult(true, "created", idPrestation);
    }
}
