namespace KekeBeauty.Application.Partner;

public sealed class CollaborateurDto
{
    public Guid IdCollaborateur { get; set; }
    public string Nom { get; set; } = string.Empty;
    public string? Specialite { get; set; }
    public string? Telephone { get; set; }
    public bool CompteActif { get; set; }
}

public interface ICollaborateurRepository
{
    Task<Guid> AjouterAsync(Guid idEtablissement, string nom, string? specialite, string? telephone, CancellationToken cancellationToken);

    Task<IReadOnlyList<CollaborateurDto>> ListerAsync(Guid idEtablissement, CancellationToken cancellationToken);

    /// <summary>Retourne false si la collaboratrice n'existe pas ou n'appartient pas a cet
    /// etablissement (verification d'appartenance, meme principe que ManagePrestationsUseCase).</summary>
    Task<bool> RetirerAsync(Guid idEtablissement, Guid idCollaborateur, CancellationToken cancellationToken);

    /// <summary>Feature 018 (Parcours 5) : associe le compte utilisateur cree lors de la premiere
    /// verification OTP (type COLLABORATEUR) a la fiche collaboratrice deja referencee par le
    /// gerant avec le meme numero de telephone. Retourne l'id_collaborateur associe, ou null si
    /// aucune fiche collaboratrice n'a ete pre-enregistree pour ce numero (compte non reference).</summary>
    Task<Guid?> LinkUtilisateurByTelephoneAsync(string telephone, Guid idUtilisateur, CancellationToken cancellationToken);

    Task<Guid?> GetIdCollaborateurByUtilisateurAsync(Guid idUtilisateur, CancellationToken cancellationToken);

    /// <summary>Verifie que la collaboratrice appartient bien a l'etablissement (pour l'assignation
    /// d'un RDV par le gerant).</summary>
    Task<bool> AppartientAEtablissementAsync(Guid idCollaborateur, Guid idEtablissement, CancellationToken cancellationToken);
}
