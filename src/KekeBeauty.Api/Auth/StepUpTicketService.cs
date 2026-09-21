using System.Security.Cryptography;
using System.Text;

namespace KekeBeauty.Api.Auth;

/// <summary>
/// Jeton temporaire (10 min) porte par le client entre "step_up_required" et la verification du
/// code : encode l'utilisateur et son type de compte sans jamais faire confiance a une valeur
/// fournie par le client sans signature. Meme principe HMAC que les autres services de jeton.
/// </summary>
public sealed class StepUpTicketService
{
    private readonly byte[] _key;

    public StepUpTicketService(IConfiguration configuration)
    {
        var secret = configuration["Authentication:SessionSigningKey"] ?? configuration["Admin:SigningKey"];
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException("Authentication:SessionSigningKey doit contenir au moins 32 octets.");
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    public string Create(Guid userId, string typeCompte)
    {
        var payload = $"{userId:N}|{typeCompte}|{DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds()}";
        var data = Encode(Encoding.UTF8.GetBytes(payload));
        var signature = Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(data)));
        return $"{data}.{signature}";
    }

    public bool TryValidate(string? ticket, out Guid userId, out string typeCompte)
    {
        userId = Guid.Empty; typeCompte = "";
        if (string.IsNullOrWhiteSpace(ticket)) return false;
        var parts = ticket.Split('.');
        if (parts.Length != 2) return false;
        var expected = Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(parts[0])));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(parts[1]))) return false;
        try
        {
            var values = Encoding.UTF8.GetString(Decode(parts[0])).Split('|');
            if (values.Length != 3 || !Guid.TryParseExact(values[0], "N", out userId) ||
                !long.TryParse(values[2], out var expiresAt) || DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expiresAt)
            {
                userId = Guid.Empty;
                return false;
            }
            typeCompte = values[1];
            return true;
        }
        catch { userId = Guid.Empty; return false; }
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Convert.FromBase64String(base64);
    }
}
