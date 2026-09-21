using System.Security.Cryptography;
using System.Text;

namespace KekeBeauty.Api.Auth;

/// <summary>
/// Jeton "appareil de confiance" (2FA par etape) : une fois le step-up complete avec succes sur un
/// navigateur, ce jeton signe (stocke cote client, ProtectedLocalStorage) permet de sauter le
/// step-up sur les connexions Google suivantes depuis le meme navigateur, pendant 60 jours. Meme
/// principe HMAC que PartnerSessionTokenService/UserSessionTokenService.
/// </summary>
public sealed class DeviceTrustTokenService
{
    private readonly byte[] _key;

    public DeviceTrustTokenService(IConfiguration configuration)
    {
        var secret = configuration["Authentication:SessionSigningKey"] ?? configuration["Admin:SigningKey"];
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException("Authentication:SessionSigningKey doit contenir au moins 32 octets.");
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    public string Create(Guid userId)
    {
        var payload = $"{userId:N}|{DateTimeOffset.UtcNow.AddDays(60).ToUnixTimeSeconds()}";
        var data = Encode(Encoding.UTF8.GetBytes(payload));
        var signature = Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(data)));
        return $"{data}.{signature}";
    }

    public bool IsTrusted(string? token, Guid userId)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var parts = token.Split('.');
        if (parts.Length != 2) return false;
        var expected = Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(parts[0])));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(parts[1]))) return false;
        try
        {
            var values = Encoding.UTF8.GetString(Decode(parts[0])).Split('|');
            return values.Length == 2 && Guid.TryParseExact(values[0], "N", out var tokenUserId) && tokenUserId == userId
                && long.TryParse(values[1], out var expiresAt) && DateTimeOffset.UtcNow.ToUnixTimeSeconds() < expiresAt;
        }
        catch { return false; }
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Convert.FromBase64String(base64);
    }
}
