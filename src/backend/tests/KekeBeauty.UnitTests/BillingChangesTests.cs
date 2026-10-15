using FluentAssertions;
using KekeBeauty.Modules.Billing.Domain;

namespace KekeBeauty.UnitTests;

public sealed class BillingChangesTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    private static Subscription Sub(BillingCycle cycle = BillingCycle.Monthly, decimal price = 5000) =>
        new() { EstablishmentId = Guid.NewGuid(), PlanId = Guid.NewGuid(), Cycle = cycle, UnitPriceSnapshot = price };

    private static Invoice InvoiceFor(Subscription s, DateTimeOffset from) =>
        new()
        {
            SubscriptionId = s.Id,
            Number = "KB-2026-000001",
            Amount = s.UnitPriceSnapshot,
            PeriodStart = from,
            PeriodEnd = BillingRules.PeriodEnd(from, s.Cycle),
            DueDate = from,
        };

    [Theory]
    [InlineData(0, 5000)] // the whole period is still ahead
    [InlineData(15, 2500)] // half of a 30-day period left
    [InlineData(30, 0)] // the period is over
    [InlineData(45, 0)]
    public void The_unused_part_of_the_paid_period_is_credited_rounded_down_to_five(int daysElapsed, int expected)
    {
        var end = Start.AddDays(30);

        BillingRules.ProrataCredit(5000, Start, end, Start.AddDays(daysElapsed)).Should().Be(expected);
    }

    [Fact]
    public void The_credit_is_always_a_multiple_of_five_and_never_more_than_the_price()
    {
        var credit = BillingRules.ProrataCredit(7500, Start, Start.AddDays(31), Start.AddHours(7));

        (credit % 5).Should().Be(0);
        credit.Should().BeInRange(0, 7500);
    }

    [Theory]
    [InlineData(10000, 5000, 5000)]
    [InlineData(10000, 10000, 0)] // the credit covers it: nothing to pay
    [InlineData(10000, 12000, 0)]
    [InlineData(10000, 9950, 100)] // the gateway takes 100 at least
    [InlineData(10000, 2503, 7500)] // rounded up to a multiple of five
    public void What_is_left_to_pay_respects_the_gateway_limits(int newPrice, int credit, int expected)
    {
        BillingRules.AmountDue(newPrice, credit).Should().Be(expected);
    }

    [Theory]
    [InlineData(10000, 0, 10000)]
    [InlineData(10000, 20, 8000)]
    [InlineData(7500, 33, 5025)] // rounded down to a multiple of five
    [InlineData(150, 90, 100)] // never below the gateways' minimum
    [InlineData(100, 50, 100)] // and never above the price
    public void A_percent_discount_is_rounded_down_to_five_within_the_gateway_limits(int price, int percent, int expected)
    {
        BillingRules.ApplyPercent(price, percent).Should().Be(expected);
    }

    [Fact]
    public void Discounted_invoices_use_up_the_remaining_periods()
    {
        var subscription = Sub(price: 10000);
        subscription.DiscountPercent = 20;
        subscription.DiscountPeriodsLeft = 1;

        BillingRules.InvoiceAmount(subscription).Should().Be(8000);

        subscription.DiscountPeriodsLeft = 0;
        BillingRules.InvoiceAmount(subscription).Should().Be(10000);
    }

    [Fact]
    public void Plans_are_compared_by_their_monthly_equivalent()
    {
        BillingRules.MonthlyEquivalent(60000, BillingCycle.Annual).Should().Be(5000);
        BillingRules.MonthlyEquivalent(5000, BillingCycle.Monthly).Should().Be(5000);
    }

    [Fact]
    public void A_downgrade_takes_effect_on_the_first_renewal_at_or_after_the_end_of_the_commitment()
    {
        var subscription = Sub();
        subscription.CurrentPeriodEnd = Start.AddMonths(1);
        subscription.CommitmentEndDate = Start.AddMonths(3);

        BillingRules.ChangeBoundary(subscription).Should().Be(Start.AddMonths(3));

        subscription.CommitmentEndDate = null;
        BillingRules.ChangeBoundary(subscription).Should().Be(Start.AddMonths(1));

        subscription.CommitmentEndDate = Start; // already over: the next renewal
        BillingRules.ChangeBoundary(subscription).Should().Be(Start.AddMonths(1));
    }

    [Fact]
    public void A_trial_starts_its_commitment_when_it_ends_not_when_the_first_invoice_is_paid()
    {
        var subscription = Sub();
        subscription.Status = SubscriptionStatus.Trialing;
        subscription.TrialEndsAt = Start.AddDays(14);
        subscription.CurrentPeriodEnd = subscription.TrialEndsAt;
        subscription.CommitmentMonths = 6;
        var invoice = InvoiceFor(subscription, Start.AddDays(14));

        BillingRules.Activate(subscription, invoice, Start.AddDays(5)); // paid early

        subscription.Status.Should().Be(SubscriptionStatus.Active);
        subscription.CommitmentEndDate.Should().Be(Start.AddDays(14).AddMonths(6));
        subscription.CurrentPeriodEnd.Should().Be(invoice.PeriodEnd);
    }

    [Fact]
    public void A_first_payment_activates_a_new_subscription_from_the_payment_date()
    {
        var subscription = Sub();
        subscription.CommitmentMonths = 3;

        BillingRules.Activate(subscription, InvoiceFor(subscription, Start), Start);

        subscription.Status.Should().Be(SubscriptionStatus.Active);
        subscription.StartDate.Should().Be(Start);
        subscription.CommitmentEndDate.Should().Be(Start.AddMonths(3));
        subscription.CurrentPeriodEnd.Should().Be(Start.AddMonths(1));
    }

    [Fact]
    public void Only_the_next_invoice_extends_a_running_subscription()
    {
        var subscription = Sub();
        subscription.Status = SubscriptionStatus.PastDue;
        subscription.CurrentPeriodEnd = Start;

        BillingRules.Activate(subscription, InvoiceFor(subscription, Start.AddMonths(5)), Start); // an unrelated old invoice
        subscription.CurrentPeriodEnd.Should().Be(Start);
        subscription.Status.Should().Be(SubscriptionStatus.Active);

        BillingRules.Activate(subscription, InvoiceFor(subscription, Start), Start);
        subscription.CurrentPeriodEnd.Should().Be(Start.AddMonths(1));
    }
}
