namespace KekeBeauty.Application.Rdv;

public interface IRdvRepository
{
    Task<IReadOnlyList<CreneauOccupe>> GetCreneauxOccupesAsync(Guid idEtablissement, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Retourne null si l'etablissement n'est pas VALIDE ou la prestation n'existe pas ;
    /// retourne un Guid si cree ; Guid.Empty si chevauchement detecte (voir research.md Decision 1).</summary>
    Task<Guid?> CreateIfNoOverlapAsync(
        Guid idEtablissement, Guid idPrestation, Guid idUtilisateurClient, DateTimeOffset dateHeureDebut, CancellationToken cancellationToken);

    Task<Guid?> GetOwnerIdAsync(Guid idEtablissement, CancellationToken cancellationToken);

    Task<string?> GetClientTelephoneAsync(Guid idRdv, CancellationToken cancellationToken);

    Task<bool> UpdateStatutAsync(Guid idEtablissement, Guid idRdv, string statutRdv, CancellationToken cancellationToken);

    /// <summary>Reprogramme si le nouveau creneau ne chevauche pas un autre RDV (FR-007).</summary>
    Task<bool> RescheduleAsync(Guid idEtablissement, Guid idRdv, DateTimeOffset nouvelleDateHeureDebut, CancellationToken cancellationToken);

    /// <summary>Feature 010 (frontend) : permet au client de relire le statut d'un RDV deja cree.
    /// Retourne null si introuvable ou si idUtilisateurClient n'est pas le proprietaire (meme
    /// reponse pour les deux cas au niveau controleur - pas de fuite d'information).</summary>
    Task<RdvStatutRow?> GetStatutAsync(Guid idRdv, Guid idUtilisateurClient, CancellationToken cancellationToken);

    /// <summary>Feature 011 (frontend partenaire) : liste toutes les demandes de RDV d'un
    /// etablissement, tous statuts, pour que le gerant puisse les traiter.</summary>
    Task<IReadOnlyList<RdvPartenaireRow>> ListByEtablissementAsync(Guid idEtablissement, CancellationToken cancellationToken);
}

public sealed class RdvStatutRow
{
    public Guid IdRdv { get; set; }
    public string StatutRdv { get; set; } = string.Empty;
    public DateTimeOffset DateHeureDebut { get; set; }
}

public sealed class RdvPartenaireRow
{
    public Guid IdRdv { get; set; }
    public string StatutRdv { get; set; } = string.Empty;
    public DateTimeOffset DateHeureDebut { get; set; }
    public string LibellePrestation { get; set; } = string.Empty;
    public string TelephoneClient { get; set; } = string.Empty;
}
