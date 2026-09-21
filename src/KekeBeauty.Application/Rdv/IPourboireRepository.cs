namespace KekeBeauty.Application.Rdv;

public interface IPourboireRepository
{
    /// <summary>Verifie que la collaboratrice appartient bien au meme etablissement que le RDV
    /// (pas de pourboire envoye a une collaboratrice d'un autre salon).</summary>
    Task<bool> CollaboratriceValidePourRdvAsync(Guid idRdv, Guid idCollaborateur, CancellationToken cancellationToken);

    Task<Guid> CreerAsync(Guid idRdv, Guid idCollaborateur, decimal montant, string referenceExterne, CancellationToken cancellationToken);

    Task MarquerStatutAsync(string referenceExterne, string statutPaiement, CancellationToken cancellationToken);

    Task<PourboireReversementDto?> ObtenirParReferenceAsync(string referenceExterne, CancellationToken cancellationToken);

    Task<IReadOnlyList<PourboireReversementDto>> ListerAReverserAsync(CancellationToken cancellationToken);

    Task EnregistrerReversementAsync(Guid idPourboire, bool success, string status, string? reference, string? erreur, CancellationToken cancellationToken);
}

public sealed class PourboireReversementDto
{
    public Guid IdPourboire { get; set; }
    public decimal Montant { get; set; }
    public string Telephone { get; set; } = string.Empty;
    public string Nom { get; set; } = string.Empty;
    public string ReferenceExterne { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}
