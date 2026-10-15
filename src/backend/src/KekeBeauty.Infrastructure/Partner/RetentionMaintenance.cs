using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Onboarding.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KekeBeauty.Infrastructure.Partner;

/// <summary>
/// Deletes the identity document images once the retention period set by the admins has passed since the decision
/// (PRD ADM-05, personal-data minimisation). The approval itself stays, so the identity is still reused for new establishments.
/// </summary>
public sealed class RetentionMaintenance(
    IServiceScopeFactory scopes,
    TimeProvider time,
    IConfiguration configuration,
    ILogger<RetentionMaintenance> logger) : MaintenanceWorker(time, configuration, logger, "Retention", TimeSpan.FromHours(6))
{
    private readonly TimeProvider clock = time;

    protected override async Task TickAsync(CancellationToken cancellationToken) => await RunOnceAsync(cancellationToken);

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var days = await scope.ServiceProvider.GetRequiredService<AppSettingsService>().KycRetentionDaysAsync(ct);
        var now = clock.GetUtcNow();
        var cutoff = now.AddDays(-days);

        var due = await db.KycIdentities
            .Where(k => (k.Status == KycStatus.Approved || k.Status == KycStatus.Rejected)
                && k.ReviewedAt != null && k.ReviewedAt < cutoff
                && k.DocumentsPurgedAt == null
                && (k.FrontKey != null || k.BackKey != null || k.SelfieKey != null))
            .ToListAsync(ct);

        foreach (var identity in due)
        {
            foreach (var key in new[] { identity.FrontKey, identity.BackKey, identity.SelfieKey }.Where(k => k is not null))
            {
                await storage.DeleteAsync(StorageBuckets.PrivateKyc, key!, ct);
            }

            identity.FrontKey = identity.BackKey = identity.SelfieKey = null;
            identity.DocumentsPurgedAt = now;
        }

        if (due.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Deleted the identity documents of {Count} partner(s) after {Days} days of retention", due.Count, days);
        }

        return due.Count;
    }
}
