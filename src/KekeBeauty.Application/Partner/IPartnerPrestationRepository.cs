namespace KekeBeauty.Application.Partner;

public sealed class EtablissementGereRow
{
    public Guid IdEtablissement { get; set; }
    public string NomEtablissement { get; set; } = string.Empty;
    public string StatutKyc { get; set; } = string.Empty;
    public bool EstSuspendu { get; set; }
}

public interface IPartnerPrestationRepository
{
    Task<Guid?> GetOwnerIdAsync(Guid idEtablissement, CancellationToken cancellationToken);

    /// <summary>Feature 011 (frontend) : permet a un gerant authentifie de decouvrir le ou les
    /// etablissements qu'il gere, sans en connaitre l'id au prealable.</summary>
    Task<IReadOnlyList<EtablissementGereRow>> GetEtablissementsByGerantAsync(Guid idGerant, CancellationToken cancellationToken);

    Task<Guid> AddAsync(Guid idEtablissement, string libelle, decimal tarif, short dureeMinutes, CancellationToken cancellationToken);

    Task<bool> UpdateAsync(Guid idEtablissement, Guid idPrestation, string libelle, decimal tarif, short dureeMinutes, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid idEtablissement, Guid idPrestation, CancellationToken cancellationToken);
}
