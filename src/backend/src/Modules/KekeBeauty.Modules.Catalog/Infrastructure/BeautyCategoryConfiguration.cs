using KekeBeauty.Modules.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KekeBeauty.Modules.Catalog.Infrastructure;

public sealed class BeautyCategoryConfiguration : IEntityTypeConfiguration<BeautyCategory>
{
    public void Configure(EntityTypeBuilder<BeautyCategory> builder)
    {
        builder.ToTable("beauty_categories", "catalog");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Icon).HasMaxLength(60).IsRequired();
        builder.HasIndex(x => x.Slug).IsUnique();
    }
}
