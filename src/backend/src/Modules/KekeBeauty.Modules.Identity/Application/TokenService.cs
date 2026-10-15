using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using KekeBeauty.Modules.Identity.Domain;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace KekeBeauty.Modules.Identity.Application;

/// <summary>Access-token (JWT) issuing plus helpers for opaque secrets (stored only as SHA-256 hashes).</summary>
public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider time)
{
    private const string RecoveryAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private readonly JsonWebTokenHandler _handler = new();

    public (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(User user, Guid sessionId)
    {
        var jwt = options.Value;
        var now = time.GetUtcNow();
        var expires = now.AddMinutes(jwt.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString()),
            new("email", user.Email),
            new("sid", sessionId.ToString()),
        };
        claims.AddRange(user.Roles.Select(role => new Claim("role", role)));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            NotBefore = now.UtcDateTime,
            IssuedAt = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                SecurityAlgorithms.HmacSha256),
        };

        return (_handler.CreateToken(descriptor), expires);
    }

    public static string NewOpaqueToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>Ten single-use recovery codes shaped like <c>ABCDE-FGHJK</c>.</summary>
    public static IReadOnlyList<string> NewRecoveryCodes(int count = 10) =>
        Enumerable.Range(0, count).Select(_ => NewRecoveryCode()).ToList();

    public static string NormalizeRecoveryCode(string code) =>
        code.Replace("-", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    private static string NewRecoveryCode()
    {
        var chars = Enumerable.Range(0, 10).Select(_ => RecoveryAlphabet[RandomNumberGenerator.GetInt32(RecoveryAlphabet.Length)]).ToArray();
        return $"{new string(chars, 0, 5)}-{new string(chars, 5, 5)}";
    }
}
