namespace KekeBeauty.Application.Staff;

public sealed class PlanningItem
{
    public Guid IdRdv { get; set; }
    public DateTimeOffset DateHeureDebut { get; set; }
    public string StatutRdv { get; set; } = string.Empty;
    public string LibellePrestation { get; set; } = string.Empty;
    public string NomEtablissement { get; set; } = string.Empty;
    public string NomClient { get; set; } = string.Empty;
    public short DureeMinutes { get; set; }
    public DateTimeOffset? DateDebutReelle { get; set; }
    public DateTimeOffset? DatePause { get; set; }
    public int DureePauseSecondes { get; set; }
    public DateTimeOffset? DateFinReelle { get; set; }
    public short? RetardMinutes { get; set; }
    public DateTimeOffset? DateSignalementRetard { get; set; }
}

public sealed class CommissionItem
{
    public Guid IdRdv { get; set; }
    public DateTimeOffset DateHeureDebut { get; set; }
    public decimal Montant { get; set; }
    public string StatutPaiement { get; set; } = string.Empty;
    public string NomClient { get; set; } = string.Empty;
    public string LibellePrestation { get; set; } = string.Empty;
    public decimal TarifPrestation { get; set; }
    /// <summary>Toujours 0 : les collaboratrices ne touchent pas de commission sur le prix de la
    /// prestation (voir specs/018-extensions-completes/spec.md) - seul le pourboire (Montant/Pourboire) leur revient.</summary>
    public decimal Commission { get; set; }
    public decimal Pourboire => Montant;
}

/// <summary>Feature 018 (Parcours 5) : portail collaboratrice - planning individuel, fiches
/// techniques clientes, commissions/pourboires. Regroupe sous un seul repository (au lieu d'un
/// repository par cas d'usage comme ailleurs dans le projet) car les 4 operations sont des lectures/
/// ecritures simples et etroitement liees au meme acteur ; consolidation deliberee vu le volume de
/// travail de cette session.</summary>
public interface IStaffRepository
{
    Task<Guid?> GetIdCollaborateurByUtilisateurAsync(Guid idUtilisateur, CancellationToken cancellationToken);

    Task<IReadOnlyList<PlanningItem>> GetPlanningAsync(Guid idCollaborateur, CancellationToken cancellationToken);

    Task<IReadOnlyList<CommissionItem>> GetCommissionsAsync(Guid idCollaborateur, CancellationToken cancellationToken);

    /// <summary>Retourne null si aucune fiche n'existe encore pour ce RDV, ou si le RDV n'est pas
    /// assigne a cette collaboratrice (pas de fuite d'information sur les fiches d'autres RDV).</summary>
    Task<string?> GetFicheTechniqueAsync(Guid idRdv, Guid idCollaborateur, CancellationToken cancellationToken);

    /// <summary>Retourne false si le RDV n'est pas assigne a cette collaboratrice.</summary>
    Task<bool> EnregistrerFicheTechniqueAsync(Guid idRdv, Guid idCollaborateur, string notes, CancellationToken cancellationToken);

    Task<string> UpdateWorkSessionAsync(Guid idRdv, Guid idCollaborateur, string action, CancellationToken cancellationToken);
}
