using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KekeBeauty.Infrastructure.Growth;

/// <summary>
/// Invites a client back to an establishment some days after her last visit there (30 by default, an admin setting, 0 = off),
/// unless she already booked again. One message per visit, only for the latest one, and only within a week of the due date
/// so that switching the feature on never floods people about old visits. Goes through the reminder channels and preference.
/// </summary>
public sealed class ReengagementMaintenance(
    IServiceScopeFactory scopes,
    TimeProvider time,
    IConfiguration configuration,
    ILogger<ReengagementMaintenance> logger) : MaintenanceWorker(time, configuration, logger, "Reengagement", Interval)
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>How late after the due date a message is still sent.</summary>
    public static readonly TimeSpan CatchUpWindow = TimeSpan.FromDays(7);

    private const int BatchSize = 200;

    private static readonly AppointmentStatus[] Upcoming =
        [AppointmentStatus.Pending, AppointmentStatus.Confirmed, AppointmentStatus.RescheduleProposed, AppointmentStatus.Completed];

    private readonly TimeProvider clock = time;
    private readonly ILogger<ReengagementMaintenance> log = logger;

    protected override async Task TickAsync(CancellationToken cancellationToken) => await RunOnceAsync(cancellationToken);

    /// <summary>Sends the invitations that are due and returns how many were sent.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
        var days = (await scope.ServiceProvider.GetRequiredService<AppSettingsService>().GetAsync(ct)).ReengagementDays;
        if (days <= 0)
        {
            return 0;
        }

        var now = clock.GetUtcNow();
        var dueBefore = now.AddDays(-days);
        var notBefore = dueBefore - CatchUpWindow;
        var due = await db.Appointments
            .Where(a => a.Status == AppointmentStatus.Completed && a.ClientUserId != null && a.WalkInName == null
                && a.ReengagementSentAt == null && a.EndsAt <= dueBefore && a.EndsAt > notBefore
                && !db.Appointments.Any(later => later.ClientUserId == a.ClientUserId && later.EstablishmentId == a.EstablishmentId
                    && later.StartsAt > a.StartsAt && Upcoming.Contains(later.Status)))
            .OrderBy(a => a.EndsAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var visit in due)
        {
            try
            {
                var establishment = await db.Establishments.AsNoTracking()
                    .Where(e => e.Id == visit.EstablishmentId)
                    .Select(e => new { e.Name, Open = e.Status == KekeBeauty.Modules.Catalog.Domain.EstablishmentStatus.Active })
                    .FirstAsync(ct);

                // Never invite people back to a place that is no longer listed; the visit is simply marked.
                if (establishment.Open)
                {
                    var variables = new Dictionary<string, string>
                    {
                        ["etablissement"] = establishment.Name,
                        ["client"] = string.Empty,
                        ["date"] = string.Empty,
                        ["heure"] = string.Empty,
                        ["motif"] = string.Empty,
                    };
                    await notifications.NotifyAsync(visit.ClientUserId!.Value, "client.reengagement", $"Envie de revenir chez {establishment.Name} ?",
                        $"Votre dernière visite chez {establishment.Name} remonte à {days} jours. Réservez votre prochain rendez-vous en quelques secondes.",
                        null, ct, variables, isReminder: true);
                    sent++;
                }

                visit.ReengagementSentAt = now;
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Left unmarked and retried on the next tick; one failure never blocks the others.
                log.LogWarning(ex, "Re-engagement message for appointment {AppointmentId} failed", visit.Id);
                db.ChangeTracker.Clear();
            }
        }

        return sent;
    }
}
