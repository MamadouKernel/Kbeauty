using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Billing.Domain;
using KekeBeauty.Modules.Catalog.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Billing;

/// <summary>Free trial and change of plan during a commitment (PRD BIL-02, BIL-03).</summary>
public sealed partial class BillingService
{
    /// <summary>The establishment tries a plan for free: its features are available until the trial ends, no payment is asked.</summary>
    public async Task<Result<SubscriptionDto>> StartTrialAsync(Guid userId, Guid establishmentId, SubscribeInput input, CancellationToken ct)
    {
        var establishment = await PartnerSupport.OwnedBy(db, userId, establishmentId).AsNoTracking().FirstOrDefaultAsync(ct);
        if (establishment is null)
        {
            return Result.Failure<SubscriptionDto>(PartnerErrors.NotFound);
        }

        if (establishment.Status != EstablishmentStatus.Active)
        {
            return Result.Failure<SubscriptionDto>(BillingErrors.NotPublished);
        }

        if (!Enum.TryParse<BillingCycle>(input.Cycle, ignoreCase: true, out var cycle) || !Enum.IsDefined(cycle))
        {
            return Result.Failure<SubscriptionDto>(BillingErrors.Invalid("Périodicité inconnue (Monthly ou Annual)."));
        }

        var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Code == input.PlanCode && p.IsActive, ct);
        if (plan is null)
        {
            return Result.Failure<SubscriptionDto>(BillingErrors.PlanNotFound);
        }

        var price = plan.PriceFor(cycle);
        if (plan.TrialDays <= 0 || price <= 0)
        {
            return Result.Failure<SubscriptionDto>(BillingErrors.TrialNotOffered);
        }

