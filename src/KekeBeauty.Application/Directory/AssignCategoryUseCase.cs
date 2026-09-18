namespace KekeBeauty.Application.Directory;

public sealed record AssignCategoryResult(bool Success, string Status, Guid? IdCategorie = null);

public sealed class AssignCategoryUseCase
{
    private readonly ICategorieRepository _categorieRepository;

    public AssignCategoryUseCase(ICategorieRepository categorieRepository)
    {
        _categorieRepository = categorieRepository;
    }

    public async Task<AssignCategoryResult> ExecuteAsync(Guid idEtablissement, string libelleCategorie, CancellationToken cancellationToken)
    {
        if (!await _categorieRepository.EtablissementExistsAsync(idEtablissement, cancellationToken))
        {
            return new AssignCategoryResult(false, "not_found");
        }

        var idCategorie = await _categorieRepository.FindOrCreateByLibelleAsync(libelleCategorie, cancellationToken);
        await _categorieRepository.AssignToEtablissementAsync(idEtablissement, idCategorie, cancellationToken);

        return new AssignCategoryResult(true, "assigned", idCategorie);
    }
}
