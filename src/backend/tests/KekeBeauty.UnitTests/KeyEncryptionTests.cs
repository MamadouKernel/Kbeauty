using System.Security.Cryptography;
using System.Xml.Linq;
using FluentAssertions;
using KekeBeauty.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KekeBeauty.UnitTests;

/// <summary>The Data Protection key ring holds the keys of every TOTP secret: it must not sit in clear next to them.</summary>
public sealed class KeyEncryptionTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keke-keyring-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static byte[] NewKey() => RandomNumberGenerator.GetBytes(32);

    private ServiceProvider Ring(byte[]? keyEncryptionKey)
    {
        var services = new ServiceCollection();
        services.AddDataProtection().SetApplicationName("keke-test").PersistKeysToFileSystem(new DirectoryInfo(_directory));
        if (keyEncryptionKey is not null)
        {
            services.AddSingleton(Options.Create(new KeyEncryptionOptions { KeyEncryptionKey = Convert.ToBase64String(keyEncryptionKey) }));
            services.Configure<KeyManagementOptions>(options => options.XmlEncryptor = new AesGcmXmlEncryptor(keyEncryptionKey));
        }

        return services.BuildServiceProvider();
    }

    private static string Protect(ServiceProvider ring, string text) => ring.GetRequiredService<IDataProtectionProvider>().CreateProtector("totp").Protect(text);

    private static string Unprotect(ServiceProvider ring, string cipher) => ring.GetRequiredService<IDataProtectionProvider>().CreateProtector("totp").Unprotect(cipher);

    private string AllKeyFiles() => string.Concat(Directory.GetFiles(_directory, "*.xml").Select(File.ReadAllText));

    [Fact]
    public void A_new_key_is_stored_encrypted_and_still_works_after_a_restart()
    {
        var kek = NewKey();
        string cipher;
        using (var ring = Ring(kek))
        {
            cipher = Protect(ring, "JBSWY3DPEHPK3PXP-like-secret");
        }

        AllKeyFiles().Should().Contain("encryptedSecret").And.NotContain("unencrypted form");

        using var restarted = Ring(kek);
        Unprotect(restarted, cipher).Should().Be("JBSWY3DPEHPK3PXP-like-secret");
    }

    [Fact]
    public void Keys_stored_in_clear_by_an_earlier_version_are_encrypted_without_losing_the_data_they_protect()
    {
        string cipher;
        using (var before = Ring(null))
        {
            cipher = Protect(before, "a second factor");
        }

        AllKeyFiles().Should().Contain("unencrypted form");

        var kek = NewKey();
        var encryptor = new AesGcmXmlEncryptor(kek);
        foreach (var file in Directory.GetFiles(_directory, "*.xml"))
        {
            var key = XElement.Load(file);
            KeyXmlProtector.HasPlaintextSecret(key).Should().BeTrue();
            KeyXmlProtector.EncryptPlaintextSecrets(key, encryptor).Should().BeTrue();
            KeyXmlProtector.HasPlaintextSecret(key).Should().BeFalse();
            File.WriteAllText(file, key.ToString(SaveOptions.DisableFormatting));
        }

        AllKeyFiles().Should().Contain("encryptedSecret");
        foreach (var file in Directory.GetFiles(_directory, "*.xml"))
        {
            KeyXmlProtector.EncryptPlaintextSecrets(XElement.Load(file), encryptor).Should().BeFalse("running it again must change nothing");
        }

        using var after = Ring(kek);
        Unprotect(after, cipher).Should().Be("a second factor");
    }

    [Fact]
    public void A_wrong_key_encryption_key_cannot_open_the_key_ring()
    {
        string cipher;
        using (var ring = Ring(NewKey()))
        {
            cipher = Protect(ring, "a second factor");
        }

        using var stranger = Ring(NewKey());
        var attempt = () => Unprotect(stranger, cipher);

        attempt.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void A_tampered_encrypted_key_no_longer_opens_what_it_protected()
    {
        var kek = NewKey();
        string cipher;
        using (var ring = Ring(kek))
        {
            cipher = Protect(ring, "a second factor");
        }

        var file = Directory.GetFiles(_directory, "*.xml").Single();
        var document = XElement.Load(file);
        var value = document.Descendants().Single(e => e.Name == XNamespace.Get("urn:kekebeauty:dataprotection") + "value");
        var bytes = Convert.FromBase64String(value.Value);
        bytes[^1] ^= 0x01;
        value.Value = Convert.ToBase64String(bytes);
        File.WriteAllText(file, document.ToString(SaveOptions.DisableFormatting));

        // The framework sets an unreadable key aside (and would create a new one); the old data stays closed.
        using var restarted = Ring(kek);
        var attempt = () => Unprotect(restarted, cipher);

        attempt.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void The_key_encryption_key_must_be_32_random_bytes()
    {
        KeyEncryption.Parse(null).Should().BeNull();
        KeyEncryption.Parse("  ").Should().BeNull();
        KeyEncryption.Parse(Convert.ToBase64String(NewKey())).Should().HaveCount(32);

        foreach (var bad in new[] { "not base64 !!", Convert.ToBase64String(new byte[16]), Convert.ToBase64String(new byte[32]), Convert.ToBase64String(RandomNumberGenerator.GetBytes(31)) })
        {
            var attempt = () => KeyEncryption.Parse(bad);
            attempt.Should().Throw<InvalidOperationException>();
        }
    }
}
