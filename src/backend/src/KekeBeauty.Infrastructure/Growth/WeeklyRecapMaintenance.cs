using System.Globalization;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Catalog.Domain;
using KekeBeauty.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KekeBeauty.Infrastructure.Growth;

/// <summary>
/// Every Monday morning (local time of the establishment), sends the owner a short recap of the previous week: appointments
/// honoured, revenue, average rating and new clients. It values the subscription and keeps the partner engaged. Sent once
/// per week per establishment (a marker row makes it idempotent), and only when there was activity, so quiet salons are not
/// spammed with zeroes.
/// </summary>
public sealed class WeeklyRecapMaintenance(
    IServiceScopeFactory scopes,
    TimeProvider time,
    IConfiguration configuration,
    ILogger<WeeklyRecapMaintenance> logger) : MaintenanceWorker(time, configuration, logger, "WeeklyRecap", Interval)
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>The recap is sent from this hour (local) on Monday; before it, the week is considered still unsettled.</summary>
    public const int SendFromHourLocal = 8;

    private const int BatchSize = 200;

    private readonly TimeProvider clock = time;
    private readonly ILogger<WeeklyRecapMaintenance> log = logger;

    protected override async Task TickAsync(CancellationToken cancellationToken) => await RunOnceAsync(cancellationToken);

    /// <summary>Sends the recaps that are due and returns how many were sent.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KekeBeautyDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
        var now = clock.GetUtcNow();

        var establishments = await db.Establishments.AsNoTracking()
            .Where(e => e.Status == EstablishmentStatus.Active && e.OwnerUserId != null)
            .Select(e => new { e.Id, e.Name, e.Timezone, OwnerUserId = e.OwnerUserId!.Value })
            .Take(BatchSize)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var establishment in establishments)
        {
            try
            {
                var zone = AppointmentWorkflow.ZoneOf(establishment.Timezone);
                var localNow = TimeZoneInfo.ConvertTime(now, zone);
                var thisMonday = MondayOf(DateOnly.FromDateTime(localNow.DateTime));

                // Wait until Monday morning: before then the recap for the week that just ended is not due yet.
                if (localNow.DayOfWeek == DayOfWeek.Monday && localNow.Hour < SendFromHourLocal)
                {
                    continue;
                }

                if (await db.WeeklyRecapLogs.AnyAsync(w => w.EstablishmentId == establishment.Id && w.WeekStart == thisMonday, ct))
                {
                    continue;
                }

                var lastMonday = thisMonday.AddDays(-7);
                var from = new DateTimeOffset(lastMonday.ToDateTime(TimeOnly.MinValue), zone.GetUtcOffset(lastMonday.ToDateTime(TimeOnly.MinValue))).ToUniversalTime();
                var to = new DateTimeOffset(thisMonday.ToDateTime(TimeOnly.MinValue), zone.GetUtcOffset(thisMonday.ToDateTime(TimeOnly.MinValue))).ToUniversalTime();

                var honoured = await db.Appointments.AsNoTracking()
                    .Where(a => a.EstablishmentId == establishment.Id && a.Status == AppointmentStatus.Completed
                        && a.StartsAt >= from && a.StartsAt < to)
                    .Select(a => new { a.TotalPrice, a.TipAmount, a.ClientUserId })
                    .ToListAsync(ct);

                // A quiet week: nothing to celebrate, mark it done without sending, so it is not retried all week.
                if (honoured.Count == 0)
                {
                    await MarkSentAsync(db, establishment.Id, thisMonday, now, ct);
                    continue;
                }

                var revenue = honoured.Sum(a => a.TotalPrice + a.TipAmount);
                var distinctClients = honoured.Where(a => a.ClientUserId != null).Select(a => a.ClientUserId).Distinct().Count();
                var rating = await db.Reviews.AsNoTracking()
                    .Where(r => r.EstablishmentId == establishment.Id && r.Status == ReviewStatus.Published)
                    .AverageAsync(r => (double?)r.Rating, ct);

                var period = $"du {lastMonday.ToString("dd/MM", CultureInfo.InvariantCulture)} au {thisMonday.AddDays(-1).ToString("dd/MM", CultureInfo.InvariantCulture)}";
                var ratingText = rating is { } value ? $", note moyenne {value.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',')}/5" : string.Empty;
                var body = $"Votre semaine chez {establishment.Name} ({period}) : {honoured.Count} rendez-vous honoré{(honoured.Count > 1 ? "s" : "")}, "
                    + $"{revenue.ToString("#,0", CultureInfo.InvariantCulture).Replace(",", " ")} F CFA, "
                    + $"{distinctClients} client{(distinctClients > 1 ? "s" : "")} servi{(distinctClients > 1 ? "s" : "")}{ratingText}. Merci d'utiliser Keke Beauty !";

                await notifications.NotifyAsync(establishment.OwnerUserId, "partner.weekly_recap", "Votre bilan de la semaine", body, null, ct);
                await MarkSentAsync(db, establishment.Id, thisMonday, now, ct);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One establishment failing never blocks the others; it is retried on the next tick (still idempotent).
                log.LogWarning(ex, "Weekly recap for establishment {EstablishmentId} failed", establishment.Id);
                db.ChangeTracker.Clear();
            }
        }

        return sent;
    }

    private static async Task MarkSentAsync(KekeBeautyDbContext db, Guid establishmentId, DateOnly weekStart, DateTimeOffset now, CancellationToken ct)
    {
        db.WeeklyRecapLogs.Add(new WeeklyRecapLog { EstablishmentId = establishmentId, WeekStart = weekStart, SentAt = now });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Marked meanwhile by another run: the unique index keeps a single row.
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>The Monday on or before <paramref name="date"/>.</summary>
    private static DateOnly MondayOf(DateOnly date) => date.AddDays(-((int)date.DayOfWeek + 6) % 7);
}
