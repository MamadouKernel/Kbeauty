using KekeBeauty.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KekeBeauty.Modules.Scheduling.Infrastructure;

public sealed class WaitlistEntryConfiguration : IEntityTypeConfiguration<WaitlistEntry>
{
    public void Configure(EntityTypeBuilder<WaitlistEntry> builder)
    {
        builder.ToTable("waitlist_entries", "scheduling");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);

        // One live entry per client, establishment and day; spent or cancelled entries do not block a new one.
        builder.HasIndex(x => new { x.EstablishmentId, x.ClientUserId, x.Date })
            .IsUnique()
            .HasFilter("status = 'Waiting'");
        builder.HasIndex(x => new { x.EstablishmentId, x.Date, x.Status });
        builder.HasIndex(x => x.ClientUserId);
    }
}

public sealed class LoyaltyProgramConfiguration : IEntityTypeConfiguration<LoyaltyProgram>
{
    public void Configure(EntityTypeBuilder<LoyaltyProgram> builder)
    {
        builder.ToTable("loyalty_programs", "scheduling");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.RewardDescription).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => x.EstablishmentId).IsUnique();
    }
}

public sealed class LoyaltyTransactionConfiguration : IEntityTypeConfiguration<LoyaltyTransaction>
{
    public void Configure(EntityTypeBuilder<LoyaltyTransaction> builder)
    {
        builder.ToTable("loyalty_transactions", "scheduling");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Note).HasMaxLength(200);

        // A visit earns points once, whoever (maintenance job, a page load) records it first.
        builder.HasIndex(x => x.AppointmentId)
            .IsUnique()
            .HasFilter("kind = 'Earn' AND appointment_id IS NOT NULL");
        builder.HasIndex(x => new { x.EstablishmentId, x.ClientUserId });
        builder.HasIndex(x => x.ClientUserId);
    }
}

public sealed class GiftCardConfiguration : IEntityTypeConfiguration<GiftCard>
{
    public void Configure(EntityTypeBuilder<GiftCard> builder)
    {
        builder.ToTable("gift_cards", "scheduling", table =>
        {
            table.HasCheckConstraint("ck_gift_cards_balance", "balance >= 0 AND balance <= initial_amount");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(20).IsRequired();
        builder.Property(x => x.InitialAmount).HasPrecision(12, 2);
        builder.Property(x => x.Balance).HasPrecision(12, 2);
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.RecipientName).HasMaxLength(80).IsRequired();
        builder.Property(x => x.BuyerName).HasMaxLength(80);
        builder.Property(x => x.Message).HasMaxLength(300);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => new { x.EstablishmentId, x.CreatedAt });
        builder.HasIndex(x => x.HolderUserId);
    }
}

public sealed class GiftCardRedemptionConfiguration : IEntityTypeConfiguration<GiftCardRedemption>
{
    public void Configure(EntityTypeBuilder<GiftCardRedemption> builder)
    {
        builder.ToTable("gift_card_redemptions", "scheduling");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Amount).HasPrecision(12, 2);
        builder.HasIndex(x => x.GiftCardId);
        builder.HasOne<GiftCard>().WithMany().HasForeignKey(x => x.GiftCardId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class GroupBookingRequestConfiguration : IEntityTypeConfiguration<GroupBookingRequest>
{
    public void Configure(EntityTypeBuilder<GroupBookingRequest> builder)
    {
        builder.ToTable("group_booking_requests", "scheduling");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.EventType).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Services).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Message).HasMaxLength(1000);
        builder.Property(x => x.Phone).HasMaxLength(20).IsRequired();
        builder.Property(x => x.QuotedPrice).HasPrecision(12, 2);
        builder.Property(x => x.PartnerMessage).HasMaxLength(1000);
        builder.HasIndex(x => new { x.EstablishmentId, x.Status, x.CreatedAt });
        builder.HasIndex(x => x.ClientUserId);
    }
}
