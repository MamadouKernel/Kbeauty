using System.Xml.Linq;
using KekeBeauty.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KekeBeauty.Infrastructure.Security;

/// <summary>
/// At startup, once a key-encryption key is configured, encrypts the Data Protection keys that an earlier version stored in clear.
/// Safe to run on every start and from several instances at once: an already encrypted key is left alone.
/// </summary>
public sealed class DataProtectionKeyMigrator(IServiceScopeFactory scopes, IOptions<KeyManagementOptions> options, ILogger<DataProtectionKeyMigrator> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.XmlEncryptor is not { } encryptor)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
        var migrated = 0;
        foreach (var row in await db.DataProtectionKeys.ToListAsync(cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(row.Xml))
            {
                continue;
            }

            var key = XElement.Parse(row.Xml);
            if (KeyXmlProtector.HasPlaintextSecret(key) && KeyXmlProtector.EncryptPlaintextSecrets(key, encryptor))
            {
                row.Xml = key.ToString(SaveOptions.DisableFormatting);
                migrated++;
            }
        }

        if (migrated > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Encrypted {Count} Data Protection key(s) that were stored in clear.", migrated);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
