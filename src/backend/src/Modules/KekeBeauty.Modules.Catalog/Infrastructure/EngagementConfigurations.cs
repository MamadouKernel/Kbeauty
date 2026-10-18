using KekeBeauty.Modules.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KekeBeauty.Modules.Catalog.Infrastructure;

public sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("reviews", "catalog", table => table.HasCheckConstraint("ck_reviews_rating", "rating BETWEEN 1 AND 5"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Comment).HasMaxLength(1000);
        builder.Property(x => x.ReplyText).HasMaxLength(600);
        builder.Property(x => x.HiddenReason).HasMaxLength(300);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
        builder.HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Cascade);

        // One review per appointment, whatever happens: the unique index is the real guarantee, not the application check.
        builder.HasIndex(x => x.AppointmentId).IsUnique();
        builder.HasIndex(x => new { x.EstablishmentId, x.Status, x.CreatedAt });
        builder.HasIndex(x => x.UserId);
    }
}

public sealed class ReviewPhotoConfiguration : IEntityTypeConfiguration<ReviewPhoto>
{
    public void Configure(EntityTypeBuilder<ReviewPhoto> builder)
    {
        builder.ToTable("review_photos", "catalog");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
        builder.Property(x => x.StorageKey).HasMaxLength(300).IsRequired();
        builder.Property(x => x.ThumbKey).HasMaxLength(300);
        builder.HasOne<Review>().WithMany().HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.ReviewId);
    }
}

public sealed class FavoriteConfiguration : IEntityTypeConfiguration<Favorite>
{
    public void Configure(EntityTypeBuilder<Favorite> builder)
    {
        builder.ToTable("favorites", "catalog");
        builder.HasKey(x => new { x.UserId, x.EstablishmentId });
        builder.HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.UserId, x.CreatedAt });
    }
}
