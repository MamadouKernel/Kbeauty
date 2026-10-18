using System.Globalization;
using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Billing.Domain;
using KekeBeauty.Modules.Scheduling.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KekeBeauty.Infrastructure.Billing;

public sealed record CollaboratorAccountView(bool Configured, string? Operator, string? AccountMasked, string? AccountName);

public sealed record CollaboratorAccountInput(string? Operator, string? Account, string? AccountName);

/// <summary>What one collaborator is owed by an establishment. <c>PayableCommission</c> is the part the establishment's balance can advance now.</summary>
public sealed record CollaboratorOwedDto(
    Guid UserId,
    string CollaboratorName,
    decimal Commission,
    decimal PayableCommission,
    decimal Tips,
    decimal Payable,
    bool HasAccount,
    string CurrencyCode);

public sealed record CollaboratorPayoutDto(
    Guid Id,
    Guid EstablishmentId,
    string EstablishmentName,
    Guid UserId,
    string CollaboratorName,
    decimal CommissionAmount,
    decimal TipAmount,
    decimal Amount,
    string CurrencyCode,
    string Operator,
    string AccountMasked,
    string Status,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

public sealed record CollaboratorPayoutsView(IReadOnlyList<CollaboratorOwedDto> Owed, IReadOnlyList<CollaboratorPayoutDto> History, decimal AvailableBalance);

/// <summary>What a collaborator is owed in one establishment, as she sees it.</summary>
public sealed record MyOwedDto(Guid EstablishmentId, string EstablishmentName, decimal Commission, decimal Tips);

public sealed record MyPayoutsView(CollaboratorAccountView Account, IReadOnlyList<MyOwedDto> Owed, IReadOnlyList<CollaboratorPayoutDto> History);

public static class CollaboratorPayoutErrors
{
    public static readonly Error NothingDue = new("collaborator_payout.nothing_due", "Rien à verser à cette collaboratrice pour le moment.");
    public static readonly Error NoDestination = new("collaborator_payout.no_destination", "La collaboratrice n'a pas encore renseigné son compte de paiement mobile.");
    public static readonly Error CollaboratorNotFound = new("collaborator.not_found", "Collaboratrice introuvable.");

    public static Error Invalid(string message) => new("validation.failed", message);
}

/// <summary>
/// Pays collaborators by mobile money (Wave and the other operators). A payout adds up the commissions of the services she finished and the
/// tips paid online for her. Tips are the platform's to pay; commissions are advanced against the establishment's balance and deducted from
/// its next payout, so the platform never pays out more than it holds for that establishment.
/// </summary>
public sealed class CollaboratorPayoutService(
    KekeBeautyDbContext db,
    IPayoutGateway gateway,
    PayoutService establishmentPayouts,
    NotificationService notifications,
    TimeProvider time,
    ILogger<CollaboratorPayoutService> logger)
{
    private sealed record CommissionRow(Guid AppointmentId, decimal Amount, bool NeedsSnapshot);

    private sealed record OwedItems(IReadOnlyList<CommissionRow> Commissions, IReadOnlyList<Guid> TipIds, decimal TipTotal);

    // ───────── Collaborator: payout account and earnings ─────────

    public async Task<CollaboratorAccountView> GetAccountAsync(Guid userId, CancellationToken ct)
    {
        var account = await db.CollaboratorPayoutAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == userId, ct);
        return account is null
            ? new CollaboratorAccountView(false, null, null, null)
            : new CollaboratorAccountView(true, account.Operator, PhoneNumbers.Mask(account.Account), account.AccountName);
    }

    public async Task<Result<CollaboratorAccountView>> SaveAccountAsync(Guid userId, CollaboratorAccountInput input, CancellationToken ct)
    {
        var account = PhoneNumbers.ToE164(input.Account);
        var holder = input.AccountName?.Trim();
        if (!PayoutOperators.IsKnown(input.Operator) || account is null || holder is not { Length: >= 2 and <= 100 })
        {
            return Result.Failure<CollaboratorAccountView>(CollaboratorPayoutErrors.Invalid(
                "Indiquez l'opérateur, le numéro de paiement mobile et le nom du titulaire du compte."));
        }

        var row = await db.CollaboratorPayoutAccounts.FirstOrDefaultAsync(a => a.UserId == userId, ct);
        if (row is null)
        {
            db.CollaboratorPayoutAccounts.Add(new CollaboratorPayoutAccount
            {
                UserId = userId,
                Operator = input.Operator!,
                Account = account,
                AccountName = holder,
                UpdatedAt = time.GetUtcNow(),
            });
        }
        else
        {
            row.Operator = input.Operator!;
            row.Account = account;
            row.AccountName = holder;
            row.UpdatedAt = time.GetUtcNow();
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(await GetAccountAsync(userId, ct));
    }

    public async Task<MyPayoutsView> MineAsync(Guid userId, CancellationToken ct)
    {
        var establishmentIds = await EstablishmentsOfAsync(userId, ct);
        var names = await db.Establishments.AsNoTracking().Where(e => establishmentIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Name, ct);
        var owed = new List<MyOwedDto>();
        foreach (var establishmentId in establishmentIds)
        {
            var items = await OwedAsync(establishmentId, userId, ct);
            owed.Add(new MyOwedDto(establishmentId, names.GetValueOrDefault(establishmentId, string.Empty), items.Commissions.Sum(c => c.Amount), items.TipTotal));
        }

        var history = await ProjectAsync(db.CollaboratorPayouts.AsNoTracking().Where(p => p.UserId == userId).OrderByDescending(p => p.CreatedAt).Take(50), ct);
        return new MyPayoutsView(await GetAccountAsync(userId, ct), owed, history);
    }

    // ───────── Establishment owner ─────────

    public async Task<Result<CollaboratorPayoutsView>> ViewAsync(Guid ownerId, Guid establishmentId, CancellationToken ct)
    {
        if (!await PartnerSupport.OwnedBy(db, ownerId, establishmentId).AnyAsync(ct))
        {
            return Result.Failure<CollaboratorPayoutsView>(PartnerErrors.NotFound);
        }

        var available = (await establishmentPayouts.BalanceAsync(establishmentId, ct)).Payable;
        var userIds = await db.Resources.AsNoTracking().Where(r => r.EstablishmentId == establishmentId && r.UserId != null).Select(r => r.UserId!.Value).ToListAsync(ct);
        var former = await db.Appointments.AsNoTracking()
            .Where(a => a.EstablishmentId == establishmentId && a.CollaboratorPayoutId == null && a.CommissionUserId != null)
            .Select(a => a.CommissionUserId!.Value).Distinct().ToListAsync(ct);
        userIds = userIds.Concat(former).Distinct().ToList();

        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);
        var withAccount = (await db.CollaboratorPayoutAccounts.AsNoTracking().Where(a => userIds.Contains(a.UserId)).Select(a => a.UserId).ToListAsync(ct)).ToHashSet();
        var owed = new List<CollaboratorOwedDto>();
        var remaining = available;
        foreach (var userId in userIds)
        {
            var items = await OwedAsync(establishmentId, userId, ct);
            var commission = items.Commissions.Sum(c => c.Amount);
            if (commission == 0 && items.TipTotal == 0)
            {
                continue;
            }

            var fits = Fit(items.Commissions, remaining).Sum(c => c.Amount);
            remaining -= fits;
            owed.Add(new CollaboratorOwedDto(userId, users.GetValueOrDefault(userId, "Collaboratrice"), commission, fits, items.TipTotal, fits + items.TipTotal, withAccount.Contains(userId), "XOF"));
        }

        var history = await ProjectAsync(db.CollaboratorPayouts.AsNoTracking().Where(p => p.EstablishmentId == establishmentId).OrderByDescending(p => p.CreatedAt).Take(50), ct);
        return Result.Success(new CollaboratorPayoutsView(owed, history, available));
    }

    /// <summary>Pays one collaborator what she is owed in this establishment, by mobile money.</summary>
    public async Task<Result<CollaboratorPayoutDto>> PayAsync(Guid ownerId, Guid establishmentId, Guid userId, CancellationToken ct)
    {
        var establishment = await PartnerSupport.OwnedBy(db, ownerId, establishmentId).AsNoTracking().FirstOrDefaultAsync(ct);
        if (establishment is null)
        {
            return Result.Failure<CollaboratorPayoutDto>(PartnerErrors.NotFound);
        }

        var account = await db.CollaboratorPayoutAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == userId, ct);
        if (account is null)
        {
            return Result.Failure<CollaboratorPayoutDto>(CollaboratorPayoutErrors.NoDestination);
        }

        // One money movement per establishment at a time: the balance read below is the one the advance is taken from.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await EstablishmentLock.AcquireAsync(db, establishmentId, ct);
        var owed = await OwedAsync(establishmentId, userId, ct);
        var available = (await establishmentPayouts.BalanceAsync(establishmentId, ct)).Payable;
        var rows = Fit(owed.Commissions, available);
        if (rows.Count == 0 && owed.TipIds.Count == 0)
        {
            return Result.Failure<CollaboratorPayoutDto>(CollaboratorPayoutErrors.NothingDue);
        }

        // Claim first, so that two clicks can never pay the same service or tip twice.
        var transactionId = "KC" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
        var payout = new CollaboratorPayout
        {
            UserId = userId,
            EstablishmentId = establishmentId,
            CommissionAmount = 0,
            TipAmount = 0,
            Amount = 0,
            Operator = account.Operator,
            Account = account.Account,
            AccountName = account.AccountName,
            Provider = gateway.Provider,
            TransactionId = transactionId,
            CreatedBy = ownerId,
            CreatedAt = time.GetUtcNow(),
        };
        var payoutId = payout.Id;
        var appointmentIds = rows.Select(r => r.AppointmentId).ToList();
        foreach (var row in rows.Where(r => r.NeedsSnapshot))
        {
            var amount = row.Amount;
            await db.Appointments.Where(a => a.Id == row.AppointmentId && a.CommissionAmount == null)
                .ExecuteUpdateAsync(set => set.SetProperty(a => a.CommissionAmount, (decimal?)amount).SetProperty(a => a.CommissionUserId, (Guid?)userId), ct);
        }

        var claimedServices = await db.Appointments.Where(a => appointmentIds.Contains(a.Id) && a.CollaboratorPayoutId == null)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.CollaboratorPayoutId, (Guid?)payoutId), ct);
        var tipIds = owed.TipIds;
        var claimedTips = await db.TipPayments.Where(t => tipIds.Contains(t.Id) && t.CollaboratorPayoutId == null && t.Status == TipPaymentStatus.Paid)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.CollaboratorPayoutId, (Guid?)payoutId), ct);
        if (claimedServices + claimedTips == 0)
        {
            return Result.Failure<CollaboratorPayoutDto>(CollaboratorPayoutErrors.NothingDue);
        }

        payout.CommissionAmount = await db.Appointments.Where(a => a.CollaboratorPayoutId == payoutId).SumAsync(a => a.CommissionAmount ?? 0, ct);
        payout.TipAmount = await db.TipPayments.Where(t => t.CollaboratorPayoutId == payoutId).SumAsync(t => t.Amount, ct);
        payout.Amount = payout.CommissionAmount + payout.TipAmount;
        db.CollaboratorPayouts.Add(payout);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        try
        {
            payout.ProviderReference = await gateway.SendAsync(
                new PayoutRequest(transactionId, payout.Operator, payout.AccountName, PhoneNumbers.ToE164(payout.Account)?.TrimStart('+') ?? payout.Account, payout.Amount,
                    $"Gains Keke Beauty — {establishment.Name}"), ct);
            await db.SaveChangesAsync(ct);
        }
        catch (PaymentGatewayException ex)
        {
            logger.LogWarning(ex, "Payout {Payout} to collaborator {User} was refused", payoutId, userId);
            await ReleaseAsync(payout, ex.Message, ct);
            return Result.Failure<CollaboratorPayoutDto>(PayoutErrors.GatewayRefused);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            // Unknown outcome: the transfer may have left. It stays pending and the follow-up job asks the operator.
            logger.LogWarning(ex, "Payout {Payout} to collaborator {User}: no answer from the operator", payoutId, userId);
        }

        await CheckAsync(payout, ct);
        return Result.Success((await ProjectAsync(db.CollaboratorPayouts.AsNoTracking().Where(p => p.Id == payoutId), ct))[0]);
    }

    // ───────── Back-office and follow-up ─────────

    public async Task<IReadOnlyList<CollaboratorPayoutDto>> AllAsync(CancellationToken ct) =>
        await ProjectAsync(db.CollaboratorPayouts.AsNoTracking().OrderByDescending(p => p.CreatedAt).Take(100), ct);

    /// <summary>One pass of the maintenance job: reads the state of payouts still pending at the operator.</summary>
    public async Task<int> CheckPendingAsync(CancellationToken ct)
    {
        var pending = await db.CollaboratorPayouts.Where(p => p.Status == PayoutStatus.Pending).OrderBy(p => p.CreatedAt).Take(100).ToListAsync(ct);
        foreach (var payout in pending)
        {
            await CheckAsync(payout, ct);
        }

        return pending.Count;
    }

    private async Task CheckAsync(CollaboratorPayout payout, CancellationToken ct)
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
            var amount = $"{payout.Amount.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ')} {payout.CurrencyCode}";
            await notifications.NotifyAsync(payout.UserId, "payout.collaborator_sent", "Gains versés",
                $"Un versement de {amount} a été envoyé sur votre compte de paiement mobile.", null, ct);
        }
        else if (verification.State == PayoutState.Failed)
        {
            await ReleaseAsync(payout, "Le transfert a été refusé par l'opérateur.", ct);
        }
    }

    /// <summary>A payout that failed gives its services and tips back, so that the next payout includes them.</summary>
    private async Task ReleaseAsync(CollaboratorPayout payout, string error, CancellationToken ct)
    {
        payout.Status = PayoutStatus.Failed;
        payout.Error = error.Length > 500 ? error[..500] : error;
        payout.CompletedAt = time.GetUtcNow();
        await db.Appointments.Where(a => a.CollaboratorPayoutId == payout.Id).ExecuteUpdateAsync(set => set.SetProperty(a => a.CollaboratorPayoutId, (Guid?)null), ct);
        await db.TipPayments.Where(t => t.CollaboratorPayoutId == payout.Id).ExecuteUpdateAsync(set => set.SetProperty(t => t.CollaboratorPayoutId, (Guid?)null), ct);
        await db.CollaboratorPayouts.Where(c => c.Id == payout.Id).ExecuteUpdateAsync(set => set.SetProperty(c => c.SettledAgainstPayoutId, (Guid?)null), ct);
        await db.SaveChangesAsync(ct);
    }

    // ───────── What is owed ─────────

    private async Task<OwedItems> OwedAsync(Guid establishmentId, Guid userId, CancellationToken ct)
    {
        var services = await (from a in db.Appointments.AsNoTracking()
                              join r in db.Resources.AsNoTracking() on a.ResourceId equals r.Id
                              where a.EstablishmentId == establishmentId && a.CollaboratorPayoutId == null && !a.AwaitingPayment
                                 && (a.Status == AppointmentStatus.Completed || (a.Status == AppointmentStatus.Confirmed && a.FinishedAt != null))
                                 && (a.CommissionUserId == userId || (a.CommissionUserId == null && r.UserId == userId))
                                 && !db.Disputes.Any(d => d.AppointmentId == a.Id && d.Status == DisputeStatus.Open)
                              orderby a.StartsAt
                              select new { a.Id, a.TotalPrice, a.CommissionAmount, r.CommissionPercent }).ToListAsync(ct);
        var commissions = services
            .Select(s => new CommissionRow(s.Id, s.CommissionAmount ?? CommissionService.CommissionOf(s.TotalPrice, s.CommissionPercent), s.CommissionAmount is null))
            .Where(c => c.Amount > 0)
            .ToList();

        var tips = await db.TipPayments.AsNoTracking()
            .Where(t => t.EstablishmentId == establishmentId && t.CollaboratorUserId == userId && t.Status == TipPaymentStatus.Paid && t.CollaboratorPayoutId == null)
            .Select(t => new { t.Id, t.Amount }).ToListAsync(ct);
        return new OwedItems(commissions, tips.Select(t => t.Id).ToList(), tips.Sum(t => t.Amount));
    }

    /// <summary>The oldest services whose commissions the establishment's balance can cover.</summary>
    private static List<CommissionRow> Fit(IReadOnlyList<CommissionRow> rows, decimal available)
    {
        var fitted = new List<CommissionRow>();
        var total = 0m;
        foreach (var row in rows)
        {
            if (total + row.Amount > available)
            {
                break;
            }

            total += row.Amount;
            fitted.Add(row);
        }

        return fitted;
    }

    private async Task<List<Guid>> EstablishmentsOfAsync(Guid userId, CancellationToken ct)
    {
        var fromResources = await db.Resources.AsNoTracking().Where(r => r.UserId == userId).Select(r => r.EstablishmentId).ToListAsync(ct);
        var fromHistory = await db.Appointments.AsNoTracking().Where(a => a.CommissionUserId == userId).Select(a => a.EstablishmentId).Distinct().ToListAsync(ct);
        return fromResources.Concat(fromHistory).Distinct().ToList();
    }

    private async Task<IReadOnlyList<CollaboratorPayoutDto>> ProjectAsync(IQueryable<CollaboratorPayout> query, CancellationToken ct)
    {
        var rows = await query.ToListAsync(ct);
        var establishmentIds = rows.Select(r => r.EstablishmentId).Distinct().ToList();
        var userIds = rows.Select(r => r.UserId).Distinct().ToList();
        var establishments = await db.Establishments.AsNoTracking().Where(e => establishmentIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Name, ct);
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);
        return rows.Select(p => new CollaboratorPayoutDto(
            p.Id,
            p.EstablishmentId,
            establishments.GetValueOrDefault(p.EstablishmentId, string.Empty),
            p.UserId,
            users.GetValueOrDefault(p.UserId, "Collaboratrice"),
            p.CommissionAmount,
            p.TipAmount,
            p.Amount,
            p.CurrencyCode,
            p.Operator,
            PhoneNumbers.Mask(p.Account),
            p.Status.ToString(),
            p.Error,
            p.CreatedAt,
            p.CompletedAt)).ToList();
    }
}

/// <summary>Serialises the money movements of one establishment for the duration of the current database transaction.</summary>
internal static class EstablishmentLock
{
    public static Task<int> AcquireAsync(KekeBeautyDbContext db, Guid establishmentId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({establishmentId.ToString()}, 0))", ct);
}
