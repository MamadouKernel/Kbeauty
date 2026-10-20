using KekeBeauty.SharedKernel;

namespace KekeBeauty.Modules.Billing.Domain;

public enum BillingCycle
{
    Monthly,
    Annual,
}

public enum SubscriptionStatus
{
    PendingPayment,
    Active,
    PastDue,
    Suspended,
    Cancelled,
    Expired,

    /// <summary>Free trial: the features of the plan are available until <see cref="Subscription.TrialEndsAt"/>, nothing is paid yet.</summary>
    Trialing,
}

public enum InvoiceStatus
{
    Open,
    Paid,
    Overdue,
    Void,
}

public enum PaymentStatus
{
    Pending,
    Succeeded,
    Failed,
}

public enum PaymentMethod
{
    Wave,
    OrangeMoney,
    Mtn,
    Moov,
    Card,
    Other,
}

/// <summary>Feature flags carried by a plan (PRD BIL-01): behaviour follows these flags, never a plan name.</summary>
public sealed class PlanFeatures
{
    public bool Booking { get; set; }
    public bool Videos { get; set; }
    public int MaxMedia { get; set; }
    public bool Featured { get; set; }
    public bool Stats { get; set; }

    /// <summary>Videos on the page when <see cref="Videos"/> is on.</summary>
    public int MaxVideos { get; set; } = 3;

    /// <summary>Active resources (employees, chairs...) bookable online; null = no limit beyond the platform maximum.</summary>
    public int? MaxResources { get; set; }

    /// <summary>Appointments accepted per calendar month; null = unlimited.</summary>
    public int? MaxAppointmentsPerMonth { get; set; }

    /// <summary>Services in the establishment's catalog; null = unlimited.</summary>
    public int? MaxServices { get; set; }

    /// <summary>Additional commercial perks displayed on the pricing card, separated by newlines.</summary>
    public string? Perks { get; set; }

    public IReadOnlyList<string> PerksList() =>
        string.IsNullOrWhiteSpace(Perks)
            ? []
            : Perks.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static PlanFeatures FreeDefaults() => new() { Booking = false, Videos = false, MaxMedia = 5, MaxVideos = 0, Featured = false, Stats = false };

    public PlanFeatures Copy() => new()
    {
        Booking = Booking,
        Videos = Videos,
        MaxMedia = MaxMedia,
        Featured = Featured,
        Stats = Stats,
        MaxVideos = MaxVideos,
        MaxResources = MaxResources,
        MaxAppointmentsPerMonth = MaxAppointmentsPerMonth,
        MaxServices = MaxServices,
        Perks = Perks,
    };
}

public sealed class Plan : Entity
{
    public required string Code { get; init; }
    public required string Name { get; set; }
    public decimal MonthlyPrice { get; set; }
    public decimal AnnualPrice { get; set; }
    public string CurrencyCode { get; set; } = "XOF";
    public PlanFeatures Features { get; set; } = PlanFeatures.FreeDefaults();
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    /// <summary>Short text shown under the name on the pricing page.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Optional ribbon such as "Populaire".</summary>
    public string? Badge { get; set; }

    /// <summary>Months a new subscription commits to (copied into the subscription when it starts).</summary>
    public int CommitmentMonths { get; set; } = BillingRules.DefaultCommitmentMonths;

    /// <summary>Length of the free trial offered once per establishment; 0 = no trial.</summary>
    public int TrialDays { get; set; }

    /// <summary>Direct promotion: percent off the price while <see cref="PromoEndsAt"/> has not passed; 0 = none.</summary>
    public int PromoPercent { get; set; }

    public DateTimeOffset? PromoEndsAt { get; set; }

    /// <summary>How many invoices the promotion applies to (1 = the first one only).</summary>
    public int PromoPeriods { get; set; } = 1;

    public bool PromoActiveAt(DateTimeOffset now) => PromoPercent > 0 && (PromoEndsAt is null || PromoEndsAt > now);

