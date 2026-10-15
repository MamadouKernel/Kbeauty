using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using OtpNet;

namespace KekeBeauty.Modules.Identity.Application;

/// <summary>RFC 6238 TOTP (Google Authenticator compatible: SHA-1, 6 digits, 30 s) with encrypted-at-rest secrets.</summary>
public sealed class TotpService(IDataProtectionProvider dataProtection, TimeProvider time, IOptions<AuthOptions> options)
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("KekeBeauty.Identity.Totp.v1");

    public string GenerateSecret() => Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));

    public string Protect(string secret) => _protector.Protect(secret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);

    public string BuildOtpauthUri(string email, string secret)
    {
        var issuer = Uri.EscapeDataString(options.Value.TotpIssuer);
        var label = $"{issuer}:{Uri.EscapeDataString(email)}";
        return $"otpauth://totp/{label}?secret={secret}&issuer={issuer}&algorithm=SHA1&digits=6&period=30";
    }

    /// <summary>
    /// Verifies a 6-digit code within ±1 time step. A code is rejected when its step is not newer than
    /// <paramref name="lastAcceptedStep"/> (replay protection).
    /// </summary>
    public bool TryVerify(string secret, string code, long? lastAcceptedStep, out long step)
    {
        step = 0;
        if (code.Length != 6 || !code.All(char.IsAsciiDigit))
        {
            return false;
        }

        var totp = new Totp(Base32Encoding.ToBytes(secret));
        var now = time.GetUtcNow().UtcDateTime;
        if (!totp.VerifyTotp(now, code, out var matched, new VerificationWindow(previous: 1, future: 1)))
        {
            return false;
        }

        if (lastAcceptedStep is { } last && matched <= last)
        {
            return false;
        }

        step = matched;
        return true;
    }
}
