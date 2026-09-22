using KekeBeauty.Domain.Entities;

namespace KekeBeauty.Application.Auth;

public sealed record LoginWithPasswordResult(bool Success, string Status, Guid? IdUtilisateur = null);

public sealed class LoginWithPasswordUseCase
{
    private readonly IUtilisateurRepository _utilisateurRepository;

    public LoginWithPasswordUseCase(IUtilisateurRepository utilisateurRepository)
    {
        _utilisateurRepository = utilisateurRepository;
    }

    public async Task<LoginWithPasswordResult> ExecuteAsync(string email, string password, TypeCompte typeCompte, CancellationToken cancellationToken)
    {
        var utilisateur = await _utilisateurRepository.FindByEmailAsync(email, typeCompte, cancellationToken);
        // Meme message d'erreur generique que le mot de passe soit faux ou le compte inexistant,
        // pour ne pas laisser deviner quels emails sont inscrits (enumeration attack).
        if (utilisateur is null || utilisateur.PasswordHash is null || !PasswordHasher.Verify(password, utilisateur.PasswordHash))
        {
            return new LoginWithPasswordResult(false, "invalid_credentials");
        }

        if (utilisateur.EstSuspendu)
        {
            return new LoginWithPasswordResult(false, "account_suspended");
        }

        if (!utilisateur.EmailVerifie)
        {
            return new LoginWithPasswordResult(false, "email_not_verified");
        }

        return new LoginWithPasswordResult(true, "verified", utilisateur.IdUtilisateur);
    }
}
