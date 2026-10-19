using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Billing.Domain;
using KekeBeauty.Modules.Identity.Application;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace KekeBeauty.Infrastructure.Billing;

public sealed record BillingMaintenanceReport(int InvoicesCreated, int Overdue, int PastDue, int Reminders, int Ended, int Reconciled, int TrialsEnded = 0, int PlansChanged = 0);

/// <summary>
/// Recurring billing work (PRD BIL-04 to BIL-06, §10.2): next invoices, reminders at D-3/D0/D+3/D+7, overdue and
/// past-due transitions after the grace period, end of cancelled subscriptions, and reconciliation of payments whose
/// notification never arrived. Runs hourly; every step is idempotent.
/// </summary>
public sealed class BillingMaintenance(
    IServiceScopeFactory scopes,
    TimeProvider time,
    IConfiguration configuration,
    ILogger<BillingMaintenance> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    public static readonly TimeSpan ReconcileAfter = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan AbandonAfter = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Billing:MaintenanceEnabled", true))
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval, time);
        try
        {
            do
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Billing maintenance failed; it will retry on the next tick");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    public async Task<BillingMaintenanceReport> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<KekeBeautyDbContext>();
        var entitlements = sp.GetRequiredService<EntitlementService>();
        var notifications = sp.GetRequiredService<NotificationService>();
        var email = sp.GetRequiredService<IEmailSender>();
        var processor = sp.GetRequiredService<PaymentProcessor>();
        var grace = TimeSpan.FromDays(await sp.GetRequiredService<AppSettingsService>().GraceDaysAsync(ct));
        var now = time.GetUtcNow();

        var changed = await ApplyScheduledChangesAsync(db, entitlements, notifications, now, ct);
        var created = await CreateNextInvoicesAsync(db, now, ct);
        var overdue = await MarkOverdueAsync(db, now, ct);
        var pastDue = await MarkPastDueAsync(db, entitlements, notifications, now, grace, ct);
        var reminders = await SendRemindersAsync(db, notifications, email, now, ct);
        var trialsEnded = await EndExpiredTrialsAsync(db, entitlements, notifications, now, ct);
        var ended = await EndCancelledAsync(db, entitlements, notifications, now, ct);
        var reconciled = await ReconcileAsync(db, processor, now, ct);
        await AbandonStaleCheckoutsAsync(db, now, ct);
        return new BillingMaintenanceReport(created, overdue, pastDue, reminders, ended, reconciled, trialsEnded, changed);
    }

    private static async Task<int> CreateNextInvoicesAsync(KekeBeautyDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var created = 0;
        var subscriptions = await db.Subscriptions
            .Where(s => (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing) && s.CurrentPeriodEnd != null)
            .ToListAsync(ct);

        foreach (var subscription in subscriptions.Where(s => BillingRules.NextInvoiceDue(s, now)))
        {
            var start = subscription.CurrentPeriodEnd!.Value;
            if (await db.Invoices.AnyAsync(i => i.SubscriptionId == subscription.Id && i.PeriodStart == start, ct))
            {
                continue;
            }

            // A change of plan chosen for the end of the commitment renews at the new plan's frozen price and rhythm.
            var switching = subscription.PendingPlanId != null && subscription.PendingChangeAt <= start;
            var cycle = switching ? subscription.PendingCycle ?? subscription.Cycle : subscription.Cycle;
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                db.Invoices.Add(new Invoice
                {
                    SubscriptionId = subscription.Id,
                    Number = await InvoiceNumbers.NextAsync(db, now.Year, ct),
                    Amount = switching ? subscription.PendingUnitPrice ?? subscription.UnitPriceSnapshot : BillingRules.InvoiceAmount(subscription),
                    CurrencyCode = subscription.CurrencyCode,
                    PeriodStart = start,
                    PeriodEnd = BillingRules.PeriodEnd(start, cycle),
                    DueDate = start,
                    CreatedAt = now,
                });
                if (!switching && subscription.DiscountPeriodsLeft > 0)
                {
                    subscription.DiscountPeriodsLeft--;
                }

                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                created++;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.ChangeTracker.Clear(); // another instance created it first; its number counter rolled back with the transaction
            }
        }

        return created;
    }

    private static Task<int> MarkOverdueAsync(KekeBeautyDbContext db, DateTimeOffset now, CancellationToken ct) =>
        db.Invoices
            .Where(i => i.Status == InvoiceStatus.Open && i.DueDate < now
                && db.Subscriptions.Any(s => s.Id == i.SubscriptionId && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue)))
            .ExecuteUpdateAsync(set => set.SetProperty(i => i.Status, InvoiceStatus.Overdue), ct);

    private static async Task<int> MarkPastDueAsync(
        KekeBeautyDbContext db,
        EntitlementService entitlements,
        NotificationService notifications,
        DateTimeOffset now,
        TimeSpan grace,
        CancellationToken ct)
    {
        var cutoff = now - grace;
        var late = await (
            from i in db.Invoices
            where i.Status == InvoiceStatus.Overdue && i.DueDate < cutoff
            join s in db.Subscriptions on i.SubscriptionId equals s.Id
            where s.Status == SubscriptionStatus.Active
            select new { Invoice = i, Subscription = s })
            .ToListAsync(ct);

        foreach (var item in late)
        {
            item.Subscription.Status = SubscriptionStatus.PastDue;
            await db.SaveChangesAsync(ct);
            await entitlements.SyncAsync(item.Subscription.EstablishmentId, ct);
            await NotifyOwnerAsync(db, notifications, null, item.Subscription.EstablishmentId, "billing.past_due",
                "Abonnement suspendu pour impayé",
                $"La facture {item.Invoice.Number} n'est pas réglée : les fonctionnalités Premium (réservation en ligne, statistiques, mise en avant) sont suspendues. Régularisez depuis votre espace abonnement.", item.Invoice, 0, ct);
        }

        return late.Count;
    }

    private static async Task<int> SendRemindersAsync(
        KekeBeautyDbContext db,
        NotificationService notifications,
        IEmailSender email,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var sent = 0;
        var invoices = await (
            from i in db.Invoices
            where i.Status == InvoiceStatus.Open || i.Status == InvoiceStatus.Overdue
            join s in db.Subscriptions on i.SubscriptionId equals s.Id
            where s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue || s.Status == SubscriptionStatus.Trialing
            select new { Invoice = i, s.EstablishmentId })
            .ToListAsync(ct);

        foreach (var item in invoices)
        {
            var due = BillingRules.DueReminders(item.Invoice.DueDate, now, item.Invoice.RemindersSent);
            if (due.Count == 0)
            {
                continue;
            }

            var latest = due.MaxBy(r => r.Days);
            foreach (var reminder in due)
            {
                item.Invoice.RemindersSent |= reminder.Bit;
            }

            await db.SaveChangesAsync(ct);
            var (title, body) = ReminderText(item.Invoice, latest.Days);
            await NotifyOwnerAsync(db, notifications, email, item.EstablishmentId, $"billing.reminder.{latest.Days}", title, body, item.Invoice, Math.Max(latest.Days, 0), ct);
            sent++;
        }

        return sent;
    }

    private static async Task<int> EndCancelledAsync(
        KekeBeautyDbContext db,
        EntitlementService entitlements,
        NotificationService notifications,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var ending = await db.Subscriptions
            .Where(s => (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue)
                && s.CancelAtCommitmentEnd && s.CommitmentEndDate != null && s.CommitmentEndDate <= now)
            .ToListAsync(ct);

        foreach (var subscription in ending)
        {
            subscription.Status = SubscriptionStatus.Cancelled;
            await db.Invoices.Where(i => i.SubscriptionId == subscription.Id && (i.Status == InvoiceStatus.Open || i.Status == InvoiceStatus.Overdue))
                .ExecuteUpdateAsync(set => set.SetProperty(i => i.Status, InvoiceStatus.Void), ct);
            await db.SaveChangesAsync(ct);
            await entitlements.SyncAsync(subscription.EstablishmentId, ct);
            await NotifyOwnerAsync(db, notifications, null, subscription.EstablishmentId, "billing.ended",
                "Abonnement terminé", "Votre abonnement est arrivé au terme de son engagement et ne sera pas renouvelé. Votre fiche reste visible avec la formule Gratuite.", null, 0, ct);
        }

        return ending.Count;
    }

    /// <summary>The plan chosen for the end of the commitment takes over: same subscription, new plan, price, rhythm and commitment.</summary>
    private static async Task<int> ApplyScheduledChangesAsync(
        KekeBeautyDbContext db,
        EntitlementService entitlements,
        NotificationService notifications,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var due = await db.Subscriptions
            .Where(s => (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue)
                && s.PendingPlanId != null && s.PendingChangeAt != null && s.PendingChangeAt <= now)
            .ToListAsync(ct);

        foreach (var subscription in due)
        {
            var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == subscription.PendingPlanId, ct);
            if (plan is null)
            {
                SubscriptionChanges.Clear(subscription);
                await db.SaveChangesAsync(ct);
                continue;
            }

            var effectiveAt = subscription.PendingChangeAt!.Value;
            subscription.PlanId = plan.Id;
            subscription.Cycle = subscription.PendingCycle ?? subscription.Cycle;
            subscription.UnitPriceSnapshot = subscription.PendingUnitPrice ?? plan.PriceFor(subscription.Cycle);
            subscription.CommitmentMonths = plan.CommitmentMonths;
            subscription.CommitmentEndDate = BillingRules.CommitmentEnd(effectiveAt, plan.CommitmentMonths);
            subscription.DiscountPercent = 0; // a promotion belongs to the plan it was granted for
            subscription.DiscountPeriodsLeft = 0;
            SubscriptionChanges.Clear(subscription);
            await db.SaveChangesAsync(ct);
            await entitlements.SyncAsync(subscription.EstablishmentId, ct);
            await NotifyOwnerAsync(db, notifications, null, subscription.EstablishmentId, "billing.plan_changed",
                "Changement de formule", $"Votre abonnement est passé à la formule « {plan.Name} ». Les nouvelles conditions s'appliquent dès maintenant.", null, 0, ct);
        }

        return due.Count;
    }

    /// <summary>A trial that ended without its first invoice being paid returns the establishment to the free plan.</summary>
    private static async Task<int> EndExpiredTrialsAsync(
        KekeBeautyDbContext db,
        EntitlementService entitlements,
        NotificationService notifications,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var ended = await db.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.Trialing && s.TrialEndsAt != null && s.TrialEndsAt <= now
                // A payment still in flight is settled first: the next run ends the trial if it failed.
                && !db.Payments.Any(p => p.Status == PaymentStatus.Pending
                    && db.Invoices.Any(i => i.Id == p.InvoiceId && i.SubscriptionId == s.Id)))
            .ToListAsync(ct);

        foreach (var subscription in ended)
        {
            subscription.Status = SubscriptionStatus.Expired;
            await db.Invoices.Where(i => i.SubscriptionId == subscription.Id && (i.Status == InvoiceStatus.Open || i.Status == InvoiceStatus.Overdue))
                .ExecuteUpdateAsync(set => set.SetProperty(i => i.Status, InvoiceStatus.Void), ct);
            await db.SaveChangesAsync(ct);
            await entitlements.SyncAsync(subscription.EstablishmentId, ct);
            await NotifyOwnerAsync(db, notifications, null, subscription.EstablishmentId, "billing.trial_ended",
                "Essai gratuit terminé", "Votre essai gratuit est terminé : votre fiche reste visible avec la formule Gratuite. Souscrivez depuis votre espace abonnement pour retrouver toutes les fonctionnalités.", null, 0, ct);
        }

        return ended.Count;
    }

    private async Task<int> ReconcileAsync(KekeBeautyDbContext db, PaymentProcessor processor, DateTimeOffset now, CancellationToken ct)
    {
        var reconciled = 0;
        var cutoff = now - ReconcileAfter;
        var pending = await db.Payments.Where(p => p.Status == PaymentStatus.Pending && p.CreatedAt < cutoff).OrderBy(p => p.CreatedAt).Take(100).ToListAsync(ct);

        foreach (var payment in pending)
        {
            var gateway = processor.GatewayFor(payment.Provider);
            if (gateway is null)
            {
                continue;
            }

            try
            {
                var verification = await gateway.VerifyAsync(payment.ProviderTransactionId, payment.ProviderReference, ct);
                if (verification.Status != GatewayStatus.Pending)
                {
                    await processor.SettleAsync(payment, verification, ct);
                    reconciled++;
                }
            }
            catch (PaymentGatewayException ex)
            {
                logger.LogWarning(ex, "Reconciliation could not reach {Provider}", payment.Provider);
            }
        }

        // A checkout left alone for a week is over, whatever the provider says.
        await db.Payments.Where(p => p.Status == PaymentStatus.Pending && p.CreatedAt < now - AbandonAfter)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.Status, PaymentStatus.Failed).SetProperty(p => p.RawPayload, "abandoned"), ct);
        return reconciled;
    }

    private static async Task AbandonStaleCheckoutsAsync(KekeBeautyDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var stale = await db.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.PendingPayment && s.CreatedAt < now - AbandonAfter)
            .ToListAsync(ct);
        foreach (var subscription in stale)
        {
            subscription.Status = SubscriptionStatus.Cancelled;
            await db.Invoices.Where(i => i.SubscriptionId == subscription.Id && i.Status == InvoiceStatus.Open)
                .ExecuteUpdateAsync(set => set.SetProperty(i => i.Status, InvoiceStatus.Void), ct);
        }

        if (stale.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    private static (string Title, string Body) ReminderText(Invoice invoice, int days) => days switch
    {
        < 0 => ("Facture à venir", $"La facture {invoice.Number} ({invoice.Amount:N0} {invoice.CurrencyCode}) arrive à échéance le {invoice.DueDate:dd/MM/yyyy}."),
        0 => ("Facture à régler aujourd'hui", $"La facture {invoice.Number} ({invoice.Amount:N0} {invoice.CurrencyCode}) est à régler aujourd'hui."),
        < 7 => ("Facture en retard", $"La facture {invoice.Number} est en retard de {days} jours. Réglez-la pour conserver vos fonctionnalités Premium."),
        _ => ("Dernier rappel", $"La facture {invoice.Number} est impayée depuis {days} jours : vos fonctionnalités Premium sont suspendues tant qu'elle n'est pas réglée."),
    };

    private static async Task NotifyOwnerAsync(
        KekeBeautyDbContext db,
        NotificationService notifications,
        IEmailSender? email,
        Guid establishmentId,
        string template,
        string title,
        string body,
        Invoice? invoice,
        int daysLate,
        CancellationToken ct)
    {
        var owner = await db.Establishments.AsNoTracking()
            .Where(e => e.Id == establishmentId && e.OwnerUserId != null)
            .Join(db.Users.AsNoTracking(), e => e.OwnerUserId, u => (Guid?)u.Id, (e, u) => new { u.Id, u.Email, e.Name })
            .FirstOrDefaultAsync(ct);
        if (owner is null)
        {
            return;
        }

        var variables = new Dictionary<string, string>
        {
            ["etablissement"] = owner.Name,
            ["facture"] = invoice?.Number ?? string.Empty,
            ["montant"] = invoice is null ? string.Empty : $"{invoice.Amount:N0} {invoice.CurrencyCode}",
            ["echeance"] = invoice?.DueDate.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            ["jours"] = daysLate.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        await notifications.NotifyAsync(owner.Id, template, title, body, null, ct, variables);
        if (email is not null)
        {
            await email.SendAsync(new EmailMessage(owner.Email, $"{title} — Keke Beauty", $"{body}\n\nÉtablissement : {owner.Name}"), ct);
        }
    }
}
