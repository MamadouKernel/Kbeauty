namespace KekeBeauty.Application.Partner;

public sealed class EtablissementGereRow
{
    public Guid IdEtablissement { get; set; }
    public string NomEtablissement { get; set; } = string.Empty;
    public string StatutKyc { get; set; } = string.Empty;
    public bool EstSuspendu { get; set; }
}
public sealed class IndisponibiliteRow { public Guid IdIndisponibilite { get; set; } public DateTimeOffset DateDebut { get; set; } public DateTimeOffset DateFin { get; set; } public string? Motif { get; set; } }

public interface IPartnerPrestationRepository
{
    Task<Guid?> GetOwnerIdAsync(Guid idEtablissement, CancellationToken cancellationToken);

    /// <summary>Feature 011 (frontend) : permet a un gerant authentifie de decouvrir le ou les
    /// etablissements qu'il gere, sans en connaitre l'id au prealable.</summary>
    Task<IReadOnlyList<EtablissementGereRow>> GetEtablissementsByGerantAsync(Guid idGerant, CancellationToken cancellationToken);

    Task<Guid> AddAsync(Guid idEtablissement, string libelle, decimal tarif, short dureeMinutes, CancellationToken cancellationToken);

    Task<bool> UpdateAsync(Guid idEtablissement, Guid idPrestation, string libelle, decimal tarif, short dureeMinutes, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid idEtablissement, Guid idPrestation, CancellationToken cancellationToken);

    Task<bool> UpdateModePaiementServiceAsync(Guid idEtablissement, string modePaiementService, bool paiementWave, bool paiementOrangeMoney, bool paiementMoovMoney, CancellationToken cancellationToken);

    Task<bool> UpdateProfilBoutiqueAsync(Guid idEtablissement, string nom, string? description, string telephone,
        decimal latitude, decimal longitude, string? horaires, CancellationToken cancellationToken);
    Task<IReadOnlyList<IndisponibiliteRow>> ListIndisponibilitesAsync(Guid idEtablissement, CancellationToken cancellationToken);
    Task<Guid> AddIndisponibiliteAsync(Guid idEtablissement, DateTimeOffset debut, DateTimeOffset fin, string? motif, CancellationToken cancellationToken);
    Task<bool> DeleteIndisponibiliteAsync(Guid idEtablissement, Guid idIndisponibilite, CancellationToken cancellationToken);
    Task<Guid> AddMediaAsync(Guid idEtablissement, string path, short ordre, CancellationToken cancellationToken);
    Task<bool> DeleteMediaAsync(Guid idEtablissement, Guid idMedia, CancellationToken cancellationToken);
    Task<string?> GetMediaPathAsync(Guid idMedia, CancellationToken cancellationToken);
}
