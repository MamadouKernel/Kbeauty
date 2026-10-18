using System.Globalization;
using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Billing.Domain;
using KekeBeauty.Modules.Catalog.Domain;
using KekeBeauty.Modules.Scheduling.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KekeBeauty.Infrastructure.Billing;

/// <summary>What the client has to pay to book, computed from the establishment's settings and the platform's.</summary>
public sealed record PaymentPlan(AppointmentPaymentKind Kind, decimal Amount, decimal PlatformFee, decimal Net, string Currency, int DepositPercent);

public sealed record AppointmentPaymentDto(
    Guid Id,
    string Status,
    string Kind,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset? PaidAt,
    DateTimeOffset? HoldExpiresAt,
    string? PaymentUrl);

/// <summary>What a booking will cost online, shown to the client before they confirm.</summary>
public sealed record PaymentQuote(string Kind, decimal Amount, string CurrencyCode, int DepositPercent, decimal Total, decimal OnTheSpot);

/// <summary>A payment cancelled after the limit: the fee kept, the part refunded and the establishment's net share of the fee.</summary>
public sealed record LateCancelRetention(AppointmentPayment Record, decimal Fee, decimal Refund, decimal RetainedNet);

/// <summary>The pricing rule of online payments: pure, so that the quote shown to the client and the amount charged can never differ.</summary>
public static class PaymentRules
{
    public const decimal MinimumAmount = 100;

    public static PaymentPlan? Plan(Establishment establishment, decimal total, string currency, bool platformEnabled, int platformFeePercent)
    {
        if (!platformEnabled
            || establishment.PaymentMode == EstablishmentPaymentMode.None
            || total <= 0
            || !PayoutOperators.IsKnown(establishment.PayoutOperator)
            || string.IsNullOrWhiteSpace(establishment.PayoutAccount))
        {
            return null;
        }

        var wanted = establishment.PaymentMode == EstablishmentPaymentMode.Full
            ? total
            : Math.Ceiling(total * establishment.DepositPercent / 100m);
        var amount = Math.Ceiling(Math.Min(total, Math.Max(wanted, MinimumAmount)));
        var fee = decimal.Truncate(amount * platformFeePercent / 100m);
        var kind = amount >= total ? AppointmentPaymentKind.Full : AppointmentPaymentKind.Deposit;
        return new PaymentPlan(kind, amount, fee, amount - fee, currency, establishment.DepositPercent);
    }

    /// <summary>The late-cancellation fee: a share of the price, never more than what the client paid online.</summary>
    public static decimal LateFee(decimal total, int feePercent, decimal paid) =>
        feePercent <= 0 ? 0 : Math.Min(Math.Ceiling(total * feePercent / 100m), paid);

    /// <summary>Splits an online payment cancelled late: what goes back to the client and what the establishment keeps (net of the platform fee).</summary>
    public static LateCancelRetention? Retention(AppointmentPayment record, decimal total, int feePercent)
    {
        var fee = LateFee(total, feePercent, record.Amount);
        if (fee <= 0)
        {
            return null;
        }

        var platformShare = record.Amount <= 0 ? 0 : decimal.Truncate(record.PlatformFee * fee / record.Amount);
        return new LateCancelRetention(record, fee, record.Amount - fee, fee - platformShare);
    }

    public static PaymentQuote Quote(PaymentPlan plan, decimal total) => new(plan.Kind.ToString(), plan.Amount, plan.Currency, plan.DepositPercent, total, total - plan.Amount);
}

public static class AppointmentPaymentErrors
{
    public static readonly Error NotPayable = new("payment.not_payable", "Ce rendez-vous n'attend pas de paiement.");
    public static readonly Error HoldExpired = new("payment.hold_expired", "Le délai de paiement est dépassé : le créneau a été libéré. Réservez à nouveau.");
    public static readonly Error RefundNotPossible = new("payment.refund_not_possible", "Ce paiement ne peut pas être remboursé de cette façon.");
}

