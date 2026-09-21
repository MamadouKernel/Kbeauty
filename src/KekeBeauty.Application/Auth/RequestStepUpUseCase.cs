using System.Security.Cryptography;
using System.Text;

namespace KekeBeauty.Application.Auth;

public sealed class RequestStepUpResult
{
    public bool EmailSent { get; init; }
    public bool WhatsAppSent { get; init; }
}

/// <summary>
/// 2FA "par etape" (step-up) pour les comptes CLIENT/PARTENAIRE connectes via Google, uniquement
/// depuis un appareil non reconnu (voir GoogleAuthController). Deux canaux best-effort, independants
/// l'un de l'autre : email (SmtpEmailSender) et WhatsApp (meme IOtpSender que l'OTP telephone,
/// reutilise tel quel). Le code n'est jamais journalise en clair (meme regle que RequestOtpUseCase).
/// </summary>
public sealed class RequestStepUpUseCase
{
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);

    private readonly IStepUpChallengeRepository _challengeRepository;
    private readonly IOtpSender _otpSender;
    private readonly IEmailSender _emailSender;

    public RequestStepUpUseCase(IStepUpChallengeRepository challengeRepository, IOtpSender otpSender, IEmailSender emailSender)
    {
        _challengeRepository = challengeRepository;
        _otpSender = otpSender;
        _emailSender = emailSender;
    }

    public async Task<RequestStepUpResult> ExecuteAsync(Guid idUtilisateur, string? email, string telephone, CancellationToken cancellationToken)
    {
        var code = GenerateSixDigitCode();
        var codeHash = HashCode(code);
        var expiresAt = DateTimeOffset.UtcNow.Add(CodeLifetime);

        await _challengeRepository.CreateAndInvalidatePreviousAsync(idUtilisateur, codeHash, expiresAt, cancellationToken);

        var whatsAppSent = await _otpSender.SendAsync(telephone, code, cancellationToken);
        var emailSent = !string.IsNullOrWhiteSpace(email) && await _emailSender.SendAsync(
            email!, "Keke Beauty — Code de vérification",
            $"Votre code de vérification Keke Beauty est : {code}. Il expire dans 10 minutes. Si vous n'êtes pas à l'origine de cette connexion, ignorez ce message.",
            cancellationToken);

        return new RequestStepUpResult { EmailSent = emailSent, WhatsAppSent = whatsAppSent };
    }

    private static string GenerateSixDigitCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
}
