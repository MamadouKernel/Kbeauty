using System.Security.Cryptography;
using System.Text;

namespace KekeBeauty.Application.Auth;

public sealed class VerifyEmailUseCase
{
    private readonly IUtilisateurRepository _utilisateurRepository;

    public VerifyEmailUseCase(IUtilisateurRepository utilisateurRepository)
    {
        _utilisateurRepository = utilisateurRepository;
    }

    public async Task<bool> ExecuteAsync(Guid idUtilisateur, string code, CancellationToken cancellationToken)
    {
        var consumed = await _utilisateurRepository.ConsumePasswordAuthTokenAsync(idUtilisateur, "VERIFY_EMAIL", HashCode(code), cancellationToken);
        if (!consumed)
        {
            return false;
        }

        return await _utilisateurRepository.SetEmailVerifieAsync(idUtilisateur, cancellationToken);
    }

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
}
