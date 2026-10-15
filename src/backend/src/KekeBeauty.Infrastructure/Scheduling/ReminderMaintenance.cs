using System.Globalization;
using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KekeBeauty.Infrastructure.Scheduling;

/// <summary>
/// Reminds clients of their confirmed appointments a configurable number of hours before (two reminders, each switched off with 0;
/// the hours are admin settings). Each reminder is sent once, never for an appointment booked inside the reminder window, and
/// goes through every channel the person accepted. Every step is idempotent: a failed tick is simply retried.
/// </summary>
public sealed class ReminderMaintenance(
    IServiceScopeFactory scopes,
    TimeProvider time,
    IConfiguration configuration,
    ILogger<ReminderMaintenance> logger) : MaintenanceWorker(time, configuration, logger, "Reminders", Interval)
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly TimeProvider clock = time;
    private readonly ILogger<ReminderMaintenance> log = logger;

    protected override async Task TickAsync(CancellationToken cancellationToken) => await RunOnceAsync(cancellationToken);

    /// <summary>Sends the reminders that are due and returns how many were sent.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
        var settings = await scope.ServiceProvider.GetRequiredService<AppSettingsService>().GetAsync(ct);

        var sent = 0;
        sent += await SendAsync(db, notifications, settings.ReminderFirstHours, first: true, ct);
        sent += await SendAsync(db, notifications, settings.ReminderSecondHours, first: false, ct);
        return sent;
    }

    private async Task<int> SendAsync(KekeBeautyDbContext db, NotificationService notifications, int hours, bool first, CancellationToken ct)
    {
        if (hours <= 0)
        {
            return 0;
        }

        var now = clock.GetUtcNow();
        var horizon = now.AddHours(hours);
        var due = await db.Appointments
            .Where(a => a.Status == AppointmentStatus.Confirmed && a.ClientUserId != null
                && a.StartsAt > now && a.StartsAt <= horizon
                && (first ? a.FirstReminderSentAt == null : a.SecondReminderSentAt == null))
            .OrderBy(a => a.StartsAt)
            .Take(200)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var appointment in due)
        {
            // Booked inside the window: the booking confirmation already told the client, a reminder would only repeat it.
            if (appointment.CreatedAt > appointment.StartsAt.AddHours(-hours))
            {
                MarkSent(appointment, first, now);
                await db.SaveChangesAsync(ct);
                continue;
            }

            try
            {
                var establishment = await db.Establishments.AsNoTracking()
                    .Where(e => e.Id == appointment.EstablishmentId)
                    .Select(e => new { e.Name, e.Timezone })
                    .FirstAsync(ct);
                var local = TimeZoneInfo.ConvertTime(appointment.StartsAt, AppointmentWorkflow.ZoneOf(establishment.Timezone));
                var variables = new Dictionary<string, string>
                {
                    ["etablissement"] = establishment.Name,
                    ["client"] = string.Empty,
                    ["date"] = local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                    ["heure"] = local.ToString("HH:mm", CultureInfo.InvariantCulture),
                    ["motif"] = string.Empty,
                };
                var body = $"Rappel : votre rendez-vous chez {establishment.Name} a lieu le {variables["date"]} à {variables["heure"]}.";
                await notifications.NotifyAsync(appointment.ClientUserId!.Value, "appointment.reminder", "Rappel de rendez-vous", body, appointment.ClientPhone, ct, variables, isReminder: true);

                MarkSent(appointment, first, now);
                await db.SaveChangesAsync(ct);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The reminder stays unmarked and is retried on the next tick; one failing appointment never blocks the others.
                log.LogWarning(ex, "Reminder for appointment {AppointmentId} failed", appointment.Id);
                db.ChangeTracker.Clear();
            }
        }

        return sent;
    }

    private static void MarkSent(Appointment appointment, bool first, DateTimeOffset now)
    {
        if (first)
        {
            appointment.FirstReminderSentAt = now;
        }
        else
        {
            appointment.SecondReminderSentAt = now;
        }
    }
}
