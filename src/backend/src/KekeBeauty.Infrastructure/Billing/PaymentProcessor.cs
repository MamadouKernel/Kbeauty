using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Billing.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace KekeBeauty.Infrastructure.Billing;

public enum NotificationOutcome
{
    Processed,
    Duplicate,
    Ignored,
    Deferred,
}

/// <summary>
/// Turns a payment notification into state changes (PRD BIL-02, §10.2): idempotent, never trusts the browser or the
/// notification body, and re-asks the provider for the real status, amount and currency before touching anything.
/// </summary>
public sealed class PaymentProcessor(
    KekeBeautyDbContext db,
    IEnumerable<IPaymentGateway> gateways,
    EntitlementService entitlements,
    NotificationService notifications,
    AppointmentPaymentService appointmentPayments,
    TipPaymentService tipPayments,
    TimeProvider time,
    ILogger<PaymentProcessor> logger)
{
    /// <summary>How far back a returning customer's payments are re-read, and how many (each read is a call to the provider).</summary>
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromHours(24);

    private const int RefreshMaxPayments = 3;

    public IPaymentGateway? GatewayFor(string provider) =>
        gateways.FirstOrDefault(g => string.Equals(g.Provider, provider, StringComparison.OrdinalIgnoreCase));

    public async Task<NotificationOutcome> HandleNotificationAsync(
        string provider,
        string eventId,
        string transactionId,
        string rawPayload,
        CancellationToken ct)
    {
        var gateway = GatewayFor(provider);
        if (gateway is null)
        {
            return NotificationOutcome.Ignored;
        }

        var record = new PaymentWebhookEvent { Provider = gateway.Provider, EventId = eventId, Payload = Truncate(rawPayload), ReceivedAt = time.GetUtcNow() };
        db.PaymentWebhookEvents.Add(record);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return NotificationOutcome.Duplicate;
        }

        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Provider == gateway.Provider && p.ProviderTransactionId == transactionId, ct);
        if (payment is null)
        {
            logger.LogWarning("Payment notification for an unknown transaction on {Provider}", gateway.Provider);
            await MarkProcessedAsync(record, ct);
            return NotificationOutcome.Ignored;
        }

        if (payment.Status != PaymentStatus.Pending)
        {
            // Already settled (paid, refused or abandoned): the provider has nothing more to tell us, and answering "ask me again" would make it
            // retry a notification that can never change anything.
            await MarkProcessedAsync(record, ct);
            return NotificationOutcome.Duplicate;
        }

        PaymentVerification verification;
        try
        {
            verification = await gateway.VerifyAsync(transactionId, payment.ProviderReference, ct);
        }
        catch (PaymentGatewayException ex)
        {
            // The provider is unreachable: forget this event so that its retry (or the reconciliation) can run again.
            logger.LogWarning(ex, "Could not verify transaction on {Provider}", gateway.Provider);
            db.PaymentWebhookEvents.Remove(record);
            await db.SaveChangesAsync(ct);
            return NotificationOutcome.Deferred;
        }

        if (verification.Status == GatewayStatus.Pending)
        {
            db.PaymentWebhookEvents.Remove(record);
            await db.SaveChangesAsync(ct);
            return NotificationOutcome.Deferred;
        }

        await SettleAsync(payment, verification, ct);
        await MarkProcessedAsync(record, ct);
        return NotificationOutcome.Processed;
    }

    /// <summary>
    /// The customer is back from the payment page. A provider may never call us back (WiniPayer's sandbox does not), so rather than
    /// wait for the hourly reconciliation we ask it about the establishment's recent pending subscription payments. Same safety as a
    /// notification: the verdict, amount and currency come from the provider, and settling is idempotent. Returns how many were settled.
    /// </summary>
    public async Task<Result<int>> RefreshPendingAsync(Guid userId, Guid establishmentId, CancellationToken ct)
    {
        if (!await PartnerSupport.OwnedBy(db, userId, establishmentId).AnyAsync(ct))
        {
            return Result.Failure<int>(PartnerErrors.NotFound);
        }

        var since = time.GetUtcNow() - RefreshWindow;
        var pending = await (
            from p in db.Payments
            join i in db.Invoices on p.InvoiceId equals (Guid?)i.Id
            join s in db.Subscriptions on i.SubscriptionId equals s.Id
            where s.EstablishmentId == establishmentId && p.Status == PaymentStatus.Pending && p.CreatedAt > since
            orderby p.CreatedAt descending
            select p).Take(RefreshMaxPayments).ToListAsync(ct);

        var settled = 0;
        var paidInvoices = new HashSet<Guid>();
        foreach (var payment in pending)
        {
            // Each "Pay" click opens a new provider invoice; once one of them paid the subscription, the others add nothing.
            if (payment.InvoiceId is { } invoiceId && paidInvoices.Contains(invoiceId))
            {
                continue;
            }

            var gateway = GatewayFor(payment.Provider);
            if (gateway is null)
            {
                continue;
            }

            try
            {
                var verification = await gateway.VerifyAsync(payment.ProviderTransactionId, payment.ProviderReference, ct);
                if (verification.Status == GatewayStatus.Pending)
                {
                    continue;
                }

                await SettleAsync(payment, verification, ct);
                settled++;
                if (verification.Status == GatewayStatus.Accepted && payment.InvoiceId is { } paid)
                {
                    paidInvoices.Add(paid);
                }
            }
            catch (PaymentGatewayException ex)
            {
                // The provider is unreachable: the next poll (or the reconciliation) tries again.
                logger.LogWarning(ex, "Could not verify transaction on {Provider}", gateway.Provider);
            }
        }

        return Result.Success(settled);
    }

    /// <summary>Applies the provider's verdict to a pending payment. Safe to call twice: only one caller wins the claim.</summary>
    public async Task SettleAsync(Payment payment, PaymentVerification verification, CancellationToken ct)
    {
        if (verification.Status == GatewayStatus.Pending)
        {
            return;
        }

        var accepted = verification.Status == GatewayStatus.Accepted;
        var mismatch = accepted && (verification.Amount != payment.Amount
            || !string.Equals(verification.Currency, payment.CurrencyCode, StringComparison.OrdinalIgnoreCase));
        var status = accepted && !mismatch ? PaymentStatus.Succeeded : PaymentStatus.Failed;
        var method = status == PaymentStatus.Succeeded ? MapMethod(payment.Provider, verification.Method) : payment.Method;
        var raw = Truncate(mismatch
            ? $"amount_mismatch: expected {payment.Amount} {payment.CurrencyCode}, provider reported {verification.Amount} {verification.Currency}. {verification.Raw}"
            : verification.Raw);

        var claimed = await db.Payments
            .Where(p => p.Id == payment.Id && p.Status == PaymentStatus.Pending)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.Status, status).SetProperty(p => p.Method, method).SetProperty(p => p.RawPayload, raw), ct);
        if (claimed == 0)
        {
            return; // somebody else already settled it
        }

        Telemetry.PaymentsSettled.Add(1, new KeyValuePair<string, object?>("provider", payment.Provider),
            new KeyValuePair<string, object?>("outcome", status == PaymentStatus.Succeeded ? "succeeded" : mismatch ? "amount_mismatch" : "failed"));

        if (mismatch)
        {
            logger.LogError("Refused payment {Payment}: the provider reported another amount or currency", payment.Id);
        }

        if (payment.TipPaymentId is not null)
        {
            await tipPayments.OnSettledAsync(payment, status == PaymentStatus.Succeeded, ct);
            return;
        }

        if (payment.AppointmentPaymentId is not null)
        {
            await appointmentPayments.OnSettledAsync(payment, status == PaymentStatus.Succeeded, ct);
            return;
        }

        var invoice = await db.Invoices.FirstAsync(i => i.Id == payment.InvoiceId, ct);
        var subscription = await db.Subscriptions.FirstAsync(s => s.Id == invoice.SubscriptionId, ct);
        var owner = await db.Establishments.AsNoTracking().Where(e => e.Id == subscription.EstablishmentId)
            .Select(e => new { e.Name, e.OwnerUserId }).FirstAsync(ct);

        var variables = new Dictionary<string, string>
        {
            ["etablissement"] = owner.Name,
            ["facture"] = invoice.Number,
            ["montant"] = $"{payment.Amount:N0} {payment.CurrencyCode}",
        };
        if (status == PaymentStatus.Succeeded)
        {
            if (invoice.Status is InvoiceStatus.Open or InvoiceStatus.Overdue)
            {
                var now = time.GetUtcNow();
                invoice.Status = InvoiceStatus.Paid;
                invoice.PaidAt = now;
                BillingRules.Activate(subscription, invoice, now);
                if (subscription.ReplacesSubscriptionId is { } replaced)
                {
                    await SubscriptionChanges.CancelReplacedAsync(db, replaced, ct);
                }

                await db.SaveChangesAsync(ct);
                await entitlements.SyncAsync(subscription.EstablishmentId, ct);
            }
            else
            {
                logger.LogWarning("Payment {Payment} succeeded for invoice {Invoice} that was not payable ({Status})", payment.Id, invoice.Number, invoice.Status);
            }

            await NotifyAsync(owner.OwnerUserId, "billing.payment_succeeded", "Paiement reçu",
                $"Votre paiement de {payment.Amount:N0} {payment.CurrencyCode} pour « {owner.Name} » (facture {invoice.Number}) a bien été reçu. Merci !", variables, ct);
        }
        else
        {
            await NotifyAsync(owner.OwnerUserId, "billing.payment_failed", "Paiement non abouti",
                $"Le paiement de la facture {invoice.Number} pour « {owner.Name} » n'a pas abouti. Vous pouvez réessayer depuis votre espace abonnement.", variables, ct);
        }
    }

    public static PaymentMethod MapMethod(string provider, string? code) =>
        string.Equals(provider, PaymentsOptions.CinetPayProvider, StringComparison.OrdinalIgnoreCase)
            ? CinetPayGateway.MapMethod(code)
            : string.Equals(provider, PaymentsOptions.WinipayProvider, StringComparison.OrdinalIgnoreCase)
                ? WinipayGateway.MapMethod(code)
                : Enum.TryParse<PaymentMethod>(code, ignoreCase: true, out var parsed) ? parsed : PaymentMethod.Other;

    private async Task MarkProcessedAsync(PaymentWebhookEvent record, CancellationToken ct)
    {
        record.ProcessedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    private async Task NotifyAsync(Guid? userId, string template, string title, string body, IReadOnlyDictionary<string, string> variables, CancellationToken ct)
    {
        if (userId is { } id)
        {
            await notifications.NotifyAsync(id, template, title, body, null, ct, variables);
        }
    }

    private static string? Truncate(string? value) => value is { Length: > 8000 } ? value[..8000] : value;
}
