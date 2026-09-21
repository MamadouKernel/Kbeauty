namespace KekeBeauty.Application.Rdv;

// Meme convention que IAbonnementRepository (Billing) : classe mutable, statut en string brut,
// Dapper materialise les ENUM PostgreSQL comme du texte.
public sealed class RdvPaiementResume
{
    public Guid IdTransactionRdv { get; set; }
    public Guid IdRdv { get; set; }
    public decimal Montant { get; set; }
    public string StatutTransaction { get; set; } = string.Empty;
    public string? ReferenceExterne { get; set; }
}

public interface IRdvPaiementRepository
{
    /// <summary>Cree la transaction de paiement en ligne liee au RDV (statut EN_COURS). Idempotent
    /// par construction (contrainte UNIQUE(id_rdv), voir data-model.md) : si une transaction existe
    /// deja pour ce RDV, la renvoie telle quelle au lieu d'en creer une seconde (FR-009).</summary>
    Task<RdvPaiementResume> CreerOuReutiliserAsync(Guid idRdv, decimal montant, string referenceExterne, CancellationToken cancellationToken);

    Task<RdvPaiementResume?> ObtenirParRdvAsync(Guid idRdv, CancellationToken cancellationToken);

    /// <summary>Applique le resultat d'un paiement WiniPayer recu via callback ou verification
    /// manuelle : met a jour la transaction (REUSSIE/ECHOUEE) identifiee par referenceExterne.
    /// Retourne false si aucune transaction ne correspond (reference d'un autre flux - ex.
    /// abonnement - ou callback deja traite). Ne touche jamais rdv.statut_rdv (FR-010).</summary>
    Task<bool> MarquerPaiementAsync(string referenceExterne, bool paiementReussi, string? operateurExterne, CancellationToken cancellationToken);

    /// <summary>Feature 015 (US1) : reinitialise une transaction ECHOUEE vers EN_COURS avec une
    /// nouvelle reference externe (nouvelle tentative de paiement). Ne fait rien et retourne false
    /// si la transaction n'est pas ECHOUEE (FR-002) ou n'existe pas.</summary>
    Task<bool> RelancerAsync(Guid idRdv, string nouvelleReferenceExterne, CancellationToken cancellationToken);

    /// <summary>Feature 015 (US2/FR-005), revu en 018 : place une transaction REUSSIE en
    /// REMBOURSEMENT_DEMANDE suite a l'annulation du RDV associe (ne marque plus REMBOURSEE
    /// directement - voir TraiterRemboursementAsync pour l'etape de traitement). Ne fait rien et
    /// retourne false si aucune transaction REUSSIE n'existe pour ce RDV (rien a rembourser).</summary>
    Task<bool> DemanderRembourseAsync(Guid idRdv, CancellationToken cancellationToken);

    /// <summary>Feature 018 : file d'attente des remboursements a traiter manuellement par
    /// l'administrateur (aucune API de virement disponible - voir spec.md).</summary>
    Task<IReadOnlyList<DemandeRemboursementDto>> ListerDemandesRemboursementAsync(CancellationToken cancellationToken);

    /// <summary>Feature 018 : marque REMBOURSEE une transaction en REMBOURSEMENT_DEMANDE, avec la
    /// reference du virement/mobile money effectue manuellement par l'administrateur en dehors de
    /// la plateforme. Retourne false si aucune transaction en REMBOURSEMENT_DEMANDE n'existe pour
    /// ce RDV.</summary>
    Task<bool> TraiterRemboursementAsync(Guid idRdv, string referenceRemboursement, CancellationToken cancellationToken);

    Task<RemboursementWaveDto?> ObtenirRemboursementWaveAsync(Guid idRdv, CancellationToken cancellationToken);

    Task EnregistrerTentativeRemboursementAsync(Guid idRdv, bool success, string? reference, string? erreur, CancellationToken cancellationToken);
}

public sealed class DemandeRemboursementDto
{
    public Guid IdRdv { get; set; }
    public decimal Montant { get; set; }
    public DateTimeOffset DateTransaction { get; set; }
    public string NomEtablissement { get; set; } = string.Empty;
    public string TelephoneClient { get; set; } = string.Empty;
}

public sealed class RemboursementWaveDto
{
    public Guid IdRdv { get; set; }
    public decimal Montant { get; set; }
    public string ReferenceExterne { get; set; } = string.Empty;
}
