using System.Globalization;
using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Billing.Domain;
using KekeBeauty.Modules.Catalog.Domain;
using KekeBeauty.Modules.Scheduling.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KekeBeauty.Infrastructure.Billing;

public sealed record BalanceDto(decimal Upcoming, decimal Payable, decimal PaidOut, string CurrencyCode, bool PayoutConfigured, string? PayoutOperator, string? PayoutAccountMasked);

public sealed record PartnerPaymentDto(
    Guid Id,
    Guid AppointmentId,
    DateTimeOffset StartsAt,
    decimal Amount,
    decimal PlatformFee,
    decimal NetAmount,
    string Kind,
    string Status,
    string AppointmentStatus,
    DateTimeOffset? PaidAt,
    bool PaidOut);

public sealed record PayoutDto(Guid Id, Guid EstablishmentId, string EstablishmentName, decimal Amount, string CurrencyCode, string Operator, string AccountMasked, string Status, string? Error, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);

public sealed record RefundDto(Guid Id, Guid AppointmentId, string EstablishmentName, decimal Amount, string CurrencyCode, string Status, string? Error, DateTimeOffset CreatedAt, DateTimeOffset? RefundedAt);

public sealed record PayoutRunReport(int Payouts, decimal Total, int Skipped);

public sealed record RefundReferenceInput(string? Reference);

public sealed record PaymentSettingsDto(string Mode, int DepositPercent, string? PayoutOperator, string? PayoutAccount, string? PayoutAccountName, bool PlatformEnabled);

public sealed record PartnerPaymentsView(BalanceDto Balance, IReadOnlyList<PartnerPaymentDto> Payments, IReadOnlyList<PayoutDto> Payouts);

public static class PayoutErrors
{
    public static readonly Error NothingToPay = new("payout.nothing_to_pay", "Aucun montant à reverser à cet établissement pour le moment.");
    public static readonly Error NoDestination = new("payout.no_destination", "L'établissement n'a pas renseigné son compte de paiement mobile.");
    public static readonly Error EstablishmentNotFound = new("establishment.not_found", "Établissement introuvable.");
    public static readonly Error GatewayRefused = new("payout.gateway_error", "Le transfert a été refusé ou le service de paiement est indisponible.");
}

