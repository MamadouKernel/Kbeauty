using KekeBeauty.Modules.Administration.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KekeBeauty.Modules.Administration.Infrastructure;

public sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("app_settings", "admin");
        builder.HasKey(x => x.Key);
        builder.Property(x => x.Key).HasMaxLength(60);
        builder.Property(x => x.Value).HasMaxLength(500).IsRequired();
    }
}

public sealed class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.ToTable("notification_templates", "admin");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(480).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

public sealed class LegalDocumentConfiguration : IEntityTypeConfiguration<LegalDocument>
{
    public void Configure(EntityTypeBuilder<LegalDocument> builder)
    {
        builder.ToTable("legal_documents", "admin");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Slug).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(60000).IsRequired();
        builder.HasIndex(x => x.Slug).IsUnique();
    }
}

public sealed class LegalDocumentVersionConfiguration : IEntityTypeConfiguration<LegalDocumentVersion>
{
    public void Configure(EntityTypeBuilder<LegalDocumentVersion> builder)
    {
        builder.ToTable("legal_document_versions", "admin");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Slug).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(60000).IsRequired();
        builder.HasIndex(x => new { x.Slug, x.Version }).IsUnique();
    }
}

public sealed class DatabaseBackupConfiguration : IEntityTypeConfiguration<DatabaseBackup>
{
    public void Configure(EntityTypeBuilder<DatabaseBackup> builder)
    {
        builder.ToTable("database_backups", "admin");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.FileKey).HasMaxLength(200);
        builder.Property(x => x.Error).HasMaxLength(500);
        builder.HasIndex(x => x.StartedAt);
    }
}
