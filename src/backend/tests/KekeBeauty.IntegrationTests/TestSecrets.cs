using System.Security.Cryptography;

namespace KekeBeauty.IntegrationTests;

/// <summary>
/// Every secret the tests need, generated at random for each test run and kept in memory only: the repository is public, so no key,
/// token or password may be written in it, not even a test one.
/// </summary>
internal static class TestSecrets
{
    private static string Random(int bytes) => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(bytes));

    /// <summary>JWT signing key of the API under test.</summary>
    public static readonly string SigningKey = Random(32);

    /// <summary>Shared secret of the SMS delivery-report webhook.</summary>
    public static readonly string WebhookSecret = Random(16);

    /// <summary>Signing secret of the Zavu (WhatsApp) webhook.</summary>
    public static readonly string ZavuWebhookSecret = "whsec_" + Random(16);

    /// <summary>Secret with which the fake CinetPay signs its notifications.</summary>
    public static readonly string CinetPaySecret = Random(16);

    /// <summary>Merchant token of the fake WiniPayer (also keys the signature of our callback URLs).</summary>
    public static readonly string WinipayToken = Random(16);

    /// <summary>Password of the bootstrap SuperAdmin the fixture creates at startup.</summary>
    public static readonly string AdminPassword = "Zx-" + Random(8) + "9";

    /// <summary>Key that encrypts the Data Protection key ring at rest (base64 of 32 random bytes).</summary>
    public static readonly string KeyEncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>The "auth" secret of a Web Push subscription (16 random bytes, base64url).</summary>
    public static readonly string PushAuth = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Password of every throw-away account; meets the password rules (length, a letter, a digit).</summary>
    public static readonly string Password = "Zx-" + Random(8) + "7";
}
