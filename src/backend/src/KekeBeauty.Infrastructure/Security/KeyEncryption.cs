using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KekeBeauty.Infrastructure.Security;

/// <summary>
/// The key that protects the Data Protection key ring at rest. The ring holds the keys that encrypt every TOTP secret and the push private
/// key: stored in clear next to them, a copy of the database (or of a backup) would open every second factor. This key lives in the
/// deployment's keystore (.env in development, a secrets file or a secret manager in production), never in the database or the repository.
/// </summary>
public sealed class KeyEncryptionOptions
{
    public const string SectionName = "DataProtection";

    /// <summary>Base64 of 32 random bytes, for example <c>openssl rand -base64 32</c>.</summary>
    public string KeyEncryptionKey { get; init; } = string.Empty;
}

public static class KeyEncryption
{
    private static readonly byte[] AssociatedData = "KekeBeauty.DataProtection.KeyRing.v1"u8.ToArray();
    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <summary>The 32-byte key, null when none is configured; throws when one is configured but is not a valid key.</summary>
    public static byte[]? Parse(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return null;
        }

        try
        {
            var key = Convert.FromBase64String(base64.Trim());
            if (key.Length == 32 && key.Distinct().Count() >= 8)
            {
                return key;
            }
        }
        catch (FormatException)
        {
        }

        throw new InvalidOperationException("DataProtection:KeyEncryptionKey must be the base64 of 32 random bytes (for example `openssl rand -base64 32`).");
    }

    internal static byte[] Seal(byte[] plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, cipher, tag, AssociatedData);
        return [.. nonce, .. tag, .. cipher];
    }

    internal static byte[] Open(byte[] sealedBytes, byte[] key)
    {
        if (sealedBytes.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("The protected key is too short.");
        }

        var nonce = sealedBytes.AsSpan(0, NonceSize);
        var tag = sealedBytes.AsSpan(NonceSize, TagSize);
        var cipher = sealedBytes.AsSpan(NonceSize + TagSize);
        var plaintext = new byte[cipher.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plaintext, AssociatedData);
        return plaintext;
    }
}

/// <summary>Encrypts the secret parts of a Data Protection key (AES-256-GCM, authenticated) before it is stored.</summary>
public sealed class AesGcmXmlEncryptor(byte[] key) : IXmlEncryptor
{
    internal static readonly XNamespace Namespace = "urn:kekebeauty:dataprotection";

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        var sealedBytes = KeyEncryption.Seal(Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting)), key);
        var element = new XElement(Namespace + "encryptedKey", new XElement(Namespace + "value", Convert.ToBase64String(sealedBytes)));
        return new EncryptedXmlInfo(element, typeof(AesGcmXmlDecryptor));
    }
}

/// <summary>Reads back what <see cref="AesGcmXmlEncryptor"/> wrote; the framework builds it from the service provider.</summary>
public sealed class AesGcmXmlDecryptor(IServiceProvider services) : IXmlDecryptor
{
    public XElement Decrypt(XElement encryptedElement)
    {
        var key = KeyEncryption.Parse(services.GetRequiredService<IOptions<KeyEncryptionOptions>>().Value.KeyEncryptionKey)
            ?? throw new InvalidOperationException("The key ring is encrypted but DataProtection:KeyEncryptionKey is not configured.");
        var value = encryptedElement.Element(AesGcmXmlEncryptor.Namespace + "value")?.Value
            ?? throw new CryptographicException("The protected key has no value.");
        return XElement.Parse(Encoding.UTF8.GetString(KeyEncryption.Open(Convert.FromBase64String(value), key)));
    }
}

/// <summary>Encrypts, in place, the secret parts of a stored Data Protection key that is still in clear (written before a key was configured).</summary>
public static class KeyXmlProtector
{
    private static readonly XNamespace Dp = "http://schemas.asp.net/2015/03/dataProtection";

    /// <summary>True when the document still holds a secret in clear.</summary>
    public static bool HasPlaintextSecret(XElement key) =>
        key.Descendants().Any(e => (string?)e.Attribute(Dp + "requiresEncryption") == "true" && e.Ancestors(Dp + "encryptedSecret").Any() is false);

    /// <summary>Replaces each clear secret by its encrypted form, in the format the framework itself writes; returns whether anything changed.</summary>
    public static bool EncryptPlaintextSecrets(XElement key, IXmlEncryptor encryptor)
    {
        var changed = false;
        foreach (var secret in key.Descendants()
                     .Where(e => (string?)e.Attribute(Dp + "requiresEncryption") == "true" && !e.Ancestors(Dp + "encryptedSecret").Any())
                     .ToList())
        {
            var info = encryptor.Encrypt(secret);
            secret.ReplaceWith(new XElement(
                Dp + "encryptedSecret",
                new XAttribute("decryptorType", info.DecryptorType.AssemblyQualifiedName!),
                info.EncryptedElement));
            changed = true;
        }

        return changed;
    }
}
