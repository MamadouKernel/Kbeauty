using System.Text.RegularExpressions;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Billing.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KekeBeauty.Infrastructure.Billing;

/// <summary>The percent off that applies to a subscription, and where it comes from.</summary>
public sealed record PromoResolution(int Percent, int Periods, Guid? CodeId, string? Source)
{
    public static readonly PromoResolution None = new(0, 0, null, null);
}

/// <summary>Promotions: a direct percent on a plan, or a code created by an administrator. They are never combined: the better one wins.</summary>
public sealed partial class PromoService(KekeBeautyDbContext db, TimeProvider time)
{
    public static readonly Error Invalid = new("promo.invalid", "Ce code promo est invalide, expiré ou ne s'applique pas à cette formule.");
    public static readonly Error NotFound = new("promo.not_found", "Code promo introuvable.");
    public static readonly Error Taken = new("promo.taken", "Ce code promo existe déjà.");

    [GeneratedRegex("^[A-Z0-9_-]{3,30}$")]
    private static partial Regex CodePattern();

    public static string Normalize(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>The discount to grant for this plan: its own promotion and/or the code, whichever is higher.</summary>
    public async Task<Result<PromoResolution>> ResolveAsync(Plan plan, string? code, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var fromPlan = plan.PromoActiveAt(now) ? new PromoResolution(plan.PromoPercent, plan.PromoPeriods, null, "Plan") : PromoResolution.None;
        var normalized = Normalize(code);
        if (normalized.Length == 0)
        {
            return Result.Success(fromPlan);
        }

        var promo = await db.PromoCodes.AsNoTracking().FirstOrDefaultAsync(c => c.Code == normalized, ct);
        var usable = promo is { IsActive: true }
            && (promo.ValidFrom is null || promo.ValidFrom <= now)
            && (promo.ValidUntil is null || promo.ValidUntil > now)
            && (promo.MaxUses is null || promo.UsedCount < promo.MaxUses)
            && (promo.PlanCodes.Count == 0 || promo.PlanCodes.Contains(plan.Code));
        if (!usable)
        {
            return Result.Failure<PromoResolution>(Invalid);
        }

        return Result.Success(promo!.Percent > fromPlan.Percent ? new PromoResolution(promo.Percent, promo.Periods, promo.Id, "Code") : fromPlan);
    }

    /// <summary>Uses one of the code's allowed uses; false when another subscription took the last one meanwhile.</summary>
    public async Task<bool> ConsumeAsync(Guid codeId, CancellationToken ct) =>
        await db.PromoCodes.Where(c => c.Id == codeId && (c.MaxUses == null || c.UsedCount < c.MaxUses))
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.UsedCount, c => c.UsedCount + 1), ct) > 0;

    public async Task<Result<PromoCheckDto>> CheckAsync(SubscribeInput input, CancellationToken ct)
    {
        if (!Enum.TryParse<BillingCycle>(input.Cycle, ignoreCase: true, out var cycle) || !Enum.IsDefined(cycle))
        {
            return Result.Failure<PromoCheckDto>(BillingErrors.Invalid("Périodicité inconnue (Monthly ou Annual)."));
        }

        var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Code == input.PlanCode && p.IsActive, ct);
        if (plan is null)
        {
            return Result.Failure<PromoCheckDto>(BillingErrors.PlanNotFound);
        }

        var price = plan.PriceFor(cycle);
        var resolved = await ResolveAsync(plan, input.PromoCode, ct);
        if (resolved.IsFailure)
        {
            return Result.Success(new PromoCheckDto(false, 0, 0, null, price, price, resolved.Error.Message));
        }

        var r = resolved.Value;
        return Result.Success(new PromoCheckDto(r.Percent > 0, r.Percent, r.Periods, r.Source, price, BillingRules.ApplyPercent(price, r.Percent), null));
    }

    public async Task<IReadOnlyList<PromoCodeDto>> ListAsync(CancellationToken ct) =>
        (await db.PromoCodes.AsNoTracking().OrderByDescending(c => c.CreatedAt).Take(500).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<Result<PromoCodeDto>> CreateAsync(Guid adminId, PromoCodeInput input, CancellationToken ct)
    {
        var code = Normalize(input.Code);
        var invalid = await ValidateAsync(code, input, ct);
        if (invalid is not null)
        {
            return Result.Failure<PromoCodeDto>(invalid);
        }

        if (await db.PromoCodes.AnyAsync(c => c.Code == code, ct))
        {
            return Result.Failure<PromoCodeDto>(Taken);
        }

        var promo = new PromoCode { Code = code, CreatedAt = time.GetUtcNow() };
        Apply(promo, input);
        db.PromoCodes.Add(promo);
        Admin.Audit.Add(db, new Admin.AdminContext(adminId, false, null), time, "promo.create", "PromoCode", promo.Id, new { promo.Code, promo.Percent, promo.Periods });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Result.Failure<PromoCodeDto>(Taken);
        }

        return Result.Success(ToDto(promo));
    }

    /// <summary>The code itself never changes (it may already be in customers' hands); its conditions and its state do.</summary>
    public async Task<Result<PromoCodeDto>> UpdateAsync(Guid adminId, Guid id, PromoCodeInput input, CancellationToken ct)
    {
        var promo = await db.PromoCodes.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (promo is null)
        {
            return Result.Failure<PromoCodeDto>(NotFound);
        }

        var invalid = await ValidateAsync(promo.Code, input, ct);
        if (invalid is not null)
        {
            return Result.Failure<PromoCodeDto>(invalid);
        }

        Apply(promo, input);
        Admin.Audit.Add(db, new Admin.AdminContext(adminId, false, null), time, "promo.update", "PromoCode", promo.Id,
            new { promo.Code, promo.Percent, promo.Periods, promo.IsActive });
        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(promo));
    }

    private async Task<Error?> ValidateAsync(string code, PromoCodeInput input, CancellationToken ct)
    {
        if (!CodePattern().IsMatch(code))
        {
            return BillingErrors.Invalid("Le code comporte de 3 à 30 caractères : lettres, chiffres, « - » ou « _ ».");
        }

        if (input.Percent < 1 || input.Percent > BillingRules.MaxPromoPercent)
        {
            return BillingErrors.Invalid($"La remise va de 1 à {BillingRules.MaxPromoPercent} %.");
        }

        if (input.Periods < 1 || input.Periods > BillingRules.MaxPromoPeriods)
        {
            return BillingErrors.Invalid($"La remise s'applique à 1 à {BillingRules.MaxPromoPeriods} factures.");
        }

        if (input.MaxUses is < 1)
        {
            return BillingErrors.Invalid("Le nombre d'utilisations maximal doit être d'au moins 1 (vide = illimité).");
        }

        if (input.ValidFrom is { } from && input.ValidUntil is { } until && until <= from)
        {
            return BillingErrors.Invalid("La fin de validité doit suivre le début.");
        }

        var planCodes = input.PlanCodes ?? [];
        if (planCodes.Count > 0)
        {
            var known = await db.Plans.AsNoTracking().Where(p => planCodes.Contains(p.Code)).CountAsync(ct);
            if (known != planCodes.Distinct().Count())
            {
                return BillingErrors.Invalid("Une des formules indiquées n'existe pas.");
            }
        }

        return null;
    }

    private static void Apply(PromoCode promo, PromoCodeInput input)
    {
        promo.Percent = input.Percent;
        promo.Periods = input.Periods;
        promo.ValidFrom = input.ValidFrom;
        promo.ValidUntil = input.ValidUntil;
        promo.MaxUses = input.MaxUses;
        promo.PlanCodes = (input.PlanCodes ?? []).Distinct().ToList();
        promo.IsActive = input.IsActive;
    }

    private static PromoCodeDto ToDto(PromoCode c) =>
        new(c.Id, c.Code, c.Percent, c.Periods, c.ValidFrom, c.ValidUntil, c.MaxUses, c.UsedCount, c.PlanCodes, c.IsActive);
}
