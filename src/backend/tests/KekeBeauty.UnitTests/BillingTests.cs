using FluentAssertions;
using KekeBeauty.Infrastructure.Billing;
using KekeBeauty.Modules.Billing.Domain;

namespace KekeBeauty.UnitTests;

public sealed class BillingTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 31, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Monthly_period_ends_a_month_later_clamped_to_month_end()
    {
        BillingRules.PeriodEnd(Start, BillingCycle.Monthly).Should().Be(new DateTimeOffset(2026, 2, 28, 10, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Annual_period_and_commitment_are_twelve_months()
    {
        BillingRules.PeriodEnd(Start, BillingCycle.Annual).Should().Be(Start.AddYears(1));
        BillingRules.CommitmentEnd(Start).Should().Be(Start.AddYears(1));
    }

    [Fact]
    public void Invoice_numbers_are_padded_per_year()
    {
        BillingRules.InvoiceNumber(2026, 42).Should().Be("KB-2026-000042");
    }

    [Theory]
    [InlineData(-4, 0, "")]
    [InlineData(-3, 0, "-3")]
    [InlineData(0, 1, "0")]
    [InlineData(8, 0, "-3,0,3,7")]
    [InlineData(8, 15, "")]
    public void Reminders_become_due_once_and_only_once(int dayOffset, int alreadySent, string expectedDays)
    {
        var reminders = BillingRules.DueReminders(Start, Start.AddDays(dayOffset), alreadySent);

        string.Join(',', reminders.Select(r => r.Days)).Should().Be(expectedDays);
    }

    [Fact]
    public void Next_invoice_is_due_three_days_before_period_end_unless_the_plan_is_ending()
    {
        var subscription = new Subscription { EstablishmentId = Guid.NewGuid(), PlanId = Guid.NewGuid(), Cycle = BillingCycle.Monthly, UnitPriceSnapshot = 1, CurrentPeriodEnd = Start, CommitmentEndDate = Start.AddMonths(6) };

        BillingRules.NextInvoiceDue(subscription, Start.AddDays(-4)).Should().BeFalse();
        BillingRules.NextInvoiceDue(subscription, Start.AddDays(-3)).Should().BeTrue();

        subscription.CancelAtCommitmentEnd = true;
        BillingRules.NextInvoiceDue(subscription, Start).Should().BeTrue("the commitment runs past this period");

        subscription.CommitmentEndDate = Start;
        BillingRules.NextInvoiceDue(subscription, Start).Should().BeFalse("the cancelled plan stops at this period end");
    }

    [Fact]
    public void CinetPay_token_is_a_stable_hmac_over_the_ordered_fields()
    {
        var fields = new Dictionary<string, string> { ["cpm_site_id"] = "1", ["cpm_trans_id"] = "KB1", ["cpm_amount"] = "15000" };

        var token = CinetPayGateway.ComputeToken("secret", fields);

        token.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]+$");
        CinetPayGateway.ComputeToken("secret", fields).Should().Be(token);
        CinetPayGateway.ComputeToken("other", fields).Should().NotBe(token);
        CinetPayGateway.ComputeToken("secret", new Dictionary<string, string>(fields) { ["cpm_amount"] = "1" }).Should().NotBe(token);
    }

    [Theory]
    [InlineData("OM", PaymentMethod.OrangeMoney)]
    [InlineData("wave", PaymentMethod.Wave)]
    [InlineData("VISA", PaymentMethod.Card)]
    [InlineData("???", PaymentMethod.Other)]
    [InlineData(null, PaymentMethod.Other)]
    public void CinetPay_methods_are_mapped(string? code, PaymentMethod expected) =>
        CinetPayGateway.MapMethod(code).Should().Be(expected);

    [Fact]
    public void Invoice_pdf_is_a_well_formed_document_with_the_invoice_data()
    {
        var invoice = new InvoiceDto(Guid.NewGuid(), "KB-2026-000007", Guid.NewGuid(), @"Salon (Éclat) \ test", "Premium", 15000, "XOF", Start, Start.AddMonths(1), Start, "Open", null);

        var pdf = InvoicePdf.Build(invoice, "Awa Koné");
        var text = System.Text.Encoding.Latin1.GetString(pdf);

        text.Should().StartWith("%PDF-1.4").And.EndWith("%%EOF" + "\n");
        text.Should().Contain("KB-2026-000007").And.Contain("15 000 XOF").And.Contain(@"Salon \(Éclat\) \\ test");
        var xref = int.Parse(text[(text.LastIndexOf("startxref", StringComparison.Ordinal) + 10)..].Split('\n')[0]);
        text[xref..].Should().StartWith("xref");
    }

    [Fact]
    public void Appointment_and_payout_receipt_pdfs_are_well_formed_documents()
    {
        var apptPdf = InvoicePdf.BuildAppointmentReceipt(
            "RC-APPT-001", "Salon Éclat", "Awa Koné", "Fatou",
            Start, [("Tresses royales", 12000m), ("Soin kératine", 5000m)],
            17000m, 1000m, "XOF", "Espèces sur place", "REF-123");
        var apptText = System.Text.Encoding.Latin1.GetString(apptPdf);
        apptText.Should().StartWith("%PDF-1.4").And.EndWith("%%EOF\n");
        apptText.Should().Contain("RC-APPT-001").And.Contain("Tresses royales").And.Contain("18 000 XOF");

        var payoutDto = new PayoutDto(
            Guid.NewGuid(), Guid.NewGuid(), "Salon Éclat", 45000m, "XOF",
            "wave-cote-divoire", "+22507****89", "Succeeded", null, Start, Start.AddHours(1));
        var payoutPdf = InvoicePdf.BuildPayoutReceipt(payoutDto, "Salon Éclat");
        var payoutText = System.Text.Encoding.Latin1.GetString(payoutPdf);
        payoutText.Should().StartWith("%PDF-1.4").And.EndWith("%%EOF\n");
        payoutText.Should().Contain("KB-PAY-").And.Contain("45 000 XOF").And.Contain("wave-cote-divoire");
    }

    [Fact]
    public void Service_promotion_and_perks_compute_effective_price_and_list()
    {
        var svc = new KekeBeauty.Modules.Catalog.Domain.Service
        {
            EstablishmentId = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            Name = "Forfait Mariée",
            Price = 25000m,
            DurationMinutes = 120,
            PromoPercent = 20,
            PromoEndsAt = Start.AddDays(5),
            Perks = "Coiffure complète\nMaquillage\nManucure",
        };

        svc.HasActivePromo(Start).Should().BeTrue();
        svc.EffectivePrice(Start).Should().Be(20000m);
        svc.HasActivePromo(Start.AddDays(6)).Should().BeFalse();
        svc.EffectivePrice(Start.AddDays(6)).Should().Be(25000m);
        svc.PerksList().Should().Equal("Coiffure complète", "Maquillage", "Manucure");

        var features = new PlanFeatures
        {
            MaxServices = 15,
            Perks = "Support VIP\nBadge vérifié",
        };
        var copy = features.Copy();
        copy.MaxServices.Should().Be(15);
        copy.PerksList().Should().Equal("Support VIP", "Badge vérifié");
    }
}
