using System.Security.Cryptography;
using System.Text;
using KekeBeauty.Domain.Entities;

namespace KekeBeauty.Application.Auth;

/// <summary>
/// Toujours "succes" cote appelant, que l'email existe ou non (evite l'enumeration de comptes) :
/// voir AuthPasswordController, qui ne distingue jamais les deux cas dans sa reponse HTTP.
/// </summary>
public sealed class RequestPasswordResetUseCase
{
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);

    private readonly IUtilisateurRepository _utilisateurRepository;
    private readonly IEmailSender _emailSender;

    public RequestPasswordResetUseCase(IUtilisateurRepository utilisateurRepository, IEmailSender emailSender)
    {
        _utilisateurRepository = utilisateurRepository;
        _emailSender = emailSender;
    }

    public async Task ExecuteAsync(string email, TypeCompte typeCompte, CancellationToken cancellationToken)
    {
        var utilisateur = await _utilisateurRepository.FindByEmailAsync(email, typeCompte, cancellationToken);
        if (utilisateur is null)
        {
            return;
        }

        var code = GenerateSixDigitCode();
        await _utilisateurRepository.StorePasswordAuthTokenAsync(
            utilisateur.IdUtilisateur, HashCode(code), "RESET_PASSWORD", DateTimeOffset.UtcNow.Add(CodeLifetime), cancellationToken);

        await _emailSender.SendAsync(
            email, "Keke Beauty — Réinitialisation du mot de passe",
            $"Votre code de réinitialisation Keke Beauty est : {code}. Il expire dans 15 minutes. Si vous n'êtes pas à l'origine de cette demande, ignorez ce message.",
            cancellationToken);
    }

    private static string GenerateSixDigitCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
}
