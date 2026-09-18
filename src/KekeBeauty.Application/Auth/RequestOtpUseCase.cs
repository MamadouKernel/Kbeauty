using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using KekeBeauty.Domain.Entities;

namespace KekeBeauty.Application.Auth;

public sealed partial class RequestOtpUseCase
{
    private static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(5);

    private readonly IOtpChallengeRepository _challengeRepository;
    private readonly IOtpSender _otpSender;

    public RequestOtpUseCase(IOtpChallengeRepository challengeRepository, IOtpSender otpSender)
    {
        _challengeRepository = challengeRepository;
        _otpSender = otpSender;
    }

    public async Task<RequestOtpResult> ExecuteAsync(string telephone, CancellationToken cancellationToken) =>
        await ExecuteAsync(telephone, TypeCompte.Client, cancellationToken);

    // Feature 006 : generalise au type de compte (CLIENT par defaut pour compatibilite avec 003).
    public async Task<RequestOtpResult> ExecuteAsync(string telephone, TypeCompte typeCompte, CancellationToken cancellationToken)
    {
        if (!IsValidE164(telephone))
        {
            return new RequestOtpResult(false, "invalid_phone", "Format de numero invalide (E.164 attendu).");
        }

        var code = GenerateSixDigitCode();
        var codeHash = HashCode(code);
        var expiresAt = DateTimeOffset.UtcNow.Add(OtpLifetime);
        var targetTypeCompte = typeCompte.ToString().ToUpperInvariant();

        await _challengeRepository.CreateAndInvalidatePreviousAsync(
            telephone, targetTypeCompte, codeHash, expiresAt, cancellationToken);

        var sent = await _otpSender.SendAsync(telephone, code, cancellationToken);
        return sent
            ? new RequestOtpResult(true, "sent")
            : new RequestOtpResult(false, "send_failed", "Envoi du code impossible pour le moment.");
    }

    private static bool IsValidE164(string telephone) => E164Regex().IsMatch(telephone);

    private static string GenerateSixDigitCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();

    [GeneratedRegex(@"^\+[1-9]\d{7,14}$")]
    private static partial Regex E164Regex();
}
