using KekeBeauty.Application.Directory;

namespace KekeBeauty.Application.Partner;

public sealed class ManageCategoriesUseCase
{
    private readonly ICategorieRepository _categorieRepository;

    public ManageCategoriesUseCase(ICategorieRepository categorieRepository)
    {
        _categorieRepository = categorieRepository;
    }

    public async Task<Guid> AssignAsync(Guid idEtablissement, string libelleCategorie, CancellationToken cancellationToken)
    {
        var idCategorie = await _categorieRepository.FindOrCreateByLibelleAsync(libelleCategorie, cancellationToken);
        await _categorieRepository.AssignToEtablissementAsync(idEtablissement, idCategorie, cancellationToken);
        return idCategorie;
    }

    public Task RemoveAsync(Guid idEtablissement, Guid idCategorie, CancellationToken cancellationToken) =>
        _categorieRepository.RemoveFromEtablissementAsync(idEtablissement, idCategorie, cancellationToken);
}
