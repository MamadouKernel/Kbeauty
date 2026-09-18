using KekeBeauty.Domain.Entities;

namespace KekeBeauty.Application.Auth;

public interface IUtilisateurRepository
{
    Task<Utilisateur?> FindByTelephoneAsync(string telephone, TypeCompte typeCompte, CancellationToken cancellationToken);

    /// <summary>
    /// Cree l'utilisateur si absent (FR-007, applique via la contrainte UNIQUE (telephone, type_compte)
    /// deja presente en base). Retourne (utilisateur, estNouveau).
    /// </summary>
    Task<(Utilisateur Utilisateur, bool IsNewAccount)> FindOrCreateAsync(
        string telephone, TypeCompte typeCompte, CancellationToken cancellationToken);
}
