using System.Globalization;
using System.Text;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Billing.Domain;
using KekeBeauty.Modules.Catalog.Domain;
using KekeBeauty.Modules.Identity.Application;
using KekeBeauty.Modules.Identity.Domain;
using KekeBeauty.Modules.Scheduling.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Admin;

public sealed record PlanCountDto(string PlanCode, string PlanName, int Active, int PastDue);

public sealed record MethodRevenueDto(string Method, decimal Amount, int Payments);

public sealed record PastDueDto(
    Guid SubscriptionId,
    Guid EstablishmentId,
    string EstablishmentName,
    string? OwnerEmail,
    string PlanName,
    string? InvoiceNumber,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset? DueDate,
    int DaysLate);

/// <summary>Figures of the financial dashboard (PRD ADM-04). Amounts are in the plans' currency (XOF).</summary>
public sealed record FinanceSummary(
    IReadOnlyList<PlanCountDto> Plans,
    decimal MonthlyRecurringRevenue,
    decimal Revenue,
    string CurrencyCode,
    IReadOnlyList<MethodRevenueDto> RevenueByMethod,
    IReadOnlyList<PastDueDto> PastDue,
    DateTimeOffset From,
    DateTimeOffset To);

public sealed record AdminDashboardDto(
    int ActiveEstablishments,
    int PendingReviewEstablishments,
    int DraftEstablishments,
    int RejectedEstablishments,
    int SuspendedEstablishments,
    int PendingAppointments,
    int ConfirmedAppointments,
    int CompletedAppointments,
    int NoShowAppointments,
    int CancelledOrRejectedAppointments,
    decimal CompletedVolume,
    decimal MonthlyRecurringRevenue,
    int ActiveSubscriptions,
    int TrialingSubscriptions,
    int PastDueSubscriptions,
    int OpenDisputes,
    int TotalUsers,
    int TotalPartners,
    int TotalStaff,
    int PublishedReviews,
    double AverageRating,
    string CurrencyCode,
    IReadOnlyList<PlanCountDto> Plans);

