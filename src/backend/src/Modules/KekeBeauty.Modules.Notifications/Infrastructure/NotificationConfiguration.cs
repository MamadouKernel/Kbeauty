using KekeBeauty.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KekeBeauty.Modules.Notifications.Infrastructure;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications", "notifications");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Channel).HasConversion<string>().HasMaxLength(10);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(x => x.TemplateCode).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.ProviderMessageId).HasMaxLength(100);
        builder.Property(x => x.Error).HasMaxLength(500);
        builder.HasIndex(x => new { x.UserId, x.Channel, x.CreatedAt });
    }
}

public sealed class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        builder.ToTable("push_subscriptions", "notifications");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Endpoint).HasMaxLength(600).IsRequired();
        builder.Property(x => x.P256dh).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Auth).HasMaxLength(100).IsRequired();
        builder.Property(x => x.UserAgent).HasMaxLength(256);
        builder.HasIndex(x => x.Endpoint).IsUnique();
        builder.HasIndex(x => x.UserId);
    }
}

public sealed class WhatsAppConsentConfiguration : IEntityTypeConfiguration<WhatsAppConsent>
{
    public void Configure(EntityTypeBuilder<WhatsAppConsent> builder)
    {
        builder.ToTable("whatsapp_consents", "notifications");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Phone).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(10);
        builder.Property(x => x.TextVersion).HasMaxLength(20).IsRequired();
        builder.Property(x => x.TextSnapshot).HasMaxLength(600).IsRequired();
        builder.Property(x => x.Source).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => new { x.UserId, x.Phone, x.Purpose, x.At });
    }
}

public sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("notification_preferences", "notifications");
        builder.HasKey(x => x.UserId);
        builder.Property(x => x.UserId).ValueGeneratedNever();
    }
}
