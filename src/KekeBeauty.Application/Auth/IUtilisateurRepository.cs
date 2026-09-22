using KekeBeauty.Domain.Entities;

namespace KekeBeauty.Application.Auth;

public interface IUtilisateurRepository
{
    Task<Utilisateur?> FindByTelephoneAsync(string telephone, TypeCompte typeCompte, CancellationToken cancellationToken);

    /// <summary>
    /// Cree l'utilisateur si absent (FR-007, applique via la contrainte UNIQUE (telephone, type_compte)
    /// deja presente en base). Retourne (utilisateur, estNouveau).
    /// </summary>
    Task<Utilisateur?> GetProfileAsync(Guid idUtilisateur, CancellationToken cancellationToken);
    Task<bool> UpdateProfileAsync(Guid idUtilisateur, string nom, string? email, bool notificationsRdv, bool notificationsMarketing, bool consentementDonnees, CancellationToken cancellationToken);
    Task<bool> AnonymizeAsync(Guid idUtilisateur, CancellationToken cancellationToken);
    Task<bool> UpdateTelephoneAsync(Guid idUtilisateur, string telephone, CancellationToken cancellationToken);
    Task<(Utilisateur Utilisateur, bool IsNewAccount)> FindOrCreateGoogleAsync(string googleSubject, string? email, string nom, TypeCompte typeCompte, CancellationToken cancellationToken);
    Task<bool> UpdatePartnerTelephoneAsync(Guid idUtilisateur, string telephone, CancellationToken cancellationToken);
    Task StorePartnerOnboardingTokenAsync(Guid idUtilisateur, string tokenHash, DateTimeOffset expireLe, CancellationToken cancellationToken);
    Task<Guid?> ConsumePartnerOnboardingTokenAsync(string tokenHash, string telephone, CancellationToken cancellationToken);
    Task DeletePartnerOnboardingTokenAsync(Guid idUtilisateur, CancellationToken cancellationToken);
    Task<bool> UpdatePartnerPhotoAsync(Guid idUtilisateur, string relativePath, CancellationToken cancellationToken);
    Task<string?> GetPartnerPhotoPathAsync(Guid idUtilisateur, CancellationToken cancellationToken);

    Task<(Utilisateur Utilisateur, bool IsNewAccount)> FindOrCreateAsync(
        string telephone, TypeCompte typeCompte, CancellationToken cancellationToken);
    Task<int> GetLoyaltyPointsAsync(Guid idUtilisateur, CancellationToken cancellationToken);

    // Auth par email + mot de passe (additive aux flux OTP/Google existants).
    Task<Utilisateur?> FindByEmailAsync(string email, TypeCompte typeCompte, CancellationToken cancellationToken);
    Task<Guid> CreateWithPasswordAsync(string nom, string telephone, string email, string passwordHash, TypeCompte typeCompte, CancellationToken cancellationToken);
    Task<bool> SetPasswordHashAsync(Guid idUtilisateur, string passwordHash, CancellationToken cancellationToken);
    Task<bool> SetEmailVerifieAsync(Guid idUtilisateur, CancellationToken cancellationToken);
    Task StorePasswordAuthTokenAsync(Guid idUtilisateur, string codeHash, string purpose, DateTimeOffset expireLe, CancellationToken cancellationToken);
    Task<bool> ConsumePasswordAuthTokenAsync(Guid idUtilisateur, string purpose, string codeHash, CancellationToken cancellationToken);
}