/// <summary>
/// The money side for establishments: what is coming, what can be paid out, and the payouts themselves (mobile-money transfers started by
/// the admins). A payout covers the net amount of every paid appointment that has taken place; refunded ones never count.
/// </summary>
public sealed class PayoutService(
    KekeBeautyDbContext db,
    IPayoutGateway gateway,
    AppSettingsService appSettings,
    NotificationService notifications,
    TimeProvider time,
    ILogger<PayoutService> logger)
{
    private static readonly AppointmentStatus[] Held = [AppointmentStatus.Pending, AppointmentStatus.Confirmed, AppointmentStatus.RescheduleProposed];
    private static readonly AppointmentStatus[] Done = [AppointmentStatus.Completed, AppointmentStatus.NoShow];

    // ───────── Partner settings ─────────

    public async Task<Result<PaymentSettingsDto>> GetSettingsAsync(Guid userId, Guid establishmentId, CancellationToken ct)
    {
        var e = await Partner.PartnerSupport.OwnedBy(db, userId, establishmentId).AsNoTracking().FirstOrDefaultAsync(ct);
        return e is null
            ? Result.Failure<PaymentSettingsDto>(Partner.PartnerErrors.NotFound)
            : Result.Success(new PaymentSettingsDto(e.PaymentMode.ToString(), e.DepositPercent, e.PayoutOperator, e.PayoutAccount, e.PayoutAccountName, await appSettings.IsEnabledAsync(AppSettingsService.AppointmentPaymentsKey, ct)));
    }

    public async Task<Result<PaymentSettingsDto>> UpdateSettingsAsync(Guid userId, Guid establishmentId, PaymentSettingsDto input, CancellationToken ct)
    {
        var e = await Partner.PartnerSupport.OwnedBy(db, userId, establishmentId).FirstOrDefaultAsync(ct);
        if (e is null)
        {
            return Result.Failure<PaymentSettingsDto>(Partner.PartnerErrors.NotFound);
        }

        if (!Enum.TryParse<EstablishmentPaymentMode>(input.Mode, ignoreCase: true, out var mode) || !Enum.IsDefined(mode))
        {
            return Result.Failure<PaymentSettingsDto>(SchedulingErrors.Invalid("Mode de paiement inconnu (None, Deposit ou Full)."));
        }

        if (input.DepositPercent is < 10 or > 100)
        {
            return Result.Failure<PaymentSettingsDto>(SchedulingErrors.Invalid("L’acompte doit représenter entre 10 et 100 % du prix."));
        }

        var account = PhoneNumbers.ToE164(input.PayoutAccount);
        var holder = input.PayoutAccountName?.Trim();
        if (mode != EstablishmentPaymentMode.None)
        {
            if (!e.BookingAllowed)
            {
                return Result.Failure<PaymentSettingsDto>(BillingErrors.FeatureRequired);
            }

            if (!PayoutOperators.IsKnown(input.PayoutOperator) || account is null || holder is not { Length: >= 2 and <= 100 })
            {
                return Result.Failure<PaymentSettingsDto>(SchedulingErrors.Invalid("Pour encaisser en ligne, indiquez l’opérateur, le numéro et le nom du titulaire du compte de paiement mobile."));
            }
        }

        e.PaymentMode = mode;
        e.DepositPercent = input.DepositPercent;
        e.PayoutOperator = PayoutOperators.IsKnown(input.PayoutOperator) ? input.PayoutOperator : null;
        e.PayoutAccount = account;
        e.PayoutAccountName = string.IsNullOrWhiteSpace(holder) ? null : holder;
        await db.SaveChangesAsync(ct);
        return Result.Success(new PaymentSettingsDto(e.PaymentMode.ToString(), e.DepositPercent, e.PayoutOperator, e.PayoutAccount, e.PayoutAccountName, await appSettings.IsEnabledAsync(AppSettingsService.AppointmentPaymentsKey, ct)));
    }

    public async Task<Result<PartnerPaymentsView>> ViewAsync(Guid userId, Guid establishmentId, CancellationToken ct)
    {
        if (!await Partner.PartnerSupport.OwnedBy(db, userId, establishmentId).AnyAsync(ct))
        {
            return Result.Failure<PartnerPaymentsView>(Partner.PartnerErrors.NotFound);
        }

        return Result.Success(new PartnerPaymentsView(
            await BalanceAsync(establishmentId, ct),
            await PaymentsAsync(establishmentId, ct),
            await PayoutsOfAsync(establishmentId, ct)));
    }

    // ───────── Partner view ─────────

    public async Task<BalanceDto> BalanceAsync(Guid establishmentId, CancellationToken ct)
    {
        var establishment = await db.Establishments.AsNoTracking().Where(e => e.Id == establishmentId)
            .Select(e => new { e.PayoutOperator, e.PayoutAccount }).FirstOrDefaultAsync(ct);
        var paid = db.AppointmentPayments.AsNoTracking().Where(p => p.EstablishmentId == establishmentId && p.Status == AppointmentPaymentStatus.Paid);
        var upcoming = await (from p in paid
                              join a in db.Appointments on p.AppointmentId equals a.Id
                              where p.PayoutId == null && Held.Contains(a.Status)
                              select (decimal?)p.NetAmount).SumAsync(ct) ?? 0;
        var payable = await (from p in paid
                             join a in db.Appointments on p.AppointmentId equals a.Id
                             where p.PayoutId == null && Done.Contains(a.Status) && !db.Disputes.Any(d => d.AppointmentId == a.Id && d.Status == DisputeStatus.Open)
                             select (decimal?)p.NetAmount).SumAsync(ct) ?? 0;
        var paidOut = await db.Payouts.AsNoTracking().Where(p => p.EstablishmentId == establishmentId && p.Status == PayoutStatus.Succeeded)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var configured = PayoutOperators.IsKnown(establishment?.PayoutOperator) && !string.IsNullOrWhiteSpace(establishment?.PayoutAccount);
        return new BalanceDto(upcoming, payable, paidOut, "XOF", configured, establishment?.PayoutOperator, establishment?.PayoutAccount is { } account ? PhoneNumbers.Mask(account) : null);
    }

    public async Task<IReadOnlyList<PartnerPaymentDto>> PaymentsAsync(Guid establishmentId, CancellationToken ct) =>
        await (from p in db.AppointmentPayments.AsNoTracking()
               join a in db.Appointments.AsNoTracking() on p.AppointmentId equals a.Id
               where p.EstablishmentId == establishmentId && p.Status != AppointmentPaymentStatus.Awaiting && p.Status != AppointmentPaymentStatus.Expired
               orderby p.CreatedAt descending
               select new PartnerPaymentDto(p.Id, p.AppointmentId, a.StartsAt, p.Amount, p.PlatformFee, p.NetAmount, p.Kind.ToString(), p.Status.ToString(), a.Status.ToString(), p.PaidAt, p.PayoutId != null))
            .Take(100).ToListAsync(ct);

    public async Task<IReadOnlyList<PayoutDto>> PayoutsOfAsync(Guid establishmentId, CancellationToken ct) =>
        await ProjectAsync(db.Payouts.AsNoTracking().Where(p => p.EstablishmentId == establishmentId).OrderByDescending(p => p.CreatedAt).Take(50), ct);

    // ───────── Back-office ─────────

    public async Task<IReadOnlyList<PayoutDto>> AllPayoutsAsync(CancellationToken ct) =>
        await ProjectAsync(db.Payouts.AsNoTracking().OrderByDescending(p => p.CreatedAt).Take(100), ct);

    public async Task<IReadOnlyList<RefundDto>> RefundsAsync(CancellationToken ct) =>
        await (from p in db.AppointmentPayments.AsNoTracking()
               join e in db.Establishments.AsNoTracking() on p.EstablishmentId equals e.Id
               where p.Status == AppointmentPaymentStatus.RefundPending || p.Status == AppointmentPaymentStatus.RefundFailed || p.Status == AppointmentPaymentStatus.Refunded
               orderby p.CreatedAt descending
               select new RefundDto(p.Id, p.AppointmentId, e.Name, p.Amount, p.CurrencyCode, p.Status.ToString(), p.RefundError, p.CreatedAt, p.RefundedAt))
            .Take(100).ToListAsync(ct);

    /// <summary>Pays one establishment what it is owed.</summary>
    public async Task<Result<PayoutDto>> PayAsync(AdminContext? admin, Guid establishmentId, CancellationToken ct)
    {
        var establishment = await db.Establishments.AsNoTracking().FirstOrDefaultAsync(e => e.Id == establishmentId, ct);
        if (establishment is null)
        {
            return Result.Failure<PayoutDto>(PayoutErrors.EstablishmentNotFound);
        }

        if (!PayoutOperators.IsKnown(establishment.PayoutOperator) || string.IsNullOrWhiteSpace(establishment.PayoutAccount) || string.IsNullOrWhiteSpace(establishment.PayoutAccountName))
        {
            return Result.Failure<PayoutDto>(PayoutErrors.NoDestination);
        }

        var ids = await (from p in db.AppointmentPayments
                         join a in db.Appointments on p.AppointmentId equals a.Id
                         where p.EstablishmentId == establishmentId && p.Status == AppointmentPaymentStatus.Paid && p.PayoutId == null && Done.Contains(a.Status) && !db.Disputes.Any(d => d.AppointmentId == a.Id && d.Status == DisputeStatus.Open)
                         select p.Id).ToListAsync(ct);
        if (ids.Count == 0)
        {
            return Result.Failure<PayoutDto>(PayoutErrors.NothingToPay);
        }

        // Claim first, so two admins clicking at once can never pay the same appointment twice.
        var transactionId = "KP" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
        var payout = new Payout
        {
            EstablishmentId = establishmentId,
            Amount = 0,
            Operator = establishment.PayoutOperator!,
            Account = establishment.PayoutAccount!,
            AccountName = establishment.PayoutAccountName!,
            Provider = gateway.Provider,
            TransactionId = transactionId,
            CreatedBy = admin?.ActorId,
            CreatedAt = time.GetUtcNow(),
        };
        var payoutId = payout.Id;
        var claimed = await db.AppointmentPayments
            .Where(p => ids.Contains(p.Id) && p.PayoutId == null && p.Status == AppointmentPaymentStatus.Paid)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.PayoutId, (Guid?)payoutId), ct);
        if (claimed == 0)
        {
            return Result.Failure<PayoutDto>(PayoutErrors.NothingToPay);
        }

        var amount = await db.AppointmentPayments.Where(p => p.PayoutId == payoutId).SumAsync(p => p.NetAmount, ct);
        payout.Amount = amount;
        db.Payouts.Add(payout);
        if (admin is not null)
        {
            Audit.Add(db, admin, time, "payout.create", "Payout", payoutId, new { establishmentId, amount, claimed });
        }

        try
        {
            await db.SaveChangesAsync(ct);
            payout.ProviderReference = await gateway.SendAsync(
                new PayoutRequest(transactionId, payout.Operator, payout.AccountName, PhoneNumbers.ToE164(payout.Account)?.TrimStart('+') ?? payout.Account, amount, $"Reversement Keke Beauty — {establishment.Name}"), ct);
            await db.SaveChangesAsync(ct);
        }
        catch (PaymentGatewayException ex)
        {
            logger.LogWarning(ex, "Payout {Payout} to establishment {Establishment} was refused", payoutId, establishmentId);
            await ReleaseAsync(payout, ex.Message, ct);
            return Result.Failure<PayoutDto>(PayoutErrors.GatewayRefused);
        }

        await CheckAsync(payout, establishment.Name, establishment.OwnerUserId, ct);
        return Result.Success(Map(payout, establishment.Name));
    }

    /// <summary>Pays every establishment that is owed something and has a payment account.</summary>
    public async Task<PayoutRunReport> RunAllAsync(AdminContext admin, CancellationToken ct)
    {
        var due = await (from p in db.AppointmentPayments.AsNoTracking()
                         join a in db.Appointments.AsNoTracking() on p.AppointmentId equals a.Id
                         where p.Status == AppointmentPaymentStatus.Paid && p.PayoutId == null && Done.Contains(a.Status) && !db.Disputes.Any(d => d.AppointmentId == a.Id && d.Status == DisputeStatus.Open)
                         select p.EstablishmentId).Distinct().ToListAsync(ct);

        int payouts = 0, skipped = 0;
        decimal total = 0;
        foreach (var establishmentId in due)
        {
            var result = await PayAsync(admin, establishmentId, ct);
            if (result.IsSuccess)
            {
                payouts++;
                total += result.Value.Amount;
            }
            else
            {
                skipped++;
            }
        }

        return new PayoutRunReport(payouts, total, skipped);
    }

    /// <summary>One pass of the maintenance job: reads the state of payouts still pending at the operator.</summary>
    public async Task<int> CheckPendingAsync(CancellationToken ct)
    {
        var pending = await db.Payouts.Where(p => p.Status == PayoutStatus.Pending).OrderBy(p => p.CreatedAt).Take(100).ToListAsync(ct);
        foreach (var payout in pending)
        {
            var info = await db.Establishments.AsNoTracking().Where(e => e.Id == payout.EstablishmentId).Select(e => new { e.Name, e.OwnerUserId }).FirstOrDefaultAsync(ct);
            await CheckAsync(payout, info?.Name ?? string.Empty, info?.OwnerUserId, ct);
        }

        return pending.Count;
    }

    private async Task CheckAsync(Payout payout, string establishmentName, Guid? ownerId, CancellationToken ct)
    {
        if (payout.Status != PayoutStatus.Pending)
        {
            return;
        }

        PayoutVerification verification;
        try
        {
            verification = await gateway.VerifyAsync(payout.TransactionId, payout.ProviderReference, ct);
        }
        catch (PaymentGatewayException)
        {
            return;
        }

        if (verification.State == PayoutState.Succeeded)
        {
            payout.Status = PayoutStatus.Succeeded;
            payout.CompletedAt = time.GetUtcNow();
            await db.SaveChangesAsync(ct);
            if (ownerId is { } owner)
            {
                var amount = $"{payout.Amount.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ')} {payout.CurrencyCode}";
                var variables = new Dictionary<string, string> { ["etablissement"] = establishmentName, ["montant"] = amount, ["facture"] = string.Empty, ["echeance"] = string.Empty, ["jours"] = string.Empty };
                await notifications.NotifyAsync(owner, "billing.payout_sent", "Reversement effectué",
                    $"Un reversement de {amount} pour « {establishmentName} » a été envoyé sur votre compte de paiement mobile.", null, ct, variables);
            }
        }
        else if (verification.State == PayoutState.Failed)
        {
            await ReleaseAsync(payout, "Le transfert a été refusé par l'opérateur.", ct);
        }
    }

    /// <summary>A payout that failed gives its appointments back, so that the next payout includes them.</summary>
    private async Task ReleaseAsync(Payout payout, string error, CancellationToken ct)
    {
        payout.Status = PayoutStatus.Failed;
        payout.Error = error.Length > 500 ? error[..500] : error;
        payout.CompletedAt = time.GetUtcNow();
        await db.AppointmentPayments.Where(p => p.PayoutId == payout.Id).ExecuteUpdateAsync(set => set.SetProperty(p => p.PayoutId, (Guid?)null), ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<PayoutDto>> ProjectAsync(IQueryable<Payout> query, CancellationToken ct)
    {
        var rows = await query.ToListAsync(ct);
        var ids = rows.Select(r => r.EstablishmentId).Distinct().ToList();
        var names = await db.Establishments.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Name, ct);
        return rows.Select(r => Map(r, names.GetValueOrDefault(r.EstablishmentId, string.Empty))).ToList();
    }

    private static PayoutDto Map(Payout p, string establishmentName) =>
        new(p.Id, p.EstablishmentId, establishmentName, p.Amount, p.CurrencyCode, p.Operator, PhoneNumbers.Mask(p.Account), p.Status.ToString(), p.Error, p.CreatedAt, p.CompletedAt);
}
