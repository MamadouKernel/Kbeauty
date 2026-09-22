using System.Security.Cryptography;
using System.Text;
using KekeBeauty.Domain.Entities;

namespace KekeBeauty.Application.Auth;

public sealed record ResetPasswordResult(bool Success, string Status);

public sealed class ResetPasswordUseCase
{
    private readonly IUtilisateurRepository _utilisateurRepository;

    public ResetPasswordUseCase(IUtilisateurRepository utilisateurRepository)
    {
        _utilisateurRepository = utilisateurRepository;
    }

    public async Task<ResetPasswordResult> ExecuteAsync(string email, TypeCompte typeCompte, string code, string newPassword, CancellationToken cancellationToken)
    {
        if (newPassword.Length < 8)
        {
            return new ResetPasswordResult(false, "weak_password");
        }

        var utilisateur = await _utilisateurRepository.FindByEmailAsync(email, typeCompte, cancellationToken);
        if (utilisateur is null)
        {
            return new ResetPasswordResult(false, "invalid_or_expired_code");
        }

        var consumed = await _utilisateurRepository.ConsumePasswordAuthTokenAsync(
            utilisateur.IdUtilisateur, "RESET_PASSWORD", HashCode(code), cancellationToken);
        if (!consumed)
        {
            return new ResetPasswordResult(false, "invalid_or_expired_code");
        }

        await _utilisateurRepository.SetPasswordHashAsync(utilisateur.IdUtilisateur, PasswordHasher.Hash(newPassword), cancellationToken);
        return new ResetPasswordResult(true, "reset");
    }

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
}
