namespace KekeBeauty.Application.Partner;

public interface IPartnerPrestationRepository
{
    Task<Guid?> GetOwnerIdAsync(Guid idEtablissement, CancellationToken cancellationToken);

    Task<Guid> AddAsync(Guid idEtablissement, string libelle, decimal tarif, short dureeMinutes, CancellationToken cancellationToken);

    Task<bool> UpdateAsync(Guid idEtablissement, Guid idPrestation, string libelle, decimal tarif, short dureeMinutes, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid idEtablissement, Guid idPrestation, CancellationToken cancellationToken);
}
