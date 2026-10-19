using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Billing.Domain;
using KekeBeauty.Modules.Catalog.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KekeBeauty.Infrastructure.Billing;

public static class BillingErrors
{
    public static readonly Error PlanNotFound = new("plan.not_found", "Plan introuvable.");
    public static readonly Error NotPayable = new("plan.not_payable", "Cette formule n'est pas payante : rien à souscrire.");
    public static readonly Error NotPublished = new("establishment.not_published", "L'établissement doit être publié avant de souscrire un abonnement.");
    public static readonly Error AlreadyActive = new("subscription.already_active", "Cet établissement a déjà un abonnement en cours.");
    public static readonly Error SubscriptionNotFound = new("subscription.not_found", "Abonnement introuvable.");
    public static readonly Error InvoiceNotFound = new("invoice.not_found", "Facture introuvable.");
    public static readonly Error NotPayableInvoice = new("invoice.not_payable", "Cette facture n'est pas à payer.");
    public static readonly Error AlreadyCancelled = new("subscription.already_ended", "Cet abonnement est déjà terminé.");
    public static readonly Error GatewayUnavailable = new("payment.gateway_error", "Le service de paiement est momentanément indisponible. Réessayez dans quelques minutes.");
    public static readonly Error TrialNotOffered = new("plan.trial_not_offered", "Cette formule ne propose pas de période d'essai.");
    public static readonly Error TrialAlreadyUsed = new("subscription.trial_used", "Cet établissement a déjà profité de l'essai gratuit de cette formule.");
    public static readonly Error NotTrialing = new("subscription.not_trialing", "Cet établissement n'est pas en période d'essai.");
    public static readonly Error NothingToChange = new("subscription.no_change", "Choisissez une autre formule ou une autre périodicité que l'abonnement actuel.");
    public static readonly Error NotChangeable = new("subscription.not_changeable", "Le changement de formule n'est possible que sur un abonnement actif et à jour.");
    public static readonly Error NoScheduledChange = new("subscription.no_scheduled_change", "Aucun changement n'est programmé.");
    public static readonly Error FeatureRequired = new("plan.feature_required", "Cette fonctionnalité fait partie de la formule Premium.");

    public static Error Invalid(string message) => new("validation.failed", message);
}

