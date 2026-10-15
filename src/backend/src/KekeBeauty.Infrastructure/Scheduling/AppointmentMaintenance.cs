using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KekeBeauty.Infrastructure.Scheduling;

/// <summary>
/// Time-driven transitions: unanswered requests expire after 24 h (RDV-03), finished appointments complete, and a
/// reschedule proposal nobody answered before the proposed start is cancelled (RDV-06).
/// </summary>
public sealed class AppointmentMaintenance(
    IServiceScopeFactory scopes,
    TimeProvider time,
    IConfiguration configuration,
    ILogger<AppointmentMaintenance> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Tests switch the loop off and call RunOnceAsync themselves, so a fake clock cannot trigger concurrent passes.
        if (!configuration.GetValue("Scheduling:MaintenanceEnabled", true))
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Appointment maintenance failed; it will retry on the next tick");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    /// <summary>One pass; public so tests can drive it with a fake clock. Returns the number of appointments changed.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
        var workflow = scope.ServiceProvider.GetRequiredService<AppointmentWorkflow>();
        var now = time.GetUtcNow();
        var changed = 0;

        changed += await ProcessAsync(
            db, workflow, ct,
            db.Appointments.Where(a => a.Status == AppointmentStatus.Pending && a.ExpiresAt != null && a.ExpiresAt <= now),
            AppointmentStatus.Expired, "Aucune réponse dans le délai");
        changed += await ProcessAsync(
            db, workflow, ct,
            db.Appointments.Where(a => a.Status == AppointmentStatus.RescheduleProposed && a.ProposedStartsAt != null && a.ProposedStartsAt <= now),
            AppointmentStatus.CancelledByClient, "Proposition de reprogrammation sans réponse");
        changed += await ProcessAsync(
            db, workflow, ct,
            db.Appointments.Where(a => a.Status == AppointmentStatus.Confirmed && a.EndsAt <= now),
            AppointmentStatus.Completed, null);
        return changed;
    }

    private static async Task<int> ProcessAsync(
        KekeBeautyDbContext db,
        AppointmentWorkflow workflow,
        CancellationToken ct,
        IQueryable<Appointment> due,
        AppointmentStatus to,
        string? comment)
    {
        var batch = await due.OrderBy(a => a.StartsAt).Take(BatchSize).ToListAsync(ct);
        foreach (var appointment in batch)
        {
            var from = appointment.Status;
            if (workflow.Apply(appointment, to, ActorKind.System, null, comment).IsFailure)
            {
                continue;
            }

            appointment.ProposedStartsAt = null;
            appointment.ProposedEndsAt = null;
            await db.SaveChangesAsync(ct);
            if (to != AppointmentStatus.Completed)
            {
                await workflow.AfterChangeAsync(appointment, from, null, ct);
            }
        }

        return batch.Count;
    }
}