    public decimal PriceFor(BillingCycle cycle) => cycle == BillingCycle.Monthly ? MonthlyPrice : AnnualPrice;
}

/// <summary>Audit of every tariff change (PRD §8.2 <c>plan_price_history</c>).</summary>
public sealed class PlanPriceHistory : Entity
{
    public required Guid PlanId { get; init; }
    public decimal MonthlyPrice { get; init; }
    public decimal AnnualPrice { get; init; }
    public Guid? ChangedBy { get; init; }
    public DateTimeOffset ChangedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class Subscription : Entity
{
    public required Guid EstablishmentId { get; init; }
    public required Guid PlanId { get; set; }
    public required BillingCycle Cycle { get; set; }

    /// <summary>Price copied at subscription time: later tariff changes only reach new subscriptions (PRD BIL-03).</summary>
    public required decimal UnitPriceSnapshot { get; set; }

    public string CurrencyCode { get; init; } = "XOF";
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.PendingPayment;
    public DateTimeOffset? StartDate { get; set; }
    public DateTimeOffset? CommitmentEndDate { get; set; }
    public DateTimeOffset? CurrentPeriodEnd { get; set; }

    /// <summary>Commitment of the plan at subscription time: later changes of the plan do not move this contract's end date.</summary>
    public int CommitmentMonths { get; set; } = BillingRules.DefaultCommitmentMonths;

    /// <summary>End of the free trial (set only for subscriptions that started with one).</summary>
    public DateTimeOffset? TrialEndsAt { get; set; }

    /// <summary>Percent off granted at subscription time (promotion or code), applied to the next <see cref="DiscountPeriodsLeft"/> invoices.</summary>
    public int DiscountPercent { get; set; }

    public int DiscountPeriodsLeft { get; set; }

    public Guid? PromoCodeId { get; set; }

    /// <summary>For an upgrade: the subscription this one replaces once its first invoice is paid.</summary>
    public Guid? ReplacesSubscriptionId { get; set; }

    /// <summary>
    /// A change of plan chosen for the end of the commitment (a downgrade): the subscription switches to it on <see cref="PendingChangeAt"/>
    /// and renews at <see cref="PendingUnitPrice"/>, the price frozen when the change was chosen.
    /// </summary>
    public Guid? PendingPlanId { get; set; }

    public BillingCycle? PendingCycle { get; set; }
    public decimal? PendingUnitPrice { get; set; }
    public DateTimeOffset? PendingChangeAt { get; set; }
    public bool CancelAtCommitmentEnd { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class Invoice : Entity
{
    public required Guid SubscriptionId { get; init; }

    /// <summary><c>KB-YYYY-NNNNNN</c>, sequential per year without gaps (PRD BIL-07).</summary>
    public required string Number { get; init; }

    public required decimal Amount { get; init; }
    public string CurrencyCode { get; init; } = "XOF";
    public required DateTimeOffset PeriodStart { get; init; }
    public required DateTimeOffset PeriodEnd { get; init; }
    public required DateTimeOffset DueDate { get; init; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Open;
    public DateTimeOffset? PaidAt { get; set; }

    /// <summary>Bit mask of the reminders already sent (see <see cref="BillingRules"/>).</summary>
    public int RemindersSent { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class Payment : Entity
{
    /// <summary>The subscription invoice this payment settles; null for the payment of an appointment (<see cref="AppointmentPaymentId"/>).</summary>
    public Guid? InvoiceId { get; init; }

    public Guid? AppointmentPaymentId { get; init; }

    /// <summary>The tip this payment settles, when it is one (see <see cref="TipPayment"/>).</summary>
    public Guid? TipPaymentId { get; init; }

    public required string Provider { get; init; }
    public required string ProviderTransactionId { get; init; }

    /// <summary>The provider's own identifier, when it differs from ours (WiniPayer invoice uuid).</summary>
    public string? ProviderReference { get; set; }

    public PaymentMethod Method { get; set; } = PaymentMethod.Other;
    public required decimal Amount { get; init; }
    public string CurrencyCode { get; init; } = "XOF";
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    /// <summary>Raw provider answer kept for dispute and audit purposes (never shown to users).</summary>
    public string? RawPayload { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>One processed notification per provider event: the unique index makes replays harmless (PRD §10.2).</summary>
public sealed class PaymentWebhookEvent : Entity
{
    public required string Provider { get; init; }
    public required string EventId { get; init; }
    public string? Payload { get; init; }
    public DateTimeOffset ReceivedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
}

public sealed class InvoiceCounter
{
    public int Year { get; init; }
    public int Last { get; set; }
}

/// <summary>Pure billing rules: period arithmetic, commitment and reminder schedule.</summary>
public static class BillingRules
{
    public const int DefaultCommitmentMonths = 12;
    public const int InvoiceLeadDays = 3;
    public const int DefaultGraceDays = 7;

    /// <summary>Reminder offsets in days from the due date (PRD BIL-04) and their bit in <see cref="Invoice.RemindersSent"/>.</summary>
    public static readonly IReadOnlyList<(int Days, int Bit)> Reminders = [(-3, 1), (0, 2), (3, 4), (7, 8)];

    public static DateTimeOffset PeriodEnd(DateTimeOffset start, BillingCycle cycle) =>
        cycle == BillingCycle.Monthly ? start.AddMonths(1) : start.AddYears(1);

    public static DateTimeOffset CommitmentEnd(DateTimeOffset start, int months = DefaultCommitmentMonths) => start.AddMonths(months);

    public const int MaxTrialDays = 90;
    public const int MaxPromoPercent = 90;
    public const int MaxPromoPeriods = 36;

    /// <summary>Price after a percent discount, rounded down to a multiple of 5 and never below the gateways' minimum of 100.</summary>
    public static decimal ApplyPercent(decimal price, int percent)
    {
        if (percent <= 0 || price <= 0)
        {
            return price;
        }

        var discounted = Math.Floor(price * (100 - percent) / 100m / 5m) * 5m;
        return Math.Min(price, Math.Max(100m, discounted));
    }

    /// <summary>What the next invoice costs: the discounted price while discounted periods remain.</summary>
    public static decimal InvoiceAmount(Subscription subscription) =>
        subscription.DiscountPeriodsLeft > 0 ? ApplyPercent(subscription.UnitPriceSnapshot, subscription.DiscountPercent) : subscription.UnitPriceSnapshot;

    public static DateTimeOffset PeriodStartOf(DateTimeOffset periodEnd, BillingCycle cycle) =>
        cycle == BillingCycle.Monthly ? periodEnd.AddMonths(-1) : periodEnd.AddYears(-1);

    /// <summary>What a plan costs per month whatever the rhythm of payment: used to tell an upgrade from a downgrade.</summary>
    public static decimal MonthlyEquivalent(decimal price, BillingCycle cycle) => cycle == BillingCycle.Annual ? price / 12m : price;

    /// <summary>
    /// The unused part of the period already paid, as a credit towards an upgrade (rounded down to a multiple of 5, the payment
    /// gateways' step). Nothing once the period is over.
    /// </summary>
    public static decimal ProrataCredit(decimal unitPrice, DateTimeOffset periodStart, DateTimeOffset periodEnd, DateTimeOffset now)
    {
        var total = (periodEnd - periodStart).TotalSeconds;
        var left = (periodEnd - now).TotalSeconds;
        if (total <= 0 || left <= 0)
        {
            return 0;
        }

        var fraction = (decimal)Math.Min(1d, left / total);
        return Math.Floor(unitPrice * fraction / 5m) * 5m;
    }

    /// <summary>What is left to pay after the credit: 0 when the credit covers it, otherwise a multiple of 5 of at least 100 (gateway limits).</summary>
    public static decimal AmountDue(decimal newPrice, decimal credit)
    {
        var due = newPrice - credit;
        return due <= 0 ? 0 : Math.Max(100m, Math.Ceiling(due / 5m) * 5m);
    }

    /// <summary>
    /// First renewal date at or after the end of the commitment: a change chosen "for the end of the commitment" takes effect then,
    /// on a date where a new period starts.
    /// </summary>
    public static DateTimeOffset ChangeBoundary(Subscription subscription)
    {
        var boundary = subscription.CurrentPeriodEnd ?? throw new InvalidOperationException("The subscription has no running period.");
        if (subscription.CommitmentEndDate is { } commitmentEnd)
        {
            for (var i = 0; i < 48 && boundary < commitmentEnd; i++)
            {
                boundary = PeriodEnd(boundary, subscription.Cycle);
            }
        }

        return boundary;
    }

    /// <summary>
    /// A paid invoice moves the subscription on: a new one becomes active (a trial starts its commitment when it ends), a running one
    /// extends its period when the invoice is the next one.
    /// </summary>
    public static void Activate(Subscription subscription, Invoice invoice, DateTimeOffset now)
    {
        if (subscription.Status == SubscriptionStatus.PendingPayment)
        {
            subscription.Status = SubscriptionStatus.Active;
            subscription.StartDate = now;
            subscription.CommitmentEndDate = CommitmentEnd(now, subscription.CommitmentMonths);
            subscription.CurrentPeriodEnd = PeriodEnd(now, subscription.Cycle);
        }
        else if (subscription.Status == SubscriptionStatus.Trialing)
        {
            // The first paid period starts when the trial ends, whenever the invoice is paid: the days of trial are never lost.
            var paidFrom = subscription.TrialEndsAt ?? now;
            subscription.Status = SubscriptionStatus.Active;
            subscription.CommitmentEndDate = CommitmentEnd(paidFrom, subscription.CommitmentMonths);
            subscription.CurrentPeriodEnd = invoice.PeriodEnd;
        }
        else if (subscription.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue)
        {
            if (invoice.PeriodStart == subscription.CurrentPeriodEnd)
            {
                subscription.CurrentPeriodEnd = invoice.PeriodEnd;
            }

            subscription.Status = SubscriptionStatus.Active;
        }
    }

    public static string InvoiceNumber(int year, int sequence) => $"KB-{year}-{sequence:000000}";

    /// <summary>Reminders whose moment has come and that were not sent yet.</summary>
    public static IReadOnlyList<(int Days, int Bit)> DueReminders(DateTimeOffset dueDate, DateTimeOffset now, int alreadySent) =>
        Reminders.Where(r => (alreadySent & r.Bit) == 0 && now >= dueDate.AddDays(r.Days)).ToList();

    /// <summary>Whether a next invoice must be created: close to the end of the paid period and the plan is not ending.</summary>
    public static bool NextInvoiceDue(Subscription subscription, DateTimeOffset now) =>
        subscription.CurrentPeriodEnd is { } periodEnd
        && now >= periodEnd.AddDays(-InvoiceLeadDays)
        && !(subscription.CancelAtCommitmentEnd && subscription.CommitmentEndDate is { } end && periodEnd >= end);
}

/// <summary>A promotion code created by an administrator: a percent off the next invoices of the plans it applies to.</summary>
public sealed class PromoCode : Entity
{
    /// <summary>Upper case letters, digits, "-" and "_" (3 to 30).</summary>
    public required string Code { get; init; }

    public int Percent { get; set; }

    /// <summary>How many invoices the discount lasts (1 = the first one only).</summary>
    public int Periods { get; set; } = 1;

    public DateTimeOffset? ValidFrom { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }

    /// <summary>Total number of subscriptions that may use it; null = unlimited.</summary>
    public int? MaxUses { get; set; }

    public int UsedCount { get; set; }

    /// <summary>Codes of the plans it applies to; empty = every paid plan.</summary>
    public List<string> PlanCodes { get; set; } = [];

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