/// <summary>Partner side of billing: plans, checkout, invoices and cancellation (PRD BIL-01 to BIL-07).</summary>
public sealed partial class BillingService(
    KekeBeautyDbContext db,
    IPaymentGateway gateway,
    EntitlementService entitlements,
    PromoService promos,
    TimeProvider time,
    IOptions<PaymentsOptions> payments)
{
    public async Task<IReadOnlyList<PlanDto>> PlansAsync(bool includeInactive, CancellationToken ct) =>
        (await db.Plans.AsNoTracking().Where(p => includeInactive || p.IsActive).OrderBy(p => p.SortOrder).ToListAsync(ct))
        .Select(ToDto).ToList();

    public async Task<Result<SubscriptionOverview>> OverviewAsync(Guid userId, Guid establishmentId, CancellationToken ct)
    {
        if (!await PartnerSupport.OwnedBy(db, userId, establishmentId).AnyAsync(ct))
        {
            return Result.Failure<SubscriptionOverview>(PartnerErrors.NotFound);
        }

        var (code, features) = await entitlements.EffectiveAsync(establishmentId, ct);
        var current = await (
            from s in db.Subscriptions.AsNoTracking()
            where s.EstablishmentId == establishmentId
                && s.Status != SubscriptionStatus.Cancelled && s.Status != SubscriptionStatus.Expired
            join p in db.Plans.AsNoTracking() on s.PlanId equals p.Id
            orderby s.CreatedAt descending
            select new { Subscription = s, Plan = p })
            .FirstOrDefaultAsync(ct);
        var pendingPlan = current?.Subscription.PendingPlanId is { } pendingId
            ? await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pendingId, ct)
            : null;

        // The free trial of a plan is offered once per establishment, and only to one that has no running subscription.
        var usedTrials = await db.Subscriptions.AsNoTracking()
            .Where(s => s.EstablishmentId == establishmentId && s.TrialEndsAt != null).Select(s => s.PlanId).ToListAsync(ct);
        var eligible = current?.Subscription.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue or SubscriptionStatus.Trialing
            ? []
            : await db.Plans.AsNoTracking().Where(p => p.IsActive && p.TrialDays > 0 && !usedTrials.Contains(p.Id)).Select(p => p.Code).ToListAsync(ct);

        return Result.Success(new SubscriptionOverview(
            current is null ? null : ToDto(current.Subscription, current.Plan, pendingPlan),
            code,
            PlanFeaturesDto.From(features),
            eligible));
    }

    public async Task<Result<CheckoutDto>> SubscribeAsync(Guid userId, Guid establishmentId, SubscribeInput input, CancellationToken ct)
    {
        var establishment = await PartnerSupport.OwnedBy(db, userId, establishmentId).AsNoTracking().FirstOrDefaultAsync(ct);
        if (establishment is null)
        {
            return Result.Failure<CheckoutDto>(PartnerErrors.NotFound);
        }

        if (establishment.Status != EstablishmentStatus.Active)
        {
            return Result.Failure<CheckoutDto>(BillingErrors.NotPublished);
        }

        if (!Enum.TryParse<BillingCycle>(input.Cycle, ignoreCase: true, out var cycle) || !Enum.IsDefined(cycle))
        {
            return Result.Failure<CheckoutDto>(BillingErrors.Invalid("Périodicité inconnue (Monthly ou Annual)."));
        }

        var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Code == input.PlanCode && p.IsActive, ct);
        if (plan is null)
        {
            return Result.Failure<CheckoutDto>(BillingErrors.PlanNotFound);
        }

        var price = plan.PriceFor(cycle);
        if (price <= 0)
        {
            return Result.Failure<CheckoutDto>(BillingErrors.NotPayable);
        }

        var existing = await db.Subscriptions.Where(s => s.EstablishmentId == establishmentId
            && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue || s.Status == SubscriptionStatus.PendingPayment
                || s.Status == SubscriptionStatus.Trialing)).ToListAsync(ct);
        if (existing.Any(s => s.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue or SubscriptionStatus.Trialing))
        {
            return Result.Failure<CheckoutDto>(BillingErrors.AlreadyActive);
        }

        var promo = await promos.ResolveAsync(plan, input.PromoCode, ct);
        if (promo.IsFailure)
        {
            return Result.Failure<CheckoutDto>(promo.Error);
        }

        var discount = promo.Value;
        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (discount.CodeId is { } codeId && !await promos.ConsumeAsync(codeId, ct))
        {
            return Result.Failure<CheckoutDto>(PromoService.Invalid); // the last allowed use was taken meanwhile
        }

        // A checkout that was never paid is replaced by the new one.
        foreach (var abandoned in existing)
        {
            await EndPendingAsync(abandoned, ct);
        }

        var subscription = new Subscription
        {
            EstablishmentId = establishmentId,
            PlanId = plan.Id,
            Cycle = cycle,
            UnitPriceSnapshot = price,
            CurrencyCode = plan.CurrencyCode,
            CommitmentMonths = plan.CommitmentMonths,
            DiscountPercent = discount.Percent,
            DiscountPeriodsLeft = Math.Max(0, discount.Periods - 1), // the first invoice is discounted below
            PromoCodeId = discount.CodeId,
            CreatedAt = now,
        };
        db.Subscriptions.Add(subscription);
        var invoice = new Invoice
        {
            SubscriptionId = subscription.Id,
            Number = await InvoiceNumbers.NextAsync(db, now.Year, ct),
            Amount = BillingRules.ApplyPercent(price, discount.Percent),
            CurrencyCode = plan.CurrencyCode,
            PeriodStart = now,
            PeriodEnd = BillingRules.PeriodEnd(now, cycle),
            DueDate = now,
            CreatedAt = now,
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return await StartPaymentAsync(subscription, invoice, establishment, plan.Name, ct);
    }

    public async Task<Result<CheckoutDto>> PayInvoiceAsync(Guid userId, Guid invoiceId, CancellationToken ct)
    {
        var found = await (
            from i in db.Invoices.AsNoTracking()
            where i.Id == invoiceId
            join s in db.Subscriptions.AsNoTracking() on i.SubscriptionId equals s.Id
            join e in db.Establishments.AsNoTracking() on s.EstablishmentId equals e.Id
            join p in db.Plans.AsNoTracking() on s.PlanId equals p.Id
            where e.OwnerUserId == userId
            select new { Invoice = i, Subscription = s, Establishment = e, PlanName = p.Name })
            .FirstOrDefaultAsync(ct);
        if (found is null)
        {
            return Result.Failure<CheckoutDto>(BillingErrors.InvoiceNotFound);
        }

        if (found.Invoice.Status is not (InvoiceStatus.Open or InvoiceStatus.Overdue))
        {
            return Result.Failure<CheckoutDto>(BillingErrors.NotPayableInvoice);
        }

        return await StartPaymentAsync(found.Subscription, found.Invoice, found.Establishment, found.PlanName, ct);
    }

    /// <summary>Cancellation takes effect at the end of the commitment (PRD BIL-06); an unpaid checkout is simply dropped.</summary>
    public async Task<Result> CancelAsync(Guid userId, Guid subscriptionId, CancellationToken ct)
    {
        var subscription = await (
            from s in db.Subscriptions
            where s.Id == subscriptionId && db.Establishments.Any(e => e.Id == s.EstablishmentId && e.OwnerUserId == userId)
            select s).FirstOrDefaultAsync(ct);
        if (subscription is null)
        {
            return Result.Failure(BillingErrors.SubscriptionNotFound);
        }

        switch (subscription.Status)
        {
            case SubscriptionStatus.PendingPayment:
                await EndPendingAsync(subscription, ct);
                await db.SaveChangesAsync(ct);
                break;
            case SubscriptionStatus.Trialing:
                // Ending the trial early is final: nothing was paid, the trial of this plan is not offered again.
                subscription.Status = SubscriptionStatus.Cancelled;
                await db.Invoices.Where(i => i.SubscriptionId == subscription.Id && i.Status == InvoiceStatus.Open)
                    .ExecuteUpdateAsync(set => set.SetProperty(i => i.Status, InvoiceStatus.Void), ct);
                await db.SaveChangesAsync(ct);
                await entitlements.SyncAsync(subscription.EstablishmentId, ct);
                break;
            case SubscriptionStatus.Active or SubscriptionStatus.PastDue:
                subscription.CancelAtCommitmentEnd = true;
                SubscriptionChanges.Clear(subscription);
                await db.SaveChangesAsync(ct);
                break;
            default:
                return Result.Failure(BillingErrors.AlreadyCancelled);
        }

        return Result.Success();
    }

    public async Task<IReadOnlyList<InvoiceDto>> InvoicesAsync(Guid userId, CancellationToken ct) =>
        await (
            from i in db.Invoices.AsNoTracking()
            join s in db.Subscriptions.AsNoTracking() on i.SubscriptionId equals s.Id
            join e in db.Establishments.AsNoTracking() on s.EstablishmentId equals e.Id
            join p in db.Plans.AsNoTracking() on s.PlanId equals p.Id
            where e.OwnerUserId == userId && i.Status != InvoiceStatus.Void
            orderby i.CreatedAt descending
            select new InvoiceDto(
                i.Id, i.Number, e.Id, e.Name, p.Name, i.Amount, i.CurrencyCode, i.PeriodStart, i.PeriodEnd, i.DueDate,
                i.Status.ToString(), i.PaidAt))
            .Take(200)
            .ToListAsync(ct);

    public async Task<Result<(byte[] Content, string FileName)>> InvoicePdfAsync(Guid userId, Guid invoiceId, CancellationToken ct)
    {
        var invoice = (await InvoicesAsync(userId, ct)).FirstOrDefault(i => i.Id == invoiceId);
        if (invoice is null)
        {
            return Result.Failure<(byte[], string)>(BillingErrors.InvoiceNotFound);
        }

        var owner = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.FirstName + " " + u.LastName).FirstAsync(ct);
        return Result.Success((InvoicePdf.Build(invoice, owner), $"{invoice.Number}.pdf"));
    }

    // ───────── Admin ─────────

    public async Task<Result<PlanDto>> UpdatePlanAsync(Guid adminId, Guid planId, PlanUpdate input, CancellationToken ct)
    {
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == planId, ct);
        if (plan is null)
        {
            return Result.Failure<PlanDto>(BillingErrors.PlanNotFound);
        }

        var invalid = ValidatePlan(input);
        if (invalid is not null)
        {
            return Result.Failure<PlanDto>(invalid);
        }

        var name = input.Name.Trim();
        if (plan.Code == EntitlementService.FreePlanCode && (input.MonthlyPrice != 0 || input.AnnualPrice != 0 || !input.IsActive))
        {
            return Result.Failure<PlanDto>(BillingErrors.Invalid("La formule Gratuite reste gratuite et active."));
        }

        var priceChanged = plan.MonthlyPrice != input.MonthlyPrice || plan.AnnualPrice != input.AnnualPrice;
        plan.Name = name;
        plan.MonthlyPrice = input.MonthlyPrice;
        plan.AnnualPrice = input.AnnualPrice;
        plan.IsActive = input.IsActive;
        plan.Features = input.Features.ToFeatures();
        plan.Description = input.Description?.Trim() ?? string.Empty;
        plan.Badge = string.IsNullOrWhiteSpace(input.Badge) ? null : input.Badge.Trim();
        plan.CommitmentMonths = input.CommitmentMonths;
        plan.TrialDays = input.TrialDays;
        plan.PromoPercent = input.PromoPercent;
        plan.PromoEndsAt = input.PromoEndsAt;
        plan.PromoPeriods = input.PromoPeriods;
        if (input.SortOrder is { } order)
        {
            plan.SortOrder = order;
        }

        if (priceChanged)
        {
            // Existing subscriptions keep their snapshot; only new ones and renewals use the new tariff (PRD BIL-03).
            db.PlanPriceHistory.Add(new PlanPriceHistory
            {
                PlanId = plan.Id,
                MonthlyPrice = input.MonthlyPrice,
                AnnualPrice = input.AnnualPrice,
                ChangedBy = adminId,
                ChangedAt = time.GetUtcNow(),
            });
        }

        Admin.Audit.Add(db, new Admin.AdminContext(adminId, false, null), time, "plan.update", "Plan", plan.Id,
            new { plan.Code, input.MonthlyPrice, input.AnnualPrice, input.IsActive, priceChanged });
        await db.SaveChangesAsync(ct);
        await SyncPlanSubscribersAsync(plan.Id, ct);
        return Result.Success(ToDto(plan));
    }

    /// <summary>Creates a new plan: its code comes from the name, and it starts with no subscribers.</summary>
    public async Task<Result<PlanDto>> CreatePlanAsync(Guid adminId, PlanUpdate input, CancellationToken ct)
    {
        var invalid = ValidatePlan(input);
        if (invalid is not null)
        {
            return Result.Failure<PlanDto>(invalid);
        }

        var name = input.Name.Trim();
        var baseCode = new string(name.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => char.IsAsciiLetterOrDigit(c) || c == ' ' || c == '-').ToArray()).Trim().Replace(' ', '-');
        baseCode = baseCode.Length == 0 ? "formule" : baseCode[..Math.Min(baseCode.Length, 24)];
        var code = baseCode;
        for (var i = 2; await db.Plans.AnyAsync(p => p.Code == code, ct); i++)
        {
            code = $"{baseCode}-{i}";
        }

        var plan = new Plan
        {
            Code = code,
            Name = name,
            MonthlyPrice = input.MonthlyPrice,
            AnnualPrice = input.AnnualPrice,
            Features = input.Features.ToFeatures(),
            IsActive = input.IsActive,
            Description = input.Description?.Trim() ?? string.Empty,
            Badge = string.IsNullOrWhiteSpace(input.Badge) ? null : input.Badge.Trim(),
            CommitmentMonths = input.CommitmentMonths,
            TrialDays = input.TrialDays,
            PromoPercent = input.PromoPercent,
            PromoEndsAt = input.PromoEndsAt,
            PromoPeriods = input.PromoPeriods,
            SortOrder = input.SortOrder ?? (await db.Plans.MaxAsync(p => (int?)p.SortOrder, ct) ?? 0) + 1,
        };
        db.Plans.Add(plan);
        db.PlanPriceHistory.Add(new PlanPriceHistory
        {
            PlanId = plan.Id,
            MonthlyPrice = plan.MonthlyPrice,
            AnnualPrice = plan.AnnualPrice,
            ChangedBy = adminId,
            ChangedAt = time.GetUtcNow(),
        });
        Admin.Audit.Add(db, new Admin.AdminContext(adminId, false, null), time, "plan.create", "Plan", plan.Id,
            new { plan.Code, plan.Name, plan.MonthlyPrice, plan.AnnualPrice });
        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(plan));
    }

    /// <summary>Deletes a plan that has never been subscribed to (the free plan is permanent).</summary>
    public async Task<Result> DeletePlanAsync(Guid adminId, Guid planId, CancellationToken ct)
    {
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == planId, ct);
        if (plan is null)
        {
            return Result.Failure(BillingErrors.PlanNotFound);
        }

        if (plan.Code == EntitlementService.FreePlanCode)
        {
            return Result.Failure(BillingErrors.Invalid("La formule Gratuite ne peut pas être supprimée."));
        }

        if (await db.Subscriptions.AnyAsync(s => s.PlanId == planId || s.PendingPlanId == planId, ct))
        {
            return Result.Failure(new Error("plan.in_use", "Impossible de supprimer une formule déjà souscrite ; désactivez-la plutôt."));
        }

        db.Plans.Remove(plan);
        Admin.Audit.Add(db, new Admin.AdminContext(adminId, false, null), time, "plan.delete", "Plan", plan.Id,
            new { plan.Code, plan.Name });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static Error? ValidatePlan(PlanUpdate input)
    {
        var name = input.Name?.Trim() ?? string.Empty;
        var features = input.Features;
        if (name.Length is < 2 or > 80 || input.MonthlyPrice < 0 || input.AnnualPrice < 0)
        {
            return BillingErrors.Invalid("Nom (2 à 80 caractères) et prix positifs attendus.");
        }

        if ((input.Description?.Length ?? 0) > 300 || (input.Badge?.Trim().Length ?? 0) > 30)
        {
            return BillingErrors.Invalid("Description de 300 caractères et étiquette de 30 caractères au plus.");
        }

        if (input.TrialDays is < 0 or > BillingRules.MaxTrialDays)
        {
            return BillingErrors.Invalid($"L'essai gratuit dure de 0 à {BillingRules.MaxTrialDays} jours (0 = pas d'essai).");
        }

        if (input.PromoPercent is < 0 or > BillingRules.MaxPromoPercent || input.PromoPeriods is < 1 or > BillingRules.MaxPromoPeriods)
        {
            return BillingErrors.Invalid($"La promotion va de 0 à {BillingRules.MaxPromoPercent} % sur 1 à {BillingRules.MaxPromoPeriods} factures (0 % = pas de promotion).");
        }

        if (input.CommitmentMonths is < 1 or > 36)
        {
            return BillingErrors.Invalid("L'engagement doit durer de 1 à 36 mois.");
        }

        if (features.MaxMedia is < 0 or > 500 || features.MaxVideos is < 0 or > 50
            || features.MaxResources is < 1 or > Scheduling.ResourceService.MaxResources
            || features.MaxAppointmentsPerMonth is < 1 or > 100000
            || features.MaxServices is < 1 or > 500)
        {
            return BillingErrors.Invalid($"Limites hors bornes : photos 0 à 500, vidéos 0 à 50, prestations 1 à 500, ressources 1 à {Scheduling.ResourceService.MaxResources}, rendez-vous par mois 1 à 100 000 (vide = illimité).");
        }

        var perks = features.Perks?.Select(p => p?.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToList() ?? [];
        if (perks.Count > 12 || perks.Any(p => p!.Length > 120))
        {
            return BillingErrors.Invalid("Au plus 12 avantages par formule, de 120 caractères maximum chacun.");
        }

        // CinetPay only takes multiples of 5, at least 100: refuse a tariff that could never be paid.
        if ((input.MonthlyPrice > 0 && !CinetPayGateway.IsAcceptedAmount(input.MonthlyPrice))
            || (input.AnnualPrice > 0 && !CinetPayGateway.IsAcceptedAmount(input.AnnualPrice)))
        {
            return BillingErrors.Invalid("Les prix doivent être des multiples de 5 d'au moins 100 (contrainte de la passerelle de paiement).");
        }

        return null;
    }

    // ───────── Helpers ─────────

    private async Task SyncPlanSubscribersAsync(Guid planId, CancellationToken ct)
    {
        var ids = await db.Subscriptions.AsNoTracking().Where(s => s.PlanId == planId && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing))
            .Select(s => s.EstablishmentId).Distinct().ToListAsync(ct);
        foreach (var id in ids)
        {
            await entitlements.SyncAsync(id, ct);
        }
    }

    private async Task EndPendingAsync(Subscription subscription, CancellationToken ct)
    {
        subscription.Status = SubscriptionStatus.Cancelled;
        await db.Invoices.Where(i => i.SubscriptionId == subscription.Id && i.Status == InvoiceStatus.Open)
            .ExecuteUpdateAsync(set => set.SetProperty(i => i.Status, InvoiceStatus.Void), ct);
    }

    private async Task<Result<CheckoutDto>> StartPaymentAsync(
        Subscription subscription,
        Invoice invoice,
        Establishment establishment,
        string planName,
        CancellationToken ct)
    {
        var transactionId = "KB" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
        var payment = new Payment
        {
            InvoiceId = invoice.Id,
            Provider = gateway.Provider,
            ProviderTransactionId = transactionId,
            Amount = invoice.Amount,
            CurrencyCode = invoice.CurrencyCode,
            CreatedAt = time.GetUtcNow(),
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);

        var owner = await db.Users.AsNoTracking().Where(u => u.Id == establishment.OwnerUserId)
            .Select(u => new { u.FirstName, u.LastName, u.Email }).FirstAsync(ct);
        var settings = payments.Value;
        try
        {
            var session = await gateway.CreateAsync(
                new PaymentRequest(
                    transactionId,
                    invoice.Amount,
                    invoice.CurrencyCode,
                    $"Abonnement {planName} — {establishment.Name} ({invoice.Number})",
                    $"{owner.FirstName} {owner.LastName}".Trim(),
                    owner.Email,
                    establishment.CustomerServicePhone,
                    $"{settings.WebBaseUrl.TrimEnd('/')}/pro/etablissements/{establishment.Id}/abonnement?tx={transactionId}",
                    $"{settings.ApiBaseUrl.TrimEnd('/')}/api/v1/webhooks/payments/{gateway.Provider}"),
                ct);
            if (session.ProviderReference is not null)
            {
                payment.ProviderReference = session.ProviderReference;
                await db.SaveChangesAsync(ct);
            }

            return Result.Success(new CheckoutDto(subscription.Id, invoice.Id, transactionId, session.PaymentUrl));
        }
        catch (PaymentGatewayException)
        {
            payment.Status = PaymentStatus.Failed;
            payment.RawPayload = "gateway unavailable at creation";
            await db.SaveChangesAsync(ct);
            return Result.Failure<CheckoutDto>(BillingErrors.GatewayUnavailable);
        }
    }

    private PlanDto ToDto(Plan p) =>
        new(p.Id, p.Code, p.Name, p.MonthlyPrice, p.AnnualPrice, p.CurrencyCode, PlanFeaturesDto.From(p.Features), p.IsActive, p.SortOrder,
            p.Description, p.Badge, p.CommitmentMonths, p.TrialDays, p.PromoPercent, p.PromoEndsAt, p.PromoPeriods, p.PromoActiveAt(time.GetUtcNow()));

    private static SubscriptionDto ToDto(Subscription s, Plan p, Plan? pending = null) =>
        new(s.Id, p.Code, p.Name, s.Cycle.ToString(), s.UnitPriceSnapshot, s.CurrencyCode, s.Status.ToString(),
            s.StartDate, s.CommitmentEndDate, s.CurrentPeriodEnd, s.CancelAtCommitmentEnd,
            s.TrialEndsAt, pending?.Code, pending?.Name, s.PendingCycle?.ToString(), s.PendingUnitPrice, s.PendingChangeAt,
            s.DiscountPercent, s.DiscountPeriodsLeft);
}

/// <summary>Sequential invoice numbers per year, assigned inside the caller's transaction so that none is ever skipped.</summary>
public static class InvoiceNumbers
{
    public static async Task<string> NextAsync(KekeBeautyDbContext db, int year, CancellationToken ct)
    {
        // INSERT ... RETURNING cannot be composed over (no SingleAsync/LIMIT): materialise it as is.
        var rows = await db.Database
            .SqlQuery<int>($"""
                INSERT INTO billing.invoice_counters (year, last) VALUES ({year}, 1)
                ON CONFLICT (year) DO UPDATE SET last = billing.invoice_counters.last + 1
                RETURNING last AS "Value"
                """)
            .ToListAsync(ct);
        return BillingRules.InvoiceNumber(year, rows.Single());
    }
}
