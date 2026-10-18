using KekeBeauty.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KekeBeauty.Modules.Scheduling.Infrastructure;

public sealed class ResourceConfiguration : IEntityTypeConfiguration<Resource>
{
    public void Configure(EntityTypeBuilder<Resource> builder)
    {
        builder.ToTable("resources", "scheduling");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.CommissionPercent).HasPrecision(5, 2);
        builder.HasIndex(x => x.EstablishmentId);
        builder.HasIndex(x => x.UserId);
    }
}

public sealed class ClientTechnicalNoteConfiguration : IEntityTypeConfiguration<ClientTechnicalNote>
{
    public void Configure(EntityTypeBuilder<ClientTechnicalNote> builder)
    {
        builder.ToTable("client_technical_notes", "scheduling");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Allergies).HasMaxLength(500);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.HasIndex(x => new { x.EstablishmentId, x.ClientUserId }).IsUnique();
    }
}

public sealed class ResourceServiceLinkConfiguration : IEntityTypeConfiguration<ResourceServiceLink>
{
    public void Configure(EntityTypeBuilder<ResourceServiceLink> builder)
    {
        builder.ToTable("resource_services", "scheduling");
        builder.HasKey(x => new { x.ResourceId, x.ServiceId });
        builder.HasOne<Resource>().WithMany().HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TimeOffConfiguration : IEntityTypeConfiguration<TimeOff>
{
    public void Configure(EntityTypeBuilder<TimeOff> builder)
    {
        builder.ToTable("time_off", "scheduling", table => table.HasCheckConstraint("ck_time_off_range", "starts_at < ends_at"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Reason).HasMaxLength(250);
        builder.HasIndex(x => new { x.EstablishmentId, x.StartsAt });
        builder.HasOne<Resource>().WithMany().HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments", "scheduling", table => table.HasCheckConstraint("ck_appointments_range", "starts_at < ends_at"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.TotalPrice).HasPrecision(12, 2);
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.ClientNote).HasMaxLength(500);
        builder.Property(x => x.ClientPhone).HasMaxLength(20);
        builder.Property(x => x.WalkInName).HasMaxLength(80);
        builder.Property(x => x.TipAmount).HasPrecision(12, 2);
        builder.Property(x => x.ServicePaidMethod).HasMaxLength(40);
        builder.Property(x => x.ServicePaidReference).HasMaxLength(60);
        builder.Property(x => x.RejectionReason).HasMaxLength(500);
        builder.HasIndex(x => new { x.EstablishmentId, x.StartsAt });
        builder.HasIndex(x => new { x.ClientUserId, x.StartsAt });
        builder.HasIndex(x => new { x.Status, x.ExpiresAt });
        builder.HasOne<Resource>().WithMany().HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);

        // The "no double booking" exclusion constraint (PRD §8.3) is PostgreSQL specific and is added by the migration:
        //   EXCLUDE USING gist (resource_id WITH =, tstzrange(starts_at, ends_at) WITH &&)
        //   WHERE (status IN ('Pending','Confirmed','RescheduleProposed'))
    }
}

public sealed class AppointmentLineConfiguration : IEntityTypeConfiguration<AppointmentLine>
{
    public void Configure(EntityTypeBuilder<AppointmentLine> builder)
    {
        builder.ToTable("appointment_services", "scheduling");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.NameSnapshot).HasMaxLength(150).IsRequired();
        builder.Property(x => x.PriceSnapshot).HasPrecision(12, 2);
        builder.HasIndex(x => x.AppointmentId);
        builder.HasOne<Appointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AppointmentStatusHistoryConfiguration : IEntityTypeConfiguration<AppointmentStatusHistory>
{
    public void Configure(EntityTypeBuilder<AppointmentStatusHistory> builder)
    {
        builder.ToTable("appointment_status_history", "scheduling");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Comment).HasMaxLength(500);
        builder.HasIndex(x => new { x.AppointmentId, x.At });
        builder.HasOne<Appointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Cascade);
    }
}
