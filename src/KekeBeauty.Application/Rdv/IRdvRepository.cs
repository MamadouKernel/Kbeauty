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
}
