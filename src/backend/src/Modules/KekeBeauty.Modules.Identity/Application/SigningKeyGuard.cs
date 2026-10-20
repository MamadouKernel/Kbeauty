using System.Security.Cryptography;

namespace KekeBeauty.Modules.Identity.Application;

/// <summary>
/// Keeps a published or guessable JWT signing key out of production. The repository is public: any key that was ever committed,
/// including the development and test keys, must be treated as known to everybody, because a token signed with it is accepted as genuine.
/// </summary>
public static class SigningKeyGuard
{
    /// <summary>
    /// SHA-256 fingerprints (lowercase hex) of keys that appeared in the repository, current or past commits. Only the fingerprints are
    /// kept here: the guard recognises a published key without repeating it.
    /// </summary>
    private static readonly string[] BurnedKeyFingerprints =
    [
        "3e18ea49bc8a5528b730b49b2ee3bef5dadc73dc8722024de0cec988d7b375af",
        "d4a436d602ed026bb1e370ffdcd5dfdab8c90b479b9482fa1f9fa1313bd7194a",
    ];

    private static readonly string[] PlaceholderMarkers = ["change-me", "changeme", "dev-only", "your-key", "example", "secret-key"];

    /// <summary>A fresh random key for a development instance that was given none; it only lives as long as the process.</summary>
    public static string NewEphemeralKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    /// <summary>SHA-256 of the key as lowercase hex.</summary>
    public static string Fingerprint(string key) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)));

    /// <summary>Why a key must not be used in production, or null when it looks acceptable.</summary>
    public static string? RejectionReason(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
        {
            return "it is missing or shorter than 32 characters";
        }

        if (BurnedKeyFingerprints.Contains(Fingerprint(key), StringComparer.Ordinal))
        {
            return "it is a development or test key that is published in the repository";
        }

        if (PlaceholderMarkers.Any(marker => key.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return "it looks like a placeholder";
        }

        return key.Distinct().Count() < 12 ? "it has too little variety to be random" : null;
    }
}
