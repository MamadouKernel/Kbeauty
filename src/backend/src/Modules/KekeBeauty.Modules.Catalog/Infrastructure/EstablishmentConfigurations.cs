using KekeBeauty.Modules.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KekeBeauty.Modules.Catalog.Infrastructure;

public sealed class EstablishmentConfiguration : IEntityTypeConfiguration<Establishment>
{
    public void Configure(EntityTypeBuilder<Establishment> builder)
    {
        builder.ToTable("establishments", "catalog");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.CustomerServicePhone).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(254);
        builder.Property(x => x.AddressLine).HasMaxLength(250).IsRequired();
        builder.Property(x => x.Location).HasColumnType("geography (point, 4326)").IsRequired();
        // ST_X / ST_Y only exist for geometry, so coordinates are generated from the geography column by PostgreSQL.
        builder.Property(x => x.Latitude).HasComputedColumnSql("ST_Y(location::geometry)", stored: true);
        builder.Property(x => x.Longitude).HasComputedColumnSql("ST_X(location::geometry)", stored: true);
        builder.Property(x => x.Timezone).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.PaymentMode).HasConversion<string>().HasMaxLength(10);
        builder.Property(x => x.PayoutOperator).HasMaxLength(40);
        builder.Property(x => x.PayoutAccount).HasMaxLength(30);
        builder.Property(x => x.PayoutAccountName).HasMaxLength(100);
        builder.OwnsMany(x => x.MobileMoney, owned => owned.ToJson("mobile_money"));

        builder.HasIndex(x => x.Slug).IsUnique();
        // GiST (location) and trigram (name) indexes are PostgreSQL specific: declared in KekeBeautyDbContext.
        builder.HasIndex(x => new { x.Status, x.CommuneId });
        builder.HasMany(x => x.Categories).WithOne().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class EstablishmentCategoryConfiguration : IEntityTypeConfiguration<EstablishmentCategory>
{
    public void Configure(EntityTypeBuilder<EstablishmentCategory> builder)
    {
        builder.ToTable("establishment_categories", "catalog");
        builder.HasKey(x => new { x.EstablishmentId, x.CategoryId });
        builder.HasOne<BeautyCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.CategoryId);
    }
}

public sealed class MediaConfiguration : IEntityTypeConfiguration<Media>
{
    public void Configure(EntityTypeBuilder<Media> builder)
    {
        builder.ToTable("media", "catalog");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
        builder.Property(x => x.StorageKey).HasMaxLength(300).IsRequired();
        builder.Property(x => x.ThumbKey).HasMaxLength(300);
        builder.Property(x => x.PosterKey).HasMaxLength(300);
        builder.Property(x => x.AltText).HasMaxLength(250);
        builder.HasIndex(x => new { x.EstablishmentId, x.SortOrder });
        builder.HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ServiceGroupConfiguration : IEntityTypeConfiguration<ServiceGroup>
{
    public void Configure(EntityTypeBuilder<ServiceGroup> builder)
    {
        builder.ToTable("service_groups", "catalog");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.ToTable("services", "catalog");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.Price).HasPrecision(12, 2);
        builder.Property(x => x.PriceType).HasConversion<string>().HasMaxLength(10);
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.HasIndex(x => new { x.EstablishmentId, x.SortOrder });
        builder.HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ServiceGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<BeautyCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class EstablishmentStatsDailyConfiguration : IEntityTypeConfiguration<EstablishmentStatsDaily>
{
    public void Configure(EntityTypeBuilder<EstablishmentStatsDaily> builder)
    {
        builder.ToTable("establishment_stats_daily", "catalog");
        builder.HasKey(x => new { x.EstablishmentId, x.Day });
        builder.HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Cascade);
    }
}
