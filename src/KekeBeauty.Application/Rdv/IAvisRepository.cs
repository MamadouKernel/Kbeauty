namespace KekeBeauty.Application.Rdv;

public sealed class AvisItem
{
    public short Note { get; set; }
    public string? Commentaire { get; set; }
    public DateTimeOffset DateCreation { get; set; }
    public Guid IdRdv { get; set; }
    public bool HasPhotoAvant { get; set; }
    public bool HasPhotoApres { get; set; }
}

public sealed class AvisEtablissement
{
    public double? NoteMoyenne { get; set; }
    public int NombreAvis { get; set; }
    public List<AvisItem> Avis { get; set; } = [];
}

public interface IAvisRepository
{
    /// <summary>Cree l'avis. Suppose deja verifie par l'appelant : RDV appartient au client et est
    /// TERMINE (LaisserAvisUseCase). Retourne null si un avis existe deja pour ce RDV (contrainte
    /// UNIQUE(id_rdv), FR-003) - l'appelant traduit en 409.</summary>
    Task<Guid?> CreerAsync(Guid idRdv, short note, string? commentaire, CancellationToken cancellationToken);

    Task<bool> ExisteAsync(Guid idRdv, CancellationToken cancellationToken);

    Task<(bool Saved,bool BonusAwarded)> SaveBeforeAfterAsync(Guid idRdv,Guid idClient,string beforePath,string afterPath,bool sharePublicly,CancellationToken cancellationToken);
    Task<string?> GetPhotoPathAsync(Guid idRdv,string type,CancellationToken cancellationToken);

    /// <summary>Liste publique (FR-005/FR-006) : note moyenne null et liste vide si aucun avis,
    /// jamais de valeur inventee.</summary>
    Task<AvisEtablissement> ListerParEtablissementAsync(Guid idEtablissement, CancellationToken cancellationToken);
}