        var existing = await db.Subscriptions.Where(s => s.EstablishmentId == establishmentId
            && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue || s.Status == SubscriptionStatus.PendingPayment
                || s.Status == SubscriptionStatus.Trialing)).ToListAsync(ct);
        if (existing.Any(s => s.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue or SubscriptionStatus.Trialing))
        {
            return Result.Failure<SubscriptionDto>(BillingErrors.AlreadyActive);
        }

        if (await db.Subscriptions.AnyAsync(s => s.EstablishmentId == establishmentId && s.PlanId == plan.Id && s.TrialEndsAt != null, ct))
        {
            return Result.Failure<SubscriptionDto>(BillingErrors.TrialAlreadyUsed);
        }

        var promo = await promos.ResolveAsync(plan, input.PromoCode, ct);
        if (promo.IsFailure)
        {
            return Result.Failure<SubscriptionDto>(promo.Error);
        }

        var discount = promo.Value;
        if (discount.CodeId is { } codeId && !await promos.ConsumeAsync(codeId, ct))
        {
            return Result.Failure<SubscriptionDto>(PromoService.Invalid);
        }

        var now = time.GetUtcNow();
        foreach (var abandoned in existing)
        {
            await EndPendingAsync(abandoned, ct);
        }

        var trialEnd = now.AddDays(plan.TrialDays);
        var subscription = new Subscription
        {
            EstablishmentId = establishmentId,
            PlanId = plan.Id,
            Cycle = cycle,
            UnitPriceSnapshot = price,
            CurrencyCode = plan.CurrencyCode,
            CommitmentMonths = plan.CommitmentMonths,
            DiscountPercent = discount.Percent,
            DiscountPeriodsLeft = discount.Periods, // the first paid invoice, raised when the trial ends, is the first discounted one
            PromoCodeId = discount.CodeId,
            Status = SubscriptionStatus.Trialing,
            StartDate = now,
            TrialEndsAt = trialEnd,
            CurrentPeriodEnd = trialEnd, // the first paid period starts when the trial ends; its invoice is created ahead like any renewal
            CreatedAt = now,
        };
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync(ct);
        await entitlements.SyncAsync(establishmentId, ct);
        return Result.Success(ToDto(subscription, plan));
    }

    /// <summary>Pays the first invoice before the trial ends: the paid period still starts at the end of the trial, no day is lost.</summary>
    public async Task<Result<CheckoutDto>> PayTrialAsync(Guid userId, Guid subscriptionId, CancellationToken ct)
    {
        var found = await (
            from s in db.Subscriptions
            where s.Id == subscriptionId
            join e in db.Establishments.AsNoTracking() on s.EstablishmentId equals e.Id
            join p in db.Plans.AsNoTracking() on s.PlanId equals p.Id
            where e.OwnerUserId == userId
            select new { Subscription = s, Establishment = e, PlanName = p.Name })
            .FirstOrDefaultAsync(ct);
        if (found is null)
        {
            return Result.Failure<CheckoutDto>(BillingErrors.SubscriptionNotFound);
        }

        var subscription = found.Subscription;
        if (subscription.Status != SubscriptionStatus.Trialing || subscription.TrialEndsAt is not { } trialEnd)
        {
            return Result.Failure<CheckoutDto>(BillingErrors.NotTrialing);
        }

        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.SubscriptionId == subscription.Id && i.PeriodStart == trialEnd, ct);
        if (invoice is null)
        {
            var now = time.GetUtcNow();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            invoice = new Invoice
            {
                SubscriptionId = subscription.Id,
                Number = await InvoiceNumbers.NextAsync(db, now.Year, ct),
                Amount = BillingRules.InvoiceAmount(subscription),
                CurrencyCode = subscription.CurrencyCode,
                PeriodStart = trialEnd,
                PeriodEnd = BillingRules.PeriodEnd(trialEnd, subscription.Cycle),
                DueDate = trialEnd,
                CreatedAt = now,
            };
            db.Invoices.Add(invoice);
            if (subscription.DiscountPeriodsLeft > 0)
            {
                subscription.DiscountPeriodsLeft--;
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        if (invoice.Status is not (InvoiceStatus.Open or InvoiceStatus.Overdue))
        {
            return Result.Failure<CheckoutDto>(BillingErrors.NotPayableInvoice);
        }

        return await StartPaymentAsync(subscription, invoice, found.Establishment, found.PlanName, ct);
    }

    /// <summary>What the change would do, so that the partner sees the credit and the amount before confirming.</summary>
    public async Task<Result<ChangeQuoteDto>> QuoteChangeAsync(Guid userId, Guid subscriptionId, SubscribeInput input, CancellationToken ct)
    {
        var prepared = await PrepareChangeAsync(userId, subscriptionId, input, ct);
        return prepared.IsFailure ? Result.Failure<ChangeQuoteDto>(prepared.Error) : Result.Success(prepared.Value.Quote);
    }

    public async Task<Result<ChangeResultDto>> ChangePlanAsync(Guid userId, Guid subscriptionId, SubscribeInput input, CancellationToken ct)
    {
        var prepared = await PrepareChangeAsync(userId, subscriptionId, input, ct);
        if (prepared.IsFailure)
        {
            return Result.Failure<ChangeResultDto>(prepared.Error);
        }

        var (quote, subscription, plan, cycle, establishment) = prepared.Value;
        var now = time.GetUtcNow();

        if (quote.Kind == "Downgrade")
        {
            // A cheaper plan takes over at the end of the commitment: what was paid keeps its full value until then.
            subscription.PendingPlanId = plan.Id;
            subscription.PendingCycle = cycle;
            subscription.PendingUnitPrice = quote.NewPrice;
            subscription.PendingChangeAt = quote.EffectiveAt;
            subscription.CancelAtCommitmentEnd = false;
            await db.SaveChangesAsync(ct);
            return Result.Success(new ChangeResultDto("Scheduled", null, quote.EffectiveAt));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var abandoned in await db.Subscriptions
                     .Where(s => s.EstablishmentId == subscription.EstablishmentId && s.Status == SubscriptionStatus.PendingPayment).ToListAsync(ct))
        {
            await EndPendingAsync(abandoned, ct);
        }

        if (quote.AmountDue == 0)
        {
            // The unused part of the paid period covers the new plan for a first period: it takes effect at once.
            subscription.PlanId = plan.Id;
            subscription.Cycle = cycle;
            subscription.UnitPriceSnapshot = quote.NewPrice;
            subscription.CommitmentMonths = plan.CommitmentMonths;
            subscription.CommitmentEndDate = BillingRules.CommitmentEnd(now, plan.CommitmentMonths);
            subscription.CurrentPeriodEnd = BillingRules.PeriodEnd(now, cycle);
            subscription.CancelAtCommitmentEnd = false;
            SubscriptionChanges.Clear(subscription);
            await db.Invoices.Where(i => i.SubscriptionId == subscription.Id && i.Status == InvoiceStatus.Open)
                .ExecuteUpdateAsync(set => set.SetProperty(i => i.Status, InvoiceStatus.Void), ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await entitlements.SyncAsync(subscription.EstablishmentId, ct);
            return Result.Success(new ChangeResultDto("Applied", null, now));
        }

        // The new subscription replaces the running one when its first invoice (the price minus the credit) is paid.
        var replacement = new Subscription
        {
            EstablishmentId = subscription.EstablishmentId,
            PlanId = plan.Id,
            Cycle = cycle,
            UnitPriceSnapshot = quote.NewPrice,
            CurrencyCode = plan.CurrencyCode,
            CommitmentMonths = plan.CommitmentMonths,
            ReplacesSubscriptionId = subscription.Id,
            CreatedAt = now,
        };
        db.Subscriptions.Add(replacement);
        var invoice = new Invoice
        {
            SubscriptionId = replacement.Id,
            Number = await InvoiceNumbers.NextAsync(db, now.Year, ct),
            Amount = quote.AmountDue,
            CurrencyCode = plan.CurrencyCode,
            PeriodStart = now,
            PeriodEnd = BillingRules.PeriodEnd(now, cycle),
            DueDate = now,
            CreatedAt = now,
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var checkout = await StartPaymentAsync(replacement, invoice, establishment, plan.Name, ct);
        return checkout.IsSuccess
            ? Result.Success(new ChangeResultDto("PaymentRequired", checkout.Value, now))
            : Result.Failure<ChangeResultDto>(checkout.Error);
    }

    public async Task<Result> CancelScheduledChangeAsync(Guid userId, Guid subscriptionId, CancellationToken ct)
    {
        var subscription = await (
            from s in db.Subscriptions
            where s.Id == subscriptionId && db.Establishments.Any(e => e.Id == s.EstablishmentId && e.OwnerUserId == userId)
            select s).FirstOrDefaultAsync(ct);
        if (subscription is null)
        {
            return Result.Failure(BillingErrors.SubscriptionNotFound);
        }

        if (subscription.PendingPlanId is null)
        {
            return Result.Failure(BillingErrors.NoScheduledChange);
        }

        SubscriptionChanges.Clear(subscription);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Result<PreparedChange>> PrepareChangeAsync(Guid userId, Guid subscriptionId, SubscribeInput input, CancellationToken ct)
    {
        var subscription = await (
            from s in db.Subscriptions
            where s.Id == subscriptionId && db.Establishments.Any(e => e.Id == s.EstablishmentId && e.OwnerUserId == userId)
            select s).FirstOrDefaultAsync(ct);
        if (subscription is null)
        {
            return Result.Failure<PreparedChange>(BillingErrors.SubscriptionNotFound);
        }

        if (subscription.Status != SubscriptionStatus.Active || subscription.CurrentPeriodEnd is not { } periodEnd)
        {
            return Result.Failure<PreparedChange>(BillingErrors.NotChangeable);
        }

        if (!Enum.TryParse<BillingCycle>(input.Cycle, ignoreCase: true, out var cycle) || !Enum.IsDefined(cycle))
        {
            return Result.Failure<PreparedChange>(BillingErrors.Invalid("Périodicité inconnue (Monthly ou Annual)."));
        }

        var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Code == input.PlanCode && p.IsActive, ct);
        if (plan is null)
        {
            return Result.Failure<PreparedChange>(BillingErrors.PlanNotFound);
        }

        var price = plan.PriceFor(cycle);
        if (price <= 0)
        {
            return Result.Failure<PreparedChange>(BillingErrors.NotPayable);
        }

        if (plan.Id == subscription.PlanId && cycle == subscription.Cycle)
        {
            return Result.Failure<PreparedChange>(BillingErrors.NothingToChange);
        }

        var establishment = await db.Establishments.AsNoTracking().FirstAsync(e => e.Id == subscription.EstablishmentId, ct);
        var now = time.GetUtcNow();
        var isUpgrade = BillingRules.MonthlyEquivalent(price, cycle) > BillingRules.MonthlyEquivalent(subscription.UnitPriceSnapshot, subscription.Cycle);
        ChangeQuoteDto quote;
        if (isUpgrade)
        {
            // The credit is worth what was really paid for this period (a discounted invoice is worth less than the plan's price).
            var paid = await db.Invoices.AsNoTracking()
                .Where(i => i.SubscriptionId == subscription.Id && i.PeriodEnd == periodEnd && i.Status == InvoiceStatus.Paid)
                .Select(i => (decimal?)i.Amount).FirstOrDefaultAsync(ct) ?? subscription.UnitPriceSnapshot;
            var credit = BillingRules.ProrataCredit(paid, BillingRules.PeriodStartOf(periodEnd, subscription.Cycle), periodEnd, now);
            quote = new ChangeQuoteDto("Upgrade", plan.Code, plan.Name, cycle.ToString(), now, credit, price,
                BillingRules.AmountDue(price, credit), plan.CurrencyCode, plan.CommitmentMonths);
        }
        else
        {
            quote = new ChangeQuoteDto("Downgrade", plan.Code, plan.Name, cycle.ToString(), BillingRules.ChangeBoundary(subscription), 0, price,
                0, plan.CurrencyCode, plan.CommitmentMonths);
        }

        return Result.Success(new PreparedChange(quote, subscription, plan, cycle, establishment));
    }

    private sealed record PreparedChange(ChangeQuoteDto Quote, Subscription Subscription, Plan Plan, BillingCycle Cycle, Establishment Establishment);
}

/// <summary>Operations on a subscription that other billing parts (payment processing, maintenance) also need.</summary>
public static class SubscriptionChanges
{
    /// <summary>Forgets a change scheduled for the end of the commitment.</summary>
    public static void Clear(Subscription subscription)
    {
        subscription.PendingPlanId = null;
        subscription.PendingCycle = null;
        subscription.PendingUnitPrice = null;
        subscription.PendingChangeAt = null;
    }

    /// <summary>The upgrade has been paid: the subscription it replaces ends now (its remaining value was credited on the first invoice).</summary>
    public static async Task CancelReplacedAsync(KekeBeautyDbContext db, Guid replacedId, CancellationToken ct)
    {
        var replaced = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == replacedId, ct);
        if (replaced is null || replaced.Status is SubscriptionStatus.Cancelled or SubscriptionStatus.Expired)
        {
            return;
        }

        replaced.Status = SubscriptionStatus.Cancelled;
        Clear(replaced);
        await db.Invoices.Where(i => i.SubscriptionId == replacedId && (i.Status == InvoiceStatus.Open || i.Status == InvoiceStatus.Overdue))
            .ExecuteUpdateAsync(set => set.SetProperty(i => i.Status, InvoiceStatus.Void), ct);
    }
}
