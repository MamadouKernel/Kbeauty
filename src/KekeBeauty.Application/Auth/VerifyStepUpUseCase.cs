using System.Security.Cryptography;
using System.Text;

namespace KekeBeauty.Application.Auth;

public sealed class VerifyStepUpUseCase
{
    private readonly IStepUpChallengeRepository _challengeRepository;

    public VerifyStepUpUseCase(IStepUpChallengeRepository challengeRepository)
    {
        _challengeRepository = challengeRepository;
    }

    public Task<bool> ExecuteAsync(Guid idUtilisateur, string code, CancellationToken cancellationToken) =>
        _challengeRepository.TryConsumeAsync(idUtilisateur, HashCode(code), cancellationToken);

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
}
