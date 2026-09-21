namespace KekeBeauty.Application.Directory;

public sealed class EtablissementCoreRow
{
    public Guid IdEtablissement { get; set; }
    public string NomEtablissement { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string NumeroServiceClient { get; set; } = string.Empty;
    public decimal GpsLatitude { get; set; }
    public decimal GpsLongitude { get; set; }
    public string ModePaiementService { get; set; } = "ESPECES";
    public bool PaiementWave { get; set; }
    public bool PaiementOrangeMoney { get; set; }
    public bool PaiementMoovMoney { get; set; }
    public string StatutKyc { get; set; } = string.Empty;
    public string? Horaires { get; set; }
    public string? MotifRejet { get; set; }
}

public interface IDirectoryRepository
{
    Task<IReadOnlyList<EtablissementSummary>> SearchAsync(string? categorie, string? commune, CancellationToken cancellationToken);

    /// <summary>Retourne null si l'etablissement n'existe pas OU n'est pas VALIDE (FR-007).</summary>
    Task<EtablissementCoreRow?> GetValidatedCoreAsync(Guid idEtablissement, CancellationToken cancellationToken);

    /// <summary>Lecture privee pour le gerant, y compris pendant la verification KYC.</summary>
    Task<EtablissementCoreRow?> GetManagedCoreAsync(Guid idEtablissement, CancellationToken cancellationToken);

    Task<IReadOnlyList<MediaDto>> GetMediasAsync(Guid idEtablissement, CancellationToken cancellationToken);

    Task<IReadOnlyList<PrestationDto>> GetPrestationsAsync(Guid idEtablissement, CancellationToken cancellationToken);
}
