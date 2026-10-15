using KekeBeauty.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KekeBeauty.Modules.Scheduling.Infrastructure;

public sealed class OpeningHourConfiguration : IEntityTypeConfiguration<OpeningHour>
{
    public void Configure(EntityTypeBuilder<OpeningHour> builder)
    {
        builder.ToTable("opening_hours", "scheduling", table =>
            table.HasCheckConstraint("ck_opening_hours_range", "opens_at < closes_at"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.DayOfWeek).HasConversion<int>();
        builder.HasIndex(x => new { x.EstablishmentId, x.DayOfWeek });
    }
}
