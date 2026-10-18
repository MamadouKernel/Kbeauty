using KekeBeauty.Modules.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KekeBeauty.Modules.Billing.Infrastructure;

public sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("plans", "billing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(80).IsRequired();
        builder.Property(x => x.MonthlyPrice).HasPrecision(12, 2);
        builder.Property(x => x.AnnualPrice).HasPrecision(12, 2);
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Badge).HasMaxLength(30);
        builder.OwnsOne(x => x.Features, features => features.ToJson("features"));
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

public sealed class PromoCodeConfiguration : IEntityTypeConfiguration<PromoCode>
{
    public void Configure(EntityTypeBuilder<PromoCode> builder)
    {
        builder.ToTable("promo_codes", "billing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(30).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

public sealed class PlanPriceHistoryConfiguration : IEntityTypeConfiguration<PlanPriceHistory>
{
    public void Configure(EntityTypeBuilder<PlanPriceHistory> builder)
    {
        builder.ToTable("plan_price_history", "billing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.MonthlyPrice).HasPrecision(12, 2);
        builder.Property(x => x.AnnualPrice).HasPrecision(12, 2);
        builder.HasOne<Plan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions", "billing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Cycle).HasConversion<string>().HasMaxLength(10);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.UnitPriceSnapshot).HasPrecision(12, 2);
        builder.Property(x => x.PendingUnitPrice).HasPrecision(12, 2);
        builder.Property(x => x.PendingCycle).HasConversion<string>().HasMaxLength(10);
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.HasIndex(x => new { x.EstablishmentId, x.Status });
        builder.HasIndex(x => new { x.Status, x.CurrentPeriodEnd });
        builder.HasOne<Plan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices", "billing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Number).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(12, 2);
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => new { x.SubscriptionId, x.PeriodStart }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.DueDate });
        builder.HasOne<Subscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", "billing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Provider).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ProviderTransactionId).HasMaxLength(60).IsRequired();
        builder.Property(x => x.ProviderReference).HasMaxLength(120);
        builder.Property(x => x.Method).HasConversion<string>().HasMaxLength(15);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(x => x.Amount).HasPrecision(12, 2);
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.RawPayload).HasColumnType("text");
        builder.HasIndex(x => x.ProviderTransactionId).IsUnique();
        builder.HasIndex(x => new { x.Status, x.CreatedAt });
        builder.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.AppointmentPaymentId);
        builder.HasIndex(x => x.TipPaymentId);
    }
}

public sealed class PaymentWebhookEventConfiguration : IEntityTypeConfiguration<PaymentWebhookEvent>
{
    public void Configure(EntityTypeBuilder<PaymentWebhookEvent> builder)
    {
        builder.ToTable("payment_webhook_events", "billing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Provider).HasMaxLength(20).IsRequired();
        builder.Property(x => x.EventId).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Payload).HasColumnType("text");
        builder.HasIndex(x => new { x.Provider, x.EventId }).IsUnique();
    }
}

public sealed class InvoiceCounterConfiguration : IEntityTypeConfiguration<InvoiceCounter>
{
    public void Configure(EntityTypeBuilder<InvoiceCounter> builder)
    {
        builder.ToTable("invoice_counters", "billing");
        builder.HasKey(x => x.Year);
        builder.Property(x => x.Year).ValueGeneratedNever();
    }
}

public sealed class AppointmentPaymentConfiguration : IEntityTypeConfiguration<AppointmentPayment>
{
    public void Configure(EntityTypeBuilder<AppointmentPayment> builder)
    {
        builder.ToTable("appointment_payments", "billing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(15);
        builder.Property(x => x.Amount).HasPrecision(12, 2);
        builder.Property(x => x.PlatformFee).HasPrecision(12, 2);
        builder.Property(x => x.NetAmount).HasPrecision(12, 2);
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.PaymentUrl).HasMaxLength(600);
        builder.Property(x => x.RefundTransactionId).HasMaxLength(60);
        builder.Property(x => x.RefundProviderReference).HasMaxLength(120);
        builder.Property(x => x.RefundError).HasMaxLength(500);
        builder.Property(x => x.RefundAmount).HasPrecision(12, 2);
        builder.Property(x => x.RetainedNet).HasPrecision(12, 2);
        builder.HasIndex(x => x.AppointmentId);
        builder.HasIndex(x => new { x.EstablishmentId, x.Status });
        builder.HasIndex(x => x.PayoutId);
    }
}

public sealed class PayoutConfiguration : IEntityTypeConfiguration<Payout>
{
    public void Configure(EntityTypeBuilder<Payout> builder)
    {
        builder.ToTable("payouts", "billing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Amount).HasPrecision(12, 2);
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Operator).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Account).HasMaxLength(30).IsRequired();
        builder.Property(x => x.AccountName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Provider).HasMaxLength(20).IsRequired();
        builder.Property(x => x.TransactionId).HasMaxLength(60).IsRequired();
        builder.Property(x => x.ProviderReference).HasMaxLength(120);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(x => x.Error).HasMaxLength(500);
        builder.HasIndex(x => x.TransactionId).IsUnique();
        builder.HasIndex(x => new { x.EstablishmentId, x.CreatedAt });
    }
}

public sealed class TipPaymentConfiguration : IEntityTypeConfiguration<TipPayment>
{
    public void Configure(EntityTypeBuilder<TipPayment> builder)
    {
        builder.ToTable("tip_payments", "billing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Amount).HasPrecision(12, 2);
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(x => x.PaymentUrl).HasMaxLength(600);
        builder.HasIndex(x => x.AppointmentId);
        builder.HasIndex(x => new { x.CollaboratorUserId, x.EstablishmentId, x.Status });
        builder.HasIndex(x => x.CollaboratorPayoutId);
    }
}

public sealed class CollaboratorPayoutAccountConfiguration : IEntityTypeConfiguration<CollaboratorPayoutAccount>
{
    public void Configure(EntityTypeBuilder<CollaboratorPayoutAccount> builder)
    {
        builder.ToTable("collaborator_payout_accounts", "billing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Operator).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Account).HasMaxLength(30).IsRequired();
        builder.Property(x => x.AccountName).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.UserId).IsUnique();
    }
}

public sealed class CollaboratorPayoutConfiguration : IEntityTypeConfiguration<CollaboratorPayout>
{
    public void Configure(EntityTypeBuilder<CollaboratorPayout> builder)
    {
        builder.ToTable("collaborator_payouts", "billing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.CommissionAmount).HasPrecision(12, 2);
        builder.Property(x => x.TipAmount).HasPrecision(12, 2);
        builder.Property(x => x.Amount).HasPrecision(12, 2);
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Operator).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Account).HasMaxLength(30).IsRequired();
        builder.Property(x => x.AccountName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Provider).HasMaxLength(20).IsRequired();
        builder.Property(x => x.TransactionId).HasMaxLength(60).IsRequired();
        builder.Property(x => x.ProviderReference).HasMaxLength(120);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(x => x.Error).HasMaxLength(500);
        builder.HasIndex(x => x.TransactionId).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.CreatedAt });
        builder.HasIndex(x => new { x.EstablishmentId, x.CreatedAt });
    }
}
