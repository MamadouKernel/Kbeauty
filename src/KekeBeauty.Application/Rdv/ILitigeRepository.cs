namespace KekeBeauty.Application.Rdv;

public sealed class LitigeDto
{
    public Guid IdLitige { get; set; }
    public Guid IdRdv { get; set; }
    public string Motif { get; set; } = string.Empty;
    public string StatutLitige { get; set; } = string.Empty;
    public string? Resolution { get; set; }
    public DateTimeOffset DateCreation { get; set; }
    public string NomEtablissement { get; set; } = string.Empty;
    public string TelephoneDeclarant { get; set; } = string.Empty;
}

public interface ILitigeRepository
{
    /// <summary>Vrai si idUtilisateur est le client du RDV ou le gerant de l'etablissement
    /// concerne (les deux parties d'un RDV peuvent declarer un litige).</summary>
    Task<bool> EstPartiePrenanteAsync(Guid idRdv, Guid idUtilisateur, CancellationToken cancellationToken);

    Task<Guid> DeclarerAsync(Guid idRdv, Guid idUtilisateurDeclarant, string motif, CancellationToken cancellationToken);

    Task<IReadOnlyList<LitigeDto>> ListerAsync(string? statut, CancellationToken cancellationToken);

    /// <summary>Retourne false si le litige n'existe pas ou est deja resolu.</summary>
    Task<bool> ResoudreAsync(Guid idLitige, string resolution, CancellationToken cancellationToken);
}
