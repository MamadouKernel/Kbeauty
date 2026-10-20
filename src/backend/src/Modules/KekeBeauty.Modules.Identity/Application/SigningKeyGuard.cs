using System.Security.Cryptography;

namespace KekeBeauty.Modules.Identity.Application;

/// <summary>
/// Keeps a published or guessable JWT signing key out of production. The repository is public: any key that was ever committed,
/// including the development and test keys, must be treated as known to everybody, because a token signed with it is accepted as genuine.
/// </summary>
public static class SigningKeyGuard
{
    /// <summary>Keys that appeared in the repository (current or past commits): never valid in production.</summary>
    private static readonly string[] BurnedKeys =
    [
        "dev-only-signing-key-change-me-0123456789abcdef",
        "integration-tests-signing-key-0123456789abcdef",
    ];

    private static readonly string[] PlaceholderMarkers = ["change-me", "changeme", "dev-only", "your-key", "example", "secret-key"];

    /// <summary>A fresh random key for a development instance that was given none; it only lives as long as the process.</summary>
    public static string NewEphemeralKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    /// <summary>Why a key must not be used in production, or null when it looks acceptable.</summary>
    public static string? RejectionReason(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
        {
            return "it is missing or shorter than 32 characters";
        }

        if (BurnedKeys.Contains(key, StringComparer.Ordinal))
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
