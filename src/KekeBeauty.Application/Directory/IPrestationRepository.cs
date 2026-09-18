namespace KekeBeauty.Application.Directory;

public interface IPrestationRepository
{
    Task<Guid> AddAsync(Guid idEtablissement, string libellePrestation, decimal tarif, short dureeMinutes, CancellationToken cancellationToken);
}
