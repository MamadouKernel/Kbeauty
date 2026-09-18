namespace KekeBeauty.Application.Billing;

// Classes (pas des record positionnels), champs enum-like en string brut : meme convention que
// ApplicationSummary/ApplicationDetail (Onboarding/Dtos.cs) - Dapper materialise les valeurs
// PostgreSQL ENUM comme du texte, jamais directement comme un enum C#.
public sealed class AbonnementResume
{
    public Guid IdAbonnement { get; set; }
    public Guid IdEtablissement { get; set; }
    public string Periodicite { get; set; } = string.Empty;
    public decimal Montant { get; set; }
    public string StatutAbonnement { get; set; } = string.Empty;
}

public sealed class TarifStandard
{
    public string Periodicite { get; set; } = string.Empty;
    public decimal Montant { get; set; }
}

public interface IAbonnementRepository
{
    Task<decimal> GetTarifStandardAsync(string periodicite, CancellationToken cancellationToken);

    /// <summary>Cree l'abonnement et sa transaction. Retourne null si un abonnement ACTIF existe deja
    /// pour cet etablissement (FR-004, insertion atomique - voir research.md Decision 2).</summary>
    Task<Guid?> CreerAvecTransactionAsync(
        Guid idEtablissement, string periodicite, decimal montant, string canal, bool paiementReussi,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AbonnementResume>> ListerAsync(string? statut, CancellationToken cancellationToken);

    Task<AbonnementResume?> GetByIdAsync(Guid idAbonnement, CancellationToken cancellationToken);

    Task<string?> GetGerantTelephoneAsync(Guid idAbonnement, CancellationToken cancellationToken);

    Task<IReadOnlyList<TarifStandard>> ListerTarifsAsync(CancellationToken cancellationToken);

    Task SetTarifStandardAsync(string periodicite, decimal montant, CancellationToken cancellationToken);
}
