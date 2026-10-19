using KekeBeauty.Modules.Billing.Domain;

namespace KekeBeauty.Infrastructure.Billing;

public sealed record PlanFeaturesDto(
    bool Booking,
    bool Videos,
    int MaxMedia,
    bool Featured,
    bool Stats,
    int MaxVideos = 3,
    int? MaxResources = null,
    int? MaxAppointmentsPerMonth = null,
    int? MaxServices = null,
    IReadOnlyList<string>? Perks = null)
{
    public static PlanFeaturesDto From(PlanFeatures features) =>
        new(features.Booking, features.Videos, features.MaxMedia, features.Featured, features.Stats,
            features.MaxVideos, features.MaxResources, features.MaxAppointmentsPerMonth, features.MaxServices, features.PerksList());

    public PlanFeatures ToFeatures()
    {
        var perks = Perks is null
            ? null
            : string.Join('\n', Perks.Select(p => p?.Trim()).Where(p => !string.IsNullOrEmpty(p)));
        return new PlanFeatures
        {
            Booking = Booking, Videos = Videos, MaxMedia = MaxMedia, Featured = Featured, Stats = Stats,
            MaxVideos = MaxVideos, MaxResources = MaxResources, MaxAppointmentsPerMonth = MaxAppointmentsPerMonth,
            MaxServices = MaxServices, Perks = string.IsNullOrEmpty(perks) ? null : perks,
        };
    }
}

public sealed record PlanDto(
    Guid Id,
    string Code,
    string Name,
    decimal MonthlyPrice,
    decimal AnnualPrice,
    string CurrencyCode,
    PlanFeaturesDto Features,
    bool IsActive,
    int SortOrder,
    string Description = "",
    string? Badge = null,
    int CommitmentMonths = 12,
    int TrialDays = 0,
    int PromoPercent = 0,
    DateTimeOffset? PromoEndsAt = null,
    int PromoPeriods = 1,
    bool PromoActive = false);

public sealed record SubscribeInput(string PlanCode, string Cycle, string? PromoCode = null);

public sealed record PromoCodeDto(
    Guid Id,
    string Code,
    int Percent,
    int Periods,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidUntil,
    int? MaxUses,
    int UsedCount,
    IReadOnlyList<string> PlanCodes,
    bool IsActive);

public sealed record PromoCodeInput(
    string Code,
    int Percent,
    int Periods = 1,
    DateTimeOffset? ValidFrom = null,
    DateTimeOffset? ValidUntil = null,
    int? MaxUses = null,
    IReadOnlyList<string>? PlanCodes = null,
    bool IsActive = true);

/// <summary>What a promotion would give on a plan, shown to the partner before paying. <c>Source</c> is "Plan" or "Code".</summary>
public sealed record PromoCheckDto(bool Valid, int Percent, int Periods, string? Source, decimal OriginalPrice, decimal DiscountedPrice, string? Message);

public sealed record CheckoutDto(Guid SubscriptionId, Guid InvoiceId, string TransactionId, string PaymentUrl);

public sealed record SubscriptionDto(
    Guid Id,
    string PlanCode,
    string PlanName,
    string Cycle,
    decimal UnitPrice,
    string CurrencyCode,
    string Status,
    DateTimeOffset? StartDate,
    DateTimeOffset? CommitmentEndDate,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtCommitmentEnd,
    DateTimeOffset? TrialEndsAt = null,
    string? PendingPlanCode = null,
    string? PendingPlanName = null,
    string? PendingCycle = null,
    decimal? PendingUnitPrice = null,
    DateTimeOffset? PendingChangeAt = null,
    int DiscountPercent = 0,
    int DiscountPeriodsLeft = 0);

/// <summary>The subscription (if any) and what the establishment may actually use right now.</summary>
public sealed record SubscriptionOverview(
    SubscriptionDto? Subscription,
    string EffectivePlanCode,
    PlanFeaturesDto Features,
    IReadOnlyList<string>? TrialEligiblePlanCodes = null);

/// <summary>
/// What changing to another plan would do, shown before the partner confirms: <c>Upgrade</c> takes effect now (the unused part of the
/// paid period is credited), <c>Downgrade</c> at the end of the commitment.
/// </summary>
public sealed record ChangeQuoteDto(
    string Kind,
    string PlanCode,
    string PlanName,
    string Cycle,
    DateTimeOffset EffectiveAt,
    decimal Credit,
    decimal NewPrice,
    decimal AmountDue,
    string CurrencyCode,
    int CommitmentMonths);

/// <summary><c>Applied</c> (nothing to pay), <c>PaymentRequired</c> (a checkout is returned) or <c>Scheduled</c> (for the end of the commitment).</summary>
public sealed record ChangeResultDto(string Outcome, CheckoutDto? Checkout, DateTimeOffset? EffectiveAt);

public sealed record InvoiceDto(
    Guid Id,
    string Number,
    Guid EstablishmentId,
    string EstablishmentName,
    string PlanName,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    DateTimeOffset DueDate,
    string Status,
    DateTimeOffset? PaidAt);

public sealed record PlanUpdate(
    string Name,
    decimal MonthlyPrice,
    decimal AnnualPrice,
    PlanFeaturesDto Features,
    bool IsActive,
    string? Description = null,
    string? Badge = null,
    int CommitmentMonths = 12,
    int? SortOrder = null,
    int TrialDays = 0,
    int PromoPercent = 0,
    DateTimeOffset? PromoEndsAt = null,
    int PromoPeriods = 1);

public sealed record SimulateInput(string Status);
