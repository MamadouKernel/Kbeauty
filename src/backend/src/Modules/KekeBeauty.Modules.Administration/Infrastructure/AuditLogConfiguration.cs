using KekeBeauty.Modules.Administration.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KekeBeauty.Modules.Administration.Infrastructure;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs", "admin");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Action).HasMaxLength(60).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Data).HasColumnType("jsonb");
        builder.Property(x => x.Ip).HasMaxLength(45);
        builder.HasIndex(x => new { x.EntityType, x.EntityId, x.At });
    }
}
