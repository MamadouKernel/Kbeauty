using System.Security.Cryptography;
using System.Text;
using KekeBeauty.Domain.Entities;

namespace KekeBeauty.Application.Auth;

public sealed class VerifyOtpUseCase
{
    private const string TargetTypeCompte = "CLIENT";

    private readonly IOtpChallengeRepository _challengeRepository;
    private readonly IUtilisateurRepository _utilisateurRepository;

    public VerifyOtpUseCase(IOtpChallengeRepository challengeRepository, IUtilisateurRepository utilisateurRepository)
    {
        _challengeRepository = challengeRepository;
        _utilisateurRepository = utilisateurRepository;
    }

    public async Task<VerifyOtpResult> ExecuteAsync(string telephone, string code, CancellationToken cancellationToken)
    {
        var challenge = await _challengeRepository.GetActivePendingAsync(telephone, TargetTypeCompte, cancellationToken);

        if (challenge is null || challenge.ExpiresAt < DateTimeOffset.UtcNow)
        {
            return new VerifyOtpResult(false, "invalid_or_expired_code");
        }

        if (!string.Equals(challenge.CodeHash, HashCode(code), StringComparison.Ordinal))
        {
            return new VerifyOtpResult(false, "invalid_or_expired_code");
        }

        await _challengeRepository.MarkConsumedAsync(challenge.Id, cancellationToken);

        var (utilisateur, isNewAccount) = await _utilisateurRepository.FindOrCreateAsync(
            telephone, TypeCompte.Client, cancellationToken);

        return new VerifyOtpResult(true, "verified", utilisateur.IdUtilisateur, isNewAccount);
    }

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
}