/// <summary>
/// Online payment of appointments (deposit or full price). Booking holds the slot for a short while; the appointment only reaches the
/// establishment once the money is in. Anything that ends the appointment before it happens (cancellation, refusal, expiry) refunds the client.
/// </summary>
public sealed class AppointmentPaymentService(
    KekeBeautyDbContext db,
    IPaymentGateway gateway,
    IPayoutGateway payouts,
    AppSettingsService settings,
    NotificationService notifications,
    IOptions<PaymentsOptions> payments,
    IServiceProvider services,
    TimeProvider time,
    ILogger<AppointmentPaymentService> logger)
{
    /// <summary>How long a slot is held for a client who has not paid yet.</summary>
    public const int HoldMinutes = 30;

    // ───────── Pricing ─────────

    /// <summary>The payment a booking requires, or null when it can be settled on the spot (feature off, no online payment, free service...).</summary>
    public async Task<PaymentPlan?> PlanAsync(Establishment establishment, decimal total, string currency, CancellationToken ct)
    {
        var platform = await settings.GetAsync(ct);
        return PaymentRules.Plan(establishment, total, currency, platform.AppointmentPaymentsEnabled, platform.PlatformFeePercent);
    }

    // ───────── Booking side ─────────

    /// <summary>Records what is owed for a freshly created (held) appointment and opens the payment page.</summary>
    public async Task<Result<AppointmentPaymentDto>> StartAsync(Appointment appointment, PaymentPlan plan, string establishmentName, CancellationToken ct)
    {
        var record = new AppointmentPayment
        {
            AppointmentId = appointment.Id,
            EstablishmentId = appointment.EstablishmentId,
            ClientUserId = appointment.ClientUserId ?? throw new InvalidOperationException("An online payment needs a client account."),
            Kind = plan.Kind,
            Amount = plan.Amount,
            CurrencyCode = plan.Currency,
            PlatformFee = plan.PlatformFee,
            NetAmount = plan.Net,
            CreatedAt = time.GetUtcNow(),
        };
        db.AppointmentPayments.Add(record);
        await db.SaveChangesAsync(ct);
        return await OpenSessionAsync(record, appointment, establishmentName, ct);
    }

    /// <summary>The client comes back to pay: the same page while it is open, a new one if the previous attempt was refused.</summary>
    public async Task<Result<AppointmentPaymentDto>> ResumeAsync(Guid clientId, Guid appointmentId, CancellationToken ct)
    {
        var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId && a.ClientUserId == clientId, ct);
        if (appointment is null)
        {
            return Result.Failure<AppointmentPaymentDto>(SchedulingErrors.AppointmentNotFound);
        }

        var record = await db.AppointmentPayments.OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync(p => p.AppointmentId == appointmentId, ct);
        if (record is null || !appointment.AwaitingPayment || record.Status is not (AppointmentPaymentStatus.Awaiting or AppointmentPaymentStatus.Failed))
        {
            return Result.Failure<AppointmentPaymentDto>(AppointmentPaymentErrors.NotPayable);
        }

        if (appointment.ExpiresAt is { } deadline && deadline <= time.GetUtcNow())
        {
            return Result.Failure<AppointmentPaymentDto>(AppointmentPaymentErrors.HoldExpired);
        }

        var current = record.PaymentId is { } id ? await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct) : null;
        if (record.Status == AppointmentPaymentStatus.Awaiting && current is { Status: PaymentStatus.Pending } && record.PaymentUrl is not null)
        {
            return Result.Success(ToDto(record, appointment));
        }

        record.Status = AppointmentPaymentStatus.Awaiting;
        var establishment = await db.Establishments.AsNoTracking().Where(e => e.Id == appointment.EstablishmentId).Select(e => e.Name).FirstAsync(ct);
        return await OpenSessionAsync(record, appointment, establishment, ct);
    }

    private async Task<Result<AppointmentPaymentDto>> OpenSessionAsync(AppointmentPayment record, Appointment appointment, string establishmentName, CancellationToken ct)
    {
        var transactionId = "KA" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
        var payment = new Payment
        {
            AppointmentPaymentId = record.Id,
            Provider = gateway.Provider,
            ProviderTransactionId = transactionId,
            Amount = record.Amount,
            CurrencyCode = record.CurrencyCode,
            CreatedAt = time.GetUtcNow(),
        };
        db.Payments.Add(payment);
        record.PaymentId = payment.Id;
        await db.SaveChangesAsync(ct);

        var client = await db.Users.AsNoTracking().Where(u => u.Id == record.ClientUserId)
            .Select(u => new { u.FirstName, u.LastName, u.Email }).FirstAsync(ct);
        var urls = payments.Value;
        try
        {
            var session = await gateway.CreateAsync(
                new PaymentRequest(
                    transactionId,
                    record.Amount,
                    record.CurrencyCode,
                    $"Rendez-vous — {establishmentName}",
                    $"{client.FirstName} {client.LastName}".Trim(),
                    client.Email,
                    appointment.ClientPhone ?? string.Empty,
                    $"{urls.WebBaseUrl.TrimEnd('/')}/compte/rendez-vous?paiement={transactionId}",
                    $"{urls.ApiBaseUrl.TrimEnd('/')}/api/v1/webhooks/payments/{gateway.Provider}"),
                ct);
            payment.ProviderReference = session.ProviderReference;
            record.PaymentUrl = session.PaymentUrl;
            await db.SaveChangesAsync(ct);
            return Result.Success(ToDto(record, appointment));
        }
        catch (PaymentGatewayException ex)
        {
            logger.LogWarning(ex, "The payment page of appointment {Appointment} could not be opened", appointment.Id);
            payment.Status = PaymentStatus.Failed;
            payment.RawPayload = "gateway unavailable at creation";
            record.Status = AppointmentPaymentStatus.Failed;
            await db.SaveChangesAsync(ct);
            return Result.Failure<AppointmentPaymentDto>(BillingErrors.GatewayUnavailable);
        }
    }

    public async Task<IReadOnlyDictionary<Guid, AppointmentPaymentDto>> InfoForAsync(IReadOnlyCollection<Appointment> appointments, CancellationToken ct)
    {
        if (appointments.Count == 0)
        {
            return new Dictionary<Guid, AppointmentPaymentDto>();
        }

        var ids = appointments.Select(a => a.Id).ToList();
        var rows = await db.AppointmentPayments.AsNoTracking().Where(p => ids.Contains(p.AppointmentId)).OrderBy(p => p.CreatedAt).ToListAsync(ct);
        var byAppointment = appointments.ToDictionary(a => a.Id);
        return rows.GroupBy(r => r.AppointmentId).ToDictionary(g => g.Key, g => ToDto(g.Last(), byAppointment[g.Key]));
    }

    private static AppointmentPaymentDto ToDto(AppointmentPayment record, Appointment appointment)
    {
        var open = record.Status == AppointmentPaymentStatus.Awaiting && appointment.AwaitingPayment;
        return new AppointmentPaymentDto(
            record.Id,
            record.Status.ToString(),
            record.Kind.ToString(),
            record.Amount,
            record.CurrencyCode,
            record.PaidAt,
            open ? appointment.ExpiresAt : null,
            open ? record.PaymentUrl : null);
    }

    // ───────── Gateway verdicts ─────────

    /// <summary>Called by the payment processor once the provider's verdict on one of our transactions is known (already claimed, so once only).</summary>
    public async Task OnSettledAsync(Payment payment, bool succeeded, CancellationToken ct)
    {
        var record = payment.AppointmentPaymentId is { } id ? await db.AppointmentPayments.FirstOrDefaultAsync(p => p.Id == id, ct) : null;
        if (record is null)
        {
            return;
        }

        Telemetry.AppointmentPayments.Add(1, new KeyValuePair<string, object?>("outcome", succeeded ? "succeeded" : "failed"));
        var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == record.AppointmentId, ct);
        if (appointment is null)
        {
            return;
        }

        var now = time.GetUtcNow();
        var name = await db.Establishments.AsNoTracking().Where(e => e.Id == record.EstablishmentId).Select(e => e.Name).FirstAsync(ct);
        if (!succeeded)
        {
            if (record.Status == AppointmentPaymentStatus.Awaiting)
            {
                record.Status = AppointmentPaymentStatus.Failed;
                await db.SaveChangesAsync(ct);
                await NotifyClientAsync(appointment, "appointment.payment_failed", "Paiement non abouti",
                    $"Le paiement de votre rendez-vous chez {name} n'a pas abouti. Réessayez depuis Mes rendez-vous avant la fin du délai de réservation.", record, name, ct);
            }

            return;
        }

        var late = record.Status != AppointmentPaymentStatus.Awaiting || !appointment.AwaitingPayment;
        record.Status = AppointmentPaymentStatus.Paid;
        record.PaidAt = now;
        await db.SaveChangesAsync(ct);

        if (late)
        {
            // The hold ran out (or the appointment was cancelled) before the money arrived: give it back.
            await RefundAsync(record, appointment, ct);
            return;
        }

        var establishment = await db.Establishments.AsNoTracking().FirstAsync(e => e.Id == record.EstablishmentId, ct);
        var workflow = services.GetRequiredService<AppointmentWorkflow>();
        appointment.AwaitingPayment = false;
        if (establishment.AutoConfirm)
        {
            workflow.Apply(appointment, AppointmentStatus.Confirmed, ActorKind.System, null, "Paiement reçu");
            appointment.ExpiresAt = null;
        }
        else
        {
            appointment.ExpiresAt = now.AddHours(AppointmentWorkflow.RequestValidityHours);
        }

        await db.SaveChangesAsync(ct);
        await NotifyClientAsync(appointment, "appointment.payment_received", "Paiement reçu",
            $"Votre paiement de {Money(record)} pour le rendez-vous chez {name} a bien été reçu.", record, name, ct);
        await workflow.AfterChangeAsync(appointment, null, null, ct);
    }

    // ───────── When an appointment ends before it happens ─────────

    /// <summary>Called after every saved transition: abandons an unpaid hold, or refunds a paid appointment that will not take place.</summary>
    public async Task AfterTransitionAsync(Appointment appointment, CancellationToken ct)
    {
        if (appointment.Status is not (AppointmentStatus.CancelledByClient or AppointmentStatus.CancelledByEstablishment or AppointmentStatus.Rejected or AppointmentStatus.Expired))
        {
            return;
        }

        var record = await db.AppointmentPayments.OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync(p => p.AppointmentId == appointment.Id, ct);
        if (record is null)
        {
            return;
        }

        if (record.Status is AppointmentPaymentStatus.Awaiting or AppointmentPaymentStatus.Failed)
        {
            record.Status = AppointmentPaymentStatus.Expired;
            appointment.AwaitingPayment = false;
            await db.SaveChangesAsync(ct);
        }
        else if (record.Status == AppointmentPaymentStatus.Paid)
        {
            await RefundAsync(record, appointment, ct);
        }
    }

    // ───────── Late cancellation ─────────

    /// <summary>What the establishment keeps if the client cancels now, or null when it keeps nothing (no fee set, or not paid online).</summary>
    public async Task<LateCancelRetention?> LateRetentionAsync(Appointment appointment, int feePercent, CancellationToken ct)
    {
        if (feePercent <= 0)
        {
            return null;
        }

        var record = await db.AppointmentPayments
            .Where(p => p.AppointmentId == appointment.Id && p.Status == AppointmentPaymentStatus.Paid)
            .OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync(ct);
        return record is null ? null : PaymentRules.Retention(record, appointment.TotalPrice, feePercent);
    }

    // ───────── Refunds ─────────

    /// <summary>Starts the transfer that gives the client's money back. Safe to call twice: only one caller claims the refund.</summary>
    public async Task RefundAsync(AppointmentPayment record, Appointment appointment, CancellationToken ct)
    {
        if (record.RefundAmount is 0)
        {
            return; // the whole payment is kept as a late-cancellation fee: nothing goes back
        }

        var claimed = await db.AppointmentPayments
            .Where(p => p.Id == record.Id && (p.Status == AppointmentPaymentStatus.Paid || p.Status == AppointmentPaymentStatus.RefundFailed))
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.Status, AppointmentPaymentStatus.RefundPending).SetProperty(p => p.RefundError, (string?)null), ct);
        if (claimed == 0)
        {
            return;
        }

        // The claim above bypassed the change tracker: reload so that the next save compares against what the database holds.
        await db.Entry(record).ReloadAsync(ct);
        var method = record.PaymentId is { } id ? await db.Payments.AsNoTracking().Where(p => p.Id == id).Select(p => p.Method).FirstOrDefaultAsync(ct) : PaymentMethod.Other;
        var operatorId = PayoutOperators.For(method);
        var account = PhoneNumbers.ToE164(appointment.ClientPhone);
        if (operatorId is null || account is null)
        {
            await FailRefundAsync(record, "Remboursement manuel nécessaire (moyen de paiement ou numéro inconnu).", ct);
            return;
        }

        var transactionId = "KR" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
        record.RefundTransactionId = transactionId;
        try
        {
            var name = await db.Users.AsNoTracking().Where(u => u.Id == record.ClientUserId).Select(u => u.FirstName + " " + u.LastName).FirstAsync(ct);
            record.RefundProviderReference = await payouts.SendAsync(
                new PayoutRequest(transactionId, operatorId, name.Trim(), account.TrimStart('+'), record.RefundAmount ?? record.Amount, "Remboursement rendez-vous Keke Beauty"), ct);
            await db.SaveChangesAsync(ct);
            await CheckRefundAsync(record, appointment, ct);
        }
        catch (PaymentGatewayException ex)
        {
            logger.LogWarning(ex, "Refund of appointment payment {Payment} could not be sent", record.Id);
            await FailRefundAsync(record, ex.Message, ct);
        }
    }

    /// <summary>Reads the state of a started refund and settles it (used right after sending and by the maintenance job).</summary>
    public async Task CheckRefundAsync(AppointmentPayment record, Appointment? appointment, CancellationToken ct)
    {
        if (record.Status != AppointmentPaymentStatus.RefundPending || record.RefundTransactionId is null)
        {
            return;
        }

        PayoutVerification verification;
        try
        {
            verification = await payouts.VerifyAsync(record.RefundTransactionId, record.RefundProviderReference, ct);
        }
        catch (PaymentGatewayException)
        {
            return; // asked again on the next pass
        }

        switch (verification.State)
        {
            case PayoutState.Succeeded:
                record.Status = AppointmentPaymentStatus.Refunded;
                record.RefundedAt = time.GetUtcNow();
                await db.SaveChangesAsync(ct);
                appointment ??= await db.Appointments.FirstOrDefaultAsync(a => a.Id == record.AppointmentId, ct);
                if (appointment is not null)
                {
                    var name = await db.Establishments.AsNoTracking().Where(e => e.Id == record.EstablishmentId).Select(e => e.Name).FirstAsync(ct);
                    await NotifyClientAsync(appointment, "appointment.refunded", "Remboursement effectué",
                        $"Votre paiement de {Money(record)} pour le rendez-vous chez {name} vous a été remboursé.", record, name, ct);
                }

                break;
            case PayoutState.Failed:
                await FailRefundAsync(record, "Le transfert a été refusé par l'opérateur.", ct);
                break;
        }
    }

    private async Task FailRefundAsync(AppointmentPayment record, string error, CancellationToken ct)
    {
        record.Status = AppointmentPaymentStatus.RefundFailed;
        record.RefundError = error.Length > 500 ? error[..500] : error;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Back-office: tries a failed refund again.</summary>
    public async Task<Result> RetryRefundAsync(AdminContext admin, Guid recordId, CancellationToken ct)
    {
        var record = await db.AppointmentPayments.FirstOrDefaultAsync(p => p.Id == recordId, ct);
        if (record is null || record.Status != AppointmentPaymentStatus.RefundFailed)
        {
            return Result.Failure(AppointmentPaymentErrors.RefundNotPossible);
        }

        var appointment = await db.Appointments.FirstAsync(a => a.Id == record.AppointmentId, ct);
        Audit.Add(db, admin, time, "refund.retry", "AppointmentPayment", record.Id, new { record.Amount });
        await db.SaveChangesAsync(ct);
        await RefundAsync(record, appointment, ct);
        return Result.Success();
    }

    /// <summary>Back-office: records that a failed refund was settled by hand (outside the application).</summary>
    public async Task<Result> MarkRefundedManuallyAsync(AdminContext admin, Guid recordId, string? reference, CancellationToken ct)
    {
        var record = await db.AppointmentPayments.FirstOrDefaultAsync(p => p.Id == recordId, ct);
        var text = reference?.Trim();
        if (record is null || record.Status != AppointmentPaymentStatus.RefundFailed)
        {
            return Result.Failure(AppointmentPaymentErrors.RefundNotPossible);
        }

        if (text is null or { Length: < 3 or > 100 })
        {
            return Result.Failure(AdminErrors.Invalid("Indiquez la référence du remboursement (3 à 100 caractères)."));
        }

        record.Status = AppointmentPaymentStatus.Refunded;
        record.RefundedAt = time.GetUtcNow();
        record.RefundProviderReference = text;
        Audit.Add(db, admin, time, "refund.manual", "AppointmentPayment", record.Id, new { record.Amount, reference = text });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>One pass of the maintenance job: refunds whose transfer was still pending.</summary>
    public async Task<int> CheckPendingRefundsAsync(CancellationToken ct)
    {
        var pending = await db.AppointmentPayments.Where(p => p.Status == AppointmentPaymentStatus.RefundPending).OrderBy(p => p.CreatedAt).Take(100).ToListAsync(ct);
        foreach (var record in pending)
        {
            await CheckRefundAsync(record, null, ct);
        }

        return pending.Count;
    }

    // ───────── Helpers ─────────

    private static string Money(AppointmentPayment record) => $"{record.Amount.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ')} {record.CurrencyCode}";

    private async Task NotifyClientAsync(Appointment appointment, string template, string title, string body, AppointmentPayment record, string establishmentName, CancellationToken ct)
    {
        var timezone = await db.Establishments.AsNoTracking().Where(e => e.Id == appointment.EstablishmentId).Select(e => e.Timezone).FirstAsync(ct);
        var local = TimeZoneInfo.ConvertTime(appointment.StartsAt, AppointmentWorkflow.ZoneOf(timezone));
        var variables = new Dictionary<string, string>
        {
            ["etablissement"] = establishmentName,
            ["client"] = string.Empty,
            ["montant"] = Money(record),
            ["date"] = local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            ["heure"] = local.ToString("HH:mm", CultureInfo.InvariantCulture),
            ["motif"] = string.Empty,
        };
        await notifications.NotifyAsync(record.ClientUserId, template, title, body, appointment.ClientPhone, ct, variables);
    }
}
