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

    /// <summary>Cree l'abonnement (IMPAYE) et sa transaction (EN_COURS, liee a referenceExterne -
    /// l'uuid du lien de paiement WiniPayer). Retourne null si un abonnement ACTIF existe deja
    /// pour cet etablissement (FR-004, insertion atomique - voir research.md Decision 2). Le
    /// resultat reel du paiement arrive plus tard via MarquerPaiementAsync (callback WiniPayer).</summary>
    Task<Guid?> CreerAvecTransactionAsync(
        Guid idEtablissement, string periodicite, decimal montant, string referenceExterne,
        CancellationToken cancellationToken);

    /// <summary>Applique le resultat d'un paiement WiniPayer recu via callback : met a jour la
    /// transaction (REUSSIE/ECHOUEE) identifiee par referenceExterne, et l'abonnement associe
    /// (ACTIF si reussi, reste IMPAYE sinon). Retourne false si aucune transaction ne correspond
    /// (callback invalide ou deja traite - idempotent car un second appel ne change rien).</summary>
    Task<bool> MarquerPaiementAsync(string referenceExterne, bool paiementReussi, string? operateurExterne, CancellationToken cancellationToken);

    Task<IReadOnlyList<AbonnementResume>> ListerAsync(string? statut, CancellationToken cancellationToken);

    Task<AbonnementResume?> GetByIdAsync(Guid idAbonnement, CancellationToken cancellationToken);

    Task<string?> GetGerantTelephoneAsync(Guid idAbonnement, CancellationToken cancellationToken);

    /// <summary>Reference externe (uuid WiniPayer) de la transaction EN_COURS la plus recente de cet
    /// abonnement. Utilise pour la reconciliation manuelle (si un callback a ete rate).</summary>
    Task<string?> GetReferenceExterneEnCoursAsync(Guid idAbonnement, CancellationToken cancellationToken);

    Task<IReadOnlyList<TarifStandard>> ListerTarifsAsync(CancellationToken cancellationToken);

    Task SetTarifStandardAsync(string periodicite, decimal montant, CancellationToken cancellationToken);
}
