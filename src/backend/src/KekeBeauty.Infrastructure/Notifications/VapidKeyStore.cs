using System.Security.Cryptography;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Administration.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KekeBeauty.Infrastructure.Notifications;

public sealed record VapidKeys(string PublicKey, string PrivateKey);

/// <summary>
/// The VAPID key pair that identifies this server to browsers' push services. Keys come from configuration when both are
/// provided; otherwise a pair is generated once and stored in the database (the private key protected with Data Protection),
/// so that existing browser subscriptions keep working across restarts and instances.
/// </summary>
public sealed class VapidKeyStore(IServiceScopeFactory scopes, IOptions<PushOptions> options, IDataProtectionProvider protection)
{
    public const string PublicSettingKey = "push.vapid_public";
    public const string PrivateSettingKey = "push.vapid_private";
    private const string Purpose = "KekeBeauty.Push.Vapid";

    private readonly SemaphoreSlim gate = new(1, 1);
    private VapidKeys? cached;

    public async Task<VapidKeys> GetAsync(CancellationToken ct)
    {
        if (cached is not null)
        {
            return cached;
        }

        await gate.WaitAsync(ct);
        try
        {
            return cached ??= await LoadOrCreateAsync(ct);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Generates a P-256 key pair in the formats Web Push uses: uncompressed public point and raw private scalar, base64url without padding.</summary>
    public static VapidKeys Generate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(true);
        var publicKey = new byte[65];
        publicKey[0] = 0x04;
        parameters.Q.X!.CopyTo(publicKey, 1);
        parameters.Q.Y!.CopyTo(publicKey, 33);
        return new VapidKeys(Base64Url(publicKey), Base64Url(parameters.D!));
    }

    private async Task<VapidKeys> LoadOrCreateAsync(CancellationToken ct)
    {
        var configured = options.Value;
        if (configured.PublicKey.Length > 0 && configured.PrivateKey.Length > 0)
        {
            return new VapidKeys(configured.PublicKey, configured.PrivateKey);
        }

        var protector = protection.CreateProtector(Purpose);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
        var stored = await ReadAsync(db, protector, ct);
        if (stored is not null)
        {
            return stored;
        }

        var created = Generate();
        var now = DateTimeOffset.UtcNow;
        db.AppSettings.Add(new AppSetting { Key = PublicSettingKey, Value = created.PublicKey, UpdatedAt = now });
        db.AppSettings.Add(new AppSetting { Key = PrivateSettingKey, Value = protector.Protect(created.PrivateKey), UpdatedAt = now });
        try
        {
            await db.SaveChangesAsync(ct);
            return created;
        }
        catch (DbUpdateException)
        {
            // Another instance created the pair first: use theirs so every instance signs with the same key.
            db.ChangeTracker.Clear();
            return await ReadAsync(db, protector, ct) ?? throw new InvalidOperationException("The VAPID keys could not be created.");
        }
    }

    private static async Task<VapidKeys?> ReadAsync(KekeBeautyDbContext db, IDataProtector protector, CancellationToken ct)
    {
        var rows = await db.AppSettings.AsNoTracking()
            .Where(s => s.Key == PublicSettingKey || s.Key == PrivateSettingKey)
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        return rows.TryGetValue(PublicSettingKey, out var publicKey) && rows.TryGetValue(PrivateSettingKey, out var privateKey)
            ? new VapidKeys(publicKey, protector.Unprotect(privateKey))
            : null;
    }

    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
