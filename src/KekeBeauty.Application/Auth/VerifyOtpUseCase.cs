using System.Security.Cryptography;
using System.Text;
using KekeBeauty.Domain.Entities;

namespace KekeBeauty.Application.Auth;

public sealed class VerifyOtpUseCase
{
    private readonly IOtpChallengeRepository _challengeRepository;
    private readonly IUtilisateurRepository _utilisateurRepository;

    public VerifyOtpUseCase(IOtpChallengeRepository challengeRepository, IUtilisateurRepository utilisateurRepository)
    {
        _challengeRepository = challengeRepository;
        _utilisateurRepository = utilisateurRepository;
    }

    public Task<VerifyOtpResult> ExecuteAsync(string telephone, string code, CancellationToken cancellationToken) =>
        ExecuteAsync(telephone, TypeCompte.Client, code, cancellationToken);

    // Feature 006 : generalise au type de compte (CLIENT par defaut pour compatibilite avec 003).
    public async Task<VerifyOtpResult> ExecuteAsync(string telephone, TypeCompte typeCompte, string code, CancellationToken cancellationToken)
    {
        var targetTypeCompte = typeCompte.ToString().ToUpperInvariant();
        var challenge = await _challengeRepository.GetActivePendingAsync(telephone, targetTypeCompte, cancellationToken);

        if (challenge is null || challenge.ExpiresAt < DateTimeOffset.UtcNow || challenge.Attempts >= OtpChallengePolicy.MaxAttempts)
        {
            return new VerifyOtpResult(false, "invalid_or_expired_code");
        }

        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(challenge.CodeHash), Encoding.UTF8.GetBytes(HashCode(code))))
        {
            // Audit securite : compteur d'echecs independant du rate limiting par IP, qui a lui
            // seul ne bloque pas un brute force distribue sur plusieurs IP pour un meme numero.
            await _challengeRepository.RegisterFailedAttemptAsync(challenge.Id, cancellationToken);
            return new VerifyOtpResult(false, "invalid_or_expired_code");
        }

        await _challengeRepository.MarkConsumedAsync(challenge.Id, cancellationToken);

        var (utilisateur, isNewAccount) = await _utilisateurRepository.FindOrCreateAsync(
            telephone, typeCompte, cancellationToken);

        return new VerifyOtpResult(true, "verified", utilisateur.IdUtilisateur, isNewAccount);
    }

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
}
