namespace KekeBeauty.Application.Directory;

public interface ICategorieRepository
{
    Task<Guid> FindOrCreateByLibelleAsync(string libelleCategorie, CancellationToken cancellationToken);

    Task AssignToEtablissementAsync(Guid idEtablissement, Guid idCategorie, CancellationToken cancellationToken);

    Task<bool> EtablissementExistsAsync(Guid idEtablissement, CancellationToken cancellationToken);
}