public sealed class AdminFinanceService(
    KekeBeautyDbContext db,
    NotificationService notifications,
    IEmailSender email,
    TimeProvider time)
{
    public static readonly Error SubscriptionNotFound = new("subscription.not_found", "Abonnement introuvable.");
    public static readonly Error NothingToRemind = new("reminder.nothing_due", "Aucune facture impayée à relancer pour cet abonnement.");
    public static readonly Error TooSoon = new("reminder.too_soon", "Une relance a déjà été envoyée il y a moins d'une heure.");

    private static readonly TimeSpan ReminderCooldown = TimeSpan.FromHours(1);
    private const int MaxRangeDays = 366;

    public async Task<AdminDashboardDto> DashboardAsync(CancellationToken ct)
    {
        var estCounts = await db.Establishments.AsNoTracking()
            .GroupBy(e => e.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);

        var apptRows = await db.Appointments.AsNoTracking()
            .Where(a => !a.AwaitingPayment)
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Volume = g.Sum(x => x.TotalPrice) })
            .ToListAsync(ct);
        var apptCounts = apptRows.ToDictionary(x => x.Status, x => x.Count);
        var completedVolume = apptRows.Where(x => x.Status == AppointmentStatus.Completed).Sum(x => x.Volume);
        var cancelledOrRejected = apptRows
            .Where(x => x.Status is AppointmentStatus.CancelledByClient or AppointmentStatus.CancelledByEstablishment or AppointmentStatus.Rejected or AppointmentStatus.Expired)
            .Sum(x => x.Count);

        var subs = await (
            from s in db.Subscriptions.AsNoTracking()
            where s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing || s.Status == SubscriptionStatus.PastDue
            join p in db.Plans.AsNoTracking() on s.PlanId equals p.Id
            select new { p.Code, p.Name, s.Status, s.Cycle, s.UnitPriceSnapshot })
            .ToListAsync(ct);

        var plans = subs.GroupBy(x => (x.Code, x.Name))
            .Select(g => new PlanCountDto(
                g.Key.Code,
                g.Key.Name,
                g.Count(x => x.Status is SubscriptionStatus.Active or SubscriptionStatus.Trialing),
                g.Count(x => x.Status == SubscriptionStatus.PastDue)))
            .OrderBy(x => x.PlanCode)
            .ToList();

        var mrr = subs.Where(x => x.Status == SubscriptionStatus.Active)
            .Sum(x => x.Cycle == BillingCycle.Annual ? x.UnitPriceSnapshot / 12m : x.UnitPriceSnapshot);

        var openDisputes = await db.Disputes.AsNoTracking().CountAsync(d => d.Status == DisputeStatus.Open, ct);
        var totalUsers = await db.Users.AsNoTracking().CountAsync(u => u.DeletedAt == null, ct);
        var totalPartners = await db.Users.AsNoTracking().CountAsync(u => u.DeletedAt == null && u.Roles.Contains(AppRoles.PartnerOwner), ct);
        var totalStaff = await db.Resources.AsNoTracking().CountAsync(r => r.IsActive, ct);

        var reviewStats = await db.Reviews.AsNoTracking()
            .Where(r => r.Status == ReviewStatus.Published)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Avg = g.Average(r => (double)r.Rating) })
            .FirstOrDefaultAsync(ct);

        return new AdminDashboardDto(
            estCounts.GetValueOrDefault(EstablishmentStatus.Active),
            estCounts.GetValueOrDefault(EstablishmentStatus.PendingReview),
            estCounts.GetValueOrDefault(EstablishmentStatus.Draft),
            estCounts.GetValueOrDefault(EstablishmentStatus.Rejected),
            estCounts.GetValueOrDefault(EstablishmentStatus.Suspended),
            apptCounts.GetValueOrDefault(AppointmentStatus.Pending) + apptCounts.GetValueOrDefault(AppointmentStatus.RescheduleProposed),
            apptCounts.GetValueOrDefault(AppointmentStatus.Confirmed),
            apptCounts.GetValueOrDefault(AppointmentStatus.Completed),
            apptCounts.GetValueOrDefault(AppointmentStatus.NoShow),
            cancelledOrRejected,
            completedVolume,
            Math.Round(mrr, 0),
            subs.Count(x => x.Status == SubscriptionStatus.Active),
            subs.Count(x => x.Status == SubscriptionStatus.Trialing),
            subs.Count(x => x.Status == SubscriptionStatus.PastDue),
            openDisputes,
            totalUsers,
            totalPartners,
            totalStaff,
            reviewStats?.Count ?? 0,
            Math.Round(reviewStats?.Avg ?? 0d, 1),
            "XOF",
            plans);
    }

    public async Task<Result<FinanceSummary>> SummaryAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var (start, end, problem) = Range(from, to);
        if (problem is not null)
        {
            return Result.Failure<FinanceSummary>(problem);
        }

        var running = await (
            from s in db.Subscriptions.AsNoTracking()
            where s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue
            join p in db.Plans.AsNoTracking() on s.PlanId equals p.Id
            select new { p.Code, p.Name, s.Status, s.Cycle, s.UnitPriceSnapshot })
            .ToListAsync(ct);
        var plans = running.GroupBy(x => (x.Code, x.Name))
            .Select(g => new PlanCountDto(g.Key.Code, g.Key.Name, g.Count(x => x.Status == SubscriptionStatus.Active), g.Count(x => x.Status == SubscriptionStatus.PastDue)))
            .OrderBy(x => x.PlanCode).ToList();
        var mrr = running.Where(x => x.Status == SubscriptionStatus.Active)
            .Sum(x => x.Cycle == BillingCycle.Annual ? x.UnitPriceSnapshot / 12m : x.UnitPriceSnapshot);

        var paid = await db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Succeeded && p.CreatedAt >= start && p.CreatedAt < end)
            .Select(p => new { p.Method, p.Amount })
            .ToListAsync(ct);
        var byMethod = paid.GroupBy(p => p.Method)
            .Select(g => new MethodRevenueDto(g.Key.ToString(), g.Sum(x => x.Amount), g.Count()))
            .OrderByDescending(x => x.Amount).ToList();

        return Result.Success(new FinanceSummary(
            plans,
            Math.Round(mrr, 0),
            paid.Sum(p => p.Amount),
            "XOF",
            byMethod,
            await PastDueAsync(ct),
            start,
            end));
    }

    public async Task<Result> RemindAsync(AdminContext admin, Guid subscriptionId, CancellationToken ct)
    {
        var line = (await PastDueRowsAsync(subscriptionId, ct)).FirstOrDefault();
        if (line is null)
        {
            var exists = await db.Subscriptions.AnyAsync(s => s.Id == subscriptionId, ct);
            return Result.Failure(exists ? NothingToRemind : SubscriptionNotFound);
        }

        var now = time.GetUtcNow();
        var since = now - ReminderCooldown;
        if (await db.AuditLogs.AnyAsync(a => a.Action == "billing.remind" && a.EntityId == subscriptionId && a.At > since, ct))
        {
            return Result.Failure(TooSoon);
        }

        var body = $"La facture {line.InvoiceNumber} de {line.Amount:N0} {line.CurrencyCode} pour « {line.EstablishmentName} » est impayée. " +
            "Réglez-la depuis votre espace abonnement pour rétablir la réservation en ligne.";
        if (line.OwnerId is { } ownerId)
        {
            await notifications.NotifyAsync(ownerId, "billing.manual_reminder", "Relance de paiement", body, null, ct);
        }

        if (line.OwnerEmail is not null)
        {
            await email.SendAsync(new EmailMessage(line.OwnerEmail, "Relance de paiement — Keke Beauty", body), ct);
        }

        Audit.Add(db, admin, time, "billing.remind", "Subscription", subscriptionId, new { invoice = line.InvoiceNumber });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Succeeded payments of the period as CSV (RFC 4180, UTF-8), one line per payment.</summary>
    public async Task<Result<string>> PaymentsCsvAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var (start, end, problem) = Range(from, to);
        if (problem is not null)
        {
            return Result.Failure<string>(problem);
        }

        var rows = await (
            from p in db.Payments.AsNoTracking()
            where p.Status == PaymentStatus.Succeeded && p.CreatedAt >= start && p.CreatedAt < end
            join i in db.Invoices.AsNoTracking() on p.InvoiceId equals i.Id
            join s in db.Subscriptions.AsNoTracking() on i.SubscriptionId equals s.Id
            join e in db.Establishments.AsNoTracking() on s.EstablishmentId equals e.Id
            orderby p.CreatedAt
            select new { p.CreatedAt, i.Number, EstablishmentName = e.Name, p.Amount, p.CurrencyCode, p.Method, p.Provider, p.ProviderTransactionId })
            .ToListAsync(ct);

        var csv = new StringBuilder("date;facture;etablissement;montant;devise;moyen;passerelle;transaction\r\n");
        foreach (var r in rows)
        {
            csv.Append(string.Join(';', new[]
            {
                Cell(r.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                Cell(r.Number),
                Cell(r.EstablishmentName),
                Cell(r.Amount.ToString("0.##", CultureInfo.InvariantCulture)),
                Cell(r.CurrencyCode),
                Cell(r.Method.ToString()),
                Cell(r.Provider),
                Cell(r.ProviderTransactionId),
            })).Append("\r\n");
        }

        return Result.Success(csv.ToString());
    }

    /// <summary>Quotes a CSV cell and neutralises spreadsheet formulas (a name starting with "=" must not execute).</summary>
    public static string Cell(string value)
    {
        var safe = value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
        return safe.Contains(';', StringComparison.Ordinal) || safe.Contains('"', StringComparison.Ordinal) || safe.Contains('\n', StringComparison.Ordinal) || safe.Contains('\r', StringComparison.Ordinal)
            ? "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : safe;
    }

    private (DateTimeOffset Start, DateTimeOffset End, Error? Problem) Range(DateTimeOffset? from, DateTimeOffset? to)
    {
        var end = to ?? time.GetUtcNow().AddMinutes(1); // the end is exclusive: a payment made right now must be counted
        var start = from ?? end.AddDays(-30);
        return end <= start || (end - start).TotalDays > MaxRangeDays
            ? (start, end, AdminErrors.Invalid($"La période doit être positive et ne pas dépasser {MaxRangeDays} jours."))
            : (start, end, null);
    }

    private sealed record PastDueRow(
        Guid SubscriptionId,
        Guid EstablishmentId,
        string EstablishmentName,
        Guid? OwnerId,
        string? OwnerEmail,
        string PlanName,
        string? InvoiceNumber,
        decimal Amount,
        string CurrencyCode,
        DateTimeOffset? DueDate);

    /// <summary>Past-due subscriptions with their oldest unpaid invoice (two plain queries: the combined one does not translate).</summary>
    private async Task<List<PastDueRow>> PastDueRowsAsync(Guid? subscriptionId, CancellationToken ct)
    {
        var subscriptions = await (
            from s in db.Subscriptions.AsNoTracking()
            where s.Status == SubscriptionStatus.PastDue && (subscriptionId == null || s.Id == subscriptionId)
            join e in db.Establishments.AsNoTracking() on s.EstablishmentId equals e.Id
            join p in db.Plans.AsNoTracking() on s.PlanId equals p.Id
            select new { s.Id, EstablishmentId = e.Id, EstablishmentName = e.Name, e.OwnerUserId, PlanName = p.Name })
            .ToListAsync(ct);
        var ids = subscriptions.Select(s => s.Id).ToList();
        var invoices = await db.Invoices.AsNoTracking()
            .Where(i => ids.Contains(i.SubscriptionId) && (i.Status == InvoiceStatus.Overdue || i.Status == InvoiceStatus.Open))
            .ToListAsync(ct);
        var ownerIds = subscriptions.Where(s => s.OwnerUserId != null).Select(s => s.OwnerUserId!.Value).Distinct().ToList();
        var owners = await db.Users.AsNoTracking().Where(u => ownerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, ct);

        var rows = new List<PastDueRow>();
        foreach (var s in subscriptions)
        {
            var oldest = invoices.Where(i => i.SubscriptionId == s.Id).OrderBy(i => i.DueDate).FirstOrDefault();
            if (oldest is null)
            {
                continue;
            }

            rows.Add(new PastDueRow(
                s.Id, s.EstablishmentId, s.EstablishmentName, s.OwnerUserId,
                s.OwnerUserId is { } owner ? owners.GetValueOrDefault(owner) : null,
                s.PlanName, oldest.Number, oldest.Amount, oldest.CurrencyCode, oldest.DueDate));
        }

        return rows;
    }

    private async Task<IReadOnlyList<PastDueDto>> PastDueAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var rows = (await PastDueRowsAsync(null, ct)).OrderBy(x => x.DueDate).ToList();
        return rows.Select(r => new PastDueDto(
            r.SubscriptionId, r.EstablishmentId, r.EstablishmentName, r.OwnerEmail, r.PlanName, r.InvoiceNumber, r.Amount,
            r.CurrencyCode, r.DueDate, r.DueDate is { } due ? Math.Max((int)(now - due).TotalDays, 0) : 0)).ToList();
    }
}
