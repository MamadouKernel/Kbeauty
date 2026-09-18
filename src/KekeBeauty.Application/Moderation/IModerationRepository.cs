namespace KekeBeauty.Application.Moderation;

public interface IModerationRepository
{
    /// <summary>Idempotent (FR-009). Retourne false si l'identifiant n'existe pas (FR-004).</summary>
    Task<bool> SetUtilisateurSuspenduAsync(Guid idUtilisateur, bool suspendu, CancellationToken cancellationToken);

    /// <summary>Idempotent (FR-009). Retourne false si l'identifiant n'existe pas (FR-004).</summary>
    Task<bool> SetEtablissementSuspenduAsync(Guid idEtablissement, bool suspendu, CancellationToken cancellationToken);
}
