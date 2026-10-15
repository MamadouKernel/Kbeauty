using KekeBeauty.Modules.Onboarding.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KekeBeauty.Modules.Onboarding.Infrastructure;

public sealed class KycIdentityConfiguration : IEntityTypeConfiguration<KycIdentity>
{
    public void Configure(EntityTypeBuilder<KycIdentity> builder)
    {
        builder.ToTable("kyc_identities", "onboarding");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.HolderName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.DocumentType).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.DocumentNumber).HasMaxLength(50).IsRequired();
        builder.Property(x => x.FrontKey).HasMaxLength(300);
        builder.Property(x => x.BackKey).HasMaxLength(300);
        builder.Property(x => x.SelfieKey).HasMaxLength(300);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.ReviewComment).HasMaxLength(1000);
        builder.Ignore(x => x.IsEditable);
        builder.Ignore(x => x.HasRequiredDocuments);
        builder.HasIndex(x => x.UserId).IsUnique();
        builder.HasIndex(x => new { x.DocumentType, x.DocumentNumber });
    }
}

public sealed class EstablishmentReviewConfiguration : IEntityTypeConfiguration<EstablishmentReview>
{
    public void Configure(EntityTypeBuilder<EstablishmentReview> builder)
    {
        builder.ToTable("establishment_reviews", "onboarding");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Comment).HasMaxLength(1000);
        builder.HasIndex(x => new { x.Status, x.SubmittedAt });
        builder.HasIndex(x => x.EstablishmentId);
    }
}
